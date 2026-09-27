using IpodSync.Core.ItunesDb;
using IpodSync.Core.Transcode;
using IpodSync.Shared.Playback;

namespace IpodSync.Web;

/// <summary>
/// Serves the iPod's audio files to the browser for playback (read-only, localhost).
/// Range requests are supported so seeking works; Apple Lossless is converted to FLAC
/// first, since browsers can't decode ALAC.
/// </summary>
public static class MediaEndpoint
{
    public static void MapIpodMedia(this WebApplication app) => app.MapGet("/media", (string root, string path, bool convert) =>
    {
        string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        string rootFull = Path.GetFullPath(root);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(Path.Combine(rootFull, "iPod_Control")) || !File.Exists(full))
            return Results.NotFound();

        if (convert)
        {
            string? converted = PlaybackMedia.ConvertForPlayback(full);
            if (converted is null) return Results.Problem("ffmpeg is needed to play Apple Lossless tracks in a browser.", statusCode: 501);
            full = converted;
        }
        return Results.File(full, PlaybackMedia.ContentType(full), enableRangeProcessing: true);
    });

    /// <summary>Same idea as /media, for a track found by LocalLibraryScanner instead of on an
    /// iPod: folder is the scanned root (the safety boundary a path must stay under), path is
    /// the track's own full path (LocalTrack.Path already is one, not root-relative).</summary>
    public static void MapLocalMedia(this WebApplication app) => app.MapGet("/local-media", (string folder, string path, bool convert) =>
    {
        string full = Path.GetFullPath(path);
        string folderFull = Path.GetFullPath(folder);
        if (!full.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return Results.NotFound();

        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (convert || ext is ".alac" or ".aif" or ".aiff")
        {
            string? converted = PlaybackMedia.ConvertForPlayback(full);
            if (converted is null) return Results.Problem("ffmpeg is needed to play this format in a browser.", statusCode: 501);
            full = converted;
        }
        return Results.File(full, PlaybackMedia.ContentType(full), enableRangeProcessing: true);
    });
}

/// <summary>Playback URLs for the web host: the endpoint above.</summary>
public sealed class WebMediaSource : IMediaSource
{
    public Task<string?> UrlAsync(string deviceRoot, Track track, bool forceConversion = false, CancellationToken ct = default)
    {
        if (track.RelativePath is not { } rel) return Task.FromResult<string?>(null);
        bool convert = forceConversion || PlaybackMedia.NeedsConversion(rel, track.FileTypeDescription, track.Bitrate);
        string url = $"/media?root={Uri.EscapeDataString(deviceRoot)}&path={Uri.EscapeDataString(rel)}&convert={(convert ? "true" : "false")}";
        return Task.FromResult<string?>(url);
    }
}
