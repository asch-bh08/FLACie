using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>A clearer DJ: names checked by ear per voice, captions timed to the clip, the music lower under the voice, and the cue sound.</summary>
public class DjClearTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-clear").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    // ---- names checked by ear ----

    /// <summary>A stand-in voice and Whisper: a clip "is" its text, and [Hears] says what Whisper writes down for it.</summary>
    sealed class FakeEar : IDjEar
    {
        public string Voice { get; set; } = "aura-1:helios";
        public bool Ready => true;
        public Func<string, string?> Hears = t => t;
        public double Budget = 1000, Spent;
        public readonly List<(string Text, string Voice)> Said = [];
        public double Cost(string text) => 10;
        public bool Take(double n) { if (Spent + n > Budget) return false; Spent += n; return true; }
        public Task<byte[]?> SayAsync(string text, string voice, CancellationToken ct) { Said.Add((text, voice)); return Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(text)); }
        public Task<string?> HearAsync(byte[] audio, CancellationToken ct) => Task.FromResult(Hears(System.Text.Encoding.UTF8.GetString(audio)));
    }

    DjPronounceCheck Check(FakeEar ear) => new(ear, new DataPaths(dir), new DjVoiceTests.Lines<DjPronounceCheck>());

    [Fact]
    public async Task A_respelling_is_dropped_when_the_plain_name_is_heard_right()
    {
        // "The Prod-ij-ee" comes back as nonsense; "The Prodigy" said plainly comes back right
        var ear = new FakeEar { Hears = t => t == "The Prod-ij-ee" ? "The prod ij ee." : t };
        var sum = await Check(ear).RunAsync([new("The Prodigy", "The Prod-ij-ee")]);
        Assert.Equal(new DjPronounceCheck.Summary(0, 1, 0, 0, false), sum);
        var heard = DjPronounceCheck.Heard(DjPronounceCheck.FileIn(dir), "aura-1:helios");
        Assert.True(heard.ContainsKey("The Prodigy")); Assert.Null(heard["The Prodigy"]);
        // and the DJ then says it as written
        var p = DjPronounce.Load(null, heard);
        Assert.Null(p.Artist("The Prodigy"));
        Assert.Equal("Next up, The Prodigy.", p.Phrases("Next up, The Prodigy."));
    }

    [Fact]
    public async Task A_good_respelling_is_kept_when_the_plain_name_is_heard_wrong()
    {
        var ear = new FakeEar { Hears = t => t switch { "Sade" => "Said.", "Shar-day" => "Sade", _ => t } };
        var sum = await Check(ear).RunAsync([new("Sade", "Shar-day")]);
        Assert.Equal(1, sum!.Kept);
        var heard = DjPronounceCheck.Heard(DjPronounceCheck.FileIn(dir), "aura-1:helios");
        Assert.Equal("Shar-day", heard["Sade"]);
        Assert.Equal("Shar-day", DjPronounce.Load(null, heard).Artist("Sade"));
        Assert.Equal([("Sade", "aura-1:helios"), ("Shar-day", "aura-1:helios")], ear.Said);
    }

    [Fact]
    public async Task The_check_stops_at_its_share_and_carries_on_another_day()
    {
        var ear = new FakeEar { Budget = 45 };   // two names a day (each pair costs 20)
        var items = new List<DjPronounceCheck.Item> { new("Sade", "Shar-day"), new("Moby", "Moe-bee"), new("Rihanna", "Ree-anna") };
        var c = Check(ear);
        var first = await c.RunAsync(items);
        Assert.True(first!.Stopped); Assert.Equal(1, first.Left); Assert.Equal(2, first.Kept + first.Dropped);
        Assert.False(c.UpToDate("aura-1:helios", items));
        ear.Budget = 1000;                       // the next day
        var second = await c.RunAsync(items);
        Assert.Equal(1, second!.Kept + second.Dropped); Assert.False(second.Stopped);
        Assert.True(c.UpToDate("aura-1:helios", items));
        Assert.Equal(new DjPronounceCheck.Summary(0, 0, 0, 0, false), await c.RunAsync(items));   // nothing left to check until the voice or the list changes
    }

    [Fact]
    public async Task Each_voice_keeps_its_own_results_and_a_new_voice_is_checked_again()
    {
        var ear = new FakeEar { Hears = t => t == "Moby" ? "Moby" : "mow bee" };
        var c = Check(ear);
        await c.RunAsync([new("Moby", "Moe-bee")]);
        ear.Voice = "aura-1:athena"; ear.Hears = t => t == "Moby" ? "Mo B" : "Moby";
        await c.RunAsync([new("Moby", "Moe-bee")]);
        var file = DjPronounceCheck.FileIn(dir);
        Assert.Null(DjPronounceCheck.Heard(file, "aura-1:helios")["Moby"]);
        Assert.Equal("Moe-bee", DjPronounceCheck.Heard(file, "aura-1:athena")["Moby"]);
        Assert.Empty(DjPronounceCheck.Heard(file, "melotts"));
        var saved = JsonSerializer.Deserialize<DjPronounceFile>(File.ReadAllText(file))!;
        Assert.Equal(2, saved.Voices.Count);
    }

    [Fact]
    public async Task A_name_that_fails_to_be_heard_is_counted_and_tried_again_later()
    {
        var ear = new FakeEar { Hears = _ => null };
        var c = Check(ear);
        var sum = await c.RunAsync([new("Sade", "Shar-day")]);
        Assert.Equal(1, sum!.Failed);
        Assert.Empty(DjPronounceCheck.Heard(DjPronounceCheck.FileIn(dir), "aura-1:helios"));
        ear.Hears = t => t == "Sade" ? "Sade" : "shard ay";
        Assert.Equal(1, (await c.RunAsync([new("Sade", "Shar-day")]))!.Dropped);
    }

    [Fact]
    public void Hearing_scores_ignore_case_accents_and_full_stops()
    {
        Assert.Equal(1.0, DjHearing.Score("Björk", "bjork."));
        Assert.True(DjHearing.Score("The Prodigy", "The prodigy!") >= DjHearing.Right);
        Assert.True(DjHearing.Score("The Prodigy", "the prod ij ee") < DjHearing.Right);
        Assert.Equal(DjHearing.Verdict.Plain, DjHearing.Choose(0.9, 1.0));    // heard right already: never respelt
        Assert.Equal(DjHearing.Verdict.Respelt, DjHearing.Choose(0.5, 0.9));
        Assert.Equal(DjHearing.Verdict.Plain, DjHearing.Choose(0.5, 0.5));    // no better: the plain name
    }

    [Fact]
    public void The_check_spends_at_most_its_share_of_the_cap_and_counts_it_in_the_day()
    {
        var paths = new DataPaths(dir); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(new DjVoiceTests.Handler(_ => Task.FromResult(DjVoiceTests.Audio()))), dp,
            new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>());
        v.Save(new DjVoiceConfig { AccountId = "0123456789abcdef0123456789abcdef", CapPercent = 50 }, "cf_fake_token");
        Assert.Equal(500, v.CheckCap);           // 10 % of a 5,000-neuron cap
        Assert.True(v.TakeForChecks(400));
        Assert.False(v.TakeForChecks(200));
        Assert.True(v.TakeForChecks(100));
    }

    // ---- captions ----

    [Fact]
    public void A_caption_lasts_the_clip_and_a_long_line_is_split_in_time()
    {
        var one = Assert.Single(DjCaptions.Plan("That was Blur. Here's Pulp.", 3000));
        Assert.Equal((0, 3000 + DjCaptions.LingerMs), (one.AtMs, one.UntilMs));
        const string text = "That was Parklife by Blur, from nineteen ninety four, a proper Britpop classic. Coming up next, something a little calmer from Pulp, to ease you into the evening.";
        var p = DjCaptions.Plan(text, 9000);
        Assert.InRange(p.Count, 2, 3);
        for (var i = 1; i < p.Count; i++) Assert.Equal(p[i - 1].UntilMs, p[i].AtMs);
        Assert.Equal(9000 + DjCaptions.LingerMs, p[^1].UntilMs);
        Assert.Equal(text, string.Join(' ', p.Select(x => x.Text)));
        Assert.Equal(p[0].Text, DjCaptions.At(p, 10)); Assert.Null(DjCaptions.At(p, 20_000));
    }

    sealed class Js : IJSRuntime
    {
        public readonly List<(string Id, object?[] Args)> Calls = [];
        public int? ClipMs = 4000;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            object? a = identifier == "flacie.djClipMs" ? ClipMs : null;
            return ValueTask.FromResult(a is null ? default(T)! : (T)a);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    sealed class FakeDj : IDjService
    {
        public DjSettings S = new() { Enabled = true, TalkAfterSkips = false };
        public DjSettings Settings(UserSession s) => S;
        public void Save(UserSession s, DjSettings settings) => S = settings;
        public Task<DjCue?> PrepareAsync(UserSession s, DjRequest req, CancellationToken ct = default) =>
            Task.FromResult<DjCue?>(new("Here's " + req.Next.Title, "Here's " + req.Next.Title, "/api/dj/clip/x.mp3", false));
        public bool VoiceReady => true;
    }

    sealed class NoPlanner : IAutoplayPlanner
    {
        public Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default) => Task.FromResult(new List<CatalogSong>());
        public IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested) => [];
        public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach) { }
    }

    static List<Track> Songs(int n) => Enumerable.Range(0, n).Select(i => new Track($"/m/{i}.flac", $"Song {i}", "A", "Al", "A", i, 1, 100_000, 2000, null, 0, TrackSource.Jellyfin, $"/m/{i}.flac", i.ToString("x32"))).ToList();

    /// <summary>A player whose caption waits are stepped by hand: each wait is kept with its length until the test lets it go.</summary>
    static (PlayerState P, Js Js, FakeDj Dj, List<(int Ms, TaskCompletionSource Go)> Waits) Player()
    {
        var js = new Js(); var p = new PlayerState(js); var dj = new FakeDj(); p.DjAskLead = null;   // (asked as a song starts, as before the 30 s lead)
        p.SetPlanner(new UserSession("jellyfin", null, null), new NoPlanner());
        p.SetDj(dj, DjSurface.Web);
        var waits = new List<(int, TaskCompletionSource)>();
        p.DjWait = (ms, ct) => { var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); ct.Register(() => t.TrySetCanceled()); lock (waits) waits.Add((ms, t)); return t.Task; };
        return (p, js, dj, waits);
    }

    static async Task Until(Func<bool> ok) { for (var i = 0; i < 200 && !ok(); i++) await Task.Delay(5); Assert.True(ok()); }

    [Fact]
    public async Task A_caption_shows_from_the_clip_start_for_the_clip_length_then_goes()
    {
        var (p, js, dj, waits) = Player();
        dj.S.Cue = false;
        await p.Play(Songs(3), 0);
        await p.OnEnded();                                   // Song 1 by itself: the clip plays
        Assert.Equal("Here's Song 1", p.DjCaption);
        // one wait for the clip's 4 s and the linger after it
        await Until(() => waits.Count == 1);
        Assert.Equal(4000 + DjCaptions.LingerMs, waits[0].Ms);
        waits[0].Go.SetResult();
        await Until(() => p.DjCaption is null);
    }

    [Fact]
    public async Task With_the_cue_on_the_caption_waits_for_the_voice()
    {
        var (p, js, dj, waits) = Player();
        await p.Play(Songs(3), 0);
        await p.OnEnded();
        Assert.Null(p.DjCaption);
        await Until(() => waits.Count == 1);
        Assert.Equal(DjCaptions.CueLeadMs, waits[0].Ms);
        Assert.Contains(js.Calls, c => c.Id == "flacie.djCue" && (bool)c.Args[0]!);
        waits[0].Go.SetResult();
        await Until(() => p.DjCaption == "Here's Song 1");
    }

    [Fact]
    public async Task A_skip_takes_the_caption_away_at_once()
    {
        var (p, _, dj, _) = Player();
        dj.S.Cue = false;
        await p.Play(Songs(4), 0);
        await p.OnEnded();
        Assert.NotNull(p.DjCaption);
        await p.Next();
        Assert.Null(p.DjCaption);
    }

    [Fact]
    public async Task Captions_off_shows_nothing_and_the_cue_switch_reaches_the_page()
    {
        var (p, js, dj, waits) = Player();
        dj.S.Captions = false; dj.S.Cue = false;
        await p.Play(Songs(3), 0);
        await p.OnEnded();
        Assert.Contains(js.Calls, c => c.Id == "flacie.djPlay");
        Assert.Null(p.DjCaption); Assert.Empty(waits);
        Assert.Contains(js.Calls, c => c.Id == "flacie.djCue" && !(bool)c.Args[0]!);
    }

    // ---- the music under the DJ ----

    [Fact]
    public void The_music_sits_lower_by_default_and_the_old_default_moves_across_once()
    {
        Assert.Equal(20, new DjSettings().Duck);
        Assert.True(new DjSettings().Captions); Assert.True(new DjSettings().Cue);
        var old = JsonSerializer.Deserialize<DjSettings>("""{"Enabled":true,"Duck":30}""")!;
        Assert.True(DjSettings.MoveOldDuck(old)); Assert.Equal(20, old.Duck);
        // someone who picked 30 % after the move keeps it
        old.Duck = 30; Assert.False(DjSettings.MoveOldDuck(old)); Assert.Equal(30, old.Duck);
        // someone on their own level is left alone
        var own = JsonSerializer.Deserialize<DjSettings>("""{"Duck":45}""")!;
        DjSettings.MoveOldDuck(own); Assert.Equal(45, own.Duck);
    }

    [Fact]
    public void A_person_still_on_the_old_default_is_moved_and_it_is_saved()
    {
        var paths = new DataPaths(dir); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var me = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };
        var states = new UserStateStore(paths, dp);
        states.For(me).Dj = new DjSettings { Enabled = true, Duck = 30 }; states.Save(me);
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(new DjVoiceTests.Handler(_ => Task.FromResult(DjVoiceTests.Audio()))), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>());
        var dj = new DjService(new UserStateStore(paths, dp), v, new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>()), paths, new DjVoiceTests.Lines<DjService>());
        Assert.Equal(20, dj.Settings(me).Duck);
        var st = dj.Settings(me); st.Duck = 30; dj.Save(me, st);              // chosen on purpose afterwards: kept
        Assert.Equal(30, new DjService(new UserStateStore(paths, dp), v, new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>()), paths, new DjVoiceTests.Lines<DjService>()).Settings(me).Duck);
    }

    [Fact]
    public void Captions_and_cue_go_through_the_settings_json()
    {
        var s = new DjSettings { Captions = false, Cue = false };
        var o = DjJson.Settings(s, true);
        Assert.False(o["captions"]!.GetValue<bool>()); Assert.False(o["cue"]!.GetValue<bool>());
        var back = new DjSettings();
        Assert.Null(DjJson.Apply(new System.Text.Json.Nodes.JsonObject { ["captions"] = false, ["cue"] = true }, back));
        Assert.False(back.Captions); Assert.True(back.Cue);
        Assert.Equal("{\"captions\":false}", DjJson.Patch(new DjSettings(), new DjSettings { Captions = false }).ToJsonString());
    }

    // ---- talk before the song (owner, 27 Oct: "make it so he speaks before the song even plays") ----

    [Fact]
    public void Before_goes_through_the_settings_json_and_is_off_unless_chosen()
    {
        Assert.False(new DjSettings().Before);
        Assert.True(DjJson.Settings(new DjSettings { Before = true }, true)["before"]!.GetValue<bool>());
        var back = new DjSettings();
        Assert.Null(DjJson.Apply(new System.Text.Json.Nodes.JsonObject { ["before"] = true }, back));
        Assert.True(back.Before);
        Assert.Equal("{\"before\":true}", DjJson.Patch(new DjSettings(), new DjSettings { Before = true }).ToJsonString());
    }

    [Fact]
    public async Task With_before_on_the_song_waits_for_the_whole_line()
    {
        var (p, js, dj, _) = Player();
        dj.S.Cue = false; dj.S.Before = true;
        await p.Play(Songs(3), 0);
        await p.OnEnded();
        var play = Assert.Single(js.Calls, c => c.Id == "flacie.djPlay");
        // the clip at once, and the song held back (silent) for the clip's 4 s and the margin, then from its start
        Assert.Equal(0, (int)play.Args[3]!);
        Assert.Equal(4000 + DjTiming.MarginMs, (int)play.Args[4]!);
    }
}
