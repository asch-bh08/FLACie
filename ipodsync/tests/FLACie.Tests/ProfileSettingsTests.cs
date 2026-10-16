using System.Text.Json.Nodes;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Small settings kept with the account (the iPod mode's look, colour and desk), so they follow the person across browsers, devices and the Windows app.</summary>
public class ProfileSettingsTests
{
    [Fact] public void ASettingIsKeptInTheProfileWithWhenItWasSet()
    {
        var s = new UserSession("jellyfin", null, null);
        Assert.Null(s.GetSetting("ipod"));
        s.SetSetting("ipod", """{"model":"mini2","color":3}""", 1234);
        Assert.Equal(("""{"model":"mini2","color":3}""", 1234L), s.GetSetting("ipod"));
        // it is part of the profile that is saved to the account (and read back from it)
        var copy = JsonNode.Parse(s.Profile.ToJsonString())!.AsObject();
        Assert.Equal("""{"model":"mini2","color":3}""", copy["settings"]!["ipod"]!["json"]!.GetValue<string>());
    }

    [Fact] public void AnOlderSettingNeverReplacesANewerOne()
    {
        var s = new UserSession("jellyfin", null, null);
        s.SetSetting("ipod", "new", 2000);
        s.SetSetting("ipod", "old", 1000);
        Assert.Equal(("new", 2000L), s.GetSetting("ipod"));
        s.SetSetting("other", "x", 5);
        Assert.Equal(("new", 2000L), s.GetSetting("ipod"));
    }
}
