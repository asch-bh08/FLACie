using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>A song downloaded from search keeps the cover it was shown with (owner, 6 Nov: Rhythm of the Night had one cover in search and another once
/// downloaded, from the Hi-Res copy's file).</summary>
public class DownloadCoversTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlcovers-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    static Track T(string title, string artist, string? art) => new("/m/" + title, title, artist, "Comp", artist, 1, 1, 1000, 1994, art, 0, TrackSource.Jellyfin, JellyfinId: "abc");

    [Fact]
    public void A_downloaded_song_shows_the_cover_it_was_found_with_after_a_restart_too()
    {
        var c = new DownloadCovers(Path.Combine(dir, "c.json"));
        c.Remember("The Rhythm of the Night", "Corona", "https://e-cdns-images.dzcdn.net/images/cover/x/250x250.jpg");
        var again = new DownloadCovers(Path.Combine(dir, "c.json"));
        var key = again.For(T("The Rhythm Of The Night", "Corona", "jfabc~t1"));
        Assert.Equal(Art.ExternalKey("https://e-cdns-images.dzcdn.net/images/cover/x/250x250.jpg"), key);
        Assert.Null(again.For(T("Other", "Corona", "jfabc~t1")));
    }

    [Fact]
    public void The_library_uses_it()
    {
        var c = new DownloadCovers(Path.Combine(dir, "c.json"));
        c.Remember("Freestyler", "Bomfunk MC's", "https://is1-ssl.mzstatic.com/image/thumb/x/100x100bb.jpg");
        var tracks = c.Apply([T("Freestyler", "Bomfunk MC's", "jfabc~t1"), T("Other", "B", "jfdef~t2")]);
        Assert.Equal(Art.ExternalKey("https://is1-ssl.mzstatic.com/image/thumb/x/100x100bb.jpg"), tracks[0].ArtKey);
        Assert.Equal("jfdef~t2", tracks[1].ArtKey);
    }

    [Fact]
    public void An_address_that_is_not_a_picture_url_is_not_kept()
    {
        var c = new DownloadCovers(Path.Combine(dir, "c.json"));
        c.Remember("A", "B", "javascript:alert(1)");
        c.Remember("A", "B", "");
        Assert.Null(c.For(T("A", "B", null)));
    }
}
