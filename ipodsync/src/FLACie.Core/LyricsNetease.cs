using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>
/// F18.1: NetEase Cloud Music as the second lyrics source, after LRCLIB (its public web API, no key). Asked only when LRCLIB has no synced lyrics for a song;
/// a result counts only when its artist matches and its length is within <see cref="Tolerance"/> seconds, so a radio edit or album version still matches
/// and another recording does not.
/// </summary>
public static class LyricsNetease
{
    public const string Api = "https://music.163.com/api";
    public const int Tolerance = 8;

    /// <summary>The NetEase song id of the best match in a search answer, or null.</summary>
    public static long? Pick(JsonNode search, Track t)
    {
        if (search["result"]?["songs"] is not JsonArray songs) return null;
        var title = Matching.NormTitle(WebCatalog.BaseTitle(t.Title)); var lead = Matching.PrimaryArtist(t.Artist);
        if (title.Length == 0 || lead.Length == 0) return null;
        var best = songs.OfType<JsonObject>()
            .Where(s => Matching.NormTitle(WebCatalog.BaseTitle(Str(s["name"]))) == title)
            .Where(s => s["artists"] is JsonArray a && a.Any(x => Matching.PrimaryArtist(Str(x?["name"])) is { Length: > 0 } n && (n == lead || n.Contains(lead) || lead.Contains(n))))
            .Select(s => (s, off: t.DurationMs > 0 ? Math.Abs(Num(s["duration"]) - t.DurationMs) / 1000.0 : 0))
            .Where(x => x.off <= Tolerance)
            .OrderBy(x => x.off).FirstOrDefault();
        return best.s is null ? null : (long)Num(best.s["id"]);
    }

    public static string SearchUrl(Track t) =>
        $"{Api}/search/get?type=1&limit=10&s={Uri.EscapeDataString(WebCatalog.BaseTitle(t.Title) + " " + Matching.PrimaryArtist(t.Artist))}";
    public static string LyricUrl(long id) => $"{Api}/song/lyric?lv=1&id={id}";

    /// <summary>The synced LRC text of a lyric answer, or null when it has none.</summary>
    public static string? Lrc(JsonNode answer) => Str(answer["lrc"]?["lyric"]) is { Length: > 0 } s && s.Contains('[') ? s : null;

    static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static double Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
}
