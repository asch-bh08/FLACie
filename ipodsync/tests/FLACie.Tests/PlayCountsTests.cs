using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F21: Frequently played (real plays, skips against, 30-day half-life) and Listen again below it with every played song.</summary>
public class PlayCountsTests
{
    static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    static long Ms(double daysAgo) => new DateTimeOffset(Now.AddDays(-daysAgo)).ToUnixTimeMilliseconds();
    static Track T(int n, string genre = "Pop") => new($"/m/{n}.flac", $"Song {n}", $"Artist {n % 7}", $"Album {n % 9}", $"Artist {n % 7}", 1, 1, 200_000, 2000, null, 0, TrackSource.Jellyfin, Genre: genre);
    static ListenEvent Play(Track t, double heard, double daysAgo) => new(Guid.NewGuid().ToString("N"), ListenKind.Play, Ms(daysAgo), t.Path, t.Title, t.Artist, heard, 200);

    [Fact]
    public void Three_plays_to_60_percent_show_one_full_play_and_a_skip_at_40_percent_do_not()
    {
        var a = T(1); var b = T(2);
        var events = new[] { Play(a, 120, 3), Play(a, 200, 2), Play(a, 130, 1), Play(b, 200, 2), Play(b, 80, 1) };   // b: one full play, then 40 %
        var ranked = PlayCounts.Rank(events, Ms(0));
        Assert.Equal(["Song 1"], ranked.Select(e => e.Title));
    }

    [Fact]
    public void Skips_count_against_and_recent_plays_weigh_more()
    {
        var a = T(1); var b = T(2); var c = T(3);
        var events = new List<ListenEvent>();
        for (var i = 0; i < 3; i++) { events.Add(Play(a, 200, 1)); events.Add(Play(b, 200, 120)); events.Add(Play(c, 200, 1)); }
        events.AddRange(Enumerable.Range(0, 4).Select(_ => Play(c, 10, 0.5)));   // c skipped four times: gone
        Assert.Equal(["Song 1", "Song 2"], PlayCounts.Rank(events, Ms(0)).Select(e => e.Title));
        Assert.True(PlayCounts.IsReal(240, 600)); Assert.False(PlayCounts.IsReal(239, 600));   // 4 minutes is real however long the song
    }

    [Fact]
    public void Listen_again_keeps_every_played_song_and_sits_below_frequently_played()
    {
        var songs = Enumerable.Range(0, 80).Select(n => T(n, n % 2 == 0 ? "Pop" : "Rock")).ToList();
        var lib = new Library(songs);
        var history = songs.Take(30).Select((t, i) => new PlayedEntry(t.Path, t.Title, t.Artist, Ms(i))).ToList();
        var events = songs.Take(4).SelectMany(t => Enumerable.Range(0, 3).Select(d => Play(t, 190, d))).ToList();
        var shelves = HomeBuilder.Build(lib, history, [], [], Now, null, null, events);
        var ids = shelves.Select(s => s.Id).ToList();
        Assert.Equal("frequently-played", ids[0]);
        Assert.True(ids.IndexOf("listen-again") > ids.IndexOf("quick-picks"));
        Assert.Equal(4, shelves[0].Tracks.Count);
        Assert.Equal(30, shelves.Single(s => s.Id == "listen-again").Tracks.Count);   // every played song, not 16
    }
}
