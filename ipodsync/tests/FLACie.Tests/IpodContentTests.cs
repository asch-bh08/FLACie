using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class IpodContentTests
{
    static Track T(string title, string artist, string album, string genre = "", string? art = null, int no = 1, long ms = 200_000) =>
        new("p/" + title, title, artist, album, artist, no, 1, ms, 2000, art, 0, TrackSource.Jellyfin, null, null, genre);

    static Library Lib() => new([
        T("Zeta", "Ann", "First", "Rock", "k1", 2), T("Alpha", "Ann", "First", "Rock", "k1", 1), T("Mid", "Ann", "Second", "Pop", "k2"), T("Solo", "Bob", "Only", "", null),
    ]);

    static IReadOnlyList<Group> NoLists() => [];

    [Fact] public void SongRowsStartWithShuffleWhenThereIsMoreThanOneSong()
    {
        var rows = IpodContent.SongRows(Lib().Songs);
        Assert.Equal("Shuffle", rows[0].Label); Assert.Equal("t:shuffle", rows[0].Id); Assert.Equal("4 songs", rows[0].Detail);
        Assert.Equal(["Alpha", "Mid", "Solo", "Zeta"], rows.Skip(1).Select(r => r.Label).ToArray());
        Assert.Equal("t:0", rows[1].Id); Assert.Equal("Ann", rows[1].Sub); Assert.Equal("First  ·  3:20", rows[1].Detail); Assert.Equal("k1", rows[1].ArtKey);
        Assert.Single(IpodContent.SongRows([T("A", "x", "y")]));
        Assert.Empty(IpodContent.SongRows([]));
    }

    [Fact] public void TheLibraryMenusOpenTheRightLevels()
    {
        var lib = Lib();
        var songs = IpodContent.Level("m:songs", lib, NoLists)!; Assert.Equal(IpodKind.Songs, songs.Kind); Assert.Equal(4, songs.Tracks!.Count);
        var artists = IpodContent.Level("m:artists", lib, NoLists)!; Assert.Equal(["Ann", "Bob"], artists.Rows.Select(r => r.Label).ToArray()); Assert.Equal(IpodKind.Artists, artists.Kind);
        var albums = IpodContent.Level("m:albums", lib, NoLists)!; Assert.Equal(3, albums.Rows.Count); Assert.Equal("Ann", albums.Rows[0].Sub); Assert.Equal("b:0", albums.Rows[0].Id);
        var genres = IpodContent.Level("m:genres", lib, NoLists)!; Assert.Equal(["Pop", "Rock", "Unknown"], genres.Rows.Select(r => r.Label).ToArray());
        var cover = IpodContent.Level("m:cover", lib, NoLists)!; Assert.Equal(IpodKind.CoverFlow, cover.Kind); Assert.StartsWith("c:", cover.Rows[0].Id);
        Assert.Null(IpodContent.Level("m:settings", lib, NoLists));
    }

    [Fact] public void EmptyListsSayNoneAndCoverFlowFallsBackToAMenu()
    {
        var e = Library.Empty;
        Assert.Equal("None", IpodContent.Level("m:artists", e, NoLists)!.Rows.Single().Label);
        Assert.Equal(IpodKind.Menu, IpodContent.Level("m:cover", e, NoLists)!.Kind);
        Assert.Equal("None", IpodContent.Level("m:pl", e, NoLists)!.Rows.Single().Label);
        var pl = IpodContent.Level("m:pl", lib: Lib(), () => [new Group("Mine", [T("A", "x", "y")], null)])!; Assert.Equal("p:0", pl.Rows[0].Id);
    }

    [Fact] public void PressingARowOpensOrPlays()
    {
        var lib = Lib();
        var songs = IpodContent.Level("m:songs", lib, NoLists)!;
        var (n1, p1) = IpodContent.Open("t:1", songs); Assert.Null(n1); Assert.Equal(1, p1!.Index); Assert.False(p1.Shuffle); Assert.Equal("Mid", p1.List[1].Title);
        var (n2, p2) = IpodContent.Open("t:shuffle", songs); Assert.True(p2!.Shuffle); Assert.Null(n2);
        Assert.Equal((null, null), IpodContent.Open("t:99", songs)); Assert.Equal((null, null), IpodContent.Open("t:x", songs)); Assert.Equal((null, null), IpodContent.Open("zz", songs));

        var artists = IpodContent.Level("m:artists", lib, NoLists)!;
        var (ann, _) = IpodContent.Open("a:0", artists);                                   // two albums: All Songs, then each album
        Assert.Equal("Ann", ann!.Title); Assert.Equal(["All Songs", "First", "Second"], ann.Rows.Select(r => r.Label).ToArray()); Assert.Equal(["b:0", "b:1", "b:2"], ann.Rows.Select(r => r.Id).ToArray());
        var (all, _) = IpodContent.Open("b:0", ann); Assert.Equal(3, all!.Tracks!.Count); var (first, _) = IpodContent.Open("b:1", ann); Assert.Equal(2, first!.Tracks!.Count); Assert.Equal("First", first.Title);
        var (bob, _) = IpodContent.Open("a:1", artists); Assert.Equal(IpodKind.Songs, bob!.Kind); Assert.Equal("Bob", bob.Title);   // one album: straight to its songs
        var albums = IpodContent.Level("m:albums", lib, NoLists)!; var (alb, _) = IpodContent.Open("b:2", albums); Assert.Equal(IpodKind.Songs, alb!.Kind);
        var genres = IpodContent.Level("m:genres", lib, NoLists)!; var (rock, _) = IpodContent.Open("g:1", genres); Assert.Equal("Rock", rock!.Title); Assert.Equal(2, rock.Tracks!.Count);
        var cover = IpodContent.Level("m:cover", lib, NoLists)!; var (c0, _) = IpodContent.Open("c:0", cover); Assert.Equal(IpodKind.Songs, c0!.Kind);
    }

    [Fact] public void TimesReadLikeAnIpod()
    {
        Assert.Equal("0:00", IpodContent.Time(-5)); Assert.Equal("3:07", IpodContent.Time(187)); Assert.Equal("1:01:01", IpodContent.Time(3661));
        Assert.Equal("1 song", IpodContent.Count(1)); Assert.Equal("0 songs", IpodContent.Count(0)); Assert.Equal("12 songs", IpodContent.Count(12));
    }
}
