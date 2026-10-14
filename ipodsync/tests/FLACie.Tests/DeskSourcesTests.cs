using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class DeskSourcesTests
{
    static Track T(string title, string? art = null) =>
        new("p/" + title, title, "Ann", "First", "Ann", 1, 1, 200_000, 2000, art, 0, TrackSource.Jellyfin);

    static PlaylistEntry E(Track t) => new(t.Path, t.Title, t.Artist);
    static Playlist P(string id, params Track[] ts) => new(id, "List " + id, 0, null, ts.Select(E).ToList());

    // resolves an entry to its track by path; anything not in the known set is dropped (as UserSession.Resolve does for songs that are gone)
    static Func<IEnumerable<PlaylistEntry>, IReadOnlyList<Track>> Resolver(params Track[] known) =>
        es => es.Select(e => known.FirstOrDefault(k => k.Path == e.Path)).OfType<Track>().ToList();

    static readonly Track A = T("a", "ka"), B = T("b", "kb"), C = T("c", "kc"), NoArt = T("n");

    [Fact] public void UpNextOnlyWhilePlayingAndStartsAtTheCurrentSong()
    {
        var s = DeskSources.Build([A, B, C], 1, [], [], Resolver());
        var up = Assert.Single(s);
        Assert.Equal("upnext", up.Id); Assert.Equal("Up Next", up.Label); Assert.Equal("2 songs", up.Sub); Assert.Equal("kb", up.ArtKey);
        Assert.Equal([B, C], up.Tracks);
        Assert.Empty(DeskSources.Build([], -1, [], [], Resolver()));
        Assert.Empty(DeskSources.Build([A, B], -1, [], [], Resolver()));
    }

    [Fact] public void OneSongSaysSong() => Assert.Equal("1 song", DeskSources.Build([A, B], 1, [], [], Resolver())[0].Sub);

    [Fact] public void FavouritesSkippedWhenEmpty()
    {
        Assert.Empty(DeskSources.Build([], -1, [], [], Resolver(A, B)));
        var fav = Assert.Single(DeskSources.Build([], -1, [E(A), E(B)], [], Resolver(A, B)));
        Assert.Equal("favorites", fav.Id); Assert.Equal("Favourites", fav.Label); Assert.Equal("2 songs", fav.Sub);
        Assert.Empty(DeskSources.Build([], -1, [E(A)], [], Resolver()));   // favourites that resolve to nothing are skipped too
    }

    [Fact] public void PlaylistsInOrderSkippingEmptyOnes()
    {
        var s = DeskSources.Build([], -1, [], [P("P1", A, B), P("P2", C), P("P3", A)], Resolver(A, B));
        Assert.Equal(["pl:P1", "pl:P3"], s.Select(x => x.Id).ToArray());
        Assert.Equal(["List P1", "List P3"], s.Select(x => x.Label).ToArray());
        Assert.Equal("2 songs", s[0].Sub);
    }

    [Fact] public void ArtIsTheFirstTrackThatHasArt()
    {
        var s = DeskSources.Build([], -1, [], [P("x", NoArt, B, C), P("y", NoArt)], Resolver(NoArt, B, C));
        Assert.Equal("kb", s[0].ArtKey); Assert.Null(s[1].ArtKey);
    }

    [Fact] public void OrderIsUpNextFavouritesThenPlaylists() =>
        Assert.Equal(["upnext", "favorites", "pl:P1"], DeskSources.Build([A], 0, [E(B)], [P("P1", C)], Resolver(A, B, C)).Select(x => x.Id).ToArray());

    [Fact] public void ActiveIdReadsTheDeskContext()
    {
        Assert.Equal("pl:P1", DeskSources.ActiveId("desk:pl:P1", true));
        Assert.Equal("upnext", DeskSources.ActiveId("album:x", true));
        Assert.Equal("upnext", DeskSources.ActiveId(null, true));
        Assert.Null(DeskSources.ActiveId(null, false));
        Assert.Null(DeskSources.ActiveId("desk:pl:P1", false));
    }
}
