using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class DeskLyricsTests
{

    [Fact] public void StaleLyricsAreIgnored()
    {
        Assert.True(DeskLyrics.Accept("p/a", "p/a"));
        Assert.False(DeskLyrics.Accept("p/a", "p/b"));   // the song changed while its lyrics were on the way
        Assert.False(DeskLyrics.Accept(null, "p/a"));
        Assert.False(DeskLyrics.Accept("p/a", null));
    }
}
