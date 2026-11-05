using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// "Paste a Spotify or Deezer link": the one path the Import page and <c>POST /api/import/link</c> both take, so they cannot drift. The link is read
/// by <see cref="PlaylistLinkReader"/> and the list handed to <paramref name="start"/> (<see cref="ImportManager.StartLists"/>), which makes the job.
/// </summary>
public static class LinkImport
{
    /// <summary>Reads [url] and starts the import. Failures are described by <see cref="Describe"/>.</summary>
    public static async Task<ImportJob> StartAsync(PlaylistLinkReader reader, Func<ImportedPlaylist, ImportJob> start, string? url, CancellationToken ct = default, DownloadServices? services = null)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidDataException(PlaylistLinks.Refusal);
        var list = await reader.ReadAsync(url.Trim(), ct, services);
        return start(list);
    }

    /// <summary>The status and the words for a failure: a link problem is 400, a person who may not import 403, anything else (Spotify or Deezer down) 502.</summary>
    public static (int Status, string Message) Describe(Exception e) => e switch
    {
        InvalidDataException d => (400, d.Message),
        ImportNotAllowedException n => (403, n.Message),
        _ => (502, FriendlyError.Of(e)),
    };
}
