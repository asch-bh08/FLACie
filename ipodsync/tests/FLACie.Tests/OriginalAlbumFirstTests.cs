using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 13 Nov: Voodoo People played and showed as "Their Law: The Singles 1990-2005 [CD1]" with that cover, though the library also has it on
/// Music for the Jilted Generation. A song on its own album and on a best-of or various-artists compilation is the album copy wherever one copy is
/// meant (search, a lookup by name); the compilation still lists its own songs.</summary>
public class OriginalAlbumFirstTests
{
    static Track T(string title, string album, string albumArtist = "The Prodigy", string path = "") =>
        new(path.Length > 0 ? path : $"/m/{album}/{title}.flac", title, "The Prodigy", album, albumArtist, 1, 1, 300_000, 1994, null, 0, TrackSource.Jellyfin);

    [Theory]
    [InlineData("Their Law: The Singles 1990-2005 [CD1]", "The Prodigy", true)]
    [InlineData("Greatest Hits", "Queen", true)]
    [InlineData("The Best Of Depeche Mode Volume 1", "Depeche Mode", true)]
    [InlineData("Now That's What I Call Music! 30", "Various Artists", true)]
    [InlineData("Dance Anthems", "Various Artists", true)]
    [InlineData("Music for the Jilted Generation", "The Prodigy", false)]
    [InlineData("Experience", "The Prodigy", false)]
    [InlineData("Hits Hits Hits", "Someone", true)]
    public void Compilations_are_known_by_name_or_by_various_artists(string album, string albumArtist, bool compilation) =>
        Assert.Equal(compilation, Matching.IsCompilation(T("x", album, albumArtist)));

    [Fact]
    public void A_lookup_by_name_finds_the_album_copy_whichever_came_first()
    {
        var best = T("Voodoo People", "Their Law: The Singles 1990-2005 [CD1]");
        var album = T("Voodoo People", "Music for the Jilted Generation");
        foreach (var lib in new[] { new Library([best, album]), new Library([album, best]) })
        {
            Assert.Same(album, lib.Find("Voodoo People", "The Prodigy"));
            Assert.Same(album, lib.Resolve("/gone.flac", "Voodoo People", "The Prodigy"));
        }
        // the compilation still has its song
        Assert.Contains(new Library([best, album]).Albums, g => g.Name.StartsWith("Their Law") && g.Tracks.Contains(best));
    }

    [Fact]
    public void Search_shows_the_album_copy_once()
    {
        var best = T("Voodoo People", "Their Law: The Singles 1990-2005 [CD1]");
        var album = T("Voodoo People", "Music for the Jilted Generation");
        var other = T("Out of Space", "Their Law: The Singles 1990-2005 [CD1]");
        var songs = new Library([best, album, other]).Search("voodoo people").Songs;
        Assert.Same(album, Assert.Single(songs));
        // a song only on the compilation is still found there
        Assert.Same(other, Assert.Single(new Library([best, album, other]).Search("out of space").Songs));
    }

    [Fact]
    public void A_file_asked_for_by_its_own_path_is_that_file()
    {
        var best = T("Voodoo People", "Their Law: The Singles 1990-2005 [CD1]");
        var album = T("Voodoo People", "Music for the Jilted Generation");
        Assert.Same(best, new Library([best, album]).Resolve(best.Path, "Voodoo People", "The Prodigy"));
    }
}
