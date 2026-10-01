using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FLACie.Core;

public sealed record LyricLine(long TimeMs, string Text);

/// <summary><see cref="Synced"/>: every line has a timestamp (highlight the current line); otherwise plain text.</summary>
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, bool Synced, string Source);

/// <summary>
/// Lyrics for any song, first hit wins (same order as the Android app): Jellyfin's own lyrics for a Jellyfin item, then LRCLIB
/// (lrclib.net, the free open lyrics database): an exact artist/title/album/length lookup, then a search on title and lead artist.
/// Results, including "none found", are cached on disk per song (a miss is retried after a week). Only the song's title, artist,
/// album and length are sent to LRCLIB.
/// </summary>
public sealed partial class LyricsService(HttpClient http, JellyfinClient jf, string cacheDir)
{
    const string Lrclib = "https://lrclib.net/api";
    static readonly TimeSpan MissFor = TimeSpan.FromDays(7);
    readonly Dictionary<string, Lyrics?> mem = [];

    public async Task<Lyrics?> GetAsync(Track t, JellyfinAccount? account, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        var key = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(Matching.MatchKey(t))));
        lock (mem) if (mem.TryGetValue(key, out var hit)) return hit;
        var file = Path.Combine(cacheDir, key + ".lrc");
        Lyrics? result;
        if (File.Exists(file) && File.ReadAllText(file) is var cached)
        {
            if (cached.StartsWith("#none")) result = DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > MissFor ? await Fetch(t, account, file, ct) : null;
            else { var nl = cached.IndexOf('\n'); result = Parse(cached[(nl + 1)..], cached[..nl]); }
        }
        else result = await Fetch(t, account, file, ct);
        lock (mem) mem[key] = result;
        return result;
    }

    async Task<Lyrics?> Fetch(Track t, JellyfinAccount? account, string file, CancellationToken ct)
    {
        var l = await FromJellyfin(t, account, ct) ?? await FromLrclib(t, ct);
        try
        {
            File.WriteAllText(file, l is null ? "#none" : l.Source + "\n" + string.Join("\n", l.Lines.Select(x => l.Synced ? $"[{Stamp(x.TimeMs)}]{x.Text}" : x.Text)));
        }
        catch (IOException) { }
        return l;
    }

    async Task<Lyrics?> FromJellyfin(Track t, JellyfinAccount? a, CancellationToken ct)
    {
        if (a is null || t.JellyfinId is not { } id) return null;
        try
        {
            if (await jf.GetAsync(a, $"/Audio/{id}/Lyrics", ct) is not { } o || o["Lyrics"] is not JsonArray arr) return null;
            var lines = arr.OfType<JsonObject>().Select(x => new LyricLine(x["Start"] is JsonValue v && v.TryGetValue<long>(out var s) ? s / 10_000 : -1, x["Text"]?.GetValue<string>() ?? "")).ToList();
            return lines.Count == 0 ? null : new Lyrics(lines, lines.All(l => l.TimeMs >= 0), "Jellyfin");
        }
        catch (Exception) { return null; }
    }

    async Task<Lyrics?> FromLrclib(Track t, CancellationToken ct)
    {
        var title = t.Title.Trim(); var artist = t.Artist.Trim();
        if (title.Length == 0) return null;
        static string Enc(string s) => Uri.EscapeDataString(s);
        var lead = LeadArtist(artist) is { Length: > 0 } l ? l : artist;
        var exact = $"{Lrclib}/get?track_name={Enc(title)}&artist_name={Enc(lead)}" + (t.Album.Length > 0 ? $"&album_name={Enc(t.Album)}" : "") + (t.DurationMs > 0 ? $"&duration={t.DurationMs / 1000}" : "");
        if (await Http(exact, ct) is { } b && ParseJson(b) is { } hit) return FromJson(hit);
        // no exact match (another album/edition, or length off): search, and take the closest-length hit that has lyrics
        if (await Http($"{Lrclib}/search?track_name={Enc(StripFeat(title))}&artist_name={Enc(lead)}", ct) is not { } q || ParseJson(q) is not JsonArray arr) return null;
        var hits = arr.OfType<JsonObject>().Where(h => Has(h, "syncedLyrics") || Has(h, "plainLyrics")).ToList();
        var best = hits.MinBy(h => (t.DurationMs > 0 ? Math.Abs((h["duration"]?.GetValue<double>() ?? 0) - t.DurationMs / 1000.0) : 0) + (Has(h, "syncedLyrics") ? 0 : 5));
        if (best is null) return null;
        if (t.DurationMs > 0 && Math.Abs((best["duration"]?.GetValue<double>() ?? 0) - t.DurationMs / 1000.0) > 20) return null; // a different recording
        return FromJson(best);
    }

    static bool Has(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0;
    static JsonNode? ParseJson(string s) { try { return JsonNode.Parse(s); } catch (Exception) { return null; } }

    static Lyrics? FromJson(JsonNode n)
    {
        if (n is not JsonObject o) return null;
        if (o["instrumental"]?.GetValue<bool>() == true) return new Lyrics([new LyricLine(-1, "Instrumental")], false, "LRCLIB");
        if (Has(o, "syncedLyrics") && Parse(o["syncedLyrics"]!.GetValue<string>(), "LRCLIB") is { } s) return s;
        return Has(o, "plainLyrics") ? Parse(o["plainLyrics"]!.GetValue<string>(), "LRCLIB") : null;
    }

    async Task<string?> Http(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("FLACie/1.0 (web music player)");
            using var res = await http.SendAsync(req, ct);
            return res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync(ct) : null;
        }
        catch (Exception) { return null; }
    }

    static string Stamp(long ms) => $"{ms / 60000:00}:{ms / 1000 % 60:00}.{ms % 1000 / 10:00}";
    static string StripFeat(string s) => Regex.Replace(s, @"\s*[(\[](feat\.?|ft\.?|featuring|with)\s[^)\]]*[)\]]", "", RegexOptions.IgnoreCase).Trim();
    static string LeadArtist(string s) => Regex.Split(s, @"\s*(;|,|&|/|\s+x\s+|\s+(feat\.?|ft\.?|featuring|with)\s+)\s*", RegexOptions.IgnoreCase).FirstOrDefault(x => x.Trim().Length > 0)?.Trim() ?? "";

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")] private static partial Regex StampRe();

    /// <summary>LRC ("[01:23.45]line", several stamps per line allowed, [ar:]/[ti:] tags ignored) or plain text.</summary>
    public static Lyrics? Parse(string text, string source)
    {
        var res = new List<LyricLine>(); var sawStamp = false;
        foreach (var raw in text.Split('\n').Select(x => x.TrimEnd('\r')))
        {
            var stamps = StampRe().Matches(raw);
            if (stamps.Count == 0)
            {
                if (raw.TrimStart().StartsWith('[') && raw.Contains(':') && raw.TrimEnd().EndsWith(']')) continue; // metadata tag
                res.Add(new LyricLine(-1, raw.Trim())); continue;
            }
            sawStamp = true;
            var last = stamps[^1];
            var words = raw[(last.Index + last.Length)..].Trim();
            foreach (Match m in stamps)
            {
                var frac = m.Groups[3].Value;
                var f = frac.Length switch { 0 => 0, 1 => int.Parse(frac) * 100, 2 => int.Parse(frac) * 10, _ => int.Parse(frac[..3]) };
                res.Add(new LyricLine(long.Parse(m.Groups[1].Value) * 60000 + long.Parse(m.Groups[2].Value) * 1000 + f, words));
            }
        }
        var lines = sawStamp ? res.Where(l => l.TimeMs >= 0).OrderBy(l => l.TimeMs).ToList()
            : res.SkipWhile(l => l.Text.Length == 0).Reverse().SkipWhile(l => l.Text.Length == 0).Reverse().ToList();
        return lines.Any(l => l.Text.Trim().Length > 0) ? new Lyrics(lines, sawStamp, source) : null;
    }
}
