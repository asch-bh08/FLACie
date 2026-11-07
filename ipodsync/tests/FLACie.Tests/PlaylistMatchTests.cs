using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// An imported Spotify playlist names songs "More And More - Single Version", "Freed From Desire - Radio Edit", "Holding Out for a Hero - From
/// "Footloose" Soundtrack"; the files downloaded for them are "More And More" and so on. The playlist page listed them under "Not in your library"
/// with "Recently downloaded" and a Download button (owner, 5 Nov).
/// </summary>
public class PlaylistMatchTests
{
    static Track T(string title, string artist) => new($"/m/{artist}/{title}.flac", title, artist, "Album", artist, 1, 1, 200_000, 1995, null, 0, TrackSource.Nas);

    [Theory]
    [InlineData("More And More - Single Version", "Captain Hollywood Project", "More And More")]
    [InlineData("Freed From Desire - Radio Edit", "Gala", "Freed From Desire")]
    [InlineData("Holding Out for a Hero - From \"Footloose\" Soundtrack", "Bonnie Tyler", "Holding Out for a Hero")]
    [InlineData("Scatman (ski-ba-bop-ba-dop-bop)", "Scatman John", "Scatman")]
    // "80s - 90s rally" still listed these as not in the library after they were downloaded (owner, 7 Nov)
    [InlineData("My Heart Beats Like A Drum (Dam Dam Dam) - Radio Edit", "A Touch Of Class", "My Heart Beats Like A Drum (Dam Dam Dam)")]
    [InlineData("Bailando - Video Edit", "Paradisio", "Bailando")]
    [InlineData("Some Song - Single Edit", "Someone", "Some Song")]
    public void A_playlist_title_with_spotifys_extras_finds_the_downloaded_song(string listed, string artist, string file)
    {
        var lib = new Library([T(file, artist), T("Other Song", artist)]);
        Assert.Equal(file, lib.FindLoose(listed, artist)?.Title);
    }

    /// <summary>A file tagged with a compilation's name for its artist ("90's Eurodance") but filed in the artist's own folder is that artist's song (owner, 7 Nov).</summary>
    [Fact]
    public void A_song_filed_in_the_artists_folder_counts_whatever_its_artist_tag_says()
    {
        var file = new Track("http://jf/Audio/1/stream", "Lover On The Line", "90's Eurodance", "90's Eurodance", "90s-Eurodance", 0, 0, 232_826, 0, null, 0, TrackSource.Jellyfin,
            FilePath: "/media/music/The Free/The Free - Lover On The Line.flac");
        var lib = new Library([file, T("Lover On The Line", "Someone Else")]);
        Assert.Same(file, lib.FindLoose("Lover On The Line", "The Free"));
        Assert.Null(new Library([T("Lover On The Line", "Someone Else")]).FindLoose("Lover On The Line", "The Free"));
    }

    [Fact]
    public void A_remix_is_still_not_the_original()
    {
        var lib = new Library([T("Freed From Desire", "Gala")]);
        Assert.Null(lib.FindLoose("Freed From Desire (Remix)", "Gala"));
    }
}
