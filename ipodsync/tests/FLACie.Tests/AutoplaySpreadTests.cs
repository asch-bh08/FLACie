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
    public void Songs_they_love_by_that_artist_are_not_counted()
    {
        var songs = new List<(string Artist, bool Loved)> { ("X", true), ("Y", false), ("X", true), ("Z", false), ("X", false), ("W", false), ("X", false), ("X", false) };
        var next = AutoplaySpread.Spread(songs, s => s.Artist, "X", s => s.Loved);
        Assert.Equal(2, next.Count(s => s.Artist == "X" && s.Loved));
        Assert.Equal(2, next.Count(s => s.Artist == "X" && !s.Loved));
    }
}
