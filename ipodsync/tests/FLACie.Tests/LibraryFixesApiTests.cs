using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

public class LibraryFixesApiTests
{
    static Track T(string title, string album, int sec, string id, string artist = "The Prodigy") =>
        new($"http://jf/Audio/{id}/stream", title, artist, album, artist, 1, 1, sec * 1000L, 1994, null, 0, TrackSource.Jellyfin, $"/m/{id}.flac", id);

    [Fact]
    public void The_phone_gets_version_names_album_fixes_and_the_hidden_copies_by_jellyfin_id()
    {
        var album = T("Voodoo People", "Music for the Jilted Generation", 386, "a".PadLeft(32, 'a'));
        var edit = T("Voodoo People", "Their Law: The Singles 1990-2005 [CD1]", 220, "b".PadLeft(32, 'b'));
        var dup = T("Voodoo People", "Greatest Hits", 385, "c".PadLeft(32, 'c'));
        var grenade = T("Grenade", "Pop Megahits", 222, "d".PadLeft(32, 'd'), "Bruno Mars");
        try
        {
            VersionNames.Lookup = p => p == edit.Path ? "05 Edit" : null;   // (kept by stream path, as VersionNameService does)
            AlbumFixes.Lookup = p => p == grenade.FilePath ? new AlbumFix("Doo-Wops & Hooligans", "Bruno Mars", "https://e-cdns-images.dzcdn.net/x.jpg", 0) : null;
            var (fixes, hidden) = LibraryFixesApi.Build(new Library([album, edit, dup, grenade]));
            Assert.Contains(fixes, f => f.Id == edit.JellyfinId && f.Title == "Voodoo People (05 Edit)" && f.Album is null);
            Assert.Contains(fixes, f => f.Id == grenade.JellyfinId && f.Album == "Doo-Wops & Hooligans" && f.Art == "https://e-cdns-images.dzcdn.net/x.jpg");
            Assert.DoesNotContain(fixes, f => f.Id == album.JellyfinId);
            Assert.Equal([dup.JellyfinId!], hidden);
        }
        finally { VersionNames.Lookup = null; AlbumFixes.Lookup = null; }
    }
}
