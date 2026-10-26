using FLACie.Core;
using FLACie.Server;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>The web player's side of the AI DJ (docs/spec/ai-dj.md): what it asks about, when the clip plays (only when the player moves on by itself,
/// to the very song asked about), when it stops, the "this queue" switch, and the playlist and Where switches as the pages change them.</summary>
public class PlayerDjTests
{
    sealed class Js : IJSRuntime
    {
        public readonly List<(string Id, object?[] Args)> Calls = [];
        /// <summary>What the page answers, by function (anything else answers the default).</summary>
        public readonly Dictionary<string, object?> Answers = new();
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult(Answers.TryGetValue(identifier, out var a) ? (T)a! : default(T)!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
        public string[] Of(string id) => Calls.Where(c => c.Id == id).Select(c => c.Args.ElementAtOrDefault(0) as string ?? "").ToArray();
        public void Clear() => Calls.Clear();
    }

    /// <summary>A stand-in DJ: answers every ask with a clip named after the next song, unless told otherwise; [Hold] keeps the answers back until released.</summary>
    sealed class FakeDj : IDjService
    {
        public DjSettings S = new() { Enabled = true };
        public readonly List<DjRequest> Asked = [];
        public Func<DjRequest, DjCue?> Answer = r => new("Here's " + r.Next.Title, "Here's " + r.Next.Title, "/api/dj/clip/" + r.Next.Title.Replace(' ', '-') + ".mp3", true);
        public bool Hold;
        public readonly List<(DjRequest Req, TaskCompletionSource<DjCue?> Tcs)> Held = [];
        public DjSettings Settings(UserSession s) => S;
        public void Save(UserSession s, DjSettings settings) => S = settings;
        public Task<DjCue?> PrepareAsync(UserSession s, DjRequest req, CancellationToken ct = default)
        {
            Asked.Add(req);
            if (!Hold) return Task.FromResult(Answer(req));
            var tcs = new TaskCompletionSource<DjCue?>(); Held.Add((req, tcs)); return tcs.Task;
        }
        public bool VoiceReady => true;
    }

    sealed class NoPlanner : IAutoplayPlanner
    {
        public Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default) => Task.FromResult(new List<CatalogSong>());
        public IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested) => [];
        public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach) { }
    }

    static Track T(int n) => new($"/m/{n}.flac", $"Song {n}", "A", "Al", "A", n, 1, 100_000, 2000, null, 0, TrackSource.Jellyfin, $"/m/{n}.flac", n.ToString("x32"));
    static List<Track> Songs(int n) => Enumerable.Range(0, n).Select(T).ToList();

    static (PlayerState P, Js Js, FakeDj Dj) Player(DjSurface surface = DjSurface.Web)
    {
        var js = new Js(); var p = new PlayerState(js); var dj = new FakeDj();
        p.SetPlanner(new UserSession("jellyfin", null, null), new NoPlanner());
        p.SetDj(dj, surface);
        return (p, js, dj);
    }

    [Fact]
    public async Task When_a_song_starts_the_DJ_is_asked_about_the_next_one_and_its_clip_is_fetched_ahead()
    {
        var (p, js, dj) = Player();
        await p.Play(Songs(4), 0);
        var r = Assert.Single(dj.Asked);
        Assert.Equal("Song 1", r.Next.Title); Assert.Equal("Song 0", r.Prev?.Title);
        Assert.Equal(DjSurface.Web, r.Surface); Assert.True(r.PrevPlayedOut); Assert.False(r.SkippedRecently); Assert.Null(r.QueueDj); Assert.Null(r.PlaylistId);
        Assert.InRange(r.LocalHour ?? -1, 0, 23); Assert.InRange(r.Weekday ?? -1, 0, 6);
        Assert.Equal("/m/1.flac", r.Next.Path); Assert.Equal(1.ToString("x32"), r.Next.JellyfinId);
        Assert.Equal(["/api/dj/clip/Song-1.mp3"], js.Of("flacie.djPreload"));
        Assert.Empty(js.Of("flacie.djPlay"));
    }

    [Fact]
    public async Task The_clip_plays_only_when_the_player_moves_on_by_itself()
    {
        var (p, js, dj) = Player();
        dj.S.TalkAfterSkips = false;                         // (with it on, a skip may bring a line too: PlayerDjLiveTests)
        await p.Play(Songs(4), 0);
        await p.OnEnded();                                   // Song 0 ended: Song 1 starts by itself
        Assert.Equal("Song 1", p.Current?.Title);
        Assert.Equal(["/api/dj/clip/Song-1.mp3"], js.Of("flacie.djPlay"));
        js.Clear();
        await p.Next();                                      // a skip: no clip for Song 2, and the clip still talking over Song 1 stops
        Assert.Equal("Song 2", p.Current?.Title);
        Assert.Empty(js.Of("flacie.djPlay"));
        Assert.Single(js.Calls, c => c.Id == "flacie.djStop");
        js.Clear();
        await p.Prev();                                      // Previous: never a clip
        await p.SkipTo(3);                                   // a song picked in the queue: never a clip
        Assert.Empty(js.Of("flacie.djPlay"));
    }

    [Fact]
    public async Task A_new_queue_never_starts_with_a_clip_and_play_counting_sees_only_songs()
    {
        var (p, js, dj) = Player();
        var started = new List<string>();
        p.Started += t => started.Add(t.Title);
        await p.Play(Songs(3), 0);
        await p.Play(Songs(3), 1);                           // a new queue on the song that was asked about: still no clip
        Assert.Empty(js.Of("flacie.djPlay"));
        await p.OnEnded();
        Assert.Single(js.Of("flacie.djPlay"));
        Assert.Equal(["Song 0", "Song 1", "Song 2"], started);   // the clip is never a song that started
    }

    [Fact]
    public async Task An_answer_about_another_song_is_never_played()
    {
        var (p, js, dj) = Player();
        dj.Hold = true;
        await p.Play(Songs(4), 0);                           // asks about Song 1 (held)
        p.MoveUpNext(3, QueueEdit.Step.Top);                 // Song 3 is next now: asked about instead
        Assert.Equal(["Song 1", "Song 3"], dj.Asked.Select(r => r.Next.Title));
        dj.Held[0].Tcs.SetResult(new("Here's Song 1", "", "/api/dj/clip/Song-1.mp3", true));   // the late answer about Song 1 is dropped
        Assert.Empty(js.Of("flacie.djPreload"));
        dj.Held[1].Tcs.SetResult(new("Here's Song 3", "", "/api/dj/clip/Song-3.mp3", true));
        Assert.Equal(["/api/dj/clip/Song-3.mp3"], js.Of("flacie.djPreload"));
        await p.OnEnded();
        Assert.Equal("Song 3", p.Current?.Title);
        Assert.Equal(["/api/dj/clip/Song-3.mp3"], js.Of("flacie.djPlay"));
    }

    [Fact]
    public async Task An_answer_still_on_its_way_when_the_song_starts_is_not_played_late()
    {
        var (p, js, dj) = Player();
        dj.Hold = true;
        await p.Play(Songs(3), 0);
        await p.OnEnded();                                   // Song 1 started before the DJ answered
        dj.Held[0].Tcs.SetResult(new("Here's Song 1", "", "/api/dj/clip/Song-1.mp3", true));
        Assert.Empty(js.Of("flacie.djPlay"));
        Assert.Empty(js.Of("flacie.djPreload"));
    }

    [Fact]
    public async Task Without_a_clip_the_device_says_the_line_when_backup_is_on_and_nothing_otherwise()
    {
        var (p, js, dj) = Player();
        dj.Answer = r => new("That was Song 0. Here's " + r.Next.Title, "", null, Backup: r.Next.Title == "Song 1");
        await p.Play(Songs(4), 0);
        Assert.Empty(js.Of("flacie.djPreload"));
        await p.OnEnded();
        Assert.Equal(["That was Song 0. Here's Song 1"], js.Of("flacie.djSay"));
        await p.OnEnded();                                   // Song 2: no clip and no backup: quiet
        Assert.Single(js.Of("flacie.djSay"));
        Assert.Empty(js.Of("flacie.djPlay"));
    }

    [Fact]
    public async Task A_pause_or_a_seek_stops_the_DJ()
    {
        var (p, js, dj) = Player();
        await p.Play(Songs(4), 0);
        p.OnState(true);
        await p.OnEnded(); p.OnState(true);
        js.Clear();
        await p.Toggle();                                    // pause
        Assert.Single(js.Calls, c => c.Id == "flacie.djStop");
        await p.OnEnded(); p.OnState(true);
        js.Clear();
        await p.Seek(20);
        Assert.Single(js.Calls, c => c.Id == "flacie.djStop");
    }

    [Fact]
    public async Task The_queue_switch_flips_the_DJ_for_this_queue_and_a_new_queue_starts_untouched()
    {
        var (p, js, dj) = Player();
        await p.Play(Songs(4), 0);
        Assert.True(p.DjOn); Assert.Null(p.QueueDj);
        await p.ToggleDjAsync();
        Assert.False(p.QueueDj); Assert.False(p.DjOn);
        await p.OnEnded();
        Assert.Empty(js.Of("flacie.djPlay"));                // off for this queue: the answer kept is not played
        var asks = dj.Asked.Count;
        await p.OnEnded();
        Assert.Equal(asks, dj.Asked.Count);                  // and nothing more is asked while it is off
        await p.ToggleDjAsync();
        Assert.True(p.QueueDj); Assert.True(p.DjOn);
        Assert.True(dj.Asked[^1].QueueDj);                   // turning it back on asks about the next song at once
        await p.Play(Songs(4), 0);
        Assert.Null(p.QueueDj);                              // a new queue: untouched again
    }

    [Fact]
    public async Task The_queue_switch_turns_the_DJ_on_even_when_settings_say_off()
    {
        var (p, js, dj) = Player();
        dj.S.Enabled = false;
        await p.Play(Songs(3), 0);
        Assert.False(p.DjOn); Assert.Empty(dj.Asked);
        await p.ToggleDjAsync();
        Assert.True(p.DjOn); Assert.Single(dj.Asked);
        await p.OnEnded();
        Assert.Single(js.Of("flacie.djPlay"));
    }

    [Fact]
    public async Task A_playlist_switch_counts_for_what_plays_from_that_playlist()
    {
        var (p, js, dj) = Player();
        dj.S.Enabled = false; dj.S.Playlists["jf-pl"] = true;
        await p.Play(Songs(3), 0, "Bedtime", "jf-pl");
        Assert.Equal("jf-pl", p.ContextId); Assert.True(p.DjOn);
        Assert.Equal("jf-pl", Assert.Single(dj.Asked).PlaylistId);
        await p.Play(Songs(3), 0, "Songs");                  // not from the playlist: settings say off
        Assert.Null(p.ContextId); Assert.False(p.DjOn);
        dj.S.Enabled = true; dj.S.Playlists["quiet"] = false;
        await p.ShuffleAll(Songs(3), "Quiet one", "quiet");
        Assert.False(p.DjOn);
    }

    [Fact]
    public async Task The_Where_tick_of_this_device_counts()
    {
        var (p, _, dj) = Player(DjSurface.Windows);
        dj.S.Web = true; dj.S.Windows = false;
        await p.Play(Songs(3), 0);
        Assert.False(p.DjOn); Assert.Empty(dj.Asked);
        dj.S.Windows = true;
        await p.DjRefreshAsync();
        Assert.True(p.DjOn); Assert.Equal(DjSurface.Windows, Assert.Single(dj.Asked).Surface);
    }

    [Fact]
    public async Task A_skip_in_the_first_10_seconds_counts_as_skipped_recently_for_two_changes()
    {
        var (p, _, dj) = Player();
        await p.Play(Songs(6), 0);
        await p.Next();                                      // Song 0 skipped at 0 s
        Assert.True(dj.Asked[^1].SkippedRecently);
        p.OnTime(40, 100);
        await p.OnEnded();                                   // Song 1 played to its end; the skip is still one of the last two changes
        Assert.True(dj.Asked[^1].SkippedRecently);
        await p.OnEnded();
        Assert.False(dj.Asked[^1].SkippedRecently);
        p.OnTime(30, 100);
        await p.Next();                                      // a skip after 30 s is not "skipped recently"
        Assert.False(dj.Asked[^1].SkippedRecently);
    }

    [Fact]
    public async Task Nothing_is_asked_on_repeat_one_or_at_the_end_of_the_queue()
    {
        var (p, _, dj) = Player();
        await p.Play(Songs(2), 1);
        Assert.Empty(dj.Asked);                              // the last song, and no Autoplay songs to come
        await p.Play(Songs(3), 0);
        p.CycleRepeat(); p.CycleRepeat();                    // repeat one
        Assert.Equal(2, p.Repeat);
        var asks = dj.Asked.Count;
        await p.OnEnded();
        Assert.Equal("Song 0", p.Current?.Title);
        Assert.Equal(asks, dj.Asked.Count);
    }

    [Fact]
    public async Task Without_a_DJ_the_player_asks_and_calls_nothing()
    {
        var js = new Js(); var p = new PlayerState(js);
        p.SetPlanner(new UserSession("jellyfin", null, null), new NoPlanner());
        await p.Play(Songs(3), 0);
        await p.OnEnded(); await p.Toggle(); await p.ToggleDjAsync();
        Assert.Null(p.Dj); Assert.False(p.DjOn);
        Assert.DoesNotContain(js.Calls, c => c.Id.StartsWith("flacie.dj"));
    }

    [Fact]
    public async Task The_iPods_Playback_page_has_a_DJ_row_only_where_there_is_a_DJ_and_it_flips_this_queue()
    {
        var prefs = new IpodPrefs();
        var (p, _, _) = Player();
        await p.Play(Songs(3), 0);
        var lib = new Library([]);
        var rows = IpodMenus.PlaybackPage(IpodActions.Info(p, prefs, lib, 0), prefs);
        Assert.Equal(["Shuffle", "Repeat", "DJ", "Speed"], rows.Take(4).Select(r => r.Label));
        Assert.Equal("On", rows[2].Value);
        Assert.True(await IpodActions.CycleAsync("dj", prefs, p));
        Assert.Equal("Off", IpodMenus.PlaybackPage(IpodActions.Info(p, prefs, lib, 0), prefs)[2].Value);
        var plain = new PlayerState(new Js());
        Assert.DoesNotContain(IpodMenus.PlaybackPage(IpodActions.Info(plain, prefs, lib, 0), prefs), r => r.Label == "DJ");
        Assert.False(await IpodActions.CycleAsync("dj", prefs, plain));
    }

    [Fact]
    public void A_playlists_DJ_chip_goes_Auto_On_Off_and_back_and_is_keyed_by_its_Jellyfin_id()
    {
        Assert.Equal("jf1", DjSwitches.KeyOf(new Playlist("p1", "Bed", 0, "jf1", [])));
        Assert.Equal("p1", DjSwitches.KeyOf(new Playlist("p1", "Bed", 0, null, [])));
        Assert.Equal("p1", DjSwitches.KeyOf(new Playlist("p1", "Bed", 0, "", [])));
        var s = new DjSettings();
        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var now = DjSwitches.OfPlaylist(s, "p1");
            seen.Add(DjSwitches.Label(now));
            DjSwitches.SetPlaylist(s, "p1", DjSwitches.NextChoice(now));
        }
        Assert.Equal(["Auto", "On", "Off", "Auto"], seen);
        Assert.True(s.Playlists["p1"]);                      // (four taps: Auto, On, Off, Auto, and on to On again)
        DjSwitches.SetPlaylist(s, "p1", null);
        Assert.False(s.Playlists.ContainsKey("p1"));         // Auto takes the entry away, so the playlist follows the rest
    }

    [Fact]
    public void The_Where_presets_set_the_ticks_and_a_copy_never_changes_the_original()
    {
        var s = new DjSettings { Enabled = true, Chatty = DjChatty.Rare, Backup = false };
        s.Playlists["a"] = true;
        Assert.Equal(DjSwitches.Preset.Everywhere, DjSwitches.PresetOf(s));
        var c = DjSwitches.Copy(s);
        DjSwitches.Apply(c, DjSwitches.Preset.OnlyInTheCar);
        c.Playlists["b"] = false;
        Assert.Equal((false, true, false, false), (c.Phone, c.Auto, c.Web, c.Windows));
        Assert.Equal(DjSwitches.Preset.OnlyInTheCar, DjSwitches.PresetOf(c));
        Assert.True(DjRules.EffectiveOn(c, DjSurface.Auto, null, null)); Assert.False(DjRules.EffectiveOn(c, DjSurface.Web, null, null));
        Assert.Equal((true, DjChatty.Rare, false), (c.Enabled, c.Chatty, c.Backup));
        Assert.Equal(DjSwitches.Preset.Everywhere, DjSwitches.PresetOf(s));   // the original is untouched
        Assert.False(s.Playlists.ContainsKey("b"));
        DjSwitches.Apply(c, DjSwitches.Preset.OnlyOnWeb);
        Assert.Equal((false, false, true, false), (c.Phone, c.Auto, c.Web, c.Windows));
        c.Phone = true;
        Assert.Equal(DjSwitches.Preset.None, DjSwitches.PresetOf(c));
    }

    [Fact]
    public void The_listeners_time_is_UTC_less_the_browsers_offset_and_the_servers_clock_without_one()
    {
        var utc = new DateTime(2026, 10, 25, 23, 30, 0, DateTimeKind.Utc);   // a Sunday
        Assert.Equal((0, 1), DjSwitches.LocalTime(utc, -60));                  // UTC+1: already Monday, half past midnight
        Assert.Equal((19, 0), DjSwitches.LocalTime(utc, 240));                 // New York in summer time: Sunday evening
        Assert.Equal((5, 1), DjSwitches.LocalTime(utc, -330));                 // India: Monday, 5 am
        var server = utc.ToLocalTime();
        Assert.Equal((server.Hour, (int)server.DayOfWeek), DjSwitches.LocalTime(utc, null));
        Assert.Equal((server.Hour, (int)server.DayOfWeek), DjSwitches.LocalTime(utc, 5000));   // nonsense: the server's clock
    }

    [Fact]
    public async Task The_DJ_is_told_the_hour_where_the_browser_is_asked_once_per_tab()
    {
        var (p, js, dj) = Player();
        js.Answers["flacie.utcOffset"] = (int?)-330;                           // the browser is in India; the server's clock says UTC
        var before = DateTime.UtcNow.AddMinutes(330);
        await p.Play(Songs(5), 0);
        await p.OnEnded(); await p.OnEnded();
        var after = DateTime.UtcNow.AddMinutes(330);
        Assert.Equal(3, dj.Asked.Count);
        foreach (var r in dj.Asked)
        {
            Assert.Contains(r.LocalHour, new int?[] { before.Hour, after.Hour });
            Assert.Contains(r.Weekday, new int?[] { (int)before.DayOfWeek, (int)after.DayOfWeek });
        }
        Assert.Single(js.Calls, c => c.Id == "flacie.utcOffset");
    }

    [Fact]
    public async Task The_clip_is_handed_its_line_and_the_backup_switch_so_the_page_can_say_it_if_the_clip_fails()
    {
        var (p, js, dj) = Player();
        dj.Answer = r => new("Here's " + r.Next.Title, "", "/api/dj/clip/x.mp3", r.Next.Title == "Song 1");
        await p.Play(Songs(4), 0);
        await p.OnEnded(); await p.OnEnded();
        var plays = js.Calls.Where(c => c.Id == "flacie.djPlay").Select(c => c.Args).ToList();
        Assert.Equal(2, plays.Count);
        Assert.Equal(new object?[] { "/api/dj/clip/x.mp3", "Here's Song 1", true }, plays[0]);
        Assert.Equal(new object?[] { "/api/dj/clip/x.mp3", "Here's Song 2", false }, plays[1]);
    }

    [Fact]
    public async Task A_queue_handed_over_or_adopted_never_keeps_the_last_playlists_switch_or_this_queue_switch()
    {
        var (p, _, dj) = Player();
        dj.S.Enabled = true; dj.S.Playlists["quiet"] = false;
        await p.Play(Songs(3), 0, "Quiet one", "quiet");
        Assert.False(p.DjOn);
        await p.ToggleDjAsync();
        Assert.True(p.QueueDj);
        await p.PlayFromAsync(Songs(3), 1, 0, false);                          // Connect, a Jam or Alexa hands this tab a queue
        Assert.Null(p.ContextId); Assert.Null(p.QueueDj); Assert.True(p.DjOn);
        Assert.Null(dj.Asked[^1].PlaylistId);
        await p.Play(Songs(3), 0, "Quiet one", "quiet");
        await p.AdoptAsync(Songs(3), 0, 0, true);                             // the phone app's queue, taken as it stands
        Assert.Null(p.ContextId); Assert.True(p.DjOn);
    }

    [Fact]
    public async Task After_a_reload_the_queue_still_counts_as_from_its_playlist()
    {
        var (p, js, dj) = Player();
        dj.S.Enabled = true; dj.S.Playlists["quiet"] = false;
        p.SetStashKey("u");
        await p.Play(Songs(3), 0, "Quiet one", "quiet");
        var json = js.Calls.Last(c => c.Id == "flacie.stash" && c.Args[0] is string k && k.StartsWith("flacie.queue.")).Args[1] as string;
        var (p2, js2, dj2) = Player();
        dj2.S = dj.S;
        js2.Answers["flacie.unstash"] = json;
        p2.SetStashKey("u");
        var s = new UserSession("jellyfin", null, null);
        s.AddDownloaded(Songs(3));
        p2.SetPlanner(s, new NoPlanner());
        await p2.RestoreAsync(s);
        Assert.Equal("Song 0", p2.Current?.Title);
        Assert.Equal("quiet", p2.ContextId); Assert.False(p2.DjOn);
    }
}
