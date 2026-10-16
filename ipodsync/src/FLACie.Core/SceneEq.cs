namespace FLACie.Core;

/// <summary>The listening scenes' sound presets: the car stereo's EQ key and the MiniDisc system's SOUND key step through these, as a head unit's or a micro
/// system's preset key does. Each is a setting of the player's own five-band EQ (flacie.js eq(): 100 Hz, 400 Hz, 1 kHz, 3 kHz and 8 kHz, -12 to +12 dB), so
/// what the key picks is what the music goes through, and what the desk's preamp, the boombox's sliders and the hi-fi's tone knobs show. A setting made by
/// hand (on those) is CUSTOM: the key's round starts again at FLAT and ends back on it. Pure and unit-tested.</summary>
public static class SceneEq
{
    public sealed record Preset(string Name, int[] Bands);

    /// <summary>FLAT first; the rest in the order the key reaches them (short names: they are shown on dot-matrix displays).</summary>
    public static readonly IReadOnlyList<Preset> Presets =
    [
        new("FLAT", [0, 0, 0, 0, 0]),
        new("ROCK", [5, 2, -2, 2, 5]),
        new("POP", [-1, 2, 4, 2, -1]),
        new("JAZZ", [3, 1, -1, 2, 3]),
        new("CLASSIC", [2, 0, -1, 1, 3]),
        new("VOCAL", [-2, 0, 3, 3, 0]),
        new("BASS", [8, 3, 0, 0, 0]),
    ];
    public const string Custom = "CUSTOM";

    /// <summary>The preset these bands are, or CUSTOM. Not known yet (or not five bands): FLAT, as the EQ starts.</summary>
    public static string NameOf(IReadOnlyList<int>? bands) => bands is not { Count: 5 } ? Presets[0].Name : Find(bands) is { } p ? p.Name : Custom;

    /// <summary>One press of the key: the preset after the one set, round to FLAT. From a setting made by hand, FLAT; after the last preset, that setting
    /// again (custom: what you had before the round began; null when there was none). A copy each time, never a preset's own array.</summary>
    public static int[] Next(IReadOnlyList<int>? bands, IReadOnlyList<int>? custom)
    {
        var at = bands is { Count: 5 } && Find(bands) is { } p ? IndexOf(p) : -1;
        if (at < 0) return [.. Presets[0].Bands];
        if (at == Presets.Count - 1 && custom is { Count: 5 } && Find(custom) is null) return [.. custom];
        return [.. Presets[(at + 1) % Presets.Count].Bands];
    }

    static Preset? Find(IReadOnlyList<int> bands) => Presets.FirstOrDefault(p => p.Bands.SequenceEqual(bands));
    static int IndexOf(Preset p) { for (var i = 0; i < Presets.Count; i++) if (ReferenceEquals(Presets[i], p)) return i; return -1; }
}
