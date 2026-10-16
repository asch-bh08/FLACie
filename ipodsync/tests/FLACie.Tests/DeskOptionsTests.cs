using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The iPod desk's options (iPod panel › Desk): which props lie on the desk, kept with the other iPod settings.</summary>
public class DeskOptionsTests
{
    [Fact] public void ANewDeskHasEverything()
    {
        // the workspace: the iPod, the notebook with its pencil, the VU amp, the CDs in their stand, a concert ticket and guitar picks, earbuds
        var d = new IpodPrefs().Desk;
        Assert.Equal(DeskOptions.Earbuds, d.Phones);
        Assert.True(d.Vu); Assert.True(d.Ticket); Assert.False(d.Picks);   // (the picks are there to choose: off, so the desk stays uncluttered) Assert.True(d.Pencil);
    }

    [Fact] public void TheChoicesAreKeptWithTheIpodSettings()
    {
        var p = new IpodPrefs();
        p.Desk.Phones = DeskOptions.Studio; p.Desk.Vu = false; p.Desk.Picks = true; p.Desk.Pencil = false; p.Desk.Ticket = false;
        var back = IpodPrefs.FromJson(p.ToJson()).Desk;
        Assert.Equal(DeskOptions.Studio, back.Phones);
        Assert.False(back.Vu); Assert.False(back.Ticket);
        Assert.True(back.Picks); Assert.False(back.Pencil);
    }

    [Fact] public void SettingsFromBeforeTheOptionsOrBrokenOnesGiveTheDefaults()
    {
        Assert.True(IpodPrefs.FromJson("""{"model":"classic6","look":2}""").Desk.Ticket);
        var broken = IpodPrefs.FromJson("""{"desk":{"phones":7,"vu":"yes","dock":3}}""").Desk;
        Assert.Equal(DeskOptions.Earbuds, broken.Phones); Assert.True(broken.Vu);
        Assert.True(IpodPrefs.FromJson("""{"desk":[1,2]}""").Desk.Vu);
    }

    [Fact] public void ResetPutsTheDeskBack()
    {
        var p = new IpodPrefs(); p.Desk.Vu = false; p.Desk.Phones = DeskOptions.Studio;
        p.Reset();
        Assert.True(p.Desk.Vu); Assert.Equal(DeskOptions.Earbuds, p.Desk.Phones);
    }

    [Fact] public void TheDockIsGoneAndAnOldSettingForItIsDropped()
    {
        // (the desk had a 30-pin dock option until the owner asked for it to go: a setting saved with it still loads, and is not written back)
        var p = IpodPrefs.FromJson("""{"model":"classic6","look":2,"desk":{"dock":true,"vu":false}}""");
        Assert.False(p.Desk.Vu);
        Assert.DoesNotContain("dock", p.ToJson());
        Assert.Null(typeof(DeskOptions).GetProperty("Dock"));
    }

    [Fact] public void TheCassetteAndMiniDiscsAreGoneAndTheirOldSettingsAreDropped()
    {
        // (the owner asked for them to go: a concert ticket and guitar picks lie there now)
        var p = IpodPrefs.FromJson("""{"desk":{"tape":false,"md":false}}""");
        Assert.True(p.Desk.Ticket); Assert.False(p.Desk.Picks);
        Assert.DoesNotContain("\"tape\"", p.ToJson()); Assert.DoesNotContain("\"md\"", p.ToJson());
        Assert.Null(typeof(DeskOptions).GetProperty("Cassette")); Assert.Null(typeof(DeskOptions).GetProperty("Minidiscs"));
    }
}
