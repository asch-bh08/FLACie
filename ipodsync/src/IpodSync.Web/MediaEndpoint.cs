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

    /// <summary>Proxies a Jellyfin track through this server instead of letting the browser/
    /// WebView hit Jellyfin directly. Jellyfin's api_key has to go somewhere on a plain
    /// &lt;audio src&gt; (it can't set headers), and a query-string key ends up in browser
    /// history, devtools and any log between client and server. Routing through here means the
    /// real key only ever travels in a header, server-side, and the client only ever sees an
    /// opaque item id. static=true means the original file, unmodified -- no server transcode,
    /// whatever format it's actually stored in.</summary>
    public static void MapJellyfinMedia(this WebApplication app) => app.MapGet("/jellyfin-media", async (string itemId, HttpContext ctx, IpodSync.Shared.JellyfinSettings settings, IHttpClientFactory factory) =>
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            ctx.Response.StatusCode = StatusCodes.Status501NotImplemented;
            await ctx.Response.WriteAsync("Jellyfin isn't configured on this host.");
            return;
        }

        var http = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl.TrimEnd('/')}/Audio/{itemId}/stream?static=true");
        req.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Client=\"ipodsync\", Device=\"FLACie\", DeviceId=\"flacie-windows\", Version=\"1\", Token=\"{settings.ApiKey}\"");
        if (ctx.Request.Headers.TryGetValue("Range", out var range)) req.Headers.TryAddWithoutValidation("Range", (string?)range);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)resp.StatusCode;
        if (resp.Content.Headers.ContentType is { } ct) ctx.Response.ContentType = ct.ToString();
        if (resp.Content.Headers.ContentLength is { } len) ctx.Response.ContentLength = len;
        if (resp.Content.Headers.ContentRange is { } cr) ctx.Response.Headers.ContentRange = cr.ToString();
        ctx.Response.Headers.AcceptRanges = "bytes";
        await using var stream = await resp.Content.ReadAsStreamAsync(ctx.RequestAborted);
        await stream.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
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
