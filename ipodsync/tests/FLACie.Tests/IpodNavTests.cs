using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class IpodNavTests
{
    static List<IpodRow> Rows(int n) => Enumerable.Range(0, n).Select(i => new IpodRow("Row " + i, "r" + i, false)).ToList();

    [Fact] public void TurningMovesTheHighlightAndStopsAtTheEnds()
    {
        var n = new IpodNav(); n.Push("Songs", Rows(10));
        Assert.Equal(0, n.Selected); Assert.True(n.Turn(3)); Assert.Equal(3, n.Selected);
        Assert.True(n.Turn(-1)); Assert.Equal(2, n.Selected);
        Assert.True(n.Turn(100)); Assert.Equal(9, n.Selected); Assert.False(n.Turn(1));
        Assert.True(n.Turn(-100)); Assert.Equal(0, n.Selected); Assert.False(n.Turn(-1));
    }

    [Fact] public void TheListScrollsOnlyWhenTheHighlightLeavesTheScreen()
    {
        var n = new IpodNav(4); n.Push("Songs", Rows(10));
        n.Turn(3); Assert.Equal(0, n.Top);
        n.Turn(1); Assert.Equal(1, n.Top); Assert.Equal("Row 1", n.Visible.First().Label); Assert.Equal(4, n.Visible.Count());
        n.Turn(-4); Assert.Equal(0, n.Top);
        n.Turn(9); Assert.Equal(6, n.Top); Assert.Equal("Row 9", n.Visible.Last().Label);
    }

    [Fact] public void EachLevelRemembersItsOwnPlace()
    {
        var n = new IpodNav(); n.Push("Music", Rows(5)); n.Turn(2);
        n.Push("Artists", Rows(20)); Assert.Equal(1, n.LastMove); n.Turn(12); Assert.Equal(12, n.Selected);
        Assert.True(n.Back()); Assert.Equal(-1, n.LastMove); Assert.Equal("Music", n.Title); Assert.Equal(2, n.Selected);
        Assert.False(n.Back()); Assert.Equal(1, n.Depth);
    }

    [Fact] public void ThumbShowsTheWindowOnlyWhenTheListIsLonger()
    {
        var n = new IpodNav(5); n.Push("Short", Rows(5)); Assert.Null(n.Thumb);
        n.Push("Long", Rows(20)); var t = n.Thumb!.Value; Assert.Equal(0, t.Start); Assert.Equal(0.25, t.Length, 3);
        n.Turn(19); t = n.Thumb!.Value; Assert.Equal(0.75, t.Start, 3);
    }

    [Fact] public void ReplacingTheRowsKeepsTheHighlightInRange()
    {
        var n = new IpodNav(); n.Push("Songs", Rows(10)); n.Turn(8);
        n.Replace(Rows(4)); Assert.Equal(3, n.Selected); Assert.Equal(0, n.Top);
        n.Replace([]); Assert.Null(n.Current); Assert.False(n.Turn(1));
    }

    [Fact] public void EmptyNavDoesNothing()
    {
        var n = new IpodNav(); Assert.Null(n.Current); Assert.False(n.Turn(1)); Assert.False(n.Back()); Assert.Equal(-1, n.Selected); Assert.Empty(n.Visible); Assert.Null(n.Thumb);
    }

    [Fact] public void WheelGivesOneDetentPerFifteenDegreesAndCarriesTheRest()
    {
        var w = new WheelDetents();
        Assert.Equal(0, w.Feed(10)); Assert.Equal(1, w.Feed(10)); Assert.Equal(0, w.Feed(4)); Assert.Equal(0, w.Feed(1)); Assert.Equal(1, w.Feed(5));
        Assert.Equal(-2, w.Feed(-31)); Assert.Equal(0, w.Feed(-1));
        Assert.Equal(3, w.Feed(45 + 2));
    }

    [Fact] public void WheelToleratesMeasurementNoiseAtAClick()
    {
        var w = new WheelDetents(); Assert.Equal(2, w.Feed(29.99999)); Assert.Equal(-2, w.Feed(-30.00001)); Assert.Equal(0, w.Feed(14.5));
    }

    [Fact] public void WheelFoldsTheWrapAroundAt180()
    {
        var w = new WheelDetents();
        Assert.Equal(1, w.Feed(-345)); // from +170 to -175 is +15 clockwise, not -345
        Assert.Equal(-1, w.Feed(345));
        w.Feed(10); w.Release(); Assert.Equal(0, w.Feed(10));
    }
}
