using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>The Windows app's DJ (FLACie.Server/Dj/HomeDj): with the family's FLACie Web named in the profile, the settings, the next-song answers and the clips
/// come from there with the person's own Jellyfin sign-in; the page never waits for it, and this PC's own DJ stands in when it can't be reached. No real server is called.</summary>
public class DjHomeTests : IDisposable
{
    const string JfToken = "jf_fake_token_0123456789abcdef";   // a fake: never a real sign-in
    const string ClipId = "0123456789abcdef0123456789abcdef";
    static readonly byte[] Mp3 = [0x49, 0x44, 0x33, 0x04, 0, 0, 0, 0, 0, 1, 0xFF, 0xFB];
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-home").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    DateTime now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A stand-in for the family's FLACie Web: records each request (with the two sign-in headers) and answers with [Answer].</summary>
    sealed class HomeServer : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Url, string Token, string Server, string Auth, string Body)> Seen = [];
        public Func<HttpRequestMessage, string, Task<HttpResponseMessage>> Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            string H(string k) => r.Headers.TryGetValues(k, out var v) ? string.Join(",", v) : "";
            lock (Seen) Seen.Add((r.Method, r.RequestUri!.ToString(), H("X-Emby-Token"), H("X-Jellyfin-Server"), r.Headers.Authorization?.ToString() ?? "", body));
            return await Answer(r, body);
        }
        public int Count(string path) { lock (Seen) return Seen.Count(x => x.Url.EndsWith(path)); }
    }

    static HttpResponseMessage Json(JsonNode j) => new(HttpStatusCode.OK) { Content = new StringContent(j.ToJsonString(), Encoding.UTF8, "application/json") };
    static JsonObject HomeSettings(bool enabled = true, bool voiceReady = true) => new()
    {
        ["enabled"] = enabled, ["chatty"] = "rare", ["phone"] = true, ["auto"] = true, ["web"] = true, ["windows"] = true, ["backup"] = true,
        ["playlists"] = new JsonObject { ["jf-pl"] = false }, ["voiceReady"] = voiceReady,
    };

    sealed record Rig(HomeDj Dj, DjService Local, HomeServer Home, DjVoiceTests.Lines<HomeDj> Log, UserSession Me);

    Rig Make(string? homeUrl = "http://home.test:8080", bool desktop = true)
    {
        var paths = new DataPaths(dir); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var none = new DjVoiceTests.Handler(_ => throw new InvalidOperationException("Cloudflare must not be called"));
        var voice = new CloudflareVoice(paths, new DjVoiceTests.Factory(none), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => now);
        var local = new DjService(new UserStateStore(paths, dp), voice, new DjClips(paths, voice, new DjVoiceTests.Lines<DjClips>(), () => now), paths, new DjVoiceTests.Lines<DjService>(), () => now);
        var home = new HomeServer(); var log = new DjVoiceTests.Lines<HomeDj>();
        var dj = new HomeDj(local, new DjVoiceTests.Factory(home), log, desktop, () => now);
        var me = new UserSession("jellyfin", new JellyfinAccount("http://jf.test/", "u1", "tester", JfToken), null) { Id = "u1" };
        if (homeUrl is not null) me.Profile["services"]!["flacieweb"] = new JsonObject { ["url"] = homeUrl };
        return new(dj, local, home, log, me);
    }

    static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 250 && !done(); i++) await Task.Delay(20);
        Assert.True(done());
    }

    static readonly DjSong A = new("Wonderwall", "Oasis", Path: "/m/a.flac"), B = new("Yellow", "Coldplay", Path: "/m/b.flac");

    void TokenOnlyInTheHeaders(Rig r)
    {
        lock (r.Home.Seen)
        {
            Assert.NotEmpty(r.Home.Seen);
            foreach (var x in r.Home.Seen)
            {
                Assert.Equal(JfToken, x.Token); Assert.Equal("http://jf.test", x.Server);
                Assert.DoesNotContain(JfToken, x.Url); Assert.DoesNotContain(JfToken, x.Body); Assert.Equal("", x.Auth);
            }
        }
        lock (r.Log.All) Assert.DoesNotContain(r.Log.All, l => l.Contains(JfToken));
        Assert.DoesNotContain(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories), f => !f.Contains("keys") && File.ReadAllText(f).Contains(JfToken));
    }

    [Theory]
    [InlineData("https://flacie.example.org", "https://flacie.example.org")]
    [InlineData("http://192.168.1.20:5000/", "http://192.168.1.20:5000")]
    [InlineData(" http://nas.local:5000/flacie/ ", "http://nas.local:5000/flacie")]
    [InlineData("http://127.0.0.1:5287", null)]      // this PC: the app's own copy
    [InlineData("http://localhost:5287", null)]
    [InlineData("http://app.localhost", null)]
    [InlineData("ftp://flacie.example.org", null)]
    [InlineData("http://me:pw@flacie.example.org", null)]
    [InlineData("flacie.example.org", null)]
    [InlineData("", null)]
    public void The_home_server_is_the_profiles_FLACie_Web_when_it_is_another_server(string url, string? expected)
    {
        var p = new JsonObject { ["services"] = new JsonObject { ["flacieweb"] = new JsonObject { ["url"] = url } } };
        Assert.Equal(expected, HomeDj.HomeOf(p));
        Assert.Null(HomeDj.HomeOf(new JsonObject()));
        Assert.Null(HomeDj.HomeOf(new JsonObject { ["services"] = new JsonObject { ["flacieweb"] = new JsonObject { ["url"] = 5 } } }));
    }

    [Fact]
    public async Task FLACie_Web_itself_or_an_app_with_no_home_server_is_its_own_DJ_and_calls_nothing()
    {
        foreach (var (url, desktop) in new[] { ("http://home.test:8080", false), (null, true), ("http://127.0.0.1:5287", true) })
        {
            var r = Make(url, desktop);
            var st = r.Local.Settings(r.Me); st.Enabled = true; st.DeviceVoice = true; r.Local.Save(r.Me, st);
            Assert.True(r.Dj.Settings(r.Me).Enabled);
            Assert.Null(r.Dj.Where(r.Me));
            Assert.NotNull(await r.Dj.PrepareAsync(r.Me, new DjRequest(DjSurface.Windows, B, A)));
            Assert.Null(await r.Dj.ClipAsync(r.Me, ClipId));
            Assert.False(await r.Dj.ReadNowAsync(r.Me));
            Assert.Empty(r.Home.Seen);

        }
    }

    [Fact]
    public async Task Settings_come_from_the_home_server_in_the_background_and_are_kept_here()
    {
        var r = Make();
        var answer = new TaskCompletionSource();
        r.Home.Answer = async (req, _) => { await answer.Task; return Json(HomeSettings()); };
        var changed = 0; r.Dj.Changed += id => { if (id == "u1") Interlocked.Increment(ref changed); };
        // the first look never waits: this PC's settings (the defaults) while the home server is asked
        var first = r.Dj.Settings(r.Me);
        Assert.False(first.Enabled);
        Assert.Equal(new DjHome("http://home.test:8080", null, "", false), r.Dj.Where(r.Me));
        answer.SetResult();
        await Until(() => r.Dj.Where(r.Me)?.Reachable == true);
        var st = r.Dj.Settings(r.Me);
        Assert.True(st.Enabled); Assert.Equal(DjChatty.Rare, st.Chatty); Assert.False(st.Playlists["jf-pl"]);
        Assert.True(r.Dj.VoiceReadyFor(r.Me)); Assert.True(r.Dj.VoiceReady);   // the home server's Cloudflare, not this PC's (none here)
        Assert.False(r.Local.VoiceReady);
        Assert.True(changed > 0);
        var (method, url, _, _, _, _) = Assert.Single(r.Home.Seen);
        Assert.Equal(HttpMethod.Get, method); Assert.Equal("http://home.test:8080/api/dj/settings", url);
        // kept: asked again only after a while (and still without waiting)
        for (var i = 0; i < 5; i++) r.Dj.Settings(r.Me);
        Assert.Single(r.Home.Seen);
        now = now.Add(HomeDj.RefreshEvery).AddSeconds(1);
        r.Home.Answer = (_, _) => Task.FromResult(Json(HomeSettings(enabled: false)));   // turned off on the phone meanwhile
        Assert.True(r.Dj.Settings(r.Me).Enabled);
        await Until(() => !r.Local.Settings(r.Me).Enabled);
        Assert.Equal(2, r.Home.Count("/api/dj/settings"));
        TokenOnlyInTheHeaders(r);
    }

    [Fact]
    public async Task A_change_is_kept_here_at_once_and_only_what_changed_is_sent()
    {
        var r = Make();
        r.Home.Answer = (req, body) => Task.FromResult(Json(req.Method == HttpMethod.Get ? HomeSettings() : ApplyTo(HomeSettings(), body)));
        Assert.True(await r.Dj.ReadNowAsync(r.Me));
        var st = r.Dj.Settings(r.Me);
        st.Web = false; st.Playlists["jf-pl"] = true;
        r.Dj.Save(r.Me, st);
        Assert.False(r.Dj.Settings(r.Me).Web);                               // at once, before the home server has answered
        await Until(() => r.Home.Count("/api/dj/settings") == 2);
        var post = r.Home.Seen[1];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal("""{"web":false,"playlist":{"id":"jf-pl","on":true}}""", post.Body);
        await Until(() => r.Dj.Where(r.Me)?.Reachable == true && r.Dj.Settings(r.Me).Playlists["jf-pl"]);
        r.Dj.Save(r.Me, r.Dj.Settings(r.Me));                                // nothing changed: nothing sent
        await Task.Delay(100);
        Assert.Equal(2, r.Home.Count("/api/dj/settings"));
        TokenOnlyInTheHeaders(r);
    }

    static JsonObject ApplyTo(JsonObject settings, string patch)
    {
        var s = DjJson.ReadSettings(settings, out var voice)!;
        Assert.Null(DjJson.Apply(JsonNode.Parse(patch)!.AsObject(), s));
        return DjJson.Settings(s, voice);
    }

    [Fact]
    public async Task The_next_song_is_asked_there_and_its_clip_is_fetched_through_this_server()
    {
        var r = Make();
        r.Home.Answer = (req, body) => Task.FromResult(req.RequestUri!.AbsolutePath switch
        {
            "/api/dj/next" => Json(new JsonObject { ["speak"] = true, ["text"] = "That was Wonderwall. Here's Yellow.", ["caption"] = "That was Wonderwall. Here's Yellow.", ["clip"] = $"/api/dj/clip/{ClipId}.mp3", ["backup"] = true }),
            var p when p == $"/api/dj/clip/{ClipId}.mp3" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Mp3) { Headers = { ContentType = new("audio/mpeg") } } },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var req = new DjRequest(DjSurface.Windows, B, A, PlaylistId: "jf-pl", QueueDj: true, LocalHour: 21, Weekday: 5);
        var cue = await r.Dj.PrepareAsync(r.Me, req);
        Assert.NotNull(cue);
        Assert.Equal("That was Wonderwall. Here's Yellow.", cue.Text);
        Assert.Equal($"/api/dj/clip/r/{ClipId}.mp3", cue.ClipUrl);            // this server's address for it: the page needs no sign-in for the home server
        Assert.True(cue.Backup);
        var sent = r.Home.Seen.Single(x => x.Url.EndsWith("/api/dj/next"));
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Null(DjJson.Request(JsonNode.Parse(sent.Body)!.AsObject(), out var back));
        Assert.Equal(req.Surface, back!.Surface); Assert.Equal(req.Next, back.Next); Assert.Equal(req.Prev, back.Prev);
        Assert.Equal(("jf-pl", true, 21, 5), (back.PlaylistId, back.QueueDj, back.LocalHour, back.Weekday));
        Assert.Equal(Mp3, await r.Dj.ClipAsync(r.Me, ClipId));
        Assert.Equal(Mp3, await r.Dj.ClipAsync(r.Me, ClipId));                // the page's fetch ahead and its play: asked there once
        Assert.Equal(1, r.Home.Count($"/api/dj/clip/{ClipId}.mp3"));
        Assert.Null(await r.Dj.ClipAsync(r.Me, "../../keys"));                // only clip ids go through
        Assert.Null(await r.Dj.ClipAsync(r.Me, new string('f', 32)));         // unknown there: 404 here
        TokenOnlyInTheHeaders(r);
    }

    [Fact]
    public async Task A_clip_address_of_another_shape_is_not_followed_and_the_device_says_the_line()
    {
        var r = Make();
        r.Home.Answer = (_, _) => Task.FromResult(Json(new JsonObject { ["speak"] = true, ["text"] = "Here's Yellow.", ["caption"] = "Here's Yellow.", ["clip"] = "https://elsewhere.test/x.mp3", ["backup"] = true }));
        var cue = await r.Dj.PrepareAsync(r.Me, new DjRequest(DjSurface.Windows, B, A));
        Assert.NotNull(cue); Assert.Null(cue.ClipUrl); Assert.True(cue.Backup);
        r.Home.Answer = (_, _) => Task.FromResult(Json(new JsonObject { ["speak"] = true, ["text"] = "Here's Yellow.", ["caption"] = "", ["clip"] = "https://elsewhere.test/x.mp3", ["backup"] = false }));
        Assert.Null(await r.Dj.PrepareAsync(r.Me, new DjRequest(DjSurface.Windows, A, B)));   // no clip and no backup voice: quiet
        r.Home.Answer = (_, _) => Task.FromResult(Json(new JsonObject { ["speak"] = false }));
        Assert.Null(await r.Dj.PrepareAsync(r.Me, new DjRequest(DjSurface.Windows, B, B)));
    }

    [Fact]
    public async Task When_the_home_server_cant_be_reached_this_PCs_own_DJ_stands_in_and_says_so()
    {
        var r = Make();
        var mine = r.Local.Settings(r.Me); mine.Enabled = true; mine.DeviceVoice = true; r.Local.Save(r.Me, mine);   // this PC's own settings (its device voice chosen)
        r.Home.Answer = (_, _) => throw new HttpRequestException("No route to host");
        Assert.False(await r.Dj.ReadNowAsync(r.Me));
        Assert.Equal(new DjHome("http://home.test:8080", false, "couldn't be reached", false), r.Dj.Where(r.Me));
        Assert.True(r.Dj.Settings(r.Me).Enabled);
        Assert.False(r.Dj.VoiceReadyFor(r.Me));                               // this PC has no Cloudflare of its own
        var calls = r.Home.Seen.Count;
        // the next song: this PC's DJ answers (no voice here, so the device's), without asking the home server again for a while
        var cue = await r.Dj.PrepareAsync(r.Me, new DjRequest(DjSurface.Windows, B, A));
        Assert.NotNull(cue); Assert.Null(cue.ClipUrl); Assert.True(cue.Backup);
        var st = r.Dj.Settings(r.Me); st.Chatty = DjChatty.Rare; r.Dj.Save(r.Me, st);   // a change while it is away stays here
        Assert.Equal(DjChatty.Rare, r.Dj.Settings(r.Me).Chatty);
        await Task.Delay(100);
        Assert.Equal(calls, r.Home.Seen.Count);
        // later it is asked again; a refused sign-in says so
        now = now.Add(HomeDj.RefreshEvery).AddSeconds(1);
        r.Home.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var meanwhile = await r.Dj.PrepareAsync(r.Me, new DjRequest(DjSurface.Windows, A, B));
        Assert.Null(meanwhile?.ClipUrl);                                      // (this PC's DJ again: no clip to be had here)
        Assert.Equal("didn't accept your sign-in", r.Dj.Where(r.Me)?.Problem);
        // an older FLACie Web with no DJ
        now = now.Add(HomeDj.RefreshEvery).AddSeconds(1);
        r.Home.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.False(await r.Dj.ReadNowAsync(r.Me));
        Assert.Equal("has no DJ yet (it needs updating)", r.Dj.Where(r.Me)?.Problem);
        // and when it is back, its settings are used again
        now = now.Add(HomeDj.RefreshEvery).AddSeconds(1);
        r.Home.Answer = (_, _) => Task.FromResult(Json(HomeSettings(enabled: false, voiceReady: false)));
        Assert.True(await r.Dj.ReadNowAsync(r.Me));
        Assert.Equal(new DjHome("http://home.test:8080", true, "", false), r.Dj.Where(r.Me));
        Assert.False(r.Dj.Settings(r.Me).Enabled);
        lock (r.Log.All) Assert.Contains(r.Log.All, l => l.Contains("couldn't be reached"));
        TokenOnlyInTheHeaders(r);
    }

    [Fact]
    public void The_json_both_ways_agrees_with_what_the_server_reads_and_writes()
    {
        var s = new DjSettings { Enabled = true, Chatty = DjChatty.Rare, Auto = false, Playlists = { ["a"] = true } };
        var back = DjJson.ReadSettings(DjJson.Settings(s, voiceReady: true), out var ready);
        Assert.True(ready);
        Assert.Empty(DjJson.Patch(s, back!));
        Assert.Null(DjJson.ReadSettings(JsonNode.Parse("""{ "enabled": "yes" }""")!.AsObject(), out _));
        var c = new DjCue("Here's Yellow.", "Here's Yellow.", "/api/dj/clip/x.mp3", true);
        Assert.Equal(c, DjJson.ReadCue(DjJson.Cue(c)));
        Assert.Null(DjJson.ReadCue(DjJson.Cue(null)));
        var t = new DjSettings { Playlists = { ["a"] = true, ["b"] = false } };
        var u = new DjSettings { Enabled = true, Playlists = { ["b"] = false, ["c"] = true } };
        Assert.Equal("""{"enabled":true,"playlists":{"b":false,"c":true}}""", DjJson.Patch(t, u).ToJsonString());
        var v = DjSwitches.Copy(t); v.Playlists.Remove("a");
        Assert.Equal("""{"playlist":{"id":"a","on":null}}""", DjJson.Patch(t, v).ToJsonString());
    }
}
