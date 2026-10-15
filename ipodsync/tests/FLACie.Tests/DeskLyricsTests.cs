using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class DeskLyricsTests
{
    static Lyrics Synced(params long[] ms) => new(ms.Select((t, i) => new LyricLine(t, "line " + i)).ToList(), true, "test");
    static Lyrics Plain(int n) => new(Enumerable.Range(0, n).Select(i => new LyricLine(0, "line " + i)).ToList(), false, "test");

    [Fact] public void StaleLyricsAreIgnored()
    {
        Assert.True(DeskLyrics.Accept("p/a", "p/a"));
        Assert.False(DeskLyrics.Accept("p/a", "p/b"));   // the song changed while its lyrics were on the way
        Assert.False(DeskLyrics.Accept(null, "p/a"));
        Assert.False(DeskLyrics.Accept("p/a", null));
    }

    [Fact] public void SyncedLinesFollowTheClockAHairEarly()
    {
        var l = Synced(1000, 5000, 9000);
        Assert.Equal(-1, DeskLyrics.CurrentLine(l, 0.5, 200));
        Assert.Equal(0, DeskLyrics.CurrentLine(l, 0.86, 200));   // 150 ms early: the eye needs the line before it is sung
        Assert.Equal(1, DeskLyrics.CurrentLine(l, 5.0, 200));
        Assert.Equal(2, DeskLyrics.CurrentLine(l, 120, 200));
    }

    [Fact] public void PlainLinesAreSpreadOverTheSong()
    {
        var l = Plain(4);
        Assert.Equal(0, DeskLyrics.CurrentLine(l, 0, 200));
        Assert.Equal(1, DeskLyrics.CurrentLine(l, 50, 200));
        Assert.Equal(3, DeskLyrics.CurrentLine(l, 199, 200));
        Assert.Equal(3, DeskLyrics.CurrentLine(l, 5, 0));   // no length known: every line at once
    }

    [Fact] public void NoLyricsNoLine()
    {
        Assert.Equal(-1, DeskLyrics.CurrentLine(null, 10, 200));
        Assert.Equal(-1, DeskLyrics.CurrentLine(new Lyrics([], true, "x"), 10, 200));
    }
}
