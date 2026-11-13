using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 13 Nov (Random Access Memories showed 3 of its 13 songs): an album lists its whole tracklist from the catalog, the songs the library has
/// as normal rows, the others greyed in their place "Not downloaded yet"; songs the catalog doesn't list (a bonus track) follow at the end.</summary>
public class AlbumRowsTests
{
    static Track T(string title, int no, string artist = "Daft Punk") => new($"/m/ram/{no}.flac", title, artist, "Random Access Memories", "Daft Punk", no, 1, 300_000, 2013, null, 0, TrackSource.Jellyfin);
    static CatalogSong C(string title, int no, string artist = "Daft Punk") => new(artist, title, "Random Access Memories", null, 300_000, no, 1);

    [Fact]
    public void The_whole_tracklist_in_order_with_the_missing_songs_in_their_place()
    {
        var have = new[] { T("Instant Crush", 5, "Daft Punk feat. Julian Casablancas"), T("Giorgio by Moroder", 3), T("Lose Yourself to Dance", 6), T("Hidden Bonus", 14) };
        var catalog = new[] { C("Give Life Back to Music", 1), C("The Game of Love", 2), C("Giorgio by Moroder", 3), C("Within", 4),
            C("Instant Crush (feat. Julian Casablancas)", 5, "Daft Punk"), C("Lose Yourself to Dance (feat. Pharrell Williams)", 6), C("Touch", 7) };
        var (play, wait) = AlbumRows.Build(catalog, have, (a, t) => null);
        Assert.Equal(["Giorgio by Moroder", "Instant Crush", "Lose Yourself to Dance", "Hidden Bonus"], play.Select(t => t.Title));
        Assert.Equal(["Give Life Back to Music", "The Game of Love", "Within", "Touch"], wait.Select(w => w.Title));
        Assert.Equal([0, 0, 1, 3], wait.Select(w => w.Before));   // Touch comes after Lose Yourself to Dance, before the bonus track
        Assert.All(wait, w => Assert.Equal("Not downloaded yet", w.Status));
    }

    [Fact]
    public void Without_a_tracklist_the_album_is_its_own_songs()
    {
        var have = new[] { T("Giorgio by Moroder", 3), T("Instant Crush", 5) };
        var (play, wait) = AlbumRows.Build([], have, (a, t) => null);
        Assert.Equal(have, play);
        Assert.Empty(wait);
    }
}
