using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>What a song's file really is, asked of whoever holds it: Jellyfin knows its streams, a NAS file is read with TagLib, a fresh
/// download is read by ffprobe through the file mover. Also where the file came from (the download log, or "it was in the library already").
/// Cached, because the Info panel is opened often and a NAS read costs a round trip.</summary>
public sealed class InfoService(JellyfinClient jf, IHttpClientFactory hf, DownloadLog log)
{
    readonly ConcurrentDictionary<string, MediaInfo> cache = new();

    public async Task<MediaInfo> GetAsync(UserSession s, Track t)
    {
        if (!cache.TryGetValue(t.Path, out var info)) { cache[t.Path] = info = await ReadAsync(s, t); FormatIndex.Current?.Set(t, info); }
        // where it came from is looked up each time (a song downloaded a moment ago has no record until the download finishes)
        var rec = log.FindByFile(info.Path.Length > 0 ? info.Path : t.Path);
        if (rec is not null) return info with { Origin = "downloaded", OriginSource = rec.Source ?? "", OriginAt = rec.FinishedAt };
        return info with { Origin = t.Source == TrackSource.Cloud ? "downloaded" : "library" };
    }

    async Task<MediaInfo> ReadAsync(UserSession s, Track t)
    {
        MediaInfo? info = null;
        try
        {
            if (t.Source == TrackSource.Jellyfin && s.Jellyfin is { } a && t.JellyfinId is { } id) info = await jf.MediaInfoAsync(a, id);
            else if (t.Source == TrackSource.Nas && s.Nas is { } n) info = await Task.Run(() => NasClient.Probe(n, NasClient.RelPath(n, t.Path)));
            else if (t.Source == TrackSource.Cloud && s.Services is { FileMoverUrl.Length: > 0 } svc && t.Path.StartsWith(svc.FileMoverUrl.TrimEnd('/'), StringComparison.Ordinal))
            {
                var dest = Uri.UnescapeDataString(t.Path[(t.Path.IndexOf("path=", StringComparison.Ordinal) + 5)..]);
                info = await ProbeAsync(svc, dest, t) ?? new MediaInfo(MediaInfo.FromExtension(dest), MediaInfo.FromExtension(dest), 0, 0, 0, 0, 0, t.DurationMs, dest, "Streaming");
            }
        }
        catch (Exception) { }
        var ext = MediaInfo.FromExtension(t.FilePath ?? t.Path);
        info ??= new MediaInfo(ext, ext, 0, 0, 0, 0, 0, t.DurationMs, t.FilePath ?? "", t.Source == TrackSource.Nas ? "NAS" : SourceLabel.Short(t.Source));
        // Jellyfin reports its own path; the extension badge needs a codec even when a source left it blank
        if (info.Codec.Length == 0 && info.Container.Length == 0) info = info with { Codec = ext };
        return info;
    }

    /// <summary>ffprobe of the file through the file mover (it reads the real bit rate, sample rate, bit depth, size and length).</summary>
    async Task<MediaInfo?> ProbeAsync(DownloadServices svc, string dest, Track t)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, svc.FileMoverUrl.TrimEnd('/') + "/probe") { Content = new StringContent(new JsonObject { ["path"] = dest }.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Api-Key", svc.FileMoverKey);
        using var res = await hf.CreateClient("downloads").SendAsync(req);
        if (!res.IsSuccessStatusCode || JsonNode.Parse(await res.Content.ReadAsStringAsync()) is not JsonObject j) return null;
        int I(string k) => j[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
        long L(string k) => j[k] is JsonValue v && v.TryGetValue<long>(out var i) ? i : 0;
        var codec = j["codec"]?.GetValue<string>() ?? "";
        var ext = MediaInfo.FromExtension(dest);
        return new MediaInfo(ext, codec.Length > 0 ? codec : ext, I("bitrateKbps"), I("sampleRate"), I("bitDepth"), I("channels"), L("size"), L("durationMs") > 0 ? L("durationMs") : t.DurationMs, dest, "Streaming");
    }

    /// <summary>The short format name from what is already known (the file extension), so a list can badge songs without asking anyone.</summary>
    public static string QuickFormat(Track t)
    {
        var src = t.FilePath ?? (t.Source == TrackSource.Jellyfin ? "" : t.Path);
        if (t.Source == TrackSource.Cloud && src.IndexOf("path=", StringComparison.Ordinal) is var at and >= 0) src = Uri.UnescapeDataString(src[(at + 5)..]);
        // a Hi-Res download is filed as "Artist - Title [Hi-Res]": say so instead of the plain format
        try { if (Uri.UnescapeDataString(src).Contains("[Hi-Res]", StringComparison.OrdinalIgnoreCase)) return "HI-RES"; } catch (Exception) { }
        // the real facts (ffprobe / Jellyfin) beat the extension: an old 24-bit FLAC with no tag in its name is still Hi-Res
        if (FormatIndex.Current?.Get(t) is { HiRes: true }) return "HI-RES";
        var e = MediaInfo.FromExtension(src).ToUpperInvariant();
        return e switch { "" => "", "MPEG" => "MP3", "M4A" => "M4A", _ => e };
    }
}
