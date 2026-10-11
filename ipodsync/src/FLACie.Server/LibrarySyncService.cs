using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Keeps each signed-in library current without listing it again: Jellyfin's WebSocket says when the library or play data changed (LibraryChanged,
/// UserDataChanged), and then only songs saved since the last sync are asked for (<see cref="UserSession.SyncDeltaAsync"/>, background priority), after a short
/// pause so a burst of messages is one request. A long fallback (15 minutes) covers a dropped socket and checks Jellyfin's song count, which catches removals a message missed.
/// The Windows app's copy reacts to socket messages only.
/// </summary>
public sealed class LibrarySyncService(SessionStore sessions, JellyfinClient jf, ILogger<LibrarySyncService> log) : BackgroundService
{
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan Fallback = TimeSpan.FromMinutes(15);

    sealed class State { public bool Subscribed; public long DirtySince; public ConcurrentBag<string> Removed = []; public DateTime LastRun = DateTime.UtcNow; }
    readonly ConcurrentDictionary<string, State> states = new();

    /// <summary>The item ids a LibraryChanged message says were removed.</summary>
    public static List<string> ParseRemoved(JsonNode? data) =>
        (data?["ItemsRemoved"] as JsonArray ?? []).Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();

    /// <summary>Handles one socket message; true when it should start a delta. Removed ids are handed to <paramref name="removed"/>.</summary>
    public static bool Note(string type, JsonNode? data, Action<string> removed)
    {
        if (type is not ("LibraryChanged" or "UserDataChanged")) return false;
        foreach (var id in ParseRemoved(data)) removed(id);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var prio = JellyfinGate.Use(GatePriority.Background);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { break; }
            foreach (var s in sessions.Active.ToList())
            {
                if (s.Kind != "jellyfin" || s.Jellyfin is null || s.Loading) continue;
                var st = states.GetOrAdd(s.Id, _ => new State());
                try
                {
                    if (!st.Subscribed && sessions.LiveFor(s) is { } live)
                    {
                        st.Subscribed = true;
                        live.Connect.Message += (type, data) =>
                        {
                            if (Note(type, data, st.Removed.Add)) Interlocked.CompareExchange(ref st.DirtySince, DateTime.UtcNow.Ticks, 0);
                        };
                    }
                    var due = DateTime.UtcNow - st.LastRun >= Fallback && !ChartsService.InDesktopShell;
                    var dirty = Interlocked.Read(ref st.DirtySince) is var d and > 0 && DateTime.UtcNow - new DateTime(d, DateTimeKind.Utc) >= Debounce;
                    if (!dirty && !due) continue;
                    Interlocked.Exchange(ref st.DirtySince, 0);
                    var removed = new List<string>(); while (st.Removed.TryTake(out var id)) removed.Add(id);
                    st.LastRun = DateTime.UtcNow;
                    var n = await s.SyncDeltaAsync(jf, removed, ct);
                    if (n > 0) log.LogInformation("library of {user}: {n} songs changed", s.DisplayName, n);
                    // the long poll also compares Jellyfin's count with ours: a mismatch means a removal nobody told us about
                    var mine = s.Library.Songs.Count(t => t.Source == TrackSource.Jellyfin);
                    if (due && await s.JellyfinSongCountAsync(jf, ct) is var total and >= 0 && total != mine)
                    {
                        log.LogInformation("library of {user}: song count differs from Jellyfin ({a} vs {b}), listing again", s.DisplayName, mine, total);
                        await s.LoadAsync(jf, ct);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { log.LogDebug(e, "library sync failed for {user}", s.DisplayName); }
            }
        }
    }
}
