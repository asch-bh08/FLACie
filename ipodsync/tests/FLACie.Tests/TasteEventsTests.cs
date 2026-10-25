using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Taste learned from what a person did: skips, finishes, hearts, thumbs-down, and the playlists they play from.</summary>
public class TasteEventsTests
{
    static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    static long Ago(double days) => new DateTimeOffset(Now.AddDays(-days)).ToUnixTimeMilliseconds();

    static Track Song(string title, string artist, string album = "Album") =>
        new($"jf:{title}", title, artist, album, artist, 1, 1, 200_000, 2001, null, 0, TrackSource.Jellyfin, Genre: "Pop");

    static ListenEvent Play(Track t, double heard, double days = 1, string? ctx = "autoplay", string? id = null) =>
        new(id ?? Guid.NewGuid().ToString(), ListenKind.Play, Ago(days), t.Path, t.Title, t.Artist, heard, 200, ctx);
    static ListenEvent Did(ListenKind k, Track t, double days = 1) => new(Guid.NewGuid().ToString(), k, Ago(days), t.Path, t.Title, t.Artist);

    static TasteProfile Taste(Library lib, IReadOnlyList<ListenEvent> events, IReadOnlyList<Playlist>? playlists = null) =>
        new(lib, [], [], playlists ?? [], Now, events: events);

    [Fact]
    public void A_finish_counts_more_than_a_skip()
    {
        var a = Song("A", "One"); var b = Song("B", "Two");
        var taste = Taste(new Library([a, b]), [Play(a, 200), Play(b, 10)]);
        Assert.True(taste.Known(a) > taste.Known(b));
    }

    [Fact]
    public void Three_skips_push_a_song_down()
    {
        var a = Song("A", "One"); var b = Song("B", "One");
        var taste = Taste(new Library([a, b]), [Play(a, 5), Play(a, 5, 2), Play(a, 5, 3), Play(b, 120)]);
        Assert.True(taste.Penalty(a) >= 0.5, $"penalty {taste.Penalty(a)}");
        Assert.Equal(0, taste.Penalty(b));
        // skips months ago are forgiven
        var old = Taste(new Library([a]), [Play(a, 5, 200), Play(a, 5, 201), Play(a, 5, 202)]);
        Assert.True(old.Penalty(a) < 0.05);
    }

    [Fact]
    public void Thumbs_down_is_a_full_penalty()
    {
        var a = Song("A", "One"); var b = Song("B", "One");
        var taste = Taste(new Library([a, b]), [Play(a, 200, 5), Did(ListenKind.ThumbDown, a)]);
        Assert.Equal(1, taste.Penalty(a));
        Assert.True(taste.Disliked(a));
        Assert.False(taste.Disliked(b));
        Assert.True(taste.Penalty(b) < 1);
    }

    [Fact]
    public void Less_like_this_marks_the_artist_down_not_only_the_song()
    {
        var a = Song("A", "One"); var b = Song("B", "One feat. Guest"); var c = Song("C", "Other");
        var taste = Taste(new Library([a, b, c]), [Did(ListenKind.LessLikeThis, a)]);
        Assert.True(taste.Penalty(a) > 0);
        Assert.True(taste.Penalty(b) > 0);
        Assert.Equal(0, taste.Penalty(c));
        Assert.False(taste.Disliked(a));
    }

    [Fact]
    public void A_shared_playlist_counts_only_for_who_plays_it()
    {
        // the family's seven playlists sit on one account; Dad plays AA only, the owner plays all seven
        var lists = Enumerable.Range(0, 7).Select(i =>
        {
            var name = i == 0 ? "AA" : "P" + i;
            var s = Song(name + " song", name + " artist", name + " album");
            return (List: new Playlist("pl" + i, name, 0, null, [new PlaylistEntry(s.Path, s.Title, s.Artist)]), Song: s);
        }).ToList();
        var lib = new Library(lists.Select(x => x.Song).ToList());
        var playlists = lists.Select(x => x.List).ToList();
        var other = Song("Elsewhere", "Nobody");

        var dad = Taste(lib, [Play(other, 200, 1, "pl0")], playlists);
        Assert.True(dad.Known(lists[0].Song) > 0);
        for (var i = 1; i < 7; i++) Assert.Equal(0, dad.Known(lists[i].Song));

        var owner = Taste(lib, lists.Select(x => Play(other, 200, 1, x.List.Id)).ToList(), playlists);
        foreach (var x in lists) Assert.True(owner.Known(x.Song) > 0);

        // nobody plays from them: being on the account is not enough
        var none = Taste(lib, [], playlists);
        foreach (var x in lists) Assert.Equal(0, none.Known(x.Song));
    }

    [Fact]
    public void Likes_is_hearted_or_finished_three_times()
    {
        var a = Song("A", "One"); var b = Song("B", "Two"); var c = Song("C", "Three"); var d = Song("D", "Four"); var e = Song("E", "Five");
        var taste = Taste(new Library([a, b, c, d, e]), [
            Did(ListenKind.Heart, a),
            Play(b, 200, 1), Play(b, 195, 10), Play(b, 190, 20),
            Play(c, 200, 1), Play(c, 200, 2),
            Play(d, 200, 100), Play(d, 200, 120), Play(d, 200, 130),
            Did(ListenKind.Heart, e, 3), Did(ListenKind.Unheart, e, 2),
        ]);
        Assert.True(taste.Likes(a));
        Assert.True(taste.Likes(b));
        Assert.False(taste.Likes(c));
        Assert.False(taste.Likes(d));
        Assert.False(taste.Likes(e));
    }

    [Fact]
    public void A_folded_day_is_neither_a_skip_nor_a_finish()
    {
        var a = Song("A", "One");
        var day = new ListenEvent("day:2026-01-01:" + a.Path, ListenKind.Play, Ago(1), a.Path, a.Title, a.Artist, 20, 600, "autoplay");
        var taste = Taste(new Library([a]), [day, day with { Id = "day:2026-01-02:" + a.Path }, day with { Id = "day:2026-01-03:" + a.Path }]);
        Assert.Equal(0, taste.Penalty(a));
        Assert.True(taste.Known(a) > 0);
    }
}
