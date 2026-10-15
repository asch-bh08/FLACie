using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The iPod desk's options (iPod panel › Desk): which props lie on the desk, kept with the other iPod settings.</summary>
public class DeskOptionsTests
{
    [Fact] public void ANewDeskHasEverything()
    {
        // the workspace: the iPod in its dock, the notebook with its pencil, the VU amp, the CDs with the cassette and the MiniDiscs, earbuds
        var d = new IpodPrefs().Desk;
        Assert.Equal(DeskOptions.Earbuds, d.Phones);
        Assert.True(d.Vu); Assert.True(d.Cassette); Assert.True(d.Minidiscs); Assert.True(d.Dock); Assert.True(d.Pencil);
    }

    [Fact] public void TheChoicesAreKeptWithTheIpodSettings()
    {
        var p = new IpodPrefs();
        p.Desk.Phones = DeskOptions.Studio; p.Desk.Vu = false; p.Desk.Minidiscs = false; p.Desk.Dock = false; p.Desk.Pencil = false; p.Desk.Cassette = false;
        var back = IpodPrefs.FromJson(p.ToJson()).Desk;
        Assert.Equal(DeskOptions.Studio, back.Phones);
        Assert.False(back.Vu); Assert.False(back.Cassette);
        Assert.False(back.Minidiscs); Assert.False(back.Dock); Assert.False(back.Pencil);
    }

    [Fact] public void SettingsFromBeforeTheOptionsOrBrokenOnesGiveTheDefaults()
    {
        Assert.True(IpodPrefs.FromJson("""{"model":"classic6","look":2}""").Desk.Cassette);
        var broken = IpodPrefs.FromJson("""{"desk":{"phones":7,"vu":"yes","dock":3}}""").Desk;
        Assert.Equal(DeskOptions.Earbuds, broken.Phones); Assert.True(broken.Vu); Assert.True(broken.Dock);
        Assert.True(IpodPrefs.FromJson("""{"desk":[1,2]}""").Desk.Vu);
    }

    [Fact] public void ResetPutsTheDeskBack()
    {
        var p = new IpodPrefs(); p.Desk.Vu = false; p.Desk.Phones = DeskOptions.Studio;
        p.Reset();
        Assert.True(p.Desk.Vu); Assert.Equal(DeskOptions.Earbuds, p.Desk.Phones);
    }

    [Theory]
    [InlineData("ipod1", false)] [InlineData("ipod2", false)]          // FireWire, no dock connector
    [InlineData("ipod3", true)] [InlineData("video5", true)] [InlineData("classic7", true)] [InlineData("mini2", true)]
    [InlineData("nano1", true)] [InlineData("nano6", true)] [InlineData("touch4", true)]
    [InlineData("nano7", false)] [InlineData("touch5", false)] [InlineData("touch7", false)]   // Lightning
    public void OnlyThirtyPinIpodsGetTheDock(string model, bool dock) => Assert.Equal(dock, DeskJacks.HasDock(model));
}
