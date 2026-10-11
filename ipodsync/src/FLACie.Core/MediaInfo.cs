using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>What the file behind a song really is: format, quality and where it lives. Any field the source can't tell is zero/empty.</summary>
public sealed record MediaInfo(string Container, string Codec, int BitrateKbps, int SampleRateHz, int BitDepth, int Channels, long SizeBytes, long DurationMs, string Path, string Source, string Origin = "", string OriginSource = "", DateTime? OriginAt = null)
{
    /// <summary>Containers and codecs a browser cannot decode; Jellyfin converts them for playback.</summary>
    public static readonly HashSet<string> NotPlayableInBrowsers = ["wma", "asf", "ape", "wv", "tta"];
    public static readonly HashSet<string> LosslessCodecs = ["flac", "alac", "wav", "pcm", "aiff", "ape", "wavpack", "wv", "dsd", "tta", "pcm_s16le", "pcm_s24le", "pcm_s32le"];
    public bool Lossless => LosslessCodecs.Contains(Codec.ToLowerInvariant()) || LosslessCodecs.Contains(Container.ToLowerInvariant());
    /// <summary>Hi-Res: lossless at more than CD quality (more than 16 bits, or above 44.1 kHz).</summary>
    public bool HiRes => IsHiRes(Lossless, BitDepth, SampleRateHz);
    /// <summary>The one Hi-Res rule, used by every surface: lossless AND (more than 16 bits OR more than 44.1 kHz). Only the file's real facts count: never a bit rate, an extension, a file name or a hint.</summary>
    public static bool IsHiRes(bool lossless, int bitDepth, int sampleRateHz) => lossless && (bitDepth > 16 || sampleRateHz > 44_100);
    /// <summary>The real facts are known (a source reported a rate, depth or bit rate).</summary>
    public bool Known => SampleRateHz > 0 || BitDepth > 0 || BitrateKbps > 0;

    /// <summary>"FLAC", "MP3" ... for a badge.</summary>
    public string Format => (Codec.Length > 0 ? Codec : Container).ToUpperInvariant() switch { "MPEG" or "MPEGAUDIO" => "MP3", "AAC" => "AAC", var f => f };

    /// <summary>"Hi-Res Lossless", "Lossless", "High quality" ... in one phrase.</summary>
    public string Tier => Lossless ? (HiRes ? "Hi-Res Lossless" : "Lossless") : BitrateKbps >= 256 ? "High quality" : BitrateKbps > 0 ? "Standard quality" : "";

    /// <summary>"24-bit / 96 kHz" or "44.1 kHz".</summary>
    public string Sampling => SampleRateHz == 0 ? "" : (BitDepth > 0 ? $"{BitDepth}-bit / " : "") + (SampleRateHz % 1000 == 0 ? $"{SampleRateHz / 1000}" : $"{SampleRateHz / 1000.0:0.#}") + " kHz";

    public string ChannelsName => Channels switch { 0 => "", 1 => "Mono", 2 => "Stereo", 6 => "5.1", 8 => "7.1", var c => c + " channels" };

    public static string FromExtension(string path)
    {
        var q = path.IndexOf('?'); if (q >= 0) path = path[..q];
        var e = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return e;
    }
}

public sealed partial class JellyfinClient
{
    /// <summary>The audio stream's real format from Jellyfin (codec, bit rate, sample rate, bit depth, channels, size, original path).</summary>
    public async Task<MediaInfo?> MediaInfoAsync(JellyfinAccount a, string id, CancellationToken ct = default)
    {
        try { return await GetAsync(a, $"/Users/{a.UserId}/Items/{id}", ct) is JsonObject o ? ParseMediaInfo(o) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>The facts one Jellyfin item record carries (null when it has no media source). Shared by the single-song read and the bulk read.</summary>
    public static MediaInfo? ParseMediaInfo(JsonObject o)
    {
        if (o["MediaSources"] is not JsonArray { Count: > 0 } ms || ms[0] is not JsonObject src) return null;
        var s = (src["MediaStreams"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => x["Type"]?.GetValue<string>() == "Audio");
        int I(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
        long L(JsonNode? n) => n is JsonValue v && v.TryGetValue<long>(out var i) ? i : 0;
        var bits = s is null ? 0 : I(s["BitDepth"]) is > 0 and var bd ? bd : 0;
        return new MediaInfo(src["Container"]?.GetValue<string>() ?? "", s?["Codec"]?.GetValue<string>() ?? "", (int)((s is null ? 0 : L(s["BitRate"])) is > 0 and var br ? br / 1000 : L(src["Bitrate"]) / 1000),
            s is null ? 0 : I(s["SampleRate"]), bits, s is null ? 0 : I(s["Channels"]), L(src["Size"]), L(src["RunTimeTicks"]) is > 0 and var rt ? rt / 10_000 : L(o["RunTimeTicks"]) / 10_000,
            src["Path"]?.GetValue<string>() ?? "", "Jellyfin");
    }

    /// <summary>One page of the library with each song's streams, for the format index: a page is hundreds of songs in one request (the
    /// alternative is one request per song). Returns each item id with its facts (null when Jellyfin has none) and the library's total.</summary>
    public async Task<(List<(string Id, MediaInfo? Info)> Items, int Total)> MediaInfoPageAsync(JellyfinAccount a, int start, int limit, CancellationToken ct = default)
    {
        var page = await GetAsync(a, $"/Users/{a.UserId}/Items?IncludeItemTypes=Audio&Recursive=true&SortBy=SortName&Fields=MediaSources,MediaStreams&EnableUserData=false&EnableImages=false&StartIndex={start}&Limit={limit}", ct);
        return (ParseMediaInfoPage(page), page?["TotalRecordCount"]?.GetValue<int>() ?? 0);
    }

    public static List<(string Id, MediaInfo? Info)> ParseMediaInfoPage(JsonNode? page) =>
        (page?["Items"] as JsonArray ?? []).OfType<JsonObject>().Where(o => o["Id"] is JsonValue).Select(o => (o["Id"]!.GetValue<string>(), ParseMediaInfo(o))).ToList();
}
