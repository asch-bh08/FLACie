using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>A song's own picture always counts; an album's picture only for songs that really belong to that album (AlbumId is shared by unrelated albums on a real Jellyfin).</summary>
public class CoverVettingTests
{
    static string Id(int n) => n.ToString("x32");
    static Track Jf(int n, string album, string? key, string artist = "Artist") =>
        new($"http://jf/Audio/{Id(n)}/stream?static=true", $"Song {n}", artist, album, artist, n, 1, 1000, 2000, key, 0, TrackSource.Jellyfin, null, Id(n));

    [Fact] public void ASongsOwnPictureIsAlwaysKept()
    {
        var ts = new List<Track> { Jf(1, "A", "jf" + Id(1) + "~t1"), Jf(2, "B", "jf" + Id(2) + "~t2"), Jf(3, "", "jf" + Id(3) + "~t3") };
        Assert.Same(ts, CoverVetting.Vet(ts));
    }

    [Fact] public void AnAlbumFallbackIsKeptWhenAllItsSongsAreOfOneAlbum()
    {
        var album = "jf" + Id(900) + "~alb";
        var ts = new List<Track> { Jf(1, "Wasting Light", album), Jf(2, "Wasting Light (Deluxe Edition)", album), Jf(3, "wasting light", album) };
        Assert.Same(ts, CoverVetting.Vet(ts));
    }

    [Fact] public void AnAlbumFallbackSharedByDifferentAlbumsIsReplacedForEverySong()
    {
        var album = "jf" + Id(900) + "~alb";
        var ts = new List<Track> { Jf(1, "You Could Have It So Much Better", album, "Franz Ferdinand"), Jf(2, "Franz Ferdinand", album, "Franz Ferdinand") };
        var r = CoverVetting.Vet(ts);
        Assert.All(r, t => Assert.StartsWith("it", t.ArtKey));   // the catalog lookup of its own artist / album / title
        Assert.NotEqual(r[0].ArtKey, r[1].ArtKey);
    }

    [Fact] public void SongsWithNoAlbumNameNeverBorrowAnAlbumsPicture()
    {
        var album = "jf" + Id(900) + "~alb";
        var ts = new List<Track> { Jf(1, "Athlete", album), Jf(2, "", album), Jf(3, "<Unknown>", album) };
        var r = CoverVetting.Vet(ts);
        Assert.Equal(album, r[0].ArtKey);
        Assert.NotEqual(album, r[1].ArtKey);
        Assert.NotEqual(album, r[2].ArtKey);
    }

    [Fact] public void AGarbageAlbumIdWithManySongsGivesThemAllTheirOwnLookup()
    {
        var album = "jf" + Id(900);
        var ts = Enumerable.Range(1, 50).Select(i => Jf(i, i % 3 == 0 ? "" : $"Album {i}", album)).ToList();
        Assert.All(CoverVetting.Vet(ts), t => Assert.NotEqual(album, t.ArtKey));
    }

    [Fact] public void OtherSourcesAndMissingKeysAreLeftAlone()
    {
        var nas = Jf(3, "A", "nfabc") with { Source = TrackSource.Nas };
        var ts = new List<Track> { Jf(1, "A", null), nas, Jf(2, "A", "it" + "abc") };
        Assert.Same(ts, CoverVetting.Vet(ts));
    }

    [Fact] public void TheLibraryMergeAppliesIt()
    {
        var album = "jf" + Id(900) + "~alb";
        var lib = Library.Merge([Jf(1, "A", album), Jf(2, "B", album)], []);
        Assert.DoesNotContain(lib.Songs, t => t.ArtKey == album);
    }
}
