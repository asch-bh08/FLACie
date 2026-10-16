using System.Globalization;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The iPod's finish on the desk: the edge showing beside it is its real back (polished steel, or anodised aluminium in the colour picked), and the
/// desk's few coloured things take their colour from the iPod's.</summary>
public class DeskFinishTests
{
    static (int R, int G, int B) Rgb(string hex) => (int.Parse(hex[1..3], NumberStyles.HexNumber), int.Parse(hex[3..5], NumberStyles.HexNumber), int.Parse(hex[5..7], NumberStyles.HexNumber));
    static double Hue(string hex)
    {
        var (r, g, b) = Rgb(hex); double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d == 0) return 0;
        var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }
    static double HueGap(double a, double b) { var d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }
    static IpodColor Colour(string model, string name) => IpodModels.Get(model).Colors.First(c => c.Name == name);

    [Fact] public void EveryIpodInEveryColourHasAFinish()
    {
        foreach (var m in IpodModels.All)
            foreach (var c in m.Colors)
            {
                var f = DeskFinishes.For(m, c);
                foreach (var tone in new[] { f.Light, f.Mid, f.Dark }) Assert.Matches("^#[0-9A-F]{6}$", tone);
                if (f.Accent is { } a) Assert.Matches("^#[0-9A-F]{6}$", a);
            }
    }

    [Fact] public void TheSteelBackedIpodsShowPolishedSteelWhateverTheirFront()
    {
        // the 1st to 5th generations, both classics, the 1st and 3rd nanos and the first four touches: a mirror-polished stainless steel back, also behind a
        // black, a U2 or a coloured aluminium front
        foreach (var id in new[] { "ipod1", "ipod2", "ipod3", "ipod4", "video5", "classic6", "classic7", "nano1", "nano3", "touch1", "touch2", "touch3", "touch4" })
            foreach (var c in IpodModels.Get(id).Colors) Assert.Equal(DeskBack.Steel, DeskFinishes.For(IpodModels.Get(id), c).Back);
        Assert.Equal(DeskFinishes.For(IpodModels.Get("classic6"), Colour("classic6", "Silver")), DeskFinishes.For(IpodModels.Get("classic6"), Colour("classic6", "Black")) with { Accent = null });
    }

    [Fact] public void TheAluminiumIpodsShowTheirOwnColourAtTheEdge()
    {
        // the minis, the 2nd and 4th to 7th nanos and the last three touches are anodised aluminium all round: the edge is the colour picked
        foreach (var id in new[] { "mini", "mini2", "nano2", "nano4", "nano5", "nano6", "nano7", "touch5", "touch6", "touch7" })
        {
            var m = IpodModels.Get(id);
            foreach (var c in m.Colors)
            {
                var f = DeskFinishes.For(m, c);
                Assert.Equal(DeskBack.Aluminium, f.Back);
                Assert.Equal(c.Bottom, f.Mid); Assert.Equal(c.Top, f.Light);
            }
        }
        // a blue mini's edge is blue, a (PRODUCT)RED touch's red
        var blue = DeskFinishes.For(IpodModels.Get("mini"), Colour("mini", "Blue"));
        Assert.InRange(Hue(blue.Mid), 195, 225); Assert.InRange(Hue(blue.Dark), 195, 225);
        var red = DeskFinishes.For(IpodModels.Get("touch5"), Colour("touch5", "(PRODUCT)RED"));
        Assert.True(HueGap(Hue(red.Mid), 357) < 12);
    }

    [Fact] public void SteelIsWarmFromTheRoomAndHasMoreContrastThanAluminium()
    {
        var steel = DeskFinishes.For(IpodModels.Get("classic6"), Colour("classic6", "Silver"));
        var alu = DeskFinishes.For(IpodModels.Get("nano2"), Colour("nano2", "Silver"));
        static double Luma(string h) { var (r, g, b) = Rgb(h); return 0.2126 * r + 0.7152 * g + 0.0722 * b; }
        Assert.True(Luma(steel.Light) - Luma(steel.Dark) > Luma(alu.Light) - Luma(alu.Dark));
        // (it reflects the oak desk and the sunlight: its tones lean warm, red over blue)
        foreach (var t in new[] { steel.Mid, steel.Dark }) { var (r, _, b) = Rgb(t); Assert.True(r > b, t); }
    }

    [Fact] public void ColouredIpodsLendTheDeskTheirColour()
    {
        foreach (var (model, name, hue) in new[] { ("nano2", "Blue", 210.0), ("mini", "Pink", 333), ("nano4", "Green", 93), ("nano5", "Orange", 26), ("touch5", "Yellow", 50), ("nano3", "Red", 358), ("nano7", "Purple", 266) })
        {
            var a = DeskFinishes.For(IpodModels.Get(model), Colour(model, name)).Accent;
            Assert.NotNull(a);
            Assert.True(HueGap(Hue(a!), hue) < 15, $"{model} {name}: {a}");
        }
        // the 3rd nano's front is coloured, its back steel: the desk still takes the front's colour
        var nano3 = DeskFinishes.For(IpodModels.Get("nano3"), Colour("nano3", "Green"));
        Assert.Equal(DeskBack.Steel, nano3.Back); Assert.NotNull(nano3.Accent);
    }

    [Fact] public void TheU2IpodLendsTheDeskItsRedWheel()
    {
        var a = DeskFinishes.For(IpodModels.Get("ipod4"), Colour("ipod4", "U2 Special")).Accent;
        Assert.NotNull(a); Assert.True(HueGap(Hue(a!), 357) < 10, a);
    }

    [Fact] public void SilverWhiteAndBlackIpodsLeaveTheDeskItsOwnColours()
    {
        foreach (var (model, name) in new[] { ("ipod1", "White"), ("classic6", "Silver"), ("classic6", "Black"), ("video5", "White"), ("mini", "Silver"), ("nano4", "Black"), ("touch5", "Space Gray"), ("touch5", "Silver"), ("touch4", "White") })
            Assert.Null(DeskFinishes.For(IpodModels.Get(model), Colour(model, name)).Accent);
    }
}
