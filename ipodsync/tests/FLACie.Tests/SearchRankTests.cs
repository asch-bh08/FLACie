using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F6: the artist you mean is the top result: the one you play and have most.</summary>
public class SearchRankTests
{
    static Group Artist(string name, int songs) => new(name, [.. Enumerable.Range(0, songs).Select(i =>
        new Track($"/m/{name}/{i}", $"Song {i}", name, "A", name, i, 1, 200000, 2000, null, 1, TrackSource.Jellyfin, JellyfinId: $"{name}-{i}"))], null);

    [Fact]
    public void Prodigy_puts_The_Prodigy_above_Prodigy()
    {
        var prodigy = Artist("Prodigy", 15); var the = Artist("The Prodigy", 171);
        Assert.Same(the, SearchRankTop.Artist([prodigy, the], "prodigy", [new("The Prodigy-3", 40, 0, false)]));
        Assert.Same(the, SearchRankTop.Artist([prodigy, the], "prodigy", []));
    }

    [Fact]
    public void Plays_beat_size()
    {
        var prodigy = Artist("Prodigy", 15); var the = Artist("The Prodigy", 171);
        Assert.Same(prodigy, SearchRankTop.Artist([the, prodigy], "prodigy", [new("Prodigy-1", 30, 0, false)]));
    }

    [Fact]
    public void Only_an_artist_named_like_the_search_is_a_top_result()
    {
        Assert.Null(SearchRankTop.Artist([Artist("Prodigy of Mobb Deep", 5)], "prodigy", []));
        Assert.Null(SearchRankTop.Artist([Artist("The Prodigy", 5)], "the", []));
    }

    static Track S(string title, string album, int year, string id) => new($"/m/tp/{id}", title, "The Prodigy", album, "The Prodigy", 1, 1, 200000, year, null, 1, TrackSource.Jellyfin, JellyfinId: id);

    [Fact]
    public void The_artist_page_has_top_five_popular_albums_and_singles_newest_first()
    {
        var fat = new[] { "Smack My Bitch Up", "Breathe", "Diesel Power", "Funky Shit", "Serial Thrilla", "Mindfields" }.Select((t, i) => S(t, "The Fat of the Land", 1997, "f" + i)).ToList();
        var jilted = new[] { "Voodoo People", "Poison", "No Good", "One Love" }.Select((t, i) => S(t, "Music for the Jilted Generation", 1994, "j" + i)).ToList();
        var single = new[] { S("Firestarter", "Firestarter", 1996, "x1"), S("Firestarter (Instrumental)", "Firestarter", 1996, "x2") };
        var ep = Enumerable.Range(0, 5).Select(i => S($"Track {i}", "Charly EP", 1991, "e" + i)).ToList();
        var all = fat.Concat(jilted).Concat(single).Concat(ep).ToList();
        var artist = new Group("The Prodigy", all, null);
        var albums = all.GroupBy(t => t.Album).Select(g => new Group(g.Key, g.ToList(), null)).ToList();
        var page = ArtistPage.Of(artist, albums, [new("j0", 50, 0, false), new("x1", 40, 0, false), new("f0", 30, 0, false)]);
        Assert.Equal(["Voodoo People", "Firestarter", "Smack My Bitch Up"], page.Top.Take(3).Select(t => t.Title));
        Assert.Equal(5, page.Top.Count);
        Assert.Equal(10, page.Popular.Count);
        Assert.DoesNotContain(page.Popular, t => page.Top.Contains(t));
        Assert.Equal(["The Fat of the Land", "Music for the Jilted Generation"], page.Albums.Select(a => a.Name));
        Assert.Equal(["Firestarter", "Charly EP"], page.Singles.Select(a => a.Name));
    }

    [Fact]
    public void Unplayed_songs_lead_with_the_ones_on_several_albums_and_no_album_fills_the_top()   // owner, 5 Nov: the top was all No Tourists
    {
        var tourists = new[] { "Need Some1", "Light Up the Sky", "We Live Forever", "No Tourists", "Fight Fire with Fire", "Timebomb Zone" }.Select((t, i) => S(t, "No Tourists", 2018, "n" + i)).ToList();
        var fat = new[] { S("Breathe", "The Fat of the Land", 1997, "f1"), S("Firestarter", "The Fat of the Land", 1997, "f2"), S("Narayan", "The Fat of the Land", 1997, "f3") };
        var law = new[] { S("Breathe", "Their Law: The Singles", 2005, "l1"), S("Firestarter", "Their Law: The Singles", 2005, "l2"), S("Omen", "Their Law: The Singles", 2005, "l3") };
        var all = tourists.Concat(fat).Concat(law).ToList();
        var page = ArtistPage.Of(new Group("The Prodigy", all, null), all.GroupBy(t => t.Album).Select(g => new Group(g.Key, g.ToList(), null)), []);
        Assert.Equal(["Breathe", "Firestarter"], page.Top.Take(2).Select(t => t.Title).Order());   // on two albums each: the hits
        Assert.True(page.Top.Count(t => t.Album == "No Tourists") <= 2);
        Assert.Equal(page.Top.Concat(page.Popular).Count(), page.Top.Concat(page.Popular).Select(t => t.Title).Distinct().Count());   // each song once
    }
}
