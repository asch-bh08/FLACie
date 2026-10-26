using System.Net;
using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The DJ went quiet on the web after "one voice" (frank, 2026-10-26 21:58): one Aura call in the pool warm-up answered 500, <see cref="CloudflareVoice.Failing"/>
/// then held for ten minutes, so every line written after it got no clip and, with one voice, nothing was said (dj-usage: 21 clips, 2 said). Now a 5xx is
/// asked again once, a failure costs only that line, and a line planned but not said is logged with the reason. Also "Every song".
/// </summary>
public class DjSilentTests : IDisposable
{
    const string Account = "0123456789abcdef0123456789abcdef";
    const string Aura = "aura-1:helios";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-silent").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    DateTime now = new(2026, 10, 26, 21, 58, 0, DateTimeKind.Utc);

    readonly UserSession me = new("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };

    sealed record Rig(DjService Dj, CloudflareVoice Voice, DjClips Clips, DjVoiceTests.Handler Http, DjVoiceTests.Lines<DjService> Log, DjVoiceTests.Lines<DjClips> ClipLog);

    Rig Make(Func<HttpRequestMessage, Task<HttpResponseMessage>> f, int cap = 50, bool save = true)
    {
        var paths = new DataPaths(dir); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var h = new DjVoiceTests.Handler(f);
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(h), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => now)
            { RetryPause = TimeSpan.FromMilliseconds(20) };
        if (save) v.Save(new DjVoiceConfig { AccountId = Account, CapPercent = cap, Voice = Aura, AiLines = false }, "cf_fake_token");
        var clipLog = new DjVoiceTests.Lines<DjClips>(); var log = new DjVoiceTests.Lines<DjService>();
        var clips = new DjClips(paths, v, clipLog, () => now);
        return new(new DjService(new UserStateStore(paths, dp), v, clips, paths, log, () => now), v, clips, h, log, clipLog);
    }

    static Task<HttpResponseMessage> Ok() => Task.FromResult(DjVoiceTests.Audio());
    static Task<HttpResponseMessage> Error500() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { ReasonPhrase = "Internal Server Error" });

    void TurnOn(Rig r, Action<DjSettings>? more = null) { var st = r.Dj.Settings(me); st.Enabled = true; more?.Invoke(st); r.Dj.Save(me, st); }

    static DjSong Song(string title, string artist, string path, long ms = 200_000, string genre = "", string album = "", int track = 0) =>
        new(title, artist, album, "", 0, genre, ms, path, null, track);
    static readonly DjSong A = Song("Wonderwall", "Oasis", "/m/a.flac"), B = Song("Yellow", "Coldplay", "/m/b.flac"), C = Song("Breathe", "The Prodigy", "/m/c.flac");

    // ---- one 500 must not silence the DJ ----

    [Fact]
    public async Task A_500_then_success_the_line_is_said_after_one_retry()
    {
        var calls = 0;
        var r = Make(_ => ++calls == 1 ? Error500() : Ok()); TurnOn(r);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.NotNull(cue?.ClipUrl);
        Assert.NotNull(await r.Clips.WaitAsync(cue.ClipUrl[13..^4], TimeSpan.FromSeconds(10)));   // the clip is there: the line is said
        Assert.Equal(2, r.Http.Seen.Count);                                                       // asked once more, in the same voice
        Assert.All(r.Http.Seen, s => Assert.Equal("helios", JsonDocument.Parse(s.Body).RootElement.GetProperty("speaker").GetString()));
        Assert.False(r.Voice.Failing);
    }

    [Fact]
    public async Task Silence_reproduced_a_500_on_one_line_does_not_stop_the_next_line_being_voiced()
    {
        // frank 21:58:14: the warm-up's Aura call answered 500; every line written after it went without a clip for ten minutes
        var fail = true;
        var r = Make(_ => fail ? Error500() : Ok()); TurnOn(r);
        Assert.Null(await r.Clips.WaitAsync(r.Clips.Request("Coming up,")!, TimeSpan.FromSeconds(10)));
        Assert.Equal(2, r.Http.Seen.Count);                     // the call and its one retry
        Assert.False(r.Voice.Failing);                          // was: true for ten minutes
        fail = false;
        now = now.AddSeconds(5);                                // the next song, a moment later
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.NotNull(cue?.ClipUrl);                           // was: null, so nothing was said
        Assert.NotNull(await r.Clips.WaitAsync(cue.ClipUrl[13..^4], TimeSpan.FromSeconds(10)));
        Assert.Equal(1, r.Voice.SoFar.Said);
    }

    [Fact]
    public async Task Only_three_failed_lines_in_a_row_rest_the_voice_and_only_for_thirty_seconds_with_no_other_voice()
    {
        var fail = true;
        var r = Make(_ => fail ? Error500() : Ok());
        for (var i = 0; i < CloudflareVoice.FailuresInARow; i++)
        {
            Assert.False(r.Voice.Failing);
            Assert.Null(await r.Clips.WaitAsync(r.Clips.Request("Line " + i)!, TimeSpan.FromSeconds(10)));
        }
        Assert.True(r.Voice.Failing);
        Assert.Null(r.Clips.Request("Another line."));
        now = now.AddSeconds(31);
        Assert.False(r.Voice.Failing);
        fail = false;
        Assert.NotNull(await r.Clips.WaitAsync(r.Clips.Request("Another line.")!, TimeSpan.FromSeconds(10)));
        Assert.DoesNotContain(r.Http.Seen, s => s.Url.Contains("melotts"));
    }

    [Fact]
    public async Task A_refused_token_is_not_asked_again_for_the_same_line()
    {
        var r = Make(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        Assert.Null(await r.Clips.WaitAsync(r.Clips.Request("A line.")!, TimeSpan.FromSeconds(10)));
        Assert.Single(r.Http.Seen);
    }

    // ---- the voice is the same on disk, in memory and in the clip ids ----

    [Fact]
    public void An_old_dj_json_saying_melotts_moves_to_Aura_on_disk_once_and_the_ids_follow()
    {
        File.WriteAllText(Path.Combine(dir, "dj.json"), """{"AccountId":"abc","Token":"","Voice":"melotts","CapPercent":50}""");
        var r = Make(_ => Ok(), save: false);
        Assert.Equal(CloudflareVoice.DefaultVoice, r.Voice.Current.Voice);
        var disk = JsonSerializer.Deserialize<DjVoiceConfig>(File.ReadAllText(Path.Combine(dir, "dj.json")))!;
        Assert.Equal(CloudflareVoice.DefaultVoice, disk.Voice); Assert.Equal(CloudflareVoice.ConfigVersion, disk.Version);
        Assert.Equal("abc", disk.AccountId);
        Assert.EndsWith(DjClips.IdOf(CloudflareVoice.DefaultVoice, "Coming up,") + ".mp3", r.Clips.PathOf("Coming up,"));
        // MeloTTS chosen on the dashboard since (Version 2) stays MeloTTS
        var picked = r.Voice.Current; picked.Voice = CloudflareVoice.MeloTts; r.Voice.Save(picked, null);
        var again = Make(_ => Ok(), save: false);
        Assert.Equal(CloudflareVoice.MeloTts, again.Voice.Current.Voice);
        Assert.Contains("\"melotts\"", File.ReadAllText(Path.Combine(dir, "dj.json")));
    }

    // ---- not said: the reason is logged ----

    [Fact]
    public async Task A_line_planned_but_not_said_is_logged_with_the_reason()
    {
        var r = Make(_ => Ok(), cap: 0); TurnOn(r);
        Assert.Null(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A)));
        Assert.Contains(r.Log.All, l => l.StartsWith("DJ line not said before Yellow: today's cap"));
        await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));                       // asked again: logged once
        Assert.Single(r.Log.All, l => l.StartsWith("DJ line not said"));

        var f = Make(_ => Error500());
        Assert.Null(await f.Clips.WaitAsync(f.Clips.Request("A line.")!, TimeSpan.FromSeconds(10)));
        Assert.Contains(f.ClipLog.All, l => l.StartsWith("DJ line not said: no clip, the voice failed (Cloudflare answered 500"));

        r.Dj.NotSaid(me, "Breathe", "timing: the DJ's answer had not come when the song started");
        Assert.Contains("DJ line not said before Breathe: timing: the DJ's answer had not come when the song started", r.Log.All);
    }

    // ---- every song ----

    static DjSettings Every(Action<DjSettings>? more = null) { var s = new DjSettings { Enabled = true, Chatty = DjChatty.Every }; more?.Invoke(s); return s; }

    [Fact]
    public void Every_song_puts_a_line_before_each_eligible_song()
    {
        var mem = new DjMemory(); var t = now;
        var songs = Enumerable.Range(0, 30).Select(i => Song("Song " + (char)('A' + i % 26) + i, "Artist " + i, $"/m/{i}.flac", album: "Same Album", track: i + 1)).ToList();
        for (var i = 1; i < songs.Count; i++)
        {
            t = t.AddMinutes(3);
            var line = DjPlanner.Plan(Every(), new DjRequest(DjSurface.Web, songs[i], songs[i - 1]), mem, t, new Random(i));
            Assert.NotNull(line);   // also the next track of the same album
        }
    }

    [Fact]
    public void Every_song_still_keeps_quiet_on_never_short_songs_and_spoken_word_and_skips_follow_talk_after_skips()
    {
        DjLine? Plan(DjSettings s, DjSong next, bool skipped = false) =>
            DjPlanner.Plan(s, new DjRequest(DjSurface.Web, next, A, PrevPlayedOut: !skipped, SkippedRecently: skipped), new DjMemory(), now, new Random(1));
        Assert.NotNull(Plan(Every(), B));
        Assert.Null(Plan(Every(s => DjRules.SetSong(s, B.Title, B.Artist, false)), B));
        Assert.Null(Plan(Every(), Song("Intro", "Coldplay", "/m/i.flac", ms: 45_000)));
        Assert.Null(Plan(Every(), Song("Chapter 1", "A Reader", "/m/s.flac", genre: "Audiobook")));
        Assert.NotNull(Plan(Every(), C, skipped: true));                                   // Talk after skips is on by default
        Assert.Null(Plan(Every(s => s.TalkAfterSkips = false), C, skipped: true));
    }

    [Fact]
    public void Every_song_goes_through_the_settings_json_and_the_budget_line()
    {
        var s = new DjSettings();
        Assert.Null(DjJson.Apply(System.Text.Json.Nodes.JsonNode.Parse("""{ "chatty": "every" }""")!.AsObject(), s));
        Assert.Equal(DjChatty.Every, s.Chatty);
        Assert.Equal("every", DjJson.Settings(s, true)["chatty"]!.GetValue<string>());
        Assert.Equal(2.0, DjBudget.HoursOfEverySong(DjBudget.SongsPerHour * 2));
    }
}
