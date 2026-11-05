using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F23.2: songs downloaded as interest shows, never all at once.</summary>
public class PlaylistFetchPolicyTests
{
    static readonly List<WebTrack> List = Enumerable.Range(1, 10).Select(i => new WebTrack($"Song {i}", "Artist")).ToList();
    static readonly HashSet<string> Have = ["Song 2", "Song 5", "Song 9"];
    static bool Has(WebTrack t) => Have.Contains(t.Title);
    const long Plenty = 2_000_000_000_000;

    static IEnumerable<string> T(IEnumerable<WebTrack> l) => l.Select(t => t.Title);

    [Fact]
    public void Opening_a_playlist_queues_all_its_missing_songs_in_play_order()
    {
        Assert.Equal(["Song 1", "Song 3", "Song 4", "Song 6", "Song 7", "Song 8", "Song 10"], T(PlaylistFetchPolicy.Plan(FetchTrigger.Opened, List, Has, Plenty)));
        // played from song 7: the next ones first, then round to the start
        Assert.Equal(["Song 7", "Song 8", "Song 10", "Song 1", "Song 3", "Song 4", "Song 6"], T(PlaylistFetchPolicy.Plan(FetchTrigger.Played, List, Has, Plenty, from: 6)));
    }

    [Fact]
    public void An_artist_search_queues_the_top_half_of_their_this_is_first()
    {
        var plan = PlaylistFetchPolicy.Plan(FetchTrigger.ArtistSearch, List, Has, Plenty);
        var top = PlaylistFetchPolicy.TopHalf(List, Has);
        Assert.Equal(["Song 1", "Song 3", "Song 4"], T(plan.Take(top)));
        Assert.Equal(["Song 6", "Song 7", "Song 8", "Song 10"], T(plan.Skip(top)));
    }

    [Fact]
    public void Popular_lists_queue_only_their_first_2_missing_songs()
    {
        Assert.Equal(["Song 1", "Song 3"], T(PlaylistFetchPolicy.Plan(FetchTrigger.Popular, List, Has, Plenty)));
    }

    [Fact]
    public void Under_100_GB_free_automatic_downloads_stop_and_a_single_asked_for_song_still_downloads()
    {
        const long low = 99L * 1000 * 1000 * 1000;
        foreach (var trigger in new[] { FetchTrigger.Opened, FetchTrigger.Played, FetchTrigger.ArtistSearch, FetchTrigger.Popular })
            Assert.Empty(PlaylistFetchPolicy.Plan(trigger, List, Has, low));
        Assert.Equal(["Song 4"], T(PlaylistFetchPolicy.Plan(FetchTrigger.Asked, List, Has, low, from: 3)));
        Assert.Empty(PlaylistFetchPolicy.Plan(FetchTrigger.Asked, List, Has, low, from: 1));   // already in the library
        Assert.Equal(7, PlaylistFetchPolicy.Plan(FetchTrigger.Opened, List, Has, null).Count);  // free space unknown: it goes ahead
    }
}
