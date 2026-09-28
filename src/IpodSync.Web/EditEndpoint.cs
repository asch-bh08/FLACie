using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Sync;
using IpodSync.Shared.Backend;

namespace IpodSync.Web;

/// <summary>
/// POST /api/apply-edits -- the write side of the remote JSON API (EDIT-PROTOCOL.md), for ipodplayer's Sync mode.
/// It is a thin shell over <see cref="IIpodSyncBackend.RunChangesAsync"/>, so every write still goes through the
/// verified WritePipeline: in-memory edit + re-parse, SQLite on a staged copy, signatures, SHA-1-verified backup,
/// write, read-back, re-verify, automatic restore on any failure.
///
/// Two extra gates because the caller is a remote device, not someone at this PC:
///  1. Only the metadata / rating / playlist ops are accepted. Ops that name a file on this host (addTrackFromFile,
///     setTrackArtwork) or delete tracks are refused -- a client on the network must not be able to point the
///     writer at arbitrary local paths.
///  2. commit=1 needs the <c>confirmToken</c> a dry run returned for the *identical* change-set against the
///     *unchanged* database. So nothing is written unless the user was first shown that exact dry-run result and
///     then explicitly confirmed it, and a database that changed in between (another write, the iPod re-synced)
///     invalidates the confirmation.
/// </summary>
public static class EditEndpoint
{
    static readonly HashSet<string> RemoteOps = new(StringComparer.Ordinal)
    {
        "setTrackFields", "setTrackRating", "addTrackToPlaylist", "removeTrackFromPlaylist",
        "reorderPlaylist", "createPlaylist", "renamePlaylist", "deletePlaylist",
    };

    public static void MapEditApi(this WebApplication app) => app.MapPost("/api/apply-edits",
        async (string root, int? commit, string? confirm, HttpRequest req, IIpodSyncBackend backend, CancellationToken ct) =>
    {
        string body;
        using (var r = new StreamReader(req.Body, Encoding.UTF8)) body = await r.ReadToEndAsync(ct);
        ChangeSet? changes;
        try { changes = JsonSerializer.Deserialize<ChangeSet>(body); }
        catch (JsonException ex) { return Results.BadRequest(new { error = "Invalid change-set JSON: " + ex.Message }); }
        if (changes?.Ops is not { Count: > 0 }) return Results.BadRequest(new { error = "The change-set has no ops." });
        if (changes.Version != 1) return Results.BadRequest(new { error = $"Unsupported change-set version {changes.Version}." });
        var refused = changes.Ops.Select(o => o.Op ?? "(missing)").Where(o => !RemoteOps.Contains(o)).Distinct().ToList();
        if (refused.Count > 0) return Results.BadRequest(new { error = "Not allowed over the network: " + string.Join(", ", refused) });
        changes.DbPath = null;   // never let a remote caller redirect the writer at another database file

        if (!backend.Capabilities.CanWrite) return Results.Problem(backend.Capabilities.ReadOnlyReason, statusCode: 501);
        string token;
        try { token = ConfirmToken(root, changes); }
        catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 404); }

        bool doCommit = commit == 1;
        if (doCommit && !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(confirm ?? ""), Encoding.ASCII.GetBytes(token)))
            return Results.Json(new { error = "Run a dry run of exactly these changes first (the database or the changes differ from what was previewed)." }, statusCode: 409);

        var log = new List<string>();
        WritePipeline.Result result;
        try { result = await backend.RunChangesAsync(root, changes, doCommit, "remote", s => { lock (log) log.Add(s); }, ct); }
        catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }

        var rep = result.Report;
        bool ok = result.ExitCode == 0 && (rep?.AllOk ?? false);
        return Results.Ok(new
        {
            dryRun = !doCommit,
            ok,
            exitCode = result.ExitCode,
            written = result.Written,
            restored = result.Restored,
            backupDir = result.BackupDir,
            ops = rep?.Ops.Select(o => new { op = o.Op, ok = o.Ok, detail = o.Detail }),
            problems = rep?.Problems,
            log = result.Log.TakeLast(200),
            // only a clean dry run can be confirmed
            confirmToken = !doCommit && ok ? token : null,
        });
    }).DisableAntiforgery();

    /// <summary>Binds a confirmation to the exact change-set and the database's current bytes.</summary>
    static string ConfirmToken(string root, ChangeSet changes)
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
