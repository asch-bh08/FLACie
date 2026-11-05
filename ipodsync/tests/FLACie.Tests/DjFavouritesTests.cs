using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>F22: "play my favourite prodigy songs": real favourites without hearting anything.</summary>
public class DjFavouritesTests
{
    static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    static Track T(string title, string artist, int year = 1997, string genre = "Electronic") =>
        new($"/p/{artist}/{title}", title, artist, artist + " LP", artist, 1, 1, 200_000, year, null, 1, TrackSource.Jellyfin, JellyfinId: Guid.NewGuid().ToString("N"), Genre: genre);
    static ListenEvent Play(Track t, double heard, double daysAgo = 1) =>
        new(Guid.NewGuid().ToString("N"), ListenKind.Play, new DateTimeOffset(Now.AddDays(-daysAgo)).ToUnixTimeMilliseconds(), t.Path, t.Title, t.Artist, heard, 200);

    static (DjAskUnderstandTests.User U, List<Track> Prodigy) Library(int prodigySongs = 20)
    {
        var u = new DjAskUnderstandTests.User();
        var prodigy = Enumerable.Range(0, prodigySongs).Select(i => T($"Track {i:00}", "The Prodigy")).ToList();
        u.Library.AddRange(prodigy);
        u.Library.AddRange([T("Toxic", "Britney Spears", 2003, "Pop"), T("Wannabe", "Spice Girls", 1996, "Pop"), T("Firework", "Katy Perry", 2010, "Pop")]);
        for (var i = 0; i < prodigySongs; i++) u.Pop[prodigy[i].Path] = 1000 - i * 10;   // Track 00 best known
        return (u, prodigy);
    }

    static Task<DjAskAnswer> Ask(DjAskUnderstandTests.User u, string said) => new DjAskEngine(clock: () => Now).AnswerAsync(u, said, null, []);

    [Fact]
    public async Task Prodigy_favourites_by_real_plays_hearted_first_early_skips_last()
    {
        var (u, p) = Library();
        u.Log.AddRange([Play(p[5], 200), Play(p[5], 190), Play(p[5], 180)]);             // three real plays
        u.Log.AddRange([Play(p[7], 200), Play(p[7], 200, 40)]);                          // two, one of them old
        u.Log.Add(Play(p[9], 130));                                                      // one real play
        u.Log.Add(Play(p[11], 90));                                                      // 45 %: counts for nothing
        u.Log.AddRange([Play(p[0], 10), Play(p[0], 12)]);                                // the best known one, skipped early twice
        u.Hearts.Add(p[3]);                                                              // hearted, never played
        var a = await Ask(u, "play my favourite prodigy songs");
        Assert.Equal(DjAskDo.Play, a.Do);
        var titles = a.Paths!.Select(x => u.Find(x)!.Title).ToList();
        Assert.Equal(["Track 03", "Track 05", "Track 07", "Track 09"], titles.Take(4));
        Assert.DoesNotContain("Track 00", titles);                                       // F26.4: skipped early twice: left out
        Assert.All(titles, t => Assert.StartsWith("Track", t));                           // only The Prodigy
        Assert.Equal(DjFavourites.Fill, titles.Count);
    }

    [Fact]
    public async Task With_few_plays_it_fills_to_15_with_the_most_popular()
    {
        var (u, p) = Library();
        u.Log.AddRange([Play(p[12], 200), Play(p[12], 200)]);
        var a = await Ask(u, "play my favourite the prodigy songs");
        var titles = a.Paths!.Select(x => u.Find(x)!.Title).ToList();
        Assert.Equal(15, titles.Count);
        Assert.Equal("Track 12", titles[0]);
        Assert.Equal(["Track 00", "Track 01", "Track 02"], titles.Skip(1).Take(3));     // then the best known
        Assert.StartsWith("Your favourite The Prodigy song first, then their biggest hits", a.Say);
    }

    [Fact]
    public async Task Ninetees_songs_and_my_favourites_work()
    {
        var (u, p) = Library(5);
        u.Log.AddRange([Play(u.Library.First(t => t.Title == "Wannabe"), 200), Play(u.Library.First(t => t.Title == "Wannabe"), 200)]);
        var nineties = await Ask(u, "play my favourite 90s songs");
        var titles = nineties.Paths!.Select(x => u.Find(x)!.Title).ToList();
        Assert.Equal("Wannabe", titles[0]);
        Assert.DoesNotContain("Toxic", titles); Assert.DoesNotContain("Firework", titles);
        Assert.Equal(1990, DjFavourites.Decade("nineties")); Assert.Equal(2000, DjFavourites.Decade("00s")); Assert.Equal(1980, DjFavourites.Decade("1980s"));
        var all = await Ask(u, "play my favourites");
        Assert.Equal(DjAskDo.Play, all.Do);
        Assert.Equal("Wannabe", u.Find(all.Paths![0])!.Title);
    }

    [Theory]
    [InlineData("play my favourite prodigy songs", "prodigy")]
    [InlineData("my favorite 90s tracks", "90s")]
    [InlineData("play my favourite songs by the prodigy", "the prodigy")]
    public void The_rule_reads_what_the_favourites_are_of(string said, string what)
    {
        var p = DjAskRules.Parse(said)!;
        Assert.Equal(AskIntent.FavouritesOf, p.Intent);
        Assert.Equal(what, p.What);
    }

    [Fact]
    public async Task Jellyfins_play_counts_count_the_fill_is_hits_not_the_alphabet_and_the_dj_says_so()
    {
        var u = new DjAskUnderstandTests.User();
        // twenty songs: titles in alphabetical order, none known to Deezer except two
        var p = Enumerable.Range(0, 20).Select(i => T($"{(char)('A' + i)} Song", "The Prodigy", 1990 + i % 5)).ToList();
        u.Library.AddRange(p);
        u.JfPlays[p[17].Path] = 40; u.JfPlays[p[12].Path] = 9; u.JfPlays[p[4].Path] = 1;     // played in other apps, before FLACie's log
        u.Pop[p[19].Path] = 900; u.Pop[p[15].Path] = 500;                                    // the hits Deezer knows
        // Q Song is on a best-of too: a hit by the albums it is on
        u.Library.Add(new Track("/p/The Prodigy/Q Song (best of)", "Q Song", "The Prodigy", "Their Law: The Singles", "The Prodigy", 1, 1, 200_000, 2005, null, 1, TrackSource.Jellyfin, JellyfinId: "x"));
        u.Log.AddRange([Play(p[2], 10), Play(p[2], 8), Play(p[2], 12)]);                     // C Song skipped three times, never played
        var a = await Ask(u, "play my favourite prodigy songs");
        var titles = a.Paths!.Select(x => u.Find(x)!.Title).ToList();
        Assert.Equal(["R Song", "M Song", "E Song"], titles.Take(3));                         // Jellyfin's counts: 40, 9, then the one play
        Assert.Equal(["T Song", "P Song", "Q Song"], titles.Skip(3).Take(3));                 // hits: Deezer's, then on several albums
        Assert.DoesNotContain("C Song", titles);
        Assert.Equal(DjFavourites.Fill, titles.Count);
        var rest = titles.Skip(6).ToList();                                                  // then by year, newest first: not A, B, D...
        Assert.NotEqual(rest.Order(StringComparer.Ordinal), rest);
        Assert.Equal(1994, u.Find(a.Paths![6])!.Year);
        Assert.Equal("You've played 3 of their songs a lot; then their biggest hits.", a.Say);
    }
}
