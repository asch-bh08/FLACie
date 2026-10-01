using System.Collections.Concurrent;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>What a song's file really is, asked of whoever holds it: Jellyfin knows its streams, a NAS file is read with TagLib, a fresh
/// download is described by its extension and size. Cached, because the Info panel is opened often and a NAS read costs a round trip.</summary>
public sealed class InfoService(JellyfinClient jf, IHttpClientFactory hf)
{
    readonly ConcurrentDictionary<string, MediaInfo> cache = new();

    public async Task<MediaInfo> GetAsync(UserSession s, Track t)
    {
        if (cache.TryGetValue(t.Path, out var hit)) return hit;
        MediaInfo? info = null;
        try
        {
            if (t.Source == TrackSource.Jellyfin && s.Jellyfin is { } a && t.JellyfinId is { } id) info = await jf.MediaInfoAsync(a, id);
            else if (t.Source == TrackSource.Nas && s.Nas is { } n) info = await Task.Run(() => NasClient.Probe(n, NasClient.RelPath(n, t.Path)));
            else if (t.Source == TrackSource.Cloud && s.Services is { FileMoverUrl.Length: > 0 } svc && t.Path.StartsWith(svc.FileMoverUrl.TrimEnd('/'), StringComparison.Ordinal))
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, t.Path);
                req.Headers.TryAddWithoutValidation("X-Api-Key", svc.FileMoverKey);
                using var res = await hf.CreateClient("downloads").SendAsync(req);
                var dest = Uri.UnescapeDataString(t.Path[(t.Path.IndexOf("path=", StringComparison.Ordinal) + 5)..]);
                info = new MediaInfo(MediaInfo.FromExtension(dest), MediaInfo.FromExtension(dest), 0, 0, 0, 0, res.Content.Headers.ContentLength ?? 0, t.DurationMs, dest, "Download");
            }
        }
        catch (Exception) { }
        var ext = MediaInfo.FromExtension(t.FilePath ?? t.Path);
        info ??= new MediaInfo(ext, ext, 0, 0, 0, 0, 0, t.DurationMs, t.FilePath ?? "", t.Source == TrackSource.Nas ? "NAS" : t.Source.ToString());
        // Jellyfin reports its own path; the extension badge needs a codec even when a source left it blank
        if (info.Codec.Length == 0 && info.Container.Length == 0) info = info with { Codec = ext };
        cache[t.Path] = info;
        return info;
    }

    /// <summary>The short format name from what is already known (the file extension), so a list can badge songs without asking anyone.</summary>
    public static string QuickFormat(Track t)
    {
        var src = t.FilePath ?? (t.Source == TrackSource.Jellyfin ? "" : t.Path);
        if (t.Source == TrackSource.Cloud && src.IndexOf("path=", StringComparison.Ordinal) is var at and >= 0) src = Uri.UnescapeDataString(src[(at + 5)..]);
        var e = MediaInfo.FromExtension(src).ToUpperInvariant();
        return e switch { "" => "", "MPEG" => "MP3", "M4A" => "M4A", _ => e };
    }
}
