using System.Net;
using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// One voice for the DJ (the owner heard two on 2026-10-26: Cloudflare failed once and the device's own voice said the next lines), the DJ volume and the
/// music under it, and the per-song choice (always introduce it, never talk over it).
/// </summary>
public class DjVoiceVolumeTests : IDisposable
{
    const string Account = "0123456789abcdef0123456789abcdef";
    const string Aura = "aura-1:helios";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-onevoice").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    DateTime now = new(2026, 10, 26, 21, 33, 0, DateTimeKind.Utc);

    readonly UserSession me = new("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };

    sealed record Rig(DjService Dj, CloudflareVoice Voice, DjClips Clips, DjVoiceTests.Handler Http);

    Rig Make(Func<HttpRequestMessage, Task<HttpResponseMessage>> f, string voiceName = Aura, int cap = 50, bool ready = true, string sub = "")
    {
        var paths = new DataPaths(Path.Combine(dir, sub)); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var h = new DjVoiceTests.Handler(f);
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(h), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => now);
        if (ready) v.Save(new DjVoiceConfig { AccountId = Account, CapPercent = cap, Voice = voiceName, AiLines = false }, "cf_fake_token");
        var clips = new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>(), () => now);
        return new(new DjService(new UserStateStore(paths, dp), v, clips, paths, new DjVoiceTests.Lines<DjService>(), () => now), v, clips, h);
    }

    static Task<HttpResponseMessage> Ok(HttpRequestMessage _) => Task.FromResult(DjVoiceTests.Audio());
    static Task<HttpResponseMessage> Fails(HttpRequestMessage _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

    void TurnOn(Rig r, Action<DjSettings>? more = null) { var st = r.Dj.Settings(me); st.Enabled = true; more?.Invoke(st); r.Dj.Save(me, st); }

    static DjSong Song(string title, string artist, string path, long ms = 200_000) => new(title, artist, "", "", 0, "", ms, path);
    static readonly DjSong A = Song("Wonderwall", "Oasis", "/m/a.flac"), B = Song("Yellow", "Coldplay", "/m/b.flac"), C = Song("Breathe", "The Prodigy", "/m/c.flac");

    // ---- one voice ----

    [Fact]
    public async Task Two_voices_reproduced_Cloudflare_failing_once_no_longer_hands_the_next_lines_to_the_device_voice()
    {
        // frank, 21:33: the pooled phrases were being made in Aura when one call answered 500; for ten minutes after it every new line came back with no clip
        // and Backup on, so the phone or browser said it in its own voice between Aura lines
        var fail = true;
        var r = Make(q => fail ? Fails(q) : Ok(q)); TurnOn(r);
        Assert.True(r.Dj.Settings(me).Backup);                                 // everyone had the old backup switch on
        Assert.Null(await r.Clips.WaitAsync(r.Clips.Request("Coming up,")!, TimeSpan.FromSeconds(10)));
        Assert.True(r.Voice.Failing);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.Null(cue);                                                       // was: { ClipUrl = null, Backup = true }: the second voice
        fail = false;
        Assert.Single(r.Http.Seen);                                             // and no call in another voice either
    }

    [Fact]
    public void Near_the_cap_a_line_is_never_made_in_MeloTTS_instead_of_the_chosen_Aura_voice()
    {
        // 1 % of 10,000 neurons = 100: a 100-character Aura line (136) does not fit, the same line in MeloTTS (under 1) would
        var r = Make(Ok, cap: 1);
        var text = "Here's a line that is about one hundred characters long, so that Aura costs more than the cap allows.";
        Assert.False(r.Voice.CanAfford(Aura, text)); Assert.True(r.Voice.CanAfford(CloudflareVoice.MeloTts, text));
        Assert.Null(r.Clips.Request(text));
        Assert.Empty(r.Http.Seen);
    }

    [Fact]
    public async Task When_the_voice_cant_be_used_a_line_already_made_in_the_same_voice_is_used_and_parts_of_another_voice_never_are()
    {
        var fail = false;
        var r = Make(q => fail ? Fails(q) : Ok(q));
        var parts = new List<DjPart> { new("Coming up,", "Coming up,"), new("Yellow, by Coldplay.", "Yellow, by Coldplay.") };
        foreach (var p in parts) Assert.NotNull((await r.Clips.MakeNowAsync(p.Text)).Id);   // both parts kept in Aura Helios
        Assert.NotNull((await r.Clips.MakeNowAsync("Here's Yellow.")).Id);
        fail = true;
        Assert.Null(await r.Clips.WaitAsync(r.Clips.Request("Something new.")!, TimeSpan.FromSeconds(10)));
        Assert.True(r.Voice.Failing);
        var calls = r.Http.Seen.Count;
        // Cloudflare is failing: nothing new is made, but what is already kept in this voice is said
        Assert.Null(r.Clips.Request("Another new line."));
        Assert.NotNull(r.Clips.Cached("Here's Yellow."));
        var id = r.Clips.CachedPooled(parts, me.Id);
        Assert.NotNull(id);
        Assert.NotNull(await r.Clips.WaitAsync(id!, TimeSpan.FromSeconds(10)));   // joined here, with no call
        Assert.Equal(calls, r.Http.Seen.Count);
        // parts made in MeloTTS (before the default changed) have other ids: never joined into an Aura line
        var other = new List<DjPart> { new("Here's", "Here's"), new("Breathe, by The Prodigy.", "Breathe, by The Prodigy.") };
        fail = false;
        foreach (var p in other) Assert.NotNull((await r.Clips.MakeNowAsync(p.Text, voiceName: CloudflareVoice.MeloTts)).Id);
        Assert.Null(r.Clips.CachedPooled(other, me.Id));
        Assert.Null(r.Clips.Cached("Here's"));
        Assert.NotEqual(DjClips.IdOf(CloudflareVoice.MeloTts, "Here's"), DjClips.IdOf(Aura, "Here's"));
    }

    [Fact]
    public async Task The_device_voice_is_used_only_when_the_person_chooses_it_and_then_for_every_line_with_no_clip()
    {
        var r = Make(Ok); TurnOn(r, s => s.DeviceVoice = true);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.NotNull(cue); Assert.Null(cue.ClipUrl); Assert.True(cue.Backup);
        Assert.Empty(r.Http.Seen);                                              // nothing made in the server's voice: never the two together
        // without the choice, and with no voice set up, the DJ is quiet rather than use the device's voice
        var quiet = Make(Ok, ready: false, sub: "elsewhere");
        var st = quiet.Dj.Settings(me); st.Enabled = true; st.Backup = true; st.DeviceVoice = false; quiet.Dj.Save(me, st);
        Assert.Null(await quiet.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, C, B)));
    }

    [Fact]
    public async Task With_the_voice_working_every_clip_is_in_the_chosen_voice_and_never_marked_for_the_device()
    {
        var r = Make(Ok); TurnOn(r);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.NotNull(cue?.ClipUrl); Assert.False(cue.Backup);
        Assert.NotNull(await r.Clips.WaitAsync(cue.ClipUrl["/api/dj/clip/".Length..^4], TimeSpan.FromSeconds(10)));
        Assert.All(r.Http.Seen, s => Assert.Contains("@cf/deepgram/aura-1", s.Url));
        Assert.All(r.Http.Seen, s => Assert.Contains("\"speaker\":\"helios\"", s.Body));
    }

    // ---- DJ volume and the music under it ----

    [Fact]
    public void Volume_and_duck_are_kept_with_the_other_settings_checked_and_clamped()
    {
        var r = Make(Ok);
        var st = r.Dj.Settings(me);
        Assert.Equal((90, 30), (st.Volume, st.Duck));
        st.Volume = 140; st.Duck = -5; r.Dj.Save(me, st);
        Assert.Equal((100, 0), (Make(Ok).Dj.Settings(me).Volume, Make(Ok).Dj.Settings(me).Duck));   // a new store reads the account's file

        var s = new DjSettings();
        Assert.Null(DjJson.Apply(JsonNode.Parse("""{ "volume": 60, "duck": 15, "deviceVoice": true }""")!.AsObject(), s));
        Assert.Equal((60, 15, true), (s.Volume, s.Duck, s.DeviceVoice));
        Assert.NotNull(DjJson.Apply(JsonNode.Parse("""{ "volume": 101 }""")!.AsObject(), s));
        Assert.NotNull(DjJson.Apply(JsonNode.Parse("""{ "duck": "loud" }""")!.AsObject(), s));
        Assert.NotNull(DjJson.Apply(JsonNode.Parse("""{ "deviceVoice": 1 }""")!.AsObject(), s));
        var j = DjJson.Settings(s, false);
        Assert.Equal((60, 15, true), (j["volume"]!.GetValue<int>(), j["duck"]!.GetValue<int>(), j["deviceVoice"]!.GetValue<bool>()));
        var back = DjJson.ReadSettings(j, out _)!;
        Assert.Equal((60, 15, true), (back.Volume, back.Duck, back.DeviceVoice));
        var patch = DjJson.Patch(new DjSettings(), s);
        Assert.Equal(60, patch["volume"]!.GetValue<int>()); Assert.Equal(15, patch["duck"]!.GetValue<int>()); Assert.True(patch["deviceVoice"]!.GetValue<bool>());
    }

    sealed class Js : IJSRuntime
    {
        public readonly List<(string Id, object?[] Args)> Calls = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) { Calls.Add((identifier, args ?? [])); return ValueTask.FromResult(default(T)!); }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    sealed class FakeDj : IDjService
    {
        public DjSettings S = new() { Enabled = true, Volume = 60, Duck = 15 };
        public bool Device;
        public DjSettings Settings(UserSession s) => S;
        public void Save(UserSession s, DjSettings settings) => S = settings;
        public Task<DjCue?> PrepareAsync(UserSession s, DjRequest req, CancellationToken ct = default) =>
            Task.FromResult<DjCue?>(Device ? new("Here's " + req.Next.Title, "", null, true) : new("Here's " + req.Next.Title, "", "/api/dj/clip/x.mp3", false));
        public bool VoiceReady => true;
    }

    sealed class NoPlanner : IAutoplayPlanner
    {
        public Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default) => Task.FromResult(new List<CatalogSong>());
        public IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested) => [];
        public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach) { }
    }

    static Track T(int n) => new($"/m/{n}.flac", $"Song {n}", "A", "Al", "A", n, 1, 100_000, 2000, null, 0, TrackSource.Jellyfin, $"/m/{n}.flac", n.ToString("x32"));

    [Theory]
    [InlineData(false, "flacie.djPlay")]
    [InlineData(true, "flacie.djSay")]
    public async Task The_web_player_sets_the_DJ_volume_and_the_music_under_it_before_each_line(bool device, string speaks)
    {
        var js = new Js(); var p = new PlayerState(js); var dj = new FakeDj { Device = device };
        p.SetPlanner(new UserSession("jellyfin", null, null), new NoPlanner());
        p.SetDj(dj, DjSurface.Web);
        await p.Play(Enumerable.Range(0, 3).Select(T).ToList(), 0);
        await p.OnEnded();
        var levels = js.Calls.FindIndex(c => c.Id == "flacie.djLevels");
        Assert.True(levels >= 0);
        Assert.Equal(new object?[] { 60, 15 }, js.Calls[levels].Args);
        Assert.True(js.Calls.FindIndex(c => c.Id == speaks) > levels);
    }

    [Fact]
    public void The_page_script_scales_the_clip_by_the_DJ_volume_and_ducks_to_the_chosen_level()
    {
        var js = File.ReadAllText(Path.Combine(Repo(), "ipodsync", "src", "FLACie.Server", "wwwroot", "flacie.js"));
        Assert.Contains("baseVolume * fadeGain * djVol", js);
        Assert.Contains("djLevels(vol, duck)", js);
        Assert.DoesNotContain("duckTo(0.3", js);
    }

    static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "ipodsync"))) d = d.Parent;
        return d!.FullName;
    }

    // ---- always introduce / never talk over a song ----

    static readonly DateTime T0 = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_song_marked_always_is_introduced_every_time_even_right_after_a_line_or_when_short()
    {
        var short1 = Song("Intro", "Oasis", "/m/i.flac", ms: 40_000);
        for (var seed = 0; seed < 200; seed++)
        {
            var st = new DjSettings { Enabled = true, Chatty = DjChatty.Rare };
            DjRules.SetSong(st, "Yellow", "Coldplay", true); DjRules.SetSong(st, "Intro", "Oasis", true);
            var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(3), SilentRun = 0 };   // a line was said just before: the forced silence
            Assert.NotNull(DjPlanner.Plan(st, new DjRequest(DjSurface.Web, B, A), mem, T0, new Random(seed)));
            Assert.NotNull(DjPlanner.Plan(st, new DjRequest(DjSurface.Web, short1, B), mem, T0.AddMinutes(3), new Random(seed)));   // under a minute: still
        }
    }

    [Fact]
    public void A_song_marked_never_is_never_talked_over_and_the_master_switches_still_win_over_always()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var st = new DjSettings { Enabled = true, Chatty = DjChatty.Often };
            DjRules.SetSong(st, "Yellow", "Coldplay", false);
            Assert.Null(DjPlanner.Plan(st, new DjRequest(DjSurface.Web, B, A), new DjMemory(), T0, new Random(seed)));   // even the session's opening line
            Assert.Null(DjPlanner.Plan(st, new DjRequest(DjSurface.Web, B, A), new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(3), SilentRun = 9 }, T0, new Random(seed)));
        }
        var always = new DjSettings { Enabled = false };
        DjRules.SetSong(always, "Yellow", "Coldplay", true);
        Assert.Null(DjPlanner.Plan(always, new DjRequest(DjSurface.Web, B, A), new DjMemory(), T0, new Random(1)));          // the DJ is off
        always.Enabled = true; always.Web = false;
        Assert.Null(DjPlanner.Plan(always, new DjRequest(DjSurface.Web, B, A), new DjMemory(), T0, new Random(1)));          // not on the web
        always.Web = true;
        Assert.Null(DjPlanner.Plan(always, new DjRequest(DjSurface.Web, B, A, QueueDj: false), new DjMemory(), T0, new Random(1)));   // off for this queue
    }

    [Fact]
    public void The_choice_is_keyed_by_title_and_artist_so_it_holds_on_another_path_or_device()
    {
        var st = new DjSettings();
        DjRules.SetSong(st, "Yellow", "Coldplay", true);
        Assert.Equal(Matching.MatchKey("Yellow", "Coldplay"), Assert.Single(st.Songs.Keys));
        Assert.True(DjRules.SongChoice(st, "Yellow (Remastered 2011)", "Coldplay feat. Someone") ?? Matching.MatchKey("Yellow (Remastered 2011)", "Coldplay feat. Someone") != Matching.MatchKey("Yellow", "Coldplay"));
        Assert.True(DjRules.SongChoice(st, "yellow", "COLDPLAY"));
        Assert.Null(DjRules.SongChoice(st, "Fix You", "Coldplay"));
        DjRules.SetSong(st, "Yellow", "Coldplay", null);
        Assert.Empty(st.Songs);
    }

    [Fact]
    public async Task The_choice_round_trips_through_the_settings_json_and_storage_and_a_change_is_followed_at_once()
    {
        var s = new DjSettings();
        Assert.Null(DjJson.Apply(JsonNode.Parse("""{ "song": { "title": "Yellow", "artist": "Coldplay", "choice": "never" } }""")!.AsObject(), s));
        Assert.False(DjRules.SongChoice(s, "Yellow", "Coldplay"));
        var j = DjJson.Settings(s, false);
        Assert.Equal("never", j["songs"]![Matching.MatchKey("Yellow", "Coldplay")]!.GetValue<string>());
        Assert.False(DjRules.SongChoice(DjJson.ReadSettings(j, out _)!, "Yellow", "Coldplay"));
        Assert.Null(DjJson.Apply(JsonNode.Parse("""{ "song": { "title": "Yellow", "artist": "Coldplay", "choice": null } }""")!.AsObject(), s));
        Assert.Empty(s.Songs);
        Assert.NotNull(DjJson.Apply(JsonNode.Parse("""{ "song": { "title": "Yellow", "choice": "sometimes" } }""")!.AsObject(), s));
        Assert.NotNull(DjJson.Apply(JsonNode.Parse("""{ "songs": { "a|b": "maybe" } }""")!.AsObject(), s));
        Assert.Equal("always", DjJson.Patch(new DjSettings(), new DjSettings { Songs = { ["a|b"] = true } })["songs"]!["a|b"]!.GetValue<string>());

        var r = Make(Ok, ready: false); TurnOn(r, x => x.DeviceVoice = true);
        Assert.NotNull(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A)));   // the session's opening line
        Assert.Null(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, C, B)));      // the forced silence after it
        var st = r.Dj.Settings(me); DjRules.SetSong(st, C.Title, C.Artist, true); r.Dj.Save(me, st);
        Assert.True(DjRules.SongChoice(Make(Ok, ready: false).Dj.Settings(me), C.Title, C.Artist));   // kept in the account's file
        Assert.NotNull(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, C, B)));   // asked again: "always" beats the forced silence at once
    }
}
