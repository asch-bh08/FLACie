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
    public void A_playlist_title_with_spotifys_extras_finds_the_downloaded_song(string listed, string artist, string file)
    {
        var lib = new Library([T(file, artist), T("Other Song", artist)]);
        Assert.Equal(file, lib.FindLoose(listed, artist)?.Title);
    }

    [Fact]
    public void A_remix_is_still_not_the_original()
    {
        var lib = new Library([T("Freed From Desire", "Gala")]);
        Assert.Null(lib.FindLoose("Freed From Desire (Remix)", "Gala"));
    }
}
