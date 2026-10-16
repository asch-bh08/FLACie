using System.Globalization;

namespace FLACie.Core;

/// <summary>Which on-screen UI family a model uses (the Android emulator's families): the 2001-2004 mono screens, the colour "video" menus, and the later nano/classic menus. Touch models have no wheel.</summary>
public enum IpodFamily { Mono, Video, Nano, Touch }

/// <summary>The colours of a click wheel (CSS colours).</summary>
public sealed record IpodWheelStyle(string RingLight, string RingDark, string Edge, string CenterLight, string CenterDark, string CenterEdge, string Label);

/// <summary>One colour of one model: body gradient, rim, wheel, bezel and the page behind the device.</summary>
public sealed record IpodColor(string Name, string Top, string Bottom, string Edge, IpodWheelStyle Wheel, bool DarkUi, string Bezel, string Backdrop)
{
    /// <summary>Dark body: the highlight on it is lighter and the wheel labels need to be light.</summary>
    public bool Dark => DarkUi || Luma(Top) < 0.38;
    /// <summary>A white front around the screen (the touch 6 and 7 and the nano 7 in every colour but the grey one).</summary>
    public bool LightFace => Luma(Bezel) > 0.6;
    static double Luma(string css) { var (r, g, b) = Rgb(css); return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0; }
    public static (int R, int G, int B) Rgb(string css) => (int.Parse(css[1..3], NumberStyles.HexNumber), int.Parse(css[3..5], NumberStyles.HexNumber), int.Parse(css[5..7], NumberStyles.HexNumber));
}

/// <summary>Screen rectangle as fractions of the body (left, top, right, bottom).</summary>
public readonly record struct IpodRect(double L, double T, double R, double B)
{
    public double W => R - L;
    public double H => B - T;
}

/// <summary>One iPod: its UI family, proportions (fractions of the body) and colours. Ported from the Android emulator's model list.</summary>
public sealed record IpodModel(
    string Id, string Name, IpodFamily Family, double Aspect, IpodRect Screen, double WheelCx, double WheelCy, double WheelR, double Radius, double BezelInset,
    IReadOnlyList<IpodColor> Colors, bool Portrait, bool Buttons, int Year, string Blurb, bool Touch, string Group, int Gen, bool Home)
{
    public string GenLabel => Gen switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => Gen + "th" };
}

/// <summary>The logical screen of a model in a look: the pixels the real device had (160x128, 320x240, or 240x320 for the tall nanos), later scaled to the screen.</summary>
public readonly record struct IpodScreenSize(int W, int H);

public static class IpodModels
{
    public const string Classic = "Classic", Mini = "mini", Nano = "nano", TouchGroup = "touch";
    public static readonly IReadOnlyList<string> Groups = [Classic, Mini, Nano, TouchGroup];
    public const string DefaultId = "ipod4";

    // ---- colour helpers (the Android ones) ----
    static (int R, int G, int B) P(string c) => IpodColor.Rgb(c);
    static string Hex(int r, int g, int b) => $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";
    static string Mix(string a, string b, double t)
    {
        var (ar, ag, ab) = P(a); var (br, bg, bb) = P(b);
        return Hex((int)(ar + (br - ar) * t), (int)(ag + (bg - ag) * t), (int)(ab + (bb - ab) * t));
    }
    public static string Lighten(string c, double t) => Mix(c, "#FFFFFF", t);
    public static string Darken(string c, double t) => Mix(c, "#000000", t);

    static IpodWheelStyle Tinted(string baseColor, string? label = null) => new(
        Lighten(baseColor, 0.55), Lighten(baseColor, 0.12), Darken(baseColor, 0.25), Lighten(baseColor, 0.6), baseColor, Darken(baseColor, 0.25), label ?? Darken(baseColor, 0.5));

    static readonly IpodWheelStyle WhiteWheel = new("#FFFFFF", "#D9DBE0", "#B4B7BE", "#F7F8FA", "#CDD0D6", "#A9ACB4", "#7C8087");
    static readonly IpodWheelStyle ClassicWheel = new("#FDFDFD", "#E2E2E4", "#B9B9BD", "#FAFAFA", "#DADADD", "#B0B0B5", "#9A9A9F");
    static readonly IpodWheelStyle DarkWheel = new("#3D3F46", "#1B1C20", "#08080A", "#34363C", "#131417", "#050506", "#B7BAC2");

    const string DefaultBezel = "#050506", DefaultBackdrop = "#0B0C0E";

    static IpodColor Metal(string name, string baseColor, IpodWheelStyle? wheel = null) =>
        new(name, Lighten(baseColor, 0.38), Darken(baseColor, 0.1), Darken(baseColor, 0.4), wheel ?? WhiteWheel, false, DefaultBezel, DefaultBackdrop);
    static IpodColor TintedMetal(string name, string baseColor) => Metal(name, baseColor, Tinted(Lighten(baseColor, 0.45)));
    static IpodColor Black(bool darkUi) => new("Black", "#44464D", "#17181B", "#000000", DarkWheel, darkUi, "#000000", "#1A1B1E");
    static IpodColor TouchMetal(string name, string baseColor) => new(name, Lighten(baseColor, 0.34), Darken(baseColor, 0.08), Darken(baseColor, 0.4), WhiteWheel, false, DefaultBezel, DefaultBackdrop);

    static readonly IpodColor Silver = new("Silver", "#E9EAED", "#BCBEC4", "#8C8F96", WhiteWheel, false, DefaultBezel, DefaultBackdrop);
    static readonly IpodColor White = new("White", "#FFFFFF", "#DBDBDE", "#9C9CA1", ClassicWheel, false, DefaultBezel, DefaultBackdrop);
    static readonly IpodColor Cream = new("White", "#FBFAF5", "#E0DED3", "#A9A79C", WhiteWheel, false, "#3A3B38", DefaultBackdrop);
    static readonly IpodColor U2 = new("U2 Special", "#3A3B40", "#0E0E10", "#000000", Tinted("#C1272D", "#FFFFFF"), false, "#000000", "#1A1B1E");
    static readonly IpodColor Blue = Metal("Blue", "#5C97D0"), Green = Metal("Green", "#76B048"), Red = Metal("Red", "#C93A40"), Pink = Metal("Pink", "#E27BAA"), Gold = Metal("Gold", "#C4A24E"),
        Purple = Metal("Purple", "#9367B8"), Orange = Metal("Orange", "#D9772C"), Yellow = Metal("Yellow", "#E2BE2E");
    static readonly IpodColor TouchSilver = new("Silver", "#EDEEF0", "#B9BCC2", "#8B8E95", WhiteWheel, false, "#08080A", DefaultBackdrop);
    static readonly IpodColor TouchBlack = new("Black", "#3C3E44", "#121316", "#000000", DarkWheel, true, "#000000", DefaultBackdrop);
    static readonly IpodColor TouchWhite = new("White", "#FFFFFF", "#DDDEE1", "#9A9CA2", WhiteWheel, false, "#F6F6F8", DefaultBackdrop);
    static readonly IpodColor TSlate = TouchMetal("Space Gray", "#6B6E75"), TGold = TouchMetal("Gold", "#D9BC94"), TPink = TouchMetal("Pink", "#E9A6B4"), TYellow = TouchMetal("Yellow", "#F2D95A"),
        TBlue = TouchMetal("Blue", "#6FA6D8"), TRed = TouchMetal("(PRODUCT)RED", "#C8323C"), TSilver5 = TouchMetal("Silver", "#C9CCD2");
    // the white fronts of the 2012 nano and the 2015 and 2019 touch (all but the grey ones, whose fronts are black)
    const string WhiteFront = "#F3F3F5";
    static IpodColor Front(IpodColor c) => c with { Bezel = WhiteFront };
    static readonly IpodColor WSilver = Front(TSilver5), WGold = Front(TGold), WPink = Front(TPink), WBlue = Front(TBlue), WRed = Front(TRed), WYellow = Front(TYellow);

    static readonly IpodRect NanoScreen = new(0.1125, 0.088, 0.8875, 0.5245), ClassicScreen = new(0.115, 0.05, 0.885, 0.395), NanoTall = new(0.105, 0.06, 0.895, 0.51),
        SlimScreen = new(0.125, 0.06, 0.875, 0.317), Mono1 = new(0.15, 0.06, 0.85, 0.396), Mono4 = new(0.171, 0.07, 0.829, 0.384), MiniScreen = new(0.175, 0.06, 0.825, 0.349);

    static IpodModel Click(string id, string name, string group, int gen, int year, IpodFamily fam, double aspect, IpodRect screen, double wcy, double wr, double radius, double bezel,
        IpodColor[] colors, string blurb, bool portrait = false, bool buttons = false) =>
        new(id, name, fam, aspect, screen, 0.5, wcy, wr, radius, bezel, colors, portrait, buttons, year, blurb, false, group, gen, false);

    static IpodModel TouchModel(string id, string name, string group, int gen, int year, double aspect, IpodRect screen, double radius, IpodColor[] colors, string blurb, bool home = true) =>
        new(id, name, IpodFamily.Touch, aspect, screen, 0.5, 0.5, 0, radius, 0.02, colors, true, false, year, blurb, true, group, gen, home);

    public static readonly IReadOnlyList<IpodModel> All =
    [
        Click("ipod1", "iPod (1st gen)", Classic, 1, 2001, IpodFamily.Mono, 0.6, Mono1, 0.7, 0.36, 0.07, 0.03, [Cream], "The original: 5 GB, mechanical scroll wheel and a mono screen.", buttons: true),
        Click("ipod2", "iPod (2nd gen)", Classic, 2, 2002, IpodFamily.Mono, 0.6, Mono1, 0.7, 0.36, 0.07, 0.03, [Cream], "Up to 20 GB and Windows support. Still the mechanical wheel.", buttons: true),
        Click("ipod3", "iPod (3rd gen)", Classic, 3, 2003, IpodFamily.Mono, 0.605, Mono4, 0.70, 0.31, 0.085, 0.028, [White], "Thinner, with a touch-sensitive wheel and dock connector."),
        Click("ipod4", "iPod (4th gen)", Classic, 4, 2004, IpodFamily.Mono, 0.597, Mono4, 0.70, 0.31, 0.085, 0.028, [White, U2], "The Click Wheel arrives. Mono screen, plus a U2 edition."),
        Click("video5", "iPod video (5th gen)", Classic, 5, 2005, IpodFamily.Video, 0.597, ClassicScreen, 0.703, 0.335, 0.085, 0.028, [White, Black(false)], "Colour 2.5\" screen and video playback."),
        Click("classic6", "iPod classic (6th gen)", Classic, 6, 2007, IpodFamily.Nano, 0.597, ClassicScreen, 0.703, 0.335, 0.085, 0.028, [Silver, Black(true)], "All-metal 160 GB classic with the modern menus and Cover Flow."),
        Click("classic7", "iPod classic (7th gen)", Classic, 7, 2009, IpodFamily.Nano, 0.59, ClassicScreen, 0.703, 0.335, 0.085, 0.028, [Silver, Black(true)], "The last classic: 160 GB, thinner, same great menus."),
        Click("mini", "iPod mini (1st gen)", Mini, 1, 2004, IpodFamily.Mono, 0.556, MiniScreen, 0.665, 0.35, 0.1, 0.03,
            [TintedMetal("Silver", "#B4B7BD"), TintedMetal("Blue", "#6FA8DC"), TintedMetal("Green", "#7FBF5A"), TintedMetal("Pink", "#E68FB5"), TintedMetal("Gold", "#D4B45E")], "Small aluminium body in five colours."),
        Click("mini2", "iPod mini (2nd gen)", Mini, 2, 2005, IpodFamily.Mono, 0.556, MiniScreen, 0.665, 0.35, 0.1, 0.03,
            [TintedMetal("Silver", "#B4B7BD"), TintedMetal("Blue", "#6FA8DC"), TintedMetal("Green", "#7FBF5A"), TintedMetal("Pink", "#E68FB5")], "Longer battery life and a brighter wheel."),
        Click("nano1", "iPod nano (1st gen)", Nano, 1, 2005, IpodFamily.Video, 0.457, SlimScreen, 0.66, 0.32, 0.1, 0.03, [White, Black(false)], "Pencil-thin flash player that replaced the mini."),
        Click("nano2", "iPod nano (2nd gen)", Nano, 2, 2006, IpodFamily.Video, 0.451, SlimScreen, 0.66, 0.32, 0.09, 0.03, [Silver, Blue, Green, Pink, Red, Gold], "Anodised aluminium in six colours."),
        Click("nano3", "iPod nano (3rd gen)", Nano, 3, 2007, IpodFamily.Nano, 0.751, NanoScreen, 0.777, 0.245, 0.075, 0.022, [Silver, Black(true), Blue, Green, Red], "The squat \"fatty\" nano with the menus and preview pane."),
        Click("nano4", "iPod nano (4th gen)", Nano, 4, 2008, IpodFamily.Nano, 0.43, NanoTall, 0.745, 0.33, 0.12, 0.03, [Silver, Black(true), Purple, Blue, Green, Yellow, Orange, Red, Pink], "Tall and curved, with a portrait screen.", portrait: true),
        Click("nano5", "iPod nano (5th gen)", Nano, 5, 2009, IpodFamily.Nano, 0.43, new IpodRect(0.105, 0.065, 0.895, 0.515), 0.75, 0.33, 0.12, 0.03, [Silver, Black(true), Purple, Blue, Green, Yellow, Orange, Red, Pink], "Adds a video camera. Nine colours.", portrait: true),
        TouchModel("nano6", "iPod nano (6th gen)", Nano, 6, 2010, 0.94, new IpodRect(0.13, 0.1522, 0.87, 0.8478), 0.12,
            [TouchSilver, TouchBlack, TouchMetal("Blue", "#6FA6D8"), TouchMetal("Green", "#83BE62"), TouchMetal("Orange", "#E48A3C"), TouchMetal("Pink", "#E9A6B4"), TouchMetal("Red", "#C8323C")], "A tiny multi-touch clip. No wheel at all.", home: false),
        TouchModel("nano7", "iPod nano (7th gen)", Nano, 7, 2012, 0.518, new IpodRect(0.119, 0.083, 0.881, 0.79349), 0.13,
            [WSilver, TSlate, Front(TouchMetal("Blue", "#6FA6D8")), Front(TouchMetal("Purple", "#9B7CC3")), WPink, WYellow, WRed], "Tall multi-touch nano with a Home button."),
        TouchModel("touch1", "iPod touch (1st gen)", TouchGroup, 1, 2007, 0.555, new IpodRect(0.1, 0.125, 0.9, 0.8), 0.1, [TouchSilver], "The first iPod with Multi-Touch and Wi-Fi."),
        TouchModel("touch2", "iPod touch (2nd gen)", TouchGroup, 2, 2008, 0.55, new IpodRect(0.1, 0.125, 0.9, 0.8), 0.11, [TouchSilver], "Built-in speaker and the App Store."),
        TouchModel("touch3", "iPod touch (3rd gen)", TouchGroup, 3, 2009, 0.55, new IpodRect(0.1, 0.125, 0.9, 0.8), 0.11, [TouchSilver], "Faster, with Voice Control."),
        TouchModel("touch4", "iPod touch (4th gen)", TouchGroup, 4, 2010, 0.53, new IpodRect(0.09, 0.125, 0.91, 0.8), 0.12, [TouchBlack, TouchWhite], "Retina display and cameras."),
        TouchModel("touch5", "iPod touch (5th gen)", TouchGroup, 5, 2012, 0.475, new IpodRect(0.06, 0.115, 0.94, 0.855), 0.13, [TSilver5, TSlate, TPink, TYellow, TBlue, TRed], "4-inch screen, colours and a loop."),
        TouchModel("touch6", "iPod touch (6th gen)", TouchGroup, 6, 2015, 0.475, new IpodRect(0.07425, 0.14104, 0.92575, 0.85896), 0.13, [TSlate, WSilver, WGold, WPink, WBlue, WRed], "A8 chip and Apple Music."),
        TouchModel("touch7", "iPod touch (7th gen)", TouchGroup, 7, 2019, 0.475, new IpodRect(0.07425, 0.14104, 0.92575, 0.85896), 0.13, [TSlate, WSilver, WGold, WPink, WBlue, WRed], "The last iPod: A10 Fusion and up to 256 GB."),
    ];

    public static IpodModel Get(string? id) => All.FirstOrDefault(m => m.Id == id) ?? All.First(m => m.Id == DefaultId);
    public static IEnumerable<IpodModel> InGroup(string group) => All.Where(m => m.Group == group);

    /// <summary>The logical pixels of the screen: 160x128 for the mono models, 320x240 for the rest, 240x320 for the tall nanos in the device look.</summary>
    public static IpodScreenSize ScreenSize(IpodModel m, bool deviceLook) =>
        deviceLook && m.Portrait ? new(240, 320) : m.Family == IpodFamily.Mono ? new(160, 128) : new(320, 240);
}
