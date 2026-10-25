using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FLACie.Tests;

/// <summary>/api/listen: each account keeps its own log, and a new account's log starts from what it has already played and liked.</summary>
public class ListenApiTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "flacie-listenapi-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    static Track Song(string title, string artist, string jf) => new($"http://jf/Audio/{jf}/stream", title, artist, "Al", artist, 1, 1, 200_000, 2020, null, 0, TrackSource.Jellyfin, null, jf);
    static UserSession Person(string id) => new("local", null, null) { Id = id };
    static ListenEvent Play(string id) => new(id, ListenKind.Play, 1000, "/m/a.flac", "A", "X", 100, 200);
    ListenLogStore Store() => new(new DataPaths(dir));

    [Fact] public void Listen_without_a_session_is_401()
    {
        var r = ListenApi.Accept(Store(), null, [Play("1")]);
        Assert.Equal(401, ((IStatusCodeHttpResult)r).StatusCode);
    }

    [Fact] public void Listen_keeps_events_for_that_account_only()
    {
        var store = Store(); var ann = Person("ann"); var bob = Person("bob");
        ListenApi.Accept(store, ann, [Play("a1")]);
        ListenApi.Accept(store, bob, [Play("b1"), Play("b2")]);
        Assert.Equal(["a1"], store.Read(ann).Select(e => e.Id));
        Assert.Equal(["b1", "b2"], store.Read(bob).Select(e => e.Id).Order());
    }

    [Fact] public void Seed_turns_favourites_into_hearts_and_jellyfin_counts_into_finished_plays()
    {
        var a = Song("One", "Ann", "a".PadRight(32, 'a')); var b = Song("Two", "Bea", "b".PadRight(32, 'b'));
        var lib = new Library([a, b]);
        var seed = ListenSeed.From([], [new PlaylistEntry(a.Path, a.Title, a.Artist)], [new JfPlay(b.JellyfinId!, 3, 5000, false), new JfPlay("missing", 2, 0, false)], lib);
        Assert.Single(seed, e => e.Kind == ListenKind.Heart && e.Path == a.Path);
        var plays = seed.Where(e => e.Kind == ListenKind.Play).ToList();
        Assert.Equal(3, plays.Count);
        Assert.All(plays, e => { Assert.Equal(b.Path, e.Path); Assert.True(ListenClass.IsFinish(e)); });
        Assert.Equal(seed.Count, seed.Select(e => e.Id).Distinct().Count());
    }

    [Fact] public void Seed_includes_the_play_history()
    {
        var a = Song("One", "Ann", "a".PadRight(32, 'a'));
        var seed = ListenSeed.From([new PlayedEntry(a.Path, a.Title, a.Artist, 777)], [], [], new Library([a]));
        var p = Assert.Single(seed);
        Assert.Equal((ListenKind.Play, 777L), (p.Kind, p.At));
    }

    [Fact] public void Seed_runs_once()
    {
        var store = Store(); var ann = Person("ann");
        var a = Song("One", "Ann", "a".PadRight(32, 'a'));
        var seed = ListenSeed.From([], [new PlaylistEntry(a.Path, a.Title, a.Artist)], [], new Library([a]));
        ListenApi.Accept(store, ann, [Play("1")], () => seed);
        var n = store.Read(ann).Count;
        Assert.Equal(2, n);
        ListenApi.Accept(store, ann, [Play("2")], () => seed);
        Assert.Equal(n + 1, store.Read(ann).Count);
    }

    [Fact] public void New_account_with_no_history_gets_an_empty_log_not_an_error()
    {
        var store = Store(); var ann = Person("ann");
        var r = ListenApi.Accept(store, ann, [], () => ListenSeed.From([], [], [], Library.Empty));
        Assert.Equal(200, ((IStatusCodeHttpResult)r).StatusCode);
        Assert.Empty(store.Read(ann));
    }
}
