using System.Text;
using IpodSync.Shared.Backend;

namespace IpodSync.Web;

/// <summary>
/// POST /api/apply-edits -- the write side of the remote JSON API (EDIT-PROTOCOL.md), for ipodplayer's Sync mode.
/// The rules (op allow-list, dry-run confirm token) live in <see cref="RemoteEdits"/>, shared with the Android app's
/// loopback API so both hosts enforce the same thing; every write goes through the verified WritePipeline.
/// </summary>
public static class EditEndpoint
{
    public static void MapEditApi(this WebApplication app) => app.MapPost("/api/apply-edits",
        async (string root, int? commit, string? confirm, HttpRequest req, IIpodSyncBackend backend, CancellationToken ct) =>
    {
        string body;
        using (var r = new StreamReader(req.Body, Encoding.UTF8)) body = await r.ReadToEndAsync(ct);
        var (status, result) = await RemoteEdits.HandleAsync(backend, root, commit == 1, confirm, body, ct);
        return Results.Json(result, RemoteEdits.Json, statusCode: status);
    }).DisableAntiforgery();
}
