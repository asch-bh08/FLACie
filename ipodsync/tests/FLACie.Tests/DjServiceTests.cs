using System.Net;
using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>The server's DJ (FLACie.Server/Dj/DjService): settings kept per account, one decision per song change, clips made in the background, and the backup voice.</summary>
public class DjServiceTests : IDisposable
{
    const string Account = "0123456789abcdef0123456789abcdef";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-service").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    DateTime now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    readonly UserSession me = new("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };

    sealed record Rig(DjService Dj, CloudflareVoice Voice, DjClips Clips, DjVoiceTests.Handler Http, UserStateStore States);

    Rig Make(Func<HttpRequestMessage, Task<HttpResponseMessage>>? f = null, bool voice = true, int cap = 50)
    {
        var paths = new DataPaths(dir); var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        var h = new DjVoiceTests.Handler(f ?? (_ => Task.FromResult(DjVoiceTests.Audio())));
        var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(h), dp, new ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => now);
        if (voice) v.Save(new DjVoiceConfig { AccountId = Account, CapPercent = cap, Voice = CloudflareVoice.MeloTts }, "cf_fake_token");
        var clips = new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>(), () => now);
        var states = new UserStateStore(paths, dp);
        return new(new DjService(states, v, clips, paths, new DjVoiceTests.Lines<DjService>(), () => now), v, clips, h, states);
    }

    void TurnOn(Rig r, bool device = false) { var st = r.Dj.Settings(me); st.Enabled = true; st.DeviceVoice = device; r.Dj.Save(me, st); }

    static DjSong Song(string title, string artist, string path) => new(title, artist, "", "", 0, "", 200_000, path);
    static readonly DjSong A = Song("Wonderwall", "Oasis", "/m/a.flac"), B = Song("Yellow", "Coldplay", "/m/b.flac"), C = Song("Breathe", "The Prodigy", "/m/c.flac");

    static string IdOf(string url) => url["/api/dj/clip/".Length..^".mp3".Length];

    [Fact]
    public void Settings_are_kept_per_account_and_come_back_after_a_restart()
    {
        var r = Make();
        var st = r.Dj.Settings(me);
        Assert.False(st.Enabled); Assert.Equal(DjChatty.Often, st.Chatty); Assert.False(st.DeviceVoice); Assert.Equal(90, st.Volume); Assert.Equal(20, st.Duck); Assert.Empty(st.Songs);
        st.Enabled = true; st.Chatty = DjChatty.Rare; st.Web = false; st.Playlists["bedtime"] = false;
        Assert.False(r.Dj.Settings(me).Enabled);   // a copy: nothing changes until it is saved
        r.Dj.Save(me, st);
        var again = Make().Dj.Settings(me);         // a new store reads the account's file
        Assert.True(again.Enabled); Assert.Equal(DjChatty.Rare, again.Chatty); Assert.False(again.Web); Assert.True(again.Phone);
        Assert.False(again.Playlists["bedtime"]);
    }

    [Fact]
    public async Task Asking_again_about_the_same_songs_gives_the_same_answer_and_moves_the_memory_once()
    {
        var r = Make(voice: false); TurnOn(r, device: true);
        var first = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));   // the session's first song change: Often opens
        Assert.NotNull(first);
        // planning again would be the second change straight after a line, which is always silent; the kept decision says the same line
        for (var i = 0; i < 3; i++) Assert.Equal(first, await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Phone, B, A)));
        Assert.Null(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, C, B)));   // the real next change: the forced silence after a line
    }

    [Fact]
    public async Task Off_or_this_queue_off_is_silent_even_for_a_decision_already_made()
    {
        var r = Make(voice: false);
        Assert.Null(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A)));
        TurnOn(r, device: true);
        Assert.NotNull(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A)));
        Assert.Null(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A, QueueDj: false)));
        var st = r.Dj.Settings(me); st.Web = false; r.Dj.Save(me, st);
        Assert.Null(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A)));
    }

    [Fact]
    public async Task Without_a_voice_the_device_says_it_only_when_the_device_voice_is_chosen_and_otherwise_nothing_is_said()
    {
        var r = Make(voice: false); TurnOn(r, device: true);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.NotNull(cue); Assert.Null(cue.ClipUrl); Assert.True(cue.Backup);
        Assert.False(r.Dj.VoiceReady);
        var quiet = Make(voice: false); TurnOn(quiet);
        Assert.Null(await quiet.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, C, B)));
        Assert.Empty(r.Http.Seen);
    }

    [Fact]
    public async Task With_a_voice_the_clip_is_made_in_the_background_once_and_served_from_the_cache()
    {
        var r = Make(); TurnOn(r);
        Assert.True(r.Dj.VoiceReady);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Phone, B, A));
        Assert.NotNull(cue?.ClipUrl);
        Assert.Matches("^/api/dj/clip/[0-9a-f]{32}\\.mp3$", cue.ClipUrl);
        var path = await r.Clips.WaitAsync(IdOf(cue.ClipUrl), TimeSpan.FromSeconds(10));
        Assert.NotNull(path);
        Assert.Equal(Path.Combine(dir, "dj-clips"), Path.GetDirectoryName(path));
        Assert.Equal(DjVoiceTests.Audio().Content.ReadAsByteArrayAsync().Result, File.ReadAllBytes(path));
        var (_, _, body) = Assert.Single(r.Http.Seen);
        Assert.Equal(cue.Text, JsonDocument.Parse(body).RootElement.GetProperty("prompt").GetString());   // the spoken form goes to Cloudflare
        Assert.Equal(cue, await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Phone, B, A)));
        Assert.Equal(IdOf(cue.ClipUrl), r.Clips.Request(cue.Text));   // the same words in the same voice: the file already there
        Assert.Single(r.Http.Seen);
        Assert.Null(await r.Clips.WaitAsync("not-an-id", TimeSpan.FromSeconds(1)));
        Assert.Null(await r.Clips.WaitAsync(new string('a', 32), TimeSpan.FromSeconds(1)));   // unknown: 404
    }

    [Fact]
    public async Task A_clip_that_failed_is_never_said_by_the_device_voice_the_dj_stays_quiet()
    {
        var r = Make(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))); TurnOn(r);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.NotNull(cue?.ClipUrl);   // asked for: nobody knows yet that it will fail
        Assert.Null(await r.Clips.WaitAsync(IdOf(cue.ClipUrl), TimeSpan.FromSeconds(10)));
        var again = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.Null(again);   // was: no clip and Backup, so the device's own voice said it (a second voice)
        Assert.Equal(2, r.Http.Seen.Count);   // the call and its one retry; then the failure is remembered: no third call
    }

    [Fact]
    public async Task After_three_lines_in_a_row_fail_new_lines_get_no_clip_for_thirty_seconds_unless_a_clip_is_made_or_the_voice_is_saved()
    {
        var fail = true;
        var r = Make(_ => Task.FromResult(fail ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : DjVoiceTests.Audio())); TurnOn(r);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.NotNull(cue?.ClipUrl);                                    // handed out before anything failed
        Assert.Null(await r.Clips.WaitAsync(IdOf(cue.ClipUrl), TimeSpan.FromSeconds(10)));   // the token is refused: the clip's address answers 404
        Assert.False(r.Voice.Failing);                                   // one line failed: only that line goes without
        Assert.True(r.Dj.VoiceReady);
        foreach (var t in new[] { "Second line.", "Third line." }) Assert.Null(await r.Clips.WaitAsync(r.Clips.Request(t)!, TimeSpan.FromSeconds(10)));
        Assert.True(r.Voice.Failing);                                    // three in a row: a short rest
        // a fresh line (another person's first song change): no clip at all, quiet (never the device's voice), and Cloudflare is not asked again
        var other = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u2", "other", "tok2"), null) { Id = "u2" };
        var st = r.Dj.Settings(other); st.Enabled = true; r.Dj.Save(other, st);
        Assert.Null(await r.Dj.PrepareAsync(other, new DjRequest(DjSurface.Web, B, A)));
        Assert.Null(r.Clips.Request("A line never said before."));
        Assert.Equal(3, r.Http.Seen.Count);                              // a refused token is not asked twice for one line
        // thirty seconds on it is worth trying again (and that fails again)
        now = now.AddSeconds(30);
        Assert.False(r.Voice.Failing);
        var id = r.Clips.Request("A line never said before.");
        Assert.NotNull(id);
        Assert.Null(await r.Clips.WaitAsync(id, TimeSpan.FromSeconds(10)));
        Assert.True(r.Voice.Failing);
        // saving the voice (a new token) clears it at once
        r.Voice.Save(r.Voice.Current, "cf_other_fake_token");
        Assert.False(r.Voice.Failing);
        // and so does a clip made (the admin's Test button once Cloudflare works again)
        foreach (var t in new[] { "A third line.", "A fifth line.", "A sixth line." }) Assert.Null(await r.Clips.WaitAsync(r.Clips.Request(t)!, TimeSpan.FromSeconds(10)));
        Assert.True(r.Voice.Failing);
        fail = false;
        var (made, _) = await r.Clips.MakeNowAsync("A test line.");
        Assert.NotNull(made); Assert.False(r.Voice.Failing);
        Assert.NotNull(r.Clips.Request("A fourth line."));
    }

    [Fact]
    public void Playlist_ids_that_are_the_same_once_trimmed_are_saved_as_one_and_the_last_wins()
    {
        var r = Make(voice: false);
        var st = r.Dj.Settings(me);
        st.Playlists["bedtime"] = true; st.Playlists[" bedtime "] = false; st.Playlists["  "] = true;
        r.Dj.Save(me, st);                                               // was a 500: ToDictionary threw on the second "bedtime"
        var back = r.Dj.Settings(me).Playlists;
        Assert.Equal(["bedtime"], back.Keys);
        Assert.False(back["bedtime"]);
    }

    [Fact]
    public async Task Over_the_daily_cap_there_is_no_clip_and_no_call()
    {
        var r = Make(cap: 0); TurnOn(r);
        Assert.Null(await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A)));
        Assert.Empty(r.Http.Seen);
    }

    [Fact]
    public async Task The_test_button_says_the_test_line_as_the_dj_says_FLACie()
    {
        var off = Make(voice: false);
        var (none, why) = await off.Dj.TestAsync();
        Assert.Null(none); Assert.Contains("account id", why);
        var r = Make();
        var (clip, _) = await r.Dj.TestAsync();
        Assert.NotNull(clip);
        Assert.NotNull(await r.Clips.WaitAsync(IdOf(clip), TimeSpan.FromSeconds(1)));
        var prompt = JsonDocument.Parse(Assert.Single(r.Http.Seen).Body).RootElement.GetProperty("prompt").GetString();
        Assert.Equal("This is the Flacky DJ. Here's a test line.", prompt);
        await r.Dj.TestAsync();
        Assert.Equal(2, r.Http.Seen.Count);   // a test always asks Cloudflare, even for a clip already made
    }

    [Fact]
    public async Task The_servers_pronounce_file_is_used_for_the_spoken_line()
    {
        File.WriteAllText(Path.Combine(dir, "dj-pronounce.tsv"), "Coldplay\tartist\tCold Play\n");
        var r = Make(voice: false); TurnOn(r, device: true);
        var cue = await r.Dj.PrepareAsync(me, new DjRequest(DjSurface.Web, B, A));
        Assert.Contains("Cold Play", cue!.Text);
        Assert.Contains("Coldplay", cue.Caption);
    }

    [Fact]
    public void Clip_ids_are_a_hash_of_the_voice_and_the_words()
    {
        var a = DjClips.IdOf("melotts", "Here's Yellow by Coldplay.");
        Assert.True(DjClips.IsId(a));
        Assert.Equal(a, DjClips.IdOf("MeloTTS", "Here's Yellow by Coldplay."));
        Assert.NotEqual(a, DjClips.IdOf("aura-1:luna", "Here's Yellow by Coldplay."));
        Assert.NotEqual(a, DjClips.IdOf("melotts", "Here's Yellow by Coldplay"));
        Assert.False(DjClips.IsId("../../keys/key.xml"));
    }
}
