using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>One copy of a song a source can give: where from, its codec (or file type), bit rate, and whether it is the uploader's original file
/// (SoundCloud's "download" for a producer's free download).</summary>
public sealed record SourceCopy(string Source, string Codec, int Kbps, bool Original);

/// <summary>
/// F7: how good a copy is, lower is better: lossless from anywhere (a SoundCloud original WAV or FLAC counts) &gt; a SoundCloud original lossy file &gt;
/// YouTube's ~160k Opus &gt; SoundCloud's 128k stream. The quickest copy plays first whatever its rank; anything lossy is queued for the lossless upgrade.
/// </summary>
public static class SourceRank
{
    static readonly HashSet<string> Lossless = new(StringComparer.OrdinalIgnoreCase) { "flac", "wav", "aiff", "aif", "alac", "ape", "wv" };

    public static bool IsLossless(SourceCopy c) => Lossless.Contains(c.Codec);

    public static int Of(SourceCopy c) =>
        IsLossless(c) ? 0
        : c.Source == "soundcloud" && c.Original ? 1
        : c.Source == "soundcloud" ? 3
        : 2;

    public static bool NeedsUpgrade(SourceCopy c) => !IsLossless(c);
}

/// <summary>F7: SoundCloud through yt-dlp ("scsearch", no API key): which of a track's formats to take.</summary>
public static class SoundCloud
{
    public const string Id = "soundcloud";

    /// <summary>The producer's original file when the track offers it (yt-dlp's "download" format), else the best stream; null when there is none.</summary>
    public static SourceCopy? Best(JsonArray formats)
    {
        static string S(JsonNode? f, string k) => f?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        static int N(JsonNode? f, string k) => f?[k] is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d : 0;
        if (formats.FirstOrDefault(f => S(f, "format_id") == "download") is { } orig)
            return new(Id, S(orig, "ext"), N(orig, "abr"), true);
        var best = formats.Where(f => f is not null && S(f, "ext").Length > 0).OrderByDescending(f => N(f, "abr")).FirstOrDefault();
        return best is null ? null : new(Id, S(best, "ext"), N(best, "abr"), false);
    }
}
