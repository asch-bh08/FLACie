namespace FLACie.Server;

/// <summary>One thing that went wrong (or that the DJ mic heard) on a device: [Kind] (stall, error, stuck, skip, download, heard, queue), [Text] what happened.</summary>
public sealed record DiagEvent(long At, string Kind, string Text, string? Device = null);
public sealed record DiagBody(List<DiagEvent>? Events);

/// <summary>
/// What a phone reports about its playback problems, kept on the server per account (data/diag/&lt;session&gt;.jsonl), so a bad drive can be read afterwards without
/// the phone (owner, 2 Nov: the stops in the car could only be guessed at). Bounded: at most 50 events a call, 300 characters each, and the file is started
/// again (the old one kept as .old) past 1 MB.
/// </summary>
public sealed class DiagLogStore(DataPaths paths)
{
    public const int MaxEvents = 50, MaxText = 300;
    public const long MaxBytes = 1024 * 1024;
    readonly object gate = new();

    public string FileOf(string sessionId) => Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Root, "diag")).FullName, sessionId + ".jsonl");

    /// <summary>Appends [events] (cut to the limits); returns how many were kept.</summary>
    public int Add(string sessionId, IEnumerable<DiagEvent> events)
    {
        var lines = events.Take(MaxEvents)
            .Where(e => e.Kind is { Length: > 0 and <= 20 })
            .Select(e => System.Text.Json.JsonSerializer.Serialize(e with { Text = (e.Text ?? "").Length > MaxText ? e.Text![..MaxText] : e.Text ?? "", Device = e.Device is { Length: > 40 } d ? d[..40] : e.Device }))
            .ToList();
        if (lines.Count == 0) return 0;
        var f = FileOf(sessionId);
        lock (gate)
        {
            if (File.Exists(f) && new FileInfo(f).Length > MaxBytes) File.Move(f, f + ".old", overwrite: true);
            File.AppendAllLines(f, lines);
        }
        return lines.Count;
    }
}
