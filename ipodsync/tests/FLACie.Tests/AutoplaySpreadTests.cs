using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class AutoplaySpreadTests
{
    static List<string> S(params string[] artists) => artists.ToList();

    [Fact]
    public void After_one_song_of_an_artist_at_most_two_more_of_theirs_follow()
    {
        // owner, 12 Nov: "Dreamer" then about ten Livin' Joy songs he did not know
        var next = AutoplaySpread.Spread(S("Livin' Joy", "Livin' Joy", "Livin' Joy", "Haddaway", "Livin' Joy", "Corona", "Livin' Joy"), a => a, "Livin' Joy");
        Assert.Equal(2, next.Count(a => a == "Livin' Joy"));
        Assert.Contains("Haddaway", next); Assert.Contains("Corona", next);
    }

    [Fact]
    public void No_artist_twice_in_a_row_while_another_can_go_between()
    {
        var next = AutoplaySpread.Spread(S("A", "A", "B", "C", "C", "D"), a => a, "Z");
        for (var i = 1; i < next.Count; i++) Assert.NotEqual(next[i - 1], next[i]);
        Assert.Equal(6, next.Count);
    }

    [Fact]
    public void Songs_they_love_by_that_artist_are_not_counted_as_unknown_ones()
    {
        var songs = new List<(string Artist, bool Loved)> { ("X", true), ("Y", false), ("X", true), ("Z", false), ("X", false), ("W", false), ("X", false), ("X", false) };
        var next = AutoplaySpread.Spread(songs, s => s.Artist, "X", s => s.Loved);
        Assert.Equal(2, next.Count(s => s.Artist == "X" && s.Loved));
        Assert.Equal(2, next.Count(s => s.Artist == "X" && !s.Loved));   // unknown ones stay at two
    }

    [Fact]
    public void The_cap_holds_across_refills_and_for_loved_songs_too()
    {
        // owner, 25 Nov: "I play Heads Will Roll and the next 10-20 songs are by Yeah Yeah Yeahs". Each refill gave two more, and loved ones were never capped.
        var refill = S("Yeah Yeah Yeahs", "Yeah Yeah Yeahs", "MGMT", "Yeah Yeah Yeahs");
        Assert.Empty(AutoplaySpread.Spread(refill, a => a, "Yeah Yeah Yeahs", already: 2).Where(a => a == "Yeah Yeah Yeahs"));
        var loved = Enumerable.Repeat(("Yeah Yeah Yeahs", true), 8).Append(("MGMT", false)).ToList();
        // owner, 26 Nov: loved ones may come more often, but never 10-20 of one artist: at most 4 (the most in any 10)
        Assert.Equal(4, AutoplaySpread.Spread(loved, s => s.Item1, "Yeah Yeah Yeahs", s => s.Item2).Count(s => s.Item1 == "Yeah Yeah Yeahs"));
    }

    [Fact]
    public void Loved_songs_by_one_artist_may_run_three_in_a_row_but_unknown_ones_never_twice()
    {
        // owner, 26 Nov: "I don't mind them all being from the same artist as long as the song is good and I like it"
        var loved = Enumerable.Repeat(("X", true), 6).Concat([("Y", false), ("Z", false)]).ToList();
        var next = AutoplaySpread.Spread(loved, s => s.Item1, "Q", s => s.Item2);
        var run = 0; var worst = 0; string? prev = null;
        foreach (var s in next) { run = s.Item1 == prev ? run + 1 : 1; prev = s.Item1; worst = Math.Max(worst, run); }
        Assert.Equal(3, worst);
        for (var i = 0; i < next.Count; i++) Assert.True(next.Skip(i).Take(10).Count(s => s.Item1 == "X") <= 4);
        // not loved: never back to back, whichever artist
        var plain = AutoplaySpread.Spread(S("A", "A", "A", "B", "C"), a => a, "Q");
        for (var i = 1; i < 3; i++) Assert.NotEqual(plain[i - 1], plain[i]);
    }

    [Fact]
    public void No_artist_more_than_four_times_in_any_ten()
    {
        var songs = Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? ("X", true) : ("Y" + i, false)).ToList();
        var next = AutoplaySpread.Spread(songs, s => s.Item1, "Q", s => s.Item2);
        for (var i = 0; i < next.Count; i++) Assert.True(next.Skip(i).Take(10).Count(s => s.Item1 == "X") <= 4);
    }

    [Fact]
    public void Other_versions_of_a_song_count_as_the_same_song()
    {
        // owner, 12 Nov: Autoplay played "Dreamer", then "Dreamer - Re-Original 7-inch Mix", then "Dreamer (Janice Robinson Vocal)"
        var k = Matching.SongKey("Dreamer", "Livin' Joy");
        Assert.Equal(k, Matching.SongKey("Dreamer - Re-Original 7-inch Mix", "Livin' Joy"));
        Assert.Equal(k, Matching.SongKey("Dreamer (Janice Robinson Vocal)", "Livin' Joy"));
        Assert.Equal(k, Matching.SongKey("Dreamer (feat. Someone) [7\" Remastered]", "Livin' Joy & Someone"));
        Assert.NotEqual(k, Matching.SongKey("Dreamer", "Prince"));
        Assert.NotEqual(k, Matching.SongKey("Dreamers", "Livin' Joy"));
    }

    [Fact]
    public void Autoplay_keeps_one_version_of_each_song_and_none_of_what_was_heard()
    {
        var songs = new List<(string T, string A)> { ("Dreamer - Re-Original 7-inch Mix", "Livin' Joy"), ("What Is Love", "Haddaway"), ("Dreamer (Janice Robinson Vocal)", "Livin' Joy"),
            ("What Is Love - 7\" Mix", "Haddaway"), ("Rhythm Of The Night", "Corona") };
        var next = AutoplaySpread.OneVersion(songs, s => s.T, s => s.A, [Matching.SongKey("Dreamer", "Livin' Joy")]);
        Assert.Equal(["What Is Love", "Rhythm Of The Night"], next.Select(s => s.T));
    }

    [Fact]
    public void Autoplay_keeps_to_what_was_chosen_not_what_it_played_itself()
    {
        // owner, 12 Nov: "autoplay should be based on the songs at the end of a playlist, or the song I asked for or clicked play on"
        var queue = Enumerable.Range(1, 30).Select(i => "p" + i).Concat(["auto1", "auto2", "auto3"]).ToList();
        // still in the playlist: the song playing and the ones before it
        Assert.Equal(["p8", "p9", "p10"], AutoplaySpread.Seeds(queue, autoplayFrom: -1, index: 9).TakeLast(3));
        // Autoplay running (it began at 30): the playlist's last songs, never Autoplay's own
        var seeds = AutoplaySpread.Seeds(queue, autoplayFrom: 30, index: 32);
        Assert.Equal(AutoplaySpread.SeedCount, seeds.Count);
        Assert.Equal("p30", seeds[^1]);
        Assert.DoesNotContain(seeds, x => x.StartsWith("auto"));
        // one song asked for: that song, all the way
        Assert.Equal(["dreamer"], AutoplaySpread.Seeds(["dreamer", "auto1", "auto2"], autoplayFrom: 1, index: 2));
        // which of the chosen songs leads the lookup moves on with each Autoplay song, so the picks are not all about one of them
        Assert.NotEqual(AutoplaySpread.Lead(queue, 30, 31), AutoplaySpread.Lead(queue, 30, 32));
        Assert.Equal("dreamer", AutoplaySpread.Lead(["dreamer", "auto1", "auto2"], 1, 2));
    }
}
