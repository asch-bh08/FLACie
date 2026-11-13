using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 13 Nov: "Grenade" showed album "Pop Megahits", "Airplanes" "2010s bops", with those playlists' pictures (files from a ripped mix folder).
/// A song whose album is really a playlist's name gets its own album's name and cover over it (the file is not changed); real albums, remixes and
/// an artist's own best-of are left alone.</summary>
public class AlbumFixesTests
{
    static Track T(string title, string artist, string album, string path, string albumArtist = "") =>
        new(path, title, artist, album, albumArtist.Length > 0 ? albumArtist : artist, 1, 1, 220_000, 2023, "playlistcover", 0, TrackSource.Jellyfin, path);

    [Fact]
    public void Songs_filed_under_a_playlists_name_are_the_ones_to_fix()
    {
        var songs = new[]
        {
            T("Grenade", "Bruno Mars", "Pop Megahits", "/m/mix/1.flac"),
            T("Airplanes (feat. Hayley Williams)", "B.o.B", "2010s bops", "/m/mix/2.flac"),
            T("Dynamite", "Taio Cruz", "2010s Bangers - 100 Hits", "/m/mix/3.flac", "Various Artists"),
            T("Firework", "Katy Perry", "2010s Bangers - 100 Hits", "/m/mix/4.flac", "Various Artists"),
            T("Teenage Dream", "Katy Perry", "Teenage Dream: The Complete Confection", "/m/mix/5.flac"),
            T("Bohemian Rhapsody", "Queen", "Greatest Hits", "/m/q/1.flac"), T("Radio Ga Ga", "Queen", "Greatest Hits", "/m/q/2.flac"),
            T("Paranoid", "Black Sabbath", "Greatest Hits", "/m/b/1.flac"), T("Changes", "Black Sabbath", "Greatest Hits", "/m/b/2.flac"),
            T("Grenade (Remix)", "Bruno Mars", "party bangers", "/m/mix/6.flac"),
        };
        var c = AlbumFixes.Candidates(songs).Select(t => t.Title).ToList();
        Assert.Equal(["Grenade", "Airplanes (feat. Hayley Williams)", "Dynamite", "Firework"], c);
    }

    [Fact]
    public void A_fix_shows_the_songs_own_album_and_cover_and_nothing_else_moves()
    {
        var grenade = T("Grenade", "Bruno Mars", "Pop Megahits", "/m/mix/1.flac");
        var other = T("Teenage Dream", "Katy Perry", "Teenage Dream", "/m/mix/5.flac");
        try
        {
            AlbumFixes.Lookup = path => path == "/m/mix/1.flac" ? new AlbumFix("Doo-Wops & Hooligans", "Bruno Mars", "https://e-cdns-images.dzcdn.net/images/cover/abc/1000x1000.jpg", 2010) : null;
            var fixedList = AlbumFixes.Apply([grenade, other]);
            Assert.Equal(("Doo-Wops & Hooligans", "Bruno Mars", 2010), (fixedList[0].Album, fixedList[0].AlbumArtist, fixedList[0].Year));
            Assert.Equal(Art.ExternalKey("https://e-cdns-images.dzcdn.net/images/cover/abc/1000x1000.jpg"), fixedList[0].ArtKey);
            Assert.Same(other, fixedList[1]);
            // in the library it now sits on its own album
            Assert.Contains(Library.Merge(fixedList, []).Albums, g => g.Name == "Doo-Wops & Hooligans");
        }
        finally { AlbumFixes.Lookup = null; }
    }
}
