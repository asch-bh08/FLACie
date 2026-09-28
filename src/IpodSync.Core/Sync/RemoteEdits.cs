using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IpodSync.Core.ItunesDb;


namespace IpodSync.Core.Sync;

/// <summary>
/// The rules for change-sets that arrive from another app rather than from this app's own UI -- shared by
/// IpodSync.Web's <c>POST /api/apply-edits</c> (ipodplayer on the network) and the Android app's loopback API
/// (ipodplayer on the same phone, iPod on USB-OTG), so both enforce exactly the same thing:
///  1. Only metadata / rating / playlist ops. Nothing that names a file on this host (addTrackFromFile,
///     setTrackArtwork) and no removeTrack.
///  2. A commit must carry the <c>confirmToken</c> a clean dry run returned for the *identical* change-set against
///     the *unchanged* database, so a write always follows a preview the user was shown and confirmed.
/// Every write still goes through WritePipeline (backup, write,
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
    /// <param name="run">Runs the change-set through WritePipeline (dry run when commit is false).</param>
    /// <param name="readOnlyReason">Non-null when this host can't write at all.</param>
    public static async Task<(int Status, object Body)> HandleAsync(Func<ChangeSet, bool, Action<string>, CancellationToken, Task<WritePipeline.Result>> run,
        string? readOnlyReason, string root, bool commit, string? confirm, string body, CancellationToken ct)
    {
        ChangeSet? changes;
        try { changes = JsonSerializer.Deserialize(body, ChangeSetJson.Default.ChangeSet); }
        catch (JsonException ex) { return (400, new ErrorReply("Invalid change-set JSON: " + ex.Message)); }
        if (changes?.Ops is not { Count: > 0 }) return (400, new ErrorReply("The change-set has no ops."));
        if (changes.Version != 1) return (400, new ErrorReply($"Unsupported change-set version {changes.Version}."));
        var refused = changes.Ops.Select(o => o.Op ?? "(missing)").Where(o => !AllowedOps.Contains(o)).Distinct().ToList();
        if (refused.Count > 0) return (400, new ErrorReply("Not allowed from another app: " + string.Join(", ", refused)));
        changes.DbPath = null;   // never let a caller redirect the writer at another database file

        if (readOnlyReason != null) return (501, new ErrorReply(readOnlyReason));
        string token;
        try { token = ConfirmToken(root, changes); }
        catch (Exception ex) { return (404, new ErrorReply(ex.Message)); }

        if (commit && !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(confirm ?? ""), Encoding.ASCII.GetBytes(token)))
            return (409, new ErrorReply("Run a dry run of exactly these changes first (the database or the changes differ from what was previewed)."));

        var log = new List<string>();
        WritePipeline.Result result;
        try { result = await run(changes, commit, s => { lock (log) log.Add(s); }, ct); }
        catch (Exception ex) { return (500, new ErrorReply(ex.Message)); }

        var rep = result.Report;
        bool ok = result.ExitCode == 0 && (rep?.AllOk ?? false);
        return (200, new ApplyReply(!commit, ok, result.ExitCode, result.Written, result.Restored, result.BackupDir,
            rep?.Ops.Select(o => new OpReply(o.Op, o.Ok, o.Detail)).ToList(), rep?.Problems.ToList(), result.Log.TakeLast(200).ToList(),
            !commit && ok ? token : null));   // only a clean dry run can be confirmed
    }

    /// <summary>Binds a confirmation to the exact change-set and the database's current bytes.</summary>
    public static string ConfirmToken(string root, ChangeSet changes)
    {
        string itunesDir = Path.GetDirectoryName(IpodSync.Core.Device.IpodDevice.Open(root).ItunesDbPath)!;
        using var sha = IpodSync.Core.Crypto.CryptoPrimitives.CreateSha256();
        sha.AppendData(Encoding.UTF8.GetBytes(Path.GetFullPath(root) + "\n" + JsonSerializer.Serialize(changes, ChangeSetJson.Default.ChangeSet) + "\n"));
        foreach (var name in new[] { "iTunesCDB", "iTunesDB" })
        {
            var p = Path.Combine(itunesDir, name);
            if (File.Exists(p)) sha.AppendData(File.ReadAllBytes(p));
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}
