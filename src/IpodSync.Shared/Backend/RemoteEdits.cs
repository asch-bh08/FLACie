using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Sync;

namespace IpodSync.Shared.Backend;

/// <summary>
/// The rules for change-sets that arrive from another app rather than from this app's own UI -- shared by
/// IpodSync.Web's <c>POST /api/apply-edits</c> (ipodplayer on the network) and the Android app's loopback API
/// (ipodplayer on the same phone, iPod on USB-OTG), so both enforce exactly the same thing:
///  1. Only metadata / rating / playlist ops. Nothing that names a file on this host (addTrackFromFile,
///     setTrackArtwork) and no removeTrack.
///  2. A commit must carry the <c>confirmToken</c> a clean dry run returned for the *identical* change-set against
///     the *unchanged* database, so a write always follows a preview the user was shown and confirmed.
/// Every write still goes through <see cref="IIpodSyncBackend.RunChangesAsync"/> / WritePipeline (backup, write,
/// read-back, re-verify, automatic restore).
/// </summary>
public static class RemoteEdits
{
    public static readonly IReadOnlySet<string> AllowedOps = new HashSet<string>(StringComparer.Ordinal)
    {
        "setTrackFields", "setTrackRating", "addTrackToPlaylist", "removeTrackFromPlaylist",
        "reorderPlaylist", "createPlaylist", "renamePlaylist", "deletePlaylist",
    };

    /// <summary>Same JSON shape ASP.NET's minimal APIs produce (camelCase), so both hosts answer identically.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <returns>HTTP status and a JSON-serializable body.</returns>
    public static async Task<(int Status, object Body)> HandleAsync(IIpodSyncBackend backend, string root, bool commit, string? confirm, string body, CancellationToken ct)
    {
        ChangeSet? changes;
        try { changes = JsonSerializer.Deserialize<ChangeSet>(body); }
        catch (JsonException ex) { return (400, new { error = "Invalid change-set JSON: " + ex.Message }); }
        if (changes?.Ops is not { Count: > 0 }) return (400, new { error = "The change-set has no ops." });
        if (changes.Version != 1) return (400, new { error = $"Unsupported change-set version {changes.Version}." });
        var refused = changes.Ops.Select(o => o.Op ?? "(missing)").Where(o => !AllowedOps.Contains(o)).Distinct().ToList();
        if (refused.Count > 0) return (400, new { error = "Not allowed from another app: " + string.Join(", ", refused) });
        changes.DbPath = null;   // never let a caller redirect the writer at another database file

        if (!backend.Capabilities.CanWrite) return (501, new { error = backend.Capabilities.ReadOnlyReason ?? "This host can't write to the iPod." });
        string token;
        try { token = ConfirmToken(root, changes); }
        catch (Exception ex) { return (404, new { error = ex.Message }); }

        if (commit && !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(confirm ?? ""), Encoding.ASCII.GetBytes(token)))
            return (409, new { error = "Run a dry run of exactly these changes first (the database or the changes differ from what was previewed)." });

        var log = new List<string>();
        WritePipeline.Result result;
        try { result = await backend.RunChangesAsync(root, changes, commit, "remote", s => { lock (log) log.Add(s); }, ct); }
        catch (Exception ex) { return (500, new { error = ex.Message }); }

        var rep = result.Report;
        bool ok = result.ExitCode == 0 && (rep?.AllOk ?? false);
        return (200, new
        {
            dryRun = !commit,
            ok,
            exitCode = result.ExitCode,
            written = result.Written,
            restored = result.Restored,
            backupDir = result.BackupDir,
            ops = rep?.Ops.Select(o => new { op = o.Op, ok = o.Ok, detail = o.Detail }),
            problems = rep?.Problems,
            log = result.Log.TakeLast(200),
            confirmToken = !commit && ok ? token : null,   // only a clean dry run can be confirmed
        });
    }

    /// <summary>Binds a confirmation to the exact change-set and the database's current bytes.</summary>
    public static string ConfirmToken(string root, ChangeSet changes)
    {
        string itunesDir = Path.GetDirectoryName(IpodSync.Core.Device.IpodDevice.Open(root).ItunesDbPath)!;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes(Path.GetFullPath(root) + "\n" + JsonSerializer.Serialize(changes) + "\n"));
        foreach (var name in new[] { "iTunesCDB", "iTunesDB" })
        {
            var p = Path.Combine(itunesDir, name);
            if (File.Exists(p)) sha.AppendData(File.ReadAllBytes(p));
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}
