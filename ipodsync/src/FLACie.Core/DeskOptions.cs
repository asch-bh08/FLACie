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
    /// <summary>A brushed-metal preamp: a VU meter whose needle follows the music, and knobs for the volume, bass and treble.</summary>
    public bool Vu { get; set; } = true;
    /// <summary>A concert ticket stub for the artist playing.</summary>
    public bool Ticket { get; set; } = true;
    /// <summary>A couple of guitar picks (off unless chosen: the desk stays uncluttered).</summary>
    public bool Picks { get; set; }
    /// <summary>A pencil lying by the journal.</summary>
    public bool Pencil { get; set; } = true;

    public void Reset() { phones = Earbuds; Vu = true; Ticket = true; Picks = false; Pencil = true; }

    public JsonObject ToJson() => new() { ["phones"] = phones, ["vu"] = Vu, ["ticket"] = Ticket, ["picks"] = Picks, ["pencil"] = Pencil };

    /// <summary>Reads what <see cref="ToJson"/> wrote; anything missing or of the wrong kind keeps its default (and the old "dock", "tape" and "md" keys, from
    /// when the desk had a dock, a cassette and MiniDiscs, are ignored).</summary>
    public static DeskOptions FromJson(JsonNode? node)
    {
        var d = new DeskOptions();
        if (node is not JsonObject o) return d;
        bool B(string k, bool def) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : def;
        if (o["phones"] is JsonValue p && p.TryGetValue<int>(out var n)) d.Phones = n;
        d.Vu = B("vu", true); d.Ticket = B("ticket", true); d.Picks = B("picks", false); d.Pencil = B("pencil", true);
        return d;
    }
}
