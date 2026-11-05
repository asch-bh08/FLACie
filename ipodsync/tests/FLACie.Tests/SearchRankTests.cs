using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F6: the artist you mean is the top result: the one you play and have most.</summary>
public class SearchRankTests
{
    static Group Artist(string name, int songs) => new(name, [.. Enumerable.Range(0, songs).Select(i =>
        new Track($"/m/{name}/{i}", $"Song {i}", name, "A", name, i, 1, 200000, 2000, null, 1, TrackSource.Jellyfin, JellyfinId: $"{name}-{i}"))], null);

    [Fact]
    public void Prodigy_puts_The_Prodigy_above_Prodigy()
    {
        var prodigy = Artist("Prodigy", 15); var the = Artist("The Prodigy", 171);
        Assert.Same(the, SearchRankTop.Artist([prodigy, the], "prodigy", [new("The Prodigy-3", 40, 0, false)]));
        Assert.Same(the, SearchRankTop.Artist([prodigy, the], "prodigy", []));
    }

    [Fact]
    public void Plays_beat_size()
    {
        var prodigy = Artist("Prodigy", 15); var the = Artist("The Prodigy", 171);
        Assert.Same(prodigy, SearchRankTop.Artist([the, prodigy], "prodigy", [new("Prodigy-1", 30, 0, false)]));
    }

    [Fact]
    public void Only_an_artist_named_like_the_search_is_a_top_result()
    {
        Assert.Null(SearchRankTop.Artist([Artist("Prodigy of Mobb Deep", 5)], "prodigy", []));
        Assert.Null(SearchRankTop.Artist([Artist("The Prodigy", 5)], "the", []));
    }
}
