using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class DeskJacksTests
{
    [Fact] public void EveryModelHasAJack()
    {
        foreach (var m in IpodModels.All)
        {
            var j = DeskJacks.For(m.Id);
            Assert.InRange(j.X, 0.05, 0.95);
        }
    }

    [Fact] public void TouchAndNanoJacksAreOnTheBottom()
    {
        foreach (var id in new[] { "nano1", "nano3", "nano5", "nano6", "nano7", "touch1", "touch5", "touch7" }) Assert.Equal(JackEdge.Bottom, DeskJacks.For(id).Edge);
        Assert.Equal(0.5, DeskJacks.For("nano6").X);
    }

    [Fact] public void ClassicJackIsTopLeft()
    {
        foreach (var id in new[] { "ipod1", "ipod4", "video5", "classic6", "classic7", "mini", "mini2" })
        {
            var j = DeskJacks.For(id);
            Assert.Equal(JackEdge.Top, j.Edge); Assert.True(j.X < 0.3, id);
        }
    }

    [Fact] public void AnUnknownModelGetsTheClassicJack() => Assert.Equal(new DeskJack(JackEdge.Top, 0.2), DeskJacks.For("zune"));
}
