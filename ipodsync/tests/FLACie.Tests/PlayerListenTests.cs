using FLACie.Core;
using FLACie.Server;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>The web player and the recommendation engine: what it tells the listening log, when Autoplay asks the engine, and what a thumbs-down removes.</summary>
public class PlayerListenTests : IDisposable
{
    sealed class FakeJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    sealed class FakePlanner : IAutoplayPlanner
    {
        public List<CatalogSong> Suggest = [];
        public int Prefetched;
        public Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default) => Task.FromResult(Suggest.ToList());
        public IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested) => [];
        public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach) { wanted.ToList(); Prefetched++; }
    }

    readonly List<ListenEvent> events = [];
    readonly UserSession me = new("local", null, null) { Id = "me" };
    readonly FakePlanner planner = new();
    Library lib = Library.Empty;

    public PlayerListenTests() { RetroHost.Listen = (_, e) => events.Add(e); RetroHost.RecommendNext = null; }
    public void Dispose() { RetroHost.Listen = null; RetroHost.RecommendNext = null; }

    static Track T(int n) => new($"/m/{n}.flac", $"Song {n}", "A" + n, "Al", "A" + n, n, 1, 100_000, 2000, null, 0, TrackSource.Jellyfin, $"/m/{n}.flac", n.ToString("x32"));
    static List<Track> Songs(int from, int count) => Enumerable.Range(from, count).Select(T).ToList();

    PlayerState Player(int libSongs = 12)
    {
        lib = new Library(Songs(0, libSongs));
        var p = new PlayerState(new FakeJs());
        p.SetLibrary(() => lib); p.SetPlanner(me, planner);
        return p;
    }

    static async Task Until(Func<bool> done) { for (var i = 0; i < 400 && !done(); i++) await Task.Delay(15); Assert.True(done()); }

    [Fact] public async Task A_play_counts_what_was_heard_not_what_was_skipped_over_and_is_told_once()
    {
        var p = Player();
        await p.Play(Songs(0, 3), 0);
        foreach (var s in new[] { 1.0, 2, 3 }) p.OnTime(s, 100);
        p.OnTime(60, 100);   // a seek
        foreach (var s in new[] { 61.0, 62 }) p.OnTime(s, 100);
        await p.NextAsync();
        var play = Assert.Single(events);
        Assert.Equal((ListenKind.Play, "/m/0.flac", 5.0, 100.0), (play.Kind, play.Path, play.Heard, play.Length));
        await p.StopAsync();
        Assert.Single(events.Where(e => e.Path == "/m/0.flac"));   // leaving the song is not told twice
        Assert.Single(events);   // the next song was heard for no time at all
    }

    [Fact] public async Task The_context_is_the_playlist_id_search_home_or_autoplay()
    {
        var p = Player();
        var pl = me.CreatePlaylist("Gym");
        async Task<string?> Heard(Func<Task> start) { events.Clear(); await start(); p.OnTime(1, 100); p.OnTime(2, 100); await p.StopAsync(); return events.Single().Context; }
        Assert.Equal(pl.Id, await Heard(() => p.Play(Songs(0, 1), 0, "Gym")));
        Assert.Equal("search", await Heard(() => p.Play(Songs(0, 1), 0, "Search: abba")));
        Assert.Equal("home", await Heard(() => p.Play(Songs(0, 1), 0, "Quick picks", listenContext: "home")));
        // the queue ran out and Autoplay carried on
        events.Clear();
        await p.Play(Songs(0, 1), 0, "Gym");
        await p.NextAsync();
        p.OnTime(1, 100); p.OnTime(2, 100);
        await p.StopAsync();
        Assert.Equal("autoplay", events.Last().Context);
    }

    [Fact] public async Task The_engine_is_asked_only_when_at_most_one_song_follows_and_the_library_list_stands_when_it_fails()
    {
        var p = Player(); var asked = new List<IReadOnlyList<string>>();
        RetroHost.RecommendNext = (_, played, _) => { asked.Add(played.ToList()); return [(T(99), "Because you like A9")]; };
        await p.Play(Songs(0, 4), 0);   // three songs follow
        await Task.Delay(200);
        Assert.Empty(asked);
        await p.SkipTo(2);   // one follows
        await Until(() => asked.Count == 1);
        await Until(() => p.AutoplayNext.Select(t => t.Path).SequenceEqual(["/m/99.flac"]));
        Assert.Equal(["/m/0.flac", "/m/2.flac"], asked[0].ToArray());
        // an engine that fails leaves the library's own list
        RetroHost.RecommendNext = (_, _, _) => throw new InvalidOperationException("down");
        await p.Play(Songs(0, 1), 0);
        await Task.Delay(300);
        Assert.NotEmpty(p.AutoplayNext);
        Assert.DoesNotContain(p.AutoplayNext, t => t.Path == "/m/99.flac");
    }

    [Fact] public async Task A_song_picked_by_the_engine_says_why_once_it_plays()
    {
        var p = Player();
        RetroHost.RecommendNext = (_, _, _) => [(T(9), "Because you like A9")];
        await p.Play(Songs(0, 1), 0);
        await Until(() => p.AutoplayNext.Count == 1 && p.AutoplayNext[0].Path == "/m/9.flac");
        Assert.Null(p.WhyCurrent);
        await p.NextAsync();
        Assert.Equal("Because you like A9", p.WhyCurrent);
    }

    [Fact] public async Task Songs_just_fetched_for_Autoplay_stay_ahead_of_the_engines_picks()
    {
        var p = Player(8);
        planner.Suggest = [new CatalogSong("Fresh", "New Song", "", null, 200_000, 1, 1)];
        RetroHost.RecommendNext = (_, _, _) => [(T(5), "why")];
        await p.Play(Songs(0, 1), 0);
        await Until(() => planner.Prefetched > 0);
        var fresh = new Track("/m/fresh.flac", "New Song", "Fresh", "Al", "Fresh", 1, 1, 200_000, 2000, null, 0, TrackSource.Jellyfin, "/m/fresh.flac", "f".PadRight(32, 'f'));
        lib = new Library([.. Songs(0, 8), fresh]);   // it lands
        await Until(() => p.AutoplayNext.Any(t => t.Path == "/m/5.flac"));
        await p.SetAutoplayAsync(true);
        await Until(() => p.AutoplayNext.Count > 0 && p.AutoplayNext[0].Path == "/m/fresh.flac");
        Assert.Contains(p.AutoplayNext, t => t.Path == "/m/5.flac");
    }

    [Fact] public async Task Disliking_the_playing_song_skips_it_and_clears_every_copy_to_come()
    {
        var p = Player();
        var q = new List<Track> { T(0), T(1), T(2), T(1), T(3) };
        await p.Play(q, 1);
        await p.Dislike("/m/1.flac");
        Assert.Equal("/m/2.flac", p.Current!.Path);
        Assert.Equal(["Song 3"], p.UpNext(10).Select(u => u.Track.Title).ToArray());
    }

    [Fact] public async Task Disliking_a_song_that_is_not_playing_takes_it_out_of_Up_next_and_Autoplay()
    {
        var p = Player();
        RetroHost.RecommendNext = (_, _, _) => [(T(99), "why"), (T(98), "why")];
        await p.Play(Songs(0, 3), 0);
        await p.Dislike("/m/2.flac");
        Assert.Equal("/m/0.flac", p.Current!.Path);
        Assert.Equal(["Song 1"], p.UpNext(10).Select(u => u.Track.Title).ToArray());
        await p.SkipTo(1);
        await Until(() => p.AutoplayNext.Any(t => t.Path == "/m/99.flac") && p.AutoplayNext.Any(t => t.Path == "/m/98.flac"));
        await p.Dislike("/m/99.flac");
        Assert.DoesNotContain(p.AutoplayNext, t => t.Path == "/m/99.flac");
        Assert.Contains(p.AutoplayNext, t => t.Path == "/m/98.flac");
    }

    [Fact] public async Task A_listening_log_that_fails_never_stops_the_next_song_or_the_stop()
    {
        var p = Player();
        RetroHost.Listen = (_, _) => throw new IOException("disk full");
        await p.Play(Songs(0, 3), 0);
        p.OnTime(1, 100); p.OnTime(2, 100);
        await p.NextAsync();
        Assert.Equal("/m/1.flac", p.Current!.Path);
        p.OnTime(1, 100); p.OnTime(2, 100);
        await p.StopAsync();
        Assert.Null(p.Current);
    }

    [Fact] public async Task Disliking_the_last_song_stops_the_player_instead_of_pausing_on_it()
    {
        var p = Player();
        await p.SetAutoplayAsync(false);
        await p.Play(Songs(0, 2), 1);
        await p.Dislike("/m/1.flac");
        Assert.Null(p.Current);
        Assert.DoesNotContain(p.Queue, t => t.Path == "/m/1.flac");
    }

    [Fact] public async Task Disliking_the_only_song_with_repeat_all_stops_and_does_not_replay_it()
    {
        var p = Player();
        await p.Play(Songs(0, 1), 0);
        p.CycleRepeat(); Assert.Equal(1, p.Repeat);
        await p.Dislike("/m/0.flac");
        Assert.Null(p.Current);
        Assert.Empty(p.Queue);
    }

    [Fact] public async Task With_repeat_all_a_later_copy_of_the_disliked_song_never_starts()
    {
        var p = Player();
        await p.Play(new List<Track> { T(0), T(1), T(2), T(1) }, 1);
        p.CycleRepeat();
        await p.Dislike("/m/1.flac");
        Assert.Equal("/m/2.flac", p.Current!.Path);
        await p.NextAsync(); await p.NextAsync(); await p.NextAsync();
        Assert.DoesNotContain(p.Queue, t => t.Path == "/m/1.flac");
        Assert.NotEqual("/m/1.flac", p.Current!.Path);
    }
}
