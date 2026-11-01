using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// Downloads are something a server's owner chooses, never something a new install or a new account does by itself: the server-wide "Allow downloads"
/// switch (off on a fresh install, on for an install that has downloaded before, FLACIE_DOWNLOADS overrides both), "Download all" and imports for
/// administrators unless they let everyone, the three automatic downloads (daily charts, Autoplay's fetching ahead, Alexa) off for a new account while
/// an existing account keeps what it had, and the YouTube source kept working where it was already on without being offered anywhere.
/// </summary>
public class DownloadPolicyTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("flacie-policy").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    static IConfiguration Conf(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    static UserSession Person(string name, string id) => new("jellyfin", new JellyfinAccount("http://jf.test", id, name, "tok"), null) { Id = id };

    // ---- the server-wide switch ----

    [Fact]
    public void A_fresh_install_has_downloads_off_and_remembers_that()
    {
        var p = new DownloadPolicy(new DataPaths(dir), Conf());
        Assert.False(p.Allowed);
        Assert.True(File.Exists(Path.Combine(dir, DownloadPolicy.FileName)));
        Assert.False(new DownloadPolicy(new DataPaths(dir), Conf()).Allowed);
    }

    [Fact]
    public void An_install_that_has_downloaded_before_keeps_downloads_on_with_no_action()
    {
        Directory.CreateDirectory(Path.Combine(dir, "downloads"));
        File.WriteAllText(Path.Combine(dir, "downloads", "someone.jsonl"), "{\"Title\":\"x\"}\n");
        Assert.True(new DownloadPolicy(new DataPaths(dir), Conf()).Allowed);
        // the answer is written down at once, so it does not change later when the log is trimmed or moved
        File.Delete(Path.Combine(dir, "downloads", "someone.jsonl"));
        Assert.True(new DownloadPolicy(new DataPaths(dir), Conf()).Allowed);
    }

    [Fact]
    public void An_empty_download_log_folder_does_not_count_as_having_downloaded()
    {
        Directory.CreateDirectory(Path.Combine(dir, "downloads"));
        File.WriteAllText(Path.Combine(dir, "downloads", "someone.jsonl"), "");
        Assert.False(new DownloadPolicy(new DataPaths(dir), Conf()).Allowed);
    }

    [Fact]
    public void An_administrator_turns_downloads_on_and_off_and_it_lasts_a_restart()
    {
        var p = new DownloadPolicy(new DataPaths(dir), Conf());
        p.SetAllowed(true);
        Assert.True(new DownloadPolicy(new DataPaths(dir), Conf()).Allowed);
        p.SetAllowed(false);
        Assert.False(new DownloadPolicy(new DataPaths(dir), Conf()).Allowed);
    }

    [Theory]
    [InlineData("on", false, true)]
    [InlineData("off", true, false)]
    [InlineData("true", false, true)]
    [InlineData("0", true, false)]
    [InlineData("", true, true)]          // empty: the saved switch counts
    [InlineData("maybe", false, false)]   // not understood: the saved switch counts
    public void FLACIE_DOWNLOADS_overrides_the_saved_switch(string env, bool saved, bool expected)
    {
        new DownloadPolicy(new DataPaths(dir), Conf()).SetAllowed(saved);
        var p = new DownloadPolicy(new DataPaths(dir), Conf(("FLACIE_DOWNLOADS", env)));
        Assert.Equal(expected, p.Allowed);
        Assert.Equal(env is "on" or "off" or "true" or "0", p.SetByHost);
    }

    [Fact]
    public void Download_all_and_imports_are_for_administrators_unless_everyone_is_let_in()
    {
        var conf = Conf(("FLACIE_ADMINS", "boss@jf.test"));
        var p = new DownloadPolicy(new DataPaths(dir), conf);
        var boss = Person("boss", "u1"); var guest = Person("guest", "u2");
        Assert.False(p.MayBulk(boss));                        // downloads are off: nobody, not even an administrator
        p.SetAllowed(true);
        Assert.True(p.MayBulk(boss));
        Assert.False(p.MayBulk(guest));
        Assert.False(p.EveryoneBulk);
        p.SetEveryoneBulk(true);
        Assert.True(new DownloadPolicy(new DataPaths(dir), conf).MayBulk(guest));
        p.SetAllowed(false);
        Assert.False(p.MayBulk(guest));                       // and the server switch still wins
    }

    [Fact]
    public void Downloads_are_ready_for_a_person_only_when_allowed_and_set_up()
    {
        var p = new DownloadPolicy(new DataPaths(dir), Conf());
        var s = Person("me", "u1");
        new DownloadServices("http://slskd", "k", "downloads", "http://fm", "k", "", "", "").WriteTo(s.Profile);
        Assert.False(p.Ready(s));
        p.SetAllowed(true);
        Assert.True(p.Ready(s));
        Assert.False(p.Ready(Person("other", "u2")));         // allowed, but this person has no services
    }

    // ---- the one place the downloads stop ----

    sealed class NoNet : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => throw new HttpRequestException("no network in tests");
    }
    sealed class Factory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(new NoNet(), false); }

    (DownloadManager M, DownloadLog Log, UserSession S) Manager(DownloadPolicy policy)
    {
        var log = new DownloadLog(new DataPaths(dir));
        var m = new DownloadManager(new Factory(), new WebCatalog(new HttpClient(new NoNet())), log, new JellyfinClient(new HttpClient(new NoNet()), "dev"), new DownloadDigest((_, _) => { }), policy);
        var s = Person("me", "u1");
        new DownloadServices("http://slskd", "k", "downloads", "http://fm", "k", "", "", "").WriteTo(s.Profile);
        return (m, log, s);
    }

    [Fact]
    public async Task With_downloads_off_nothing_is_fetched_from_any_path()
    {
        var (m, log, s) = Manager(new DownloadPolicy(new DataPaths(dir), Conf()));
        Assert.False(m.Allowed);
        Assert.False(m.Ready(s));
        var r = await m.FetchAsync(s, "Chart", "Artist", "Title", "Album", null, 200_000);   // charts, imports, Autoplay, Alexa and the DJ all come through here
        Assert.False(r.Ok);
        Assert.Contains("turned off", r.Message);
        m.Song(s, new CatalogSong("Artist", "Other", "Album", null, 200_000, 1, 1));
        m.Album(s, new CatalogAlbum(1, "Artist", "Album", null, 2, "2020"));
        await Task.Delay(100);
        Assert.Empty(m.JobsOf(s));
        Assert.Empty(log.For(s.Id));
        Assert.False(m.Retry(s, new DownloadRecord { Artist = "Artist", Title = "Title", Outcome = "failed", UserId = s.Id }));
    }

    [Fact]
    public async Task With_downloads_on_a_fetch_is_tried_as_before()
    {
        var p = new DownloadPolicy(new DataPaths(dir), Conf()); p.SetAllowed(true);
        var (m, _, s) = Manager(p);
        Assert.True(m.Ready(s));
        var r = await m.FetchAsync(s, "Search", "Artist", "Title", "Album", null, 200_000);
        Assert.DoesNotContain("turned off", r.Message);
        Assert.Single(m.JobsOf(s));
    }

    [Fact]
    public async Task Autoplay_does_not_fetch_ahead_while_downloads_are_off_even_when_its_switch_is_on()
    {
        var policy = new DownloadPolicy(new DataPaths(dir), Conf());
        var (m, _, s) = Manager(policy);
        var states = new UserStateStore(new DataPaths(dir), new EphemeralDataProtectionProvider());
        states.For(s).Prefetch = true;
        var planner = new AutoplayPlanner(new WebCatalog(new HttpClient(new NoNet())), m, states, new StorageGuard(states));
        planner.Prefetch(s, [new CatalogSong("Artist", "Ahead", "Album", null, 200_000, 0, 0)], 5, () => { });
        await Task.Delay(100);
        Assert.Empty(m.JobsOf(s));
    }

    // ---- the automatic downloads of a new account, and of an existing one ----

    [Fact]
    public void A_new_account_starts_with_daily_charts_and_fetching_ahead_off()
    {
        var states = new UserStateStore(new DataPaths(dir), new EphemeralDataProtectionProvider());
        var st = states.For(Person("new", "fresh-account"));
        Assert.False(st.Charts.Enabled);
        Assert.False(st.Prefetch);
        states.Save(Person("new", "fresh-account"));
        var again = new UserStateStore(new DataPaths(dir), new EphemeralDataProtectionProvider()).For(Person("new", "fresh-account"));
        Assert.False(again.Charts.Enabled);
        Assert.False(again.Prefetch);
    }

    [Fact]
    public void An_existing_account_keeps_its_switches_on_and_they_are_written_down()
    {
        // an account file from before this change: the switches were on by being left out (an older file had neither field)
        var users = Directory.CreateDirectory(Path.Combine(dir, "users")).FullName;
        File.WriteAllText(Path.Combine(users, "old-account.json"), "{ \"MinFreeGb\": 20, \"Charts\": { \"Lists\": [0, 132] } }");
        var st = new UserStateStore(new DataPaths(dir), new EphemeralDataProtectionProvider()).For(Person("old", "old-account"));
        Assert.True(st.Charts.Enabled);
        Assert.True(st.Prefetch);
        Assert.Equal(20, st.MinFreeGb);
        // written down as they are now, so a later change of what "missing" means can not flip them
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(users, "old-account.json")))!;
        Assert.True(saved["Prefetch"]!.GetValue<bool>());
        Assert.False(saved["Charts"]!["Off"]!.GetValue<bool>());
        Assert.True(saved["Version"]!.GetValue<int>() >= 1);
    }

    [Fact]
    public void An_existing_account_that_switched_them_off_keeps_them_off()
    {
        var users = Directory.CreateDirectory(Path.Combine(dir, "users")).FullName;
        File.WriteAllText(Path.Combine(users, "off-account.json"), "{ \"Prefetch\": false, \"Charts\": { \"Off\": true } }");
        var st = new UserStateStore(new DataPaths(dir), new EphemeralDataProtectionProvider()).For(Person("old", "off-account"));
        Assert.False(st.Charts.Enabled);
        Assert.False(st.Prefetch);
    }

    static AlexaStore Alexa(string d) => new(new DataPaths(d), DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(d, "keys"))), new ConfigurationBuilder().Build());

    [Fact]
    public void Alexa_downloads_are_off_on_a_server_that_never_set_Alexa_up()
    {
        Assert.False(Alexa(dir).Config.Downloads);
    }

    [Fact]
    public void Alexa_downloads_stay_as_they_were_on_a_server_that_has_Alexa_settings()
    {
        File.WriteAllText(Path.Combine(dir, "alexa.json"), "{\"Config\":{\"Enabled\":true,\"SkillIds\":\"x\"},\"Links\":[]}");
        Assert.True(Alexa(dir).Config.Downloads);           // saved before the switch was written out: it was on
        File.WriteAllText(Path.Combine(dir, "alexa.json"), "{\"Config\":{\"Enabled\":true,\"Downloads\":false},\"Links\":[]}");
        Assert.False(Alexa(dir).Config.Downloads);
    }

    // ---- YouTube: works where it is already on, offered nowhere ----

    [Fact]
    public void YouTube_saved_on_keeps_working()
    {
        var services = new JsonObject { ["opensources"] = new JsonObject { ["ytdl"] = true, ["order"] = new JsonArray("archive", "ytdl") } };
        Assert.Contains(OpenSourceSettings.YtdlId, OpenSourceSettings.From(services).Active);
    }

    [Fact]
    public void YouTube_is_off_unless_saved_on_or_the_host_turns_it_on()
    {
        var services = new JsonObject { ["opensources"] = new JsonObject { ["ytdl"] = false } };
        Assert.DoesNotContain(OpenSourceSettings.YtdlId, OpenSourceSettings.From(services).Active);
        Assert.DoesNotContain(OpenSourceSettings.YtdlId, OpenSourceSettings.From(null).Active);
        var byHost = OpenSourceSettings.From(services, null, youtubeByHost: true);
        Assert.Contains(OpenSourceSettings.YtdlId, byHost.Active);
        // the host's switch is never written into the profile: the profile keeps what the person saved
        var o = new JsonObject(); byHost.WriteTo(o);
        Assert.False(o["opensources"]!["ytdl"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("off", false)]
    [InlineData(null, false)]
    public void FLACIE_YOUTUBE_reads_as_on_or_off(string? env, bool on) => Assert.Equal(on, OpenSourceSettings.HostSwitch(env));
}
