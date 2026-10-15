using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The listening scenes beside the iPod (iPod panel › Scene): which one the page shows, kept with the iPod settings.</summary>
public class RetroScenesTests
{
    [Fact] public void TheIpodIsTheSceneUntilAnotherIsChosen() => Assert.Equal(IpodPrefs.SceneIpod, new IpodPrefs().Scene);

    [Theory]
    [InlineData(IpodPrefs.SceneCar)] [InlineData(IpodPrefs.SceneHifi)] [InlineData(IpodPrefs.SceneBoombox)] [InlineData(IpodPrefs.SceneMinidisc)] [InlineData(IpodPrefs.SceneIpod)]
    public void TheSceneIsKept(int scene)
    {
        var p = new IpodPrefs { Scene = scene };
        Assert.Equal(scene, IpodPrefs.FromJson(p.ToJson()).Scene);
    }

    [Fact] public void AnUnknownOrBrokenSceneIsTheIpod()
    {
        Assert.Equal(IpodPrefs.SceneIpod, IpodPrefs.FromJson("""{"scene":42}""").Scene);
        Assert.Equal(IpodPrefs.SceneIpod, IpodPrefs.FromJson("""{"scene":"car"}""").Scene);
        Assert.Equal(IpodPrefs.SceneIpod, new IpodPrefs { Scene = -1 }.Scene);
    }

    [Fact] public void EverySceneHasAName()
    {
        Assert.Equal(["iPod", "Car", "Hi-Fi", "Boombox", "MiniDisc"], IpodPrefs.SceneNames);
        Assert.Equal(["ipod", "car", "hifi", "boombox", "minidisc"], IpodPrefs.SceneKeys);
    }

    [Fact] public void ResetGoesBackToTheIpod()
    {
        var p = new IpodPrefs { Scene = IpodPrefs.SceneHifi }; p.Reset();
        Assert.Equal(IpodPrefs.SceneIpod, p.Scene);
    }
}
