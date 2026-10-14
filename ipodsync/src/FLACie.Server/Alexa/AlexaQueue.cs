using System.Text.Json;

namespace FLACie.Server.Alexa;

public enum AlexaRepeat { Off, One, All }

/// <summary>
/// What one linked Alexa account is playing: the list (by FLACie track path), where it is, shuffle and repeat, and the offset to resume from.
/// Pure state with no I/O, so it can be tested; <see cref="AlexaQueues"/> keeps one per link and saves it.
/// </summary>
public sealed class AlexaQueue
{
    public List<string> Original { get; set; } = [];
    public List<string> Order { get; set; } = [];
    public int Index { get; set; }
    public bool Shuffle { get; set; }
    public AlexaRepeat Repeat { get; set; }
    public long OffsetMs { get; set; }
    public bool Playing { get; set; }
    /// <summary>Changes with every track change; the AudioPlayer token. A late event for an older token is ignored.</summary>
    public long Serial { get; set; }
    public string Title { get; set; } = "";
    public string? LastDownloadPath { get; set; }
    public string? PendingTitle { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [System.Text.Json.Serialization.JsonIgnore] public Random Rng { get; set; } = Random.Shared;

    public bool Empty => Order.Count == 0;
    public string? Current => Index >= 0 && Index < Order.Count ? Order[Index] : null;
    public string Token => $"q{Serial}";

    /// <summary>Starts a new list at [start] (an index into [paths]). With shuffle the chosen song still plays first.</summary>
    public void Set(IReadOnlyList<string> paths, int start = 0, bool? shuffle = null, string title = "")
    {
        Original = [.. paths]; Title = title; OffsetMs = 0; Playing = true;
        if (shuffle is { } sh) Shuffle = sh;
        start = Math.Clamp(start, 0, Math.Max(0, paths.Count - 1));
        if (Shuffle && paths.Count > 0)
        {
            var first = paths[start];
            var rest = paths.Where((_, i) => i != start).ToList(); Fisher(rest);
            Order = [first, .. rest]; Index = 0;
        }
        else { Order = [.. paths]; Index = start; }
        Serial++; Touch();
    }

    void Fisher(List<string> l) { for (var i = l.Count - 1; i > 0; i--) { var j = Rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); } }
    void Touch() => UpdatedAt = DateTime.UtcNow;

    /// <summary>What plays after the current song (null at the end of a list that does not repeat). Does not move.</summary>
    public int? NextIndex(bool userSkip)
    {
        if (Order.Count == 0) return null;
        if (Repeat == AlexaRepeat.One && !userSkip) return Index;
        if (Index + 1 < Order.Count) return Index + 1;
        return Repeat == AlexaRepeat.Off ? null : 0;
    }

    public string? PeekNext() => NextIndex(false) is { } i ? Order[i] : null;

    /// <summary>Moves to the next song; false (and stays) at the end of a list that does not repeat.</summary>
    public bool Next(bool userSkip = true)
    {
        if (NextIndex(userSkip) is not { } i) return false;
        Index = i; OffsetMs = 0; Serial++; Touch(); return true;
    }

    /// <summary>Previous: past the first 5 seconds it restarts the song, as players do; at the top of the list it restarts it (or wraps with repeat all).</summary>
    public bool Previous()
    {
        if (Order.Count == 0) return false;
        if (OffsetMs > 5000) { OffsetMs = 0; Serial++; Touch(); return true; }
        if (Index > 0) Index--;
        else if (Repeat == AlexaRepeat.All) Index = Order.Count - 1;
        OffsetMs = 0; Serial++; Touch(); return true;
    }

    /// <summary>Turns shuffle on or off without changing the song that is playing.</summary>
    public void SetShuffle(bool on)
    {
        if (Shuffle == on) return;
        Shuffle = on;
        var cur = Current;
        if (Order.Count > 0 && cur is not null)
        {
            if (on) { var rest = Order.Where((_, i) => i != Index).ToList(); Fisher(rest); Order = [cur, .. rest]; Index = 0; }
            else { Order = [.. Original]; Index = Math.Max(0, Order.IndexOf(cur)); }
        }
        Touch();
    }

    /// <summary>Puts songs right after the current one ("play next").</summary>
    public void InsertNext(IEnumerable<string> paths)
    {
        var add = paths.ToList(); if (add.Count == 0) return;
        Order.InsertRange(Math.Min(Index + 1, Order.Count), add);
        Original.AddRange(add); Touch();
    }

    public void Clear() { Original = []; Order = []; Index = 0; OffsetMs = 0; Playing = false; Serial++; Touch(); }

    /// <summary>Drops one song that can no longer be played (it left the library); the current index keeps pointing at the song after it.</summary>
    public void Remove(string path)
    {
        var at = Order.IndexOf(path); if (at < 0) return;
        Order.RemoveAt(at); Original.Remove(path);
        if (at < Index) Index--;
        if (Index >= Order.Count) Index = Math.Max(0, Order.Count - 1);
    }
}

/// <summary>One <see cref="AlexaQueue"/> per link, saved to disk (small JSON per link in the data folder) so "resume" works after a restart.</summary>
public sealed class AlexaQueues(DataPaths paths)
{
    readonly Dictionary<string, AlexaQueue> queues = [];
    readonly object gate = new();
    string Dir => Path.Combine(paths.Root, "alexa-queues");

    public AlexaQueue For(string linkId)
    {
        lock (gate)
        {
            if (queues.TryGetValue(linkId, out var q)) return q;
            q = new();
            try { var f = Path.Combine(Dir, linkId + ".json"); if (File.Exists(f)) q = JsonSerializer.Deserialize<AlexaQueue>(File.ReadAllText(f)) ?? new(); } catch (Exception) { }
            return queues[linkId] = q;
        }
    }

    public void Save(string linkId)
    {
        AlexaQueue q; lock (gate) { if (!queues.TryGetValue(linkId, out q!)) return; }
        try
        {
            Directory.CreateDirectory(Dir);
            string json; lock (q) json = JsonSerializer.Serialize(q);
            var f = Path.Combine(Dir, linkId + ".json"); File.WriteAllText(f + ".tmp", json); File.Move(f + ".tmp", f, true);
        }
        catch (Exception) { }
    }

    public void Forget(string linkId)
    {
        lock (gate) queues.Remove(linkId);
        try { File.Delete(Path.Combine(Dir, linkId + ".json")); } catch (Exception) { }
    }
}
