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

    [Fact]
    public void Other_versions_carry_their_real_name_and_a_second_copy_of_the_same_recording_is_hidden()
    {
        // owner, 13 Nov: "make sure they're titled differently" and "give it the name of the actual version; if it's another copy, why keep it"
        static Track L(string album, int sec, string path) => new(path, "Voodoo People", "The Prodigy", album, "The Prodigy", 1, 1, sec * 1000L, 1994, null, 0, TrackSource.Jellyfin);
        var album = L("Music for the Jilted Generation", 386, "/a.flac");
        var edit = L("Their Law: The Singles 1990-2005 [CD1]", 220, "/b.flac");
        var longer = L("Their Law: The Singles 1990-2005 [CD2]", 480, "/c.flac");
        var same = L("Greatest Hits", 384, "/d.flac");
        try
        {
            VersionNames.Lookup = null;
            var lib = new Library([edit, album, longer, same]);
            Assert.Equal("Voodoo People", lib.DisplayTitle(album));              // the main copy keeps its plain title
            Assert.Equal("Voodoo People (3:40 edit)", lib.DisplayTitle(edit));   // no name found yet: its length says what it is
            Assert.Equal("Voodoo People (8:00 version)", lib.DisplayTitle(longer));
            Assert.True(lib.IsDuplicate(same));                                  // the same recording again: hidden from the song lists
            Assert.DoesNotContain(same, lib.Songs);
            Assert.Contains(lib.Albums, g => g.Name == "Greatest Hits" && g.Tracks.Contains(same));   // its album keeps it
            Assert.Equal(["/b.flac", "/c.flac"], lib.OtherVersions(album).Select(t => t.Path).Order());
            // the catalog's own name for it, once looked up
            VersionNames.Lookup = path => path == "/b.flac" ? "05 Edit" : null;
            Assert.Equal("Voodoo People (05 Edit)", new Library([edit, album]).DisplayTitle(edit));
            Assert.Equal("Voodoo People", new Library([album]).DisplayTitle(album));
        }
        finally { VersionNames.Lookup = null; }
    }

    [Theory]
    [InlineData("Voodoo People (05 Edit)", "Voodoo People", "05 Edit")]
    [InlineData("Voodoo People - Edit", "Voodoo People", "Edit")]
    [InlineData("Voodoo People (Pendulum Remix)", "Voodoo People", "Pendulum Remix")]
    [InlineData("Voodoo People", "Voodoo People", "")]
    [InlineData("Voodoo People (Remastered 2008)", "Voodoo People", "Remastered 2008")]
    public void The_version_part_of_a_catalog_title(string catalog, string file, string version) => Assert.Equal(version, VersionNames.NameFrom(catalog, file));

    [Fact]
    public void Search_folds_a_songs_mixes_and_versions_under_the_plain_one()
    {
        // owner, 13 Nov: "dreamer" listed Dreamer, Dreamer - Re-Original 7-inch Mix and Dreamer (Janice Robinson Vocal) as three songs
        static Track S(string title, string artist, string album, string path) => new(path, title, artist, album, artist, 1, 1, 220_000, 1995, null, 0, TrackSource.Jellyfin);
        var plain = S("Dreamer", "Livin' Joy", "Don't Stop Movin'", "/1");
        var mix = S("Dreamer - Re-Original 7-inch Mix", "Livin' Joy", "Dreamer", "/2");
        var vocal = S("Dreamer (Janice Robinson Vocal)", "Livin' Joy", "Don't Stop Movin'", "/3");
        var prince = S("Dreamer", "Prince", "Lotusflow3r", "/4");
        var lib = new Library([mix, vocal, plain, prince]);
        var songs = lib.Search("dreamer").Songs;
        Assert.Equal(2, songs.Count);
        Assert.Contains(plain, songs); Assert.Contains(prince, songs);
        Assert.Equal(["/2", "/3"], lib.OtherVersions(plain).Select(t => t.Path).Order());
        Assert.Empty(lib.OtherVersions(prince));
        // asked for by its own name, a version is found as itself
        Assert.Same(vocal, Assert.Single(lib.Search("janice robinson").Songs));
        // their own titles already tell them apart: no added name
        Assert.Equal("Dreamer (Janice Robinson Vocal)", lib.DisplayTitle(vocal));
        Assert.Equal("Dreamer", lib.DisplayTitle(plain));
    }
}
