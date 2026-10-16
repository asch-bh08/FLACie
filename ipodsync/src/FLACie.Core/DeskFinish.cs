namespace FLACie.Core;

/// <summary>What an iPod's back is made of: mirror-polished stainless steel, or anodised aluminium (in the body's colour).</summary>
public enum DeskBack { Steel, Aluminium }

/// <summary>The iPod's finish as the desk shows it. The iPod lies on the desk seen from above, and its side shows beside it (the edge under it, lit from the
/// window): <see cref="Back"/> is what that side is made of and <see cref="Light"/>, <see cref="Mid"/>, <see cref="Dark"/> the tones it is drawn in (CSS
/// colours). <see cref="Accent"/> is the colour the desk's few coloured things take from the iPod's (the notebook's ribbon, the ticket's band...); null for a
/// silver, white or black iPod, whose desk keeps its own warm red.</summary>
public sealed record DeskFinish(DeskBack Back, string Light, string Mid, string Dark, string? Accent);

/// <summary>Each iPod's finish on the desk, from the real ones. Pure and unit-tested.</summary>
public static class DeskFinishes
{
    // polished steel seen at its edge mirrors what is round it: mostly the oak desk beside it (seen from above, a mirrored side shows the desk), with
    // glints of the window, so its tones lean warm and run from near white to nearly the desk's shadow (aluminium, satin, keeps to its own colour)
    const string SteelLight = "#FAF6EF", SteelMid = "#998E81", SteelDark = "#463C33";

    /// <summary>The 1st to 5th generations, both classics, the 1st and 3rd nanos and the first four touches have a stainless steel back (behind a white,
    /// black, U2 or coloured front alike); the minis, the 2nd and 4th to 7th nanos and the last three touches are anodised aluminium all round.</summary>
    public static DeskBack BackOf(string modelId) => modelId switch
    {
        "mini" or "mini2" or "nano2" or "nano4" or "nano5" or "nano6" or "nano7" or "touch5" or "touch6" or "touch7" => DeskBack.Aluminium,
        _ => DeskBack.Steel,
    };

    public static DeskFinish For(IpodModel model, IpodColor colour) => BackOf(model.Id) == DeskBack.Aluminium
        // the side faces away from the window: the body's own colour, its lit top, and a shade of it
        ? new(DeskBack.Aluminium, colour.Top, colour.Bottom, IpodModels.Darken(colour.Bottom, 0.35), AccentOf(colour))
        : new(DeskBack.Steel, SteelLight, SteelMid, SteelDark, AccentOf(colour));

    /// <summary>The desk's accent from the iPod's colour: a coloured body's own (its deeper tone, which reads on cream paper), else a coloured wheel's (the U2
    /// iPod's red); null for silver, white, cream, grey and black.</summary>
    public static string? AccentOf(IpodColor c) =>
        Chroma(c.Bottom) >= MinChroma ? c.Bottom : Chroma(c.Wheel.CenterDark) >= MinChroma ? c.Wheel.CenterDark : null;

    // how far a colour is from grey (0 to 1): silvers and space grey are under 0.05, the palest gold over 0.2
    const double MinChroma = 0.12;
    static double Chroma(string css)
    {
        var (r, g, b) = IpodColor.Rgb(css);
        return (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) / 255.0;
    }
}
