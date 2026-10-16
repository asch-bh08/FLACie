using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class IpodModelsTests
{
    [Fact] public void TheWholeLineUpIsThere()
    {
        Assert.Equal(23, IpodModels.All.Count);
        Assert.Equal(IpodModels.All.Count, IpodModels.All.Select(m => m.Id).Distinct().Count());
        Assert.Equal([7, 2, 7, 7], IpodModels.Groups.Select(g => IpodModels.InGroup(g).Count()).ToArray());
        Assert.Equal("iPod (4th gen)", IpodModels.Get(IpodModels.DefaultId).Name);
        Assert.Equal(IpodModels.DefaultId, IpodModels.Get("nonsense").Id);
        Assert.Equal("4th", IpodModels.Get("ipod4").GenLabel); Assert.Equal("2nd", IpodModels.Get("mini2").GenLabel); Assert.Equal("3rd", IpodModels.Get("nano3").GenLabel);
    }

    [Fact] public void EveryModelIsDrawable()
    {
        foreach (var m in IpodModels.All)
        {
            Assert.NotEmpty(m.Colors);
            Assert.InRange(m.Aspect, 0.4, 0.95);
            Assert.True(m.Screen.L >= 0 && m.Screen.R <= 1 && m.Screen.T >= 0 && m.Screen.B <= 1 && m.Screen.W > 0.5 && m.Screen.H > 0.2, m.Id);
            foreach (var c in m.Colors) foreach (var css in new[] { c.Top, c.Bottom, c.Edge, c.Bezel, c.Backdrop, c.Wheel.RingLight, c.Wheel.Label }) Assert.Matches("^#[0-9A-F]{6}$", css);
            if (m.Touch) { Assert.Equal(0, m.WheelR); Assert.Equal(IpodFamily.Touch, m.Family); } else Assert.InRange(m.WheelR, 0.2, 0.4);
        }
        Assert.All(IpodModels.InGroup("touch"), m => Assert.True(m.Touch));
        Assert.Equal(["nano6", "nano7"], IpodModels.All.Where(m => m.Touch && m.Group == "nano").Select(m => m.Id).ToArray());
    }

    [Fact] public void TheLogicalScreenIsWhatTheRealDeviceHad()
    {
        Assert.Equal(new IpodScreenSize(160, 128), IpodModels.ScreenSize(IpodModels.Get("ipod1"), true));
        Assert.Equal(new IpodScreenSize(160, 128), IpodModels.ScreenSize(IpodModels.Get("mini"), false));
        Assert.Equal(new IpodScreenSize(320, 240), IpodModels.ScreenSize(IpodModels.Get("video5"), true));
        Assert.Equal(new IpodScreenSize(240, 320), IpodModels.ScreenSize(IpodModels.Get("nano4"), true));
        Assert.Equal(new IpodScreenSize(320, 240), IpodModels.ScreenSize(IpodModels.Get("nano4"), false));
        Assert.Equal(IpodFamily.Mono, IpodModels.Get("ipod3").Family); Assert.Equal(IpodFamily.Video, IpodModels.Get("nano1").Family); Assert.Equal(IpodFamily.Nano, IpodModels.Get("classic7").Family);
        Assert.True(IpodModels.Get("ipod1").Buttons && IpodModels.Get("ipod2").Buttons && !IpodModels.Get("ipod3").Buttons);
    }

    [Fact] public void EachWheelHasTheControlsOfTheRealIpod()
    {
        Assert.Equal(IpodControls.ScrollWheel, IpodModels.Get("ipod1").Controls);       // the turning wheel, four buttons around it
        Assert.Equal(IpodControls.TouchWheel, IpodModels.Get("ipod2").Controls);        // the same ring of buttons round a touch wheel
        Assert.Equal(IpodControls.TouchRow, IpodModels.Get("ipod3").Controls);          // a row of four touch buttons above a plain touch wheel
        Assert.True(IpodModels.Get("ipod3").KeyRow && !IpodModels.Get("ipod3").Buttons && !IpodModels.Get("ipod1").KeyRow);
        Assert.All(IpodModels.All.Where(m => !m.Touch && !(m.Group == IpodModels.Classic && m.Gen <= 3)), m => Assert.Equal(IpodControls.ClickWheel, m.Controls));   // the 4th generation on, the minis, the nanos
        // the 3rd generation's wheel sits lower and smaller than the click wheel, leaving room under the screen for the row (buttons 14% of the body wide;
        // the wheel's radius is a fraction of the width, its centre of the height)
        var g3 = IpodModels.Get("ipod3"); Assert.True(g3.WheelR < IpodModels.Get("ipod4").WheelR);
        Assert.True(g3.WheelCy - g3.WheelR * g3.Aspect > g3.Screen.B + g3.BezelInset * g3.Aspect + 0.14 * g3.Aspect + 0.04);
    }

    [Fact] public void ColoursAreTheOnesOfTheAndroidEmulator()
    {
        Assert.Equal(["Silver", "Black"], IpodModels.Get("classic6").Colors.Select(c => c.Name).ToArray());
        Assert.Equal(9, IpodModels.Get("nano4").Colors.Count); Assert.Equal(5, IpodModels.Get("mini").Colors.Count); Assert.Equal(4, IpodModels.Get("mini2").Colors.Count);
        Assert.Equal(["White", "U2 Special"], IpodModels.Get("ipod4").Colors.Select(c => c.Name).ToArray());
        Assert.Equal("#99BEE1", IpodModels.Get("nano2").Colors[1].Top);       // Blue, lightened like the Android metal()
        Assert.True(IpodModels.Get("classic6").Colors[1].Dark); Assert.False(IpodModels.Get("classic6").Colors[0].Dark);
        Assert.True(IpodModels.Get("ipod4").Colors[1].Dark);
        Assert.Equal("#7F7F7F", IpodModels.Lighten("#000000", 0.5)); Assert.Equal("#7F7F7F", IpodModels.Darken("#FFFFFF", 0.5));
    }
}

public class IpodPrefsTests
{
    [Fact] public void DefaultsMatchTheEmulator()
    {
        var p = new IpodPrefs();
        Assert.Equal("ipod4", p.Model); Assert.Equal(0, p.Color); Assert.Equal(IpodPrefs.LookDevice, p.Look);
        Assert.Equal(2, p.WheelSpeed); Assert.Equal(1, p.WheelAccel); Assert.Equal(1, p.ClickVolume); Assert.Equal(100, p.VolumeLimit); Assert.Empty(p.HiddenMenu);
        Assert.Equal(0, p.Clicker); Assert.False(p.ClickSound); Assert.False(p.Haptics);
    }

    [Fact] public void DeskLookRoundTrips()
    {
        var p = new IpodPrefs { Look = IpodPrefs.LookDesk };
        Assert.Equal(IpodPrefs.LookDesk, IpodPrefs.FromJson(p.ToJson()).Look);
        Assert.Equal("Desk", IpodPrefs.LookNames[IpodPrefs.LookDesk]);
    }

    [Fact] public void TheIpodsOwnLookToggleSkipsDesk()
    {
        var p = new IpodPrefs { Look = IpodPrefs.LookDesk };
        p.Cycle("look"); Assert.Equal(IpodPrefs.LookDevice, p.Look);
        p.Cycle("look"); Assert.Equal(IpodPrefs.LookWheelOnly, p.Look);
        p.Cycle("look"); Assert.Equal(IpodPrefs.LookDevice, p.Look);
    }

    [Fact] public void UnknownLookFallsBackToDevice() => Assert.Equal(IpodPrefs.LookDevice, IpodPrefs.FromJson("""{"look":7}""").Look);

    [Fact] public void AnotherModelStartsWithItsFirstColourAndColoursStayInRange()
    {
        var p = new IpodPrefs(); p.ChooseModel("nano4"); p.Color = 7; Assert.Equal(7, p.Color); Assert.Equal("Red", p.CurrentColor.Name);
        p.ChooseModel("ipod1"); Assert.Equal(0, p.Color); p.Color = 5; Assert.Equal(0, p.Color);   // the 1st gen has one colour
        p.Color = 8; p.Model = "nano4"; p.Color = 8; p.Model = "classic6"; Assert.Equal(1, p.Color);  // moving to a model with fewer colours clamps
    }

    [Fact] public void EverySettingCyclesAndWraps()
    {
        var p = new IpodPrefs();
        for (var i = 0; i < 5; i++) p.Cycle("wheelSpeed"); Assert.Equal(2, p.WheelSpeed);
        for (var i = 0; i < 4; i++) p.Cycle("wheelAccel"); Assert.Equal(1, p.WheelAccel);
        p.Cycle("clicker"); Assert.True(p.ClickSound && !p.Haptics); p.Cycle("clicker"); Assert.True(!p.ClickSound && p.Haptics); p.Cycle("clicker"); Assert.True(p.ClickSound && p.Haptics); p.Cycle("clicker"); Assert.Equal(0, p.Clicker);
        p.Cycle("clickVolume"); p.Cycle("clickVolume"); Assert.Equal(0, p.ClickVolume);
        p.Cycle("look"); Assert.Equal(IpodPrefs.LookWheelOnly, p.Look); p.Cycle("look"); Assert.Equal(IpodPrefs.LookDevice, p.Look);
        p.Cycle("clock"); Assert.True(p.Clock); p.Cycle("clock"); Assert.False(p.Clock);
        p.Cycle("volumeLimit"); Assert.Equal(85, p.VolumeLimit); p.Cycle("volumeLimit"); p.Cycle("volumeLimit"); p.Cycle("volumeLimit"); Assert.Equal(100, p.VolumeLimit);
        Assert.False(p.Cycle("nonsense"));
    }

    [Fact] public void SavesAndLoadsAndSurvivesRubbish()
    {
        var p = new IpodPrefs(); p.ChooseModel("nano4"); p.Color = 3; p.Look = IpodPrefs.LookWheelOnly; p.WheelSpeed = 4; p.WheelAccel = 3; p.Clicker = 3; p.ClickVolume = 2; p.VolumeLimit = 70;
        p.Clock = true; p.HiddenMenu.Add("Extras"); p.HiddenMenu.Add("Music");   // Music can not be hidden
        var back = IpodPrefs.FromJson(p.ToJson());
        Assert.Equal("nano4", back.Model); Assert.Equal(3, back.Color); Assert.Equal(IpodPrefs.LookWheelOnly, back.Look); Assert.Equal(4, back.WheelSpeed); Assert.Equal(3, back.WheelAccel);
        Assert.True(back.Clock); Assert.Equal(3, back.Clicker); Assert.Equal(2, back.ClickVolume); Assert.Equal(70, back.VolumeLimit); Assert.Contains("Extras", back.HiddenMenu); Assert.DoesNotContain("Music", back.HiddenMenu);
        foreach (var bad in new[] { null, "", "   ", "{", "[1,2]", "{\"model\":5,\"color\":\"x\",\"speed\":99,\"limit\":42}" })
        {
            var q = IpodPrefs.FromJson(bad); Assert.Equal("ipod4", q.Model); Assert.InRange(q.WheelSpeed, 0, 4); Assert.Equal(100, q.VolumeLimit);
        }
        Assert.Equal(4, IpodPrefs.FromJson("{\"speed\":99}").WheelSpeed);
        Assert.Equal("ipod4", IpodPrefs.FromJson("{\"model\":\"nope\"}").Model);
    }

    [Fact] public void ResetPutsEverythingBack()
    {
        var p = new IpodPrefs(); p.ChooseModel("mini"); p.Color = 2; p.WheelSpeed = 0; p.Clicker = 3; p.HiddenMenu.Add("Extras"); p.Reset();
        Assert.Equal(new IpodPrefs().ToJson(), p.ToJson());
    }

    [Fact] public void WheelFeelFollowsTheSettings()
    {
        Assert.Equal([56.0, 44, 34, 26, 20], Enumerable.Range(0, 5).Select(WheelFeel.DegreesPerDetent).ToArray());
        Assert.Equal(56, WheelFeel.DegreesPerDetent(-3)); Assert.Equal(20, WheelFeel.DegreesPerDetent(9));
        Assert.Equal(1, WheelFeel.Gain(900, 20, 3));                 // short lists never speed up
        Assert.Equal(1, WheelFeel.Gain(900, 5000, 0));               // accel off
        Assert.Equal(WheelFeel.ListGain(5000), WheelFeel.Gain(100, 5000, 3));   // slow spin: only the long-list gain
        Assert.Equal(WheelFeel.MaxTotalGain, WheelFeel.Gain(2000, 5000, 3), 6);   // the speed gain of 3.2 times the list gain of 2, capped
        Assert.Equal(3.2 * WheelFeel.ListGain(100), WheelFeel.Gain(2000, 100, 3), 6); Assert.Equal(1, WheelFeel.ListGain(60)); Assert.Equal(2, WheelFeel.ListGain(100000), 6);
        var mid = WheelFeel.Gain(500, 5000, 2); Assert.InRange(mid, 2.0, 6.0);
        Assert.Equal(WheelFeel.Gain(500, 5000, 2), WheelFeel.Gain(-500, 5000, 2));
        var p = new IpodPrefs { WheelSpeed = 4, WheelAccel = 3 }; Assert.Equal(20, p.DegreesPerDetent); Assert.Equal(3.2, p.MaxGain);
    }

    [Fact] public void TheSpeedUpMovesTheListFurtherPerTurn()
    {
        var plain = new WheelDetents(34); var fast = new WheelDetents(34);
        Assert.Equal(1, plain.Feed(40)); Assert.Equal(3, fast.Feed(40, 3.2)); Assert.Equal(1, new WheelDetents(34).Feed(40, 0.2));
    }
}

public class IpodMenusTests
{
    static string[] L(IEnumerable<IpodRow> r) => r.Select(x => x.Label).ToArray();
    static readonly IReadOnlySet<string> None = new HashSet<string>();

    [Fact] public void EachFamilyHasItsOwnMainMenu()
    {
        Assert.Equal(["Playlists", "Browse", "Extras", "Settings", "Shuffle Songs"], L(IpodMenus.Root(IpodFamily.Mono, None, false)));
        Assert.Equal(["Music", "Cover Flow", "Extras", "Settings", "Shuffle Songs"], L(IpodMenus.Root(IpodFamily.Video, None, false)));
        Assert.Equal(["Music", "Cover Flow", "Extras", "Settings", "Shuffle Songs", "Now Playing"], L(IpodMenus.Root(IpodFamily.Nano, None, true)));
        Assert.Equal(["No Music", "Music", "Cover Flow", "Extras", "Settings", "Shuffle Songs"], L(IpodMenus.Root(IpodFamily.Video, None, false, anyMusic: false)));
        Assert.Equal("iPod", IpodMenus.RootTitle);
    }

    [Fact] public void HiddenEntriesGoButTheFixedOnesStay()
    {
        var hide = new HashSet<string> { "Extras", "Settings", "Music", "Shuffle Songs" };
        Assert.Equal(["Music", "Cover Flow", "Settings"], L(IpodMenus.Root(IpodFamily.Video, hide, false)));
        var mm = IpodMenus.MainMenu(IpodFamily.Video, new HashSet<string> { "Extras" });
        Assert.Equal(["Cover Flow", "Extras", "Shuffle Songs"], L(mm)); Assert.Equal([true, false, true], mm.Select(r => r.Checked!.Value).ToArray());
    }

    [Fact] public void MusicMenuDiffersForTheMonoModels()
    {
        Assert.Equal("Browse", IpodMenus.MusicTitle(IpodFamily.Mono)); Assert.Equal("Music", IpodMenus.MusicTitle(IpodFamily.Nano));
        Assert.Equal(["Artists", "Albums", "Songs", "Genres"], L(IpodMenus.Music(IpodFamily.Mono)));
        Assert.Equal(["Playlists", "Artists", "Albums", "Songs", "Genres"], L(IpodMenus.Music(IpodFamily.Video)));
    }

    [Fact] public void SettingsPagesShowTheirValues()
    {
        Assert.Equal(["Appearance", "Playback", "Controls", "Main Menu", "About", "Account", "FLACie Settings", "Reset Settings"], L(IpodMenus.Settings()));
        var p = new IpodPrefs(); p.ChooseModel("nano2"); p.Color = 1;
        var ap = IpodMenus.Appearance(p); Assert.Equal(["iPod nano (2nd gen)", "Blue", "Device", "Title"], ap.Select(r => r.Value!).ToArray());
        Assert.Equal(["Medium", "Low", "Off", "Medium"], IpodMenus.Controls(p).Select(r => r.Value!).ToArray());
        var pb = IpodMenus.PlaybackPage(new IpodPlayback(true, 2, 1.25, 30, 4, true, 10, 2, "1.0"), p);
        Assert.Equal(["Songs", "One", "1.25x", "On", "4 s", "Off", "30 min"], pb.Select(r => r.Value!).ToArray());
        Assert.Equal(["Off", "Off", "1x", "Off", "Off", "Off", "Off"], IpodMenus.PlaybackPage(new IpodPlayback(false, 0, 1, 0, 0, false, 0, 0, ""), p).Select(r => r.Value!).ToArray());
        Assert.Equal(["10", "2", "0", "1.0"], IpodMenus.About(new IpodPlayback(false, 0, 1, 0, 0, false, 10, 2, "1.0")).Select(r => r.Value!).ToArray());
        Assert.All(IpodMenus.Controls(p).Concat(pb), r => Assert.StartsWith("cyc:", r.Id));
        Assert.Equal(["Cancel", "Reset"], L(IpodMenus.Reset()));
    }

    [Fact] public void ThePickersListEveryModelAndColour()
    {
        Assert.Equal(["iPod / classic", "iPod mini", "iPod nano", "iPod touch"], L(IpodMenus.ModelGroups()));
        Assert.Equal("iPod nano", IpodMenus.GroupTitle("nano"));
        var nanos = IpodMenus.Models("nano", "nano3"); Assert.Equal(7, nanos.Count); Assert.Equal("set:model:nano3", nanos.Single(r => r.Checked == true).Id); Assert.Equal("2007", nanos[2].Value);
        var cols = IpodMenus.Colors(IpodModels.Get("nano4"), 2); Assert.Equal(9, cols.Count); Assert.Equal("Purple", cols.Single(r => r.Checked == true).Label); Assert.Equal("set:color:2", cols[2].Id);
        Assert.Equal(23, IpodMenus.ModelGroups().Sum(g => IpodMenus.Models(g.Id[8..], "").Count));
    }

    [Fact] public void TheAuditedModelsLookLikeTheRealOnes()
    {
        Assert.Equal(["Silver", "Blue", "Green", "Pink", "Red", "Black"], IpodModels.Get("nano2").Colors.Select(c => c.Name).ToArray());
        Assert.Equal(IpodModels.Get("classic6").Aspect, IpodModels.Get("classic7").Aspect, 3);
        var u2 = IpodModels.Get("ipod4").Colors[1].Wheel;   // solid red, near-black lettering
        Assert.Equal("#1A1A1C", u2.Label);
        Assert.Equal("#C4252B", u2.RingLight); Assert.Equal("#C1272D", u2.RingDark);
    }
}
