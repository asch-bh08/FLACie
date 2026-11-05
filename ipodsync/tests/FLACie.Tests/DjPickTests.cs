using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>F11: the right version of a song, and "not that one".</summary>
public class DjPickTests
{
    static readonly DateTime T0 = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    static DjAskUnderstandTests.User Library()
    {
        var u = new DjAskUnderstandTests.User();
        u.Library.AddRange([DjAskUnderstandTests.T("Rhythm of the Night", "DeBarge"), DjAskUnderstandTests.T("The Rhythm of the Night", "Corona"), DjAskUnderstandTests.T("Toxic", "Britney Spears")]);
        u.Pop["/p/Corona/The Rhythm of the Night"] = 90; u.Pop["/p/DeBarge/Rhythm of the Night"] = 40;
        return u;
    }

    [Fact]
    public async Task Rhythm_of_the_night_picks_Corona_over_DeBarge()
    {
        var a = await new DjAskEngine().AnswerAsync(Library(), "play rhythm of the night", null, []);
        Assert.Equal("/p/Corona/The Rhythm of the Night", a.Paths![0]);
    }

    [Fact]
    public async Task Own_plays_beat_popularity()
    {
        var u = Library(); u.Top.Add(u.Library[0]);
        var a = await new DjAskEngine().AnswerAsync(u, "play rhythm of the night", null, []);
        Assert.Equal("/p/DeBarge/Rhythm of the Night", a.Paths![0]);
    }

    [Theory]
    [InlineData("not that one")]
    [InlineData("the other one")]
    [InlineData("the debarge one")]
    public async Task Not_that_one_within_a_minute_plays_the_next_match(string said)
    {
        var u = Library(); var t = T0;
        var e = new DjAskEngine(clock: () => t);
        await e.AnswerAsync(u, "play rhythm of the night", null, []);
        t += TimeSpan.FromSeconds(30);
        var a = await e.AnswerAsync(u, said, "/p/Corona/The Rhythm of the Night", []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.Equal("/p/DeBarge/Rhythm of the Night", a.Paths![0]);
    }

    [Fact]
    public async Task After_a_minute_there_is_no_other_one()
    {
        var u = Library(); var t = T0;
        var e = new DjAskEngine(clock: () => t);
        await e.AnswerAsync(u, "play rhythm of the night", null, []);
        t += TimeSpan.FromSeconds(61);
        var a = await e.AnswerAsync(u, "not that one", null, []);
        Assert.NotEqual(DjAskDo.Play, a.Do);
    }
}
