using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Ready-made playlists, like Spotify's made-for-you lists: searching "2010s", "rock" or an artist finds one (owner, 2 Nov).</summary>
public class MadePlaylistsTests
{
    static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    static int seq;

    static Track T(string title, string artist, int year, string genre = "Rock", string album = "LP", long dur = 200_000)
    {
        var n = Interlocked.Increment(ref seq);
        return new Track($"jf:{n:x32}", title, artist, album, artist, 1, 1, dur, year, $"art{n}", n, TrackSource.Jellyfin, Genre: genre, JellyfinId: $"{n:x32}");
    }

    /// <summary>[n] songs, each by its own artist unless [artists] says how many artists share them.</summary>
    static List<Track> Many(int n, int year, string genre, string prefix, int artists = 0) =>
        Enumerable.Range(0, n).Select(i => T($"{prefix} song {i}", artists > 0 ? $"{prefix} artist {i % artists}" : $"{prefix} artist {i}", year, genre)).ToList();

    static TasteProfile NoTaste(Library lib) => new(lib, [], [], [], Now);

    [Fact] public void ADecadeWithFifteenSongsGetsAList_FourteenDoesNot()
    {
        var lib = new Library([.. Many(15, 2014, "Pop", "a"), .. Many(14, 1995, "Pop", "b")]);
        var made = MadePlaylists.Build(lib, NoTaste(lib));
        var d = Assert.Single(made, m => m.Id.StartsWith("made:decade:"));
        Assert.Equal("made:decade:2010", d.Id);
        Assert.Equal("2010s", d.Name);
        Assert.Equal(15, d.Paths.Count);
    }

    [Fact] public void GenresUseTheRecommendersSpellingAndSkipSpokenWord()
    {
        var lib = new Library([.. Many(10, 2001, "rock", "a"), .. Many(10, 2002, " Rock ", "b"), .. Many(20, 2003, "Audiobook", "c"), .. Many(20, 2004, "Podcast", "d")]);
        var made = MadePlaylists.Build(lib, NoTaste(lib));
        var g = Assert.Single(made, m => m.Id.StartsWith("made:genre:"));
        Assert.Equal("made:genre:rock", g.Id);
        Assert.Equal("Rock", g.Name);
        Assert.Equal(20, g.Paths.Count);
        // spoken word does not count towards a decade either
        Assert.Equal(20, made.Single(m => m.Id == "made:decade:2000").Paths.Count);
    }

    [Fact] public void ABestOfNeedsEightSongsByTheLeadArtist()
    {
        var prodigy = Enumerable.Range(0, 8).Select(i => T($"Firestarter {i}", i == 0 ? "The Prodigy feat. Keith" : "The Prodigy", 1997)).ToList();
        var few = Enumerable.Range(0, 7).Select(i => T($"Few {i}", "Few", 1997)).ToList();
        var lib = new Library([.. prodigy, .. few]);
        var made = MadePlaylists.Build(lib, NoTaste(lib));
        var a = Assert.Single(made, m => m.Id.StartsWith("made:artist:"));
        Assert.Equal("made:artist:prodigy", a.Id);
        Assert.Equal("Best of The Prodigy", a.Name);
        Assert.Equal(8, a.Paths.Count);
    }

    [Fact] public void ListsAreCappedAndHoldThreeSongsPerArtistAtMost()
    {
        var lib = new Library([.. Many(200, 2012, "Pop", "x", artists: 10), .. Enumerable.Range(0, 40).Select(i => T($"solo {i}", "Solo", 1980, "Jazz"))]);
        var made = MadePlaylists.Build(lib, NoTaste(lib));
        var pop = made.Single(m => m.Id == "made:genre:pop");
        Assert.Equal(30, pop.Paths.Count);   // ten artists, three each
        Assert.All(pop.Paths.Select(p => lib.ByPath(p)!).GroupBy(t => t.Artist), g => Assert.True(g.Count() <= 3));
        var big = new Library(Many(120, 2012, "Pop", "y"));
        Assert.Equal(50, MadePlaylists.Build(big, NoTaste(big)).Single(m => m.Id == "made:decade:2010").Paths.Count);
        Assert.Equal(25, made.Single(m => m.Id == "made:artist:solo").Paths.Count);
    }

    [Fact] public void OneCopyOfEachSong()
    {
        var songs = Many(15, 2015, "Pop", "a");
        var copies = songs.Select(t => t with { Path = t.Path + "/nas", Album = "Other" }).ToList();
        var lib = new Library([.. songs, .. copies]);
        var d = MadePlaylists.Build(lib, NoTaste(lib)).Single(m => m.Id == "made:decade:2010");
        Assert.Equal(15, d.Paths.Count);
        Assert.Equal(15, d.Paths.Select(p => Matching.MatchKey(lib.ByPath(p)!)).Distinct().Count());
    }

    [Fact] public void TheSongsYouPlayComeFirst()
    {
        var songs = Many(20, 2016, "Pop", "a");
        var lib = new Library(songs);
        var loved = songs[13];
        var played = Enumerable.Range(0, 5).Select(i => new PlayedEntry(loved.Path, loved.Title, loved.Artist, new DateTimeOffset(Now.AddDays(-i)).ToUnixTimeMilliseconds())).ToList();
        var taste = new TasteProfile(lib, played, [], [], Now);
        Assert.Equal(loved.Path, MadePlaylists.Build(lib, taste).Single(m => m.Id == "made:decade:2010").Paths[0]);
    }

    [Fact] public void TheOrderIsTheSameEveryTime()
    {
        var lib = new Library(Many(40, 2011, "Pop", "a"));
        var a = MadePlaylists.Build(lib, NoTaste(lib)).Single(m => m.Id == "made:decade:2010").Paths;
        var b = MadePlaylists.Build(new Library(lib.Songs.Reverse().ToList()), NoTaste(lib)).Single(m => m.Id == "made:decade:2010").Paths;
        Assert.Equal(a, b);
        // not just the library's own order
        Assert.NotEqual(lib.Songs.Take(40).Select(t => t.Path), a);
    }

    [Fact] public void TheListCarriesItsFirstSongsPictureAndASubtitle()
    {
        var lib = new Library(Many(15, 2014, "Pop", "a"));
        var d = MadePlaylists.Build(lib, NoTaste(lib)).Single(m => m.Id == "made:decade:2010");
        Assert.Equal(lib.ByPath(d.Paths[0])!.ArtKey, d.ArtKey);
        Assert.Equal("15 songs · made for you", d.Subtitle);
    }

    static Library Mixed() => new([
        .. Many(20, 2013, "Rock", "r"), .. Many(20, 1994, "Pop", "p"), .. Many(20, 2005, "Alternative Rock", "alt"),
        .. Enumerable.Range(0, 10).Select(i => T($"Breathe {i}", "The Prodigy", 1997, "Electronic")),
        .. Enumerable.Range(0, 10).Select(i => T($"Rocket {i}", "Rockets", 1999, "Electronic")),
    ]);

    [Theory]
    [InlineData("2010s", "made:decade:2010")]
    [InlineData("2010", "made:decade:2010")]
    [InlineData("2013", "made:decade:2010")]
    [InlineData("90s", "made:decade:1990")]
    [InlineData("rock", "made:genre:rock")]
    [InlineData("ROCK", "made:genre:rock")]
    [InlineData("prodigy", "made:artist:prodigy")]
    [InlineData("best of prodigy", "made:artist:prodigy")]
    [InlineData("the prodigy", "made:artist:prodigy")]
    [InlineData("rock playlist", "made:genre:rock")]
    public void SearchFindsTheList(string q, string id)
    {
        var lib = Mixed();
        var found = MadePlaylists.Search(MadePlaylists.Build(lib, NoTaste(lib)), q);
        Assert.Equal(id, found.First().Id);
    }

    [Fact] public void SearchPutsTheClosestNameFirstAndShowsSixAtMost()
    {
        var lib = Mixed();
        var found = MadePlaylists.Search(MadePlaylists.Build(lib, NoTaste(lib)), "rock");
        Assert.Contains(found, m => m.Id == "made:genre:alternative rock");
        Assert.Equal("made:genre:rock", found[0].Id);
        var many = new Library(Enumerable.Range(0, 10).SelectMany(g => Many(15, 2000 + g, $"Rock {g}", $"g{g}")).ToList());
        Assert.Equal(6, MadePlaylists.Search(MadePlaylists.Build(many, NoTaste(many)), "rock").Count);
    }

    [Fact] public void SearchFindsNothingForNoiseOrBlank()
    {
        var lib = Mixed();
        var all = MadePlaylists.Build(lib, NoTaste(lib));
        Assert.Empty(MadePlaylists.Search(all, ""));
        Assert.Empty(MadePlaylists.Search(all, "zzzz"));
        Assert.Empty(MadePlaylists.Search(all, "playlist"));
    }

    [Fact] public void ADislikedSongIsLeftOut()
    {
        var songs = Many(16, 2016, "Pop", "a");
        var lib = new Library(songs);
        var bad = songs[3];
        var taste = new TasteProfile(lib, [], [], [], Now, events: [new ListenEvent("x", ListenKind.ThumbDown, new DateTimeOffset(Now).ToUnixTimeMilliseconds(), bad.Path, bad.Title, bad.Artist)]);
        var d = MadePlaylists.Build(lib, taste).Single(m => m.Id == "made:decade:2010");
        Assert.DoesNotContain(bad.Path, d.Paths);
    }

    [Fact] public void FindByIdAndNothingForAnUnknownOne()
    {
        var lib = Mixed();
        var all = MadePlaylists.Build(lib, NoTaste(lib));
        Assert.Equal("Rock", MadePlaylists.Find(all, "made:genre:rock")!.Name);
        Assert.Null(MadePlaylists.Find(all, "made:genre:polka"));
    }

    static UserSession Session(IEnumerable<Track> songs) { var s = new UserSession("local", null, null); s.AddDownloaded(songs); return s; }

    [Fact] public void SavingMakesAnOrdinaryPlaylistOfTheSongs()
    {
        var s = Session(Many(20, 2013, "Rock", "r"));
        var rock = MadePlaylists.Find(MadePlaylists.For(s, null, Now), "made:genre:rock")!;
        var pl = MadePlaylists.SaveTo(s, rock);
        Assert.Equal("Rock", pl.Name);
        Assert.Equal(rock.Paths, pl.Entries.Select(e => e.Path));
        Assert.Contains(s.Playlists, p => p.Id == pl.Id);
    }

    [Fact] public void SavingTwiceKeepsBothUnderTheirOwnNames()
    {
        var s = Session(Many(20, 2013, "Rock", "r"));
        var rock = MadePlaylists.Find(MadePlaylists.For(s, null, Now), "made:genre:rock")!;
        MadePlaylists.SaveTo(s, rock);
        Assert.Equal("Rock 2", MadePlaylists.SaveTo(s, rock).Name);
    }

    [Fact] public void TheListsAreKeptUntilTheLibraryChanges()
    {
        var s = Session(Many(20, 2013, "Rock", "r"));
        var reads = 0;
        IReadOnlyList<ListenEvent>? Log() { reads++; return null; }
        var a = MadePlaylists.Cached(s, Log, Now);
        Assert.Same(a, MadePlaylists.Cached(s, Log, Now.AddMinutes(5)));
        Assert.Equal(1, reads);
        s.AddDownloaded(Many(15, 1985, "Pop", "p"));
        Assert.Contains(MadePlaylists.Cached(s, Log, Now.AddMinutes(5)), m => m.Id == "made:decade:1980");
        Assert.NotSame(a, MadePlaylists.Cached(s, Log, Now.AddMinutes(30)));
    }
}
