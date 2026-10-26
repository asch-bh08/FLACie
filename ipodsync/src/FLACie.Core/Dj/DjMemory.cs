namespace FLACie.Core;

/// <summary>
/// What the DJ remembers about one account: the last 20 lines (so templates, slots and openings vary), how many songs since it last spoke, which artists were heard
/// when (for "Back to X now"), recent skips (for "Let's try something different") and when it was last asked (30 minutes idle starts a new session).
/// Kept in memory on the server and shared by every device of the account. Not thread-safe: lock it while planning.
/// </summary>
public sealed class DjMemory
{
    public sealed record Line(string TemplateId, DjSlot Slot, string Opener, string Text, DateTime At);
    public sealed record Heard(DateTime At, int Song, string Title);

    public const int Keep = 20;
    /// <summary>"Not spoken for a long time": the start of a session, when nothing forces silence.</summary>
    public const int Long = 1_000_000;

    /// <summary>Oldest first, at most <see cref="Keep"/>.</summary>
    public List<Line> Lines { get; } = [];
    /// <summary>Song changes since the last line (0 right after one).</summary>
    public int SilentRun { get; set; } = Long;
    /// <summary>Song changes decided so far.</summary>
    public int Songs { get; set; }
    public DateTime? LastRequest { get; set; }
    /// <summary>By <see cref="Matching.PrimaryArtist"/> (a lookup key, never spoken).</summary>
    public Dictionary<string, Heard> Artists { get; } = new();
    /// <summary>Whether each of the last 5 song changes was a skip, oldest first.</summary>
    public List<bool> Skips { get; } = [];

    public void Add(Line l) { Lines.Add(l); if (Lines.Count > Keep) Lines.RemoveAt(0); }

    public void Skipped(bool skip) { Skips.Add(skip); if (Skips.Count > 5) Skips.RemoveAt(0); }

    /// <summary>A new session: the artists and skips of the last one no longer count (the lines still do, so the next session does not open the same way).</summary>
    public void NewSession() { Artists.Clear(); Skips.Clear(); SilentRun = Long; }
}
