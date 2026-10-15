using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>The iPod desk's options (iPod panel › Desk): which props lie on the desk. Kept with the other iPod settings (<see cref="IpodPrefs.Desk"/>).</summary>
public sealed class DeskOptions
{
    public const int Earbuds = 0, Studio = 1;
    public static readonly string[] PhonesNames = ["Earbuds", "Studio"];

    /// <summary>Earbuds (the visualiser in their arcs), or Studio: a coiled studio cable ending at a loose gold 1/4" adapter.</summary>
    public int Phones { get => phones; set => phones = value is Earbuds or Studio ? value : Earbuds; }
    int phones = Earbuds;
    /// <summary>A brushed-metal amp with a VU meter whose needle follows the music.</summary>
    public bool Vu { get; set; } = true;
    /// <summary>A cassette whose reels turn while music plays; its label is one of your playlists, and a click plays it.</summary>
    public bool Cassette { get; set; } = true;
    /// <summary>A small stack of MiniDiscs, each one of your playlists.</summary>
    public bool Minidiscs { get; set; } = true;
    /// <summary>A 30-pin dock plugged into the iPod (only iPods with the dock connector: <see cref="DeskJacks.HasDock"/>).</summary>
    public bool Dock { get; set; } = true;
    /// <summary>A pencil lying across the journal.</summary>
    public bool Pencil { get; set; } = true;

    public void Reset() { phones = Earbuds; Vu = true; Cassette = true; Minidiscs = true; Dock = true; Pencil = true; }

    public JsonObject ToJson() => new() { ["phones"] = phones, ["vu"] = Vu, ["tape"] = Cassette, ["md"] = Minidiscs, ["dock"] = Dock, ["pencil"] = Pencil };

    /// <summary>Reads what <see cref="ToJson"/> wrote; anything missing or of the wrong kind keeps its default.</summary>
    public static DeskOptions FromJson(JsonNode? node)
    {
        var d = new DeskOptions();
        if (node is not JsonObject o) return d;
        bool B(string k, bool def) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : def;
        if (o["phones"] is JsonValue p && p.TryGetValue<int>(out var n)) d.Phones = n;
        d.Vu = B("vu", true); d.Cassette = B("tape", true); d.Minidiscs = B("md", true); d.Dock = B("dock", true); d.Pencil = B("pencil", true);
        return d;
    }
}
