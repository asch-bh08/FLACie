using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class DeskSourcesTests
{
    static Track T(string title, string album, string artist = "Ann", string? art = null) =>
        new("p/" + title, title, artist, album, artist, 1, 1, 200_000, 2000, art ?? "k-" + album, 0, TrackSource.Jellyfin);

    static int[] Seq(int n) => Enumerable.Range(0, n).ToArray();

    [Fact] public void NextSkipsTheCurrentAlbumAndRepeats()
    {
        Track[] q = [T("a1", "A"), T("a2", "A"), T("b1", "B"), T("b2", "B"), T("c1", "C"), T("a3", "A"), T("d1", "D")];
        var next = DeskSources.Next(q, Seq(q.Length), 0);
        Assert.Equal(["b", "c", "d"], next.Select(n => n.Album.ToLowerInvariant()).ToArray());
        Assert.Equal([2, 4, 6], next.Select(n => n.QueueIndex).ToArray());
        Assert.Equal("b1", next[0].First.Title); Assert.Equal("k-B", next[0].ArtKey); Assert.Equal("Ann", next[0].Artist);
    }

    [Fact] public void FollowsPlayOrderNotQueueOrder()
    {
        Track[] q = [T("a", "A"), T("b", "B"), T("c", "C")];
        var next = DeskSources.Next(q, [2, 0, 1], 2);   // playing C first, then A, then B
        Assert.Equal(["A", "B"], next.Select(n => n.Album).ToArray());
        Assert.Equal([0, 1], next.Select(n => n.QueueIndex).ToArray());
    }

    [Fact] public void ShortAndEmptyQueues()
    {
        Assert.Empty(DeskSources.Next([], [], -1));
        Track[] one = [T("a1", "A"), T("a2", "A")];
        Assert.Empty(DeskSources.Next(one, Seq(2), 0));
        Track[] many = [T("a", "A"), T("b", "B"), T("c", "C"), T("d", "D"), T("e", "E")];
        Assert.Equal(2, DeskSources.Next(many, Seq(5), 0, count: 2).Count);
        Assert.Empty(DeskSources.Next(many, Seq(5), 4));   // the last song: nothing after it
        Assert.Empty(DeskSources.Next(many, Seq(5), 9));   // an index outside the queue
    }

    [Fact] public void AnOrderThatDoesNotMatchTheQueueFallsBackToQueueOrder()
    {
        Track[] q = [T("a", "A"), T("b", "B"), T("c", "C")];
        Assert.Equal(["B", "C"], DeskSources.Next(q, [0, 1], 0).Select(n => n.Album).ToArray());
    }

    [Fact] public void RepeatAllCarriesOnFromTheStartOfThePlayOrder()
    {
        Track[] q = [T("a1", "A"), T("b1", "B"), T("c1", "C")];
        Assert.Empty(DeskSources.Next(q, Seq(3), 2));                                                   // the end of the queue, repeat off
        Assert.Equal(["A", "B"], DeskSources.Next(q, Seq(3), 2, wrap: true).Select(n => n.Album).ToArray());   // repeat all: the queue again
        Assert.Equal([0, 1], DeskSources.Next(q, Seq(3), 2, wrap: true).Select(n => n.QueueIndex).ToArray());
        Assert.Equal(["C", "A"], DeskSources.Next(q, Seq(3), 1, wrap: true).Select(n => n.Album).ToArray());   // each album once
        Track[] one = [T("a1", "A"), T("a2", "A")];
        var again = DeskSources.Next(one, Seq(2), 1, wrap: true);                                         // one album on repeat: it comes round again
        Assert.Equal("A", Assert.Single(again).Album); Assert.Equal(0, again[0].QueueIndex);
    }

    [Fact] public void EachAlbumCarriesItsSongsInAlbumOrder()
    {
        // a shuffled queue: the album's songs are spread over it; the CD holds them all, disc by disc and track by track
        Track Tn(string title, string album, int disc, int no) => T(title, album) with { DiscNo = disc, TrackNo = no };
        Track[] q = [Tn("x", "X", 1, 1), Tn("b3", "B", 1, 3), Tn("c1", "C", 1, 1), Tn("b1", "B", 1, 1), Tn("b21", "B", 2, 1), Tn("b2", "B", 1, 2)];
        var b = DeskSources.Next(q, Seq(q.Length), 0)[0];
        Assert.Equal("B", b.Album);
        Assert.Equal([3, 5, 1, 4], b.Tracks.ToArray());
    }

    [Fact] public void AlbumKeyIgnoresCaseAndFallsBackToPath()
    {
        Assert.Equal(DeskSources.AlbumKey(T("x", "Album", "Ann")), DeskSources.AlbumKey(T("y", "ALBUM", "ann")));
        Assert.NotEqual(DeskSources.AlbumKey(T("x", "Album", "Ann")), DeskSources.AlbumKey(T("y", "Album", "Bob")));
        Assert.Equal("p/z", DeskSources.AlbumKey(T("z", "")));
    }
}
