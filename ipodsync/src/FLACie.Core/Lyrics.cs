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
            var nl = cached.IndexOf('\n'); var head = nl < 0 ? cached : cached[..nl];
            if (cached.StartsWith("#none")) result = DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > MissFor ? await Fetch(t, account, file, ct) : null;
            // saved before LRCLIB's exact match came first: Jellyfin's file is looked at again once (the new order marks what it saves with "\t2")
            else if (head.TrimEnd('\r') == "Jellyfin") result = await Fetch(t, account, file, ct) ?? Parse(cached[(nl + 1)..], "Jellyfin");
            else result = Parse(cached[(nl + 1)..], head.Split('\t')[0].TrimEnd('\r'));
        }
        else
        {
            var ask = new Ask();
            result = await Fetch(t, account, file, ct, ask);
            // LRCLIB could not be asked (down, or still busy after asking again): nothing is remembered, so the next look asks again (before, a minute of
            // LRCLIB trouble meant "no lyrics" for that song for a week)
            if (result is null && ask.Unanswered) return null;
        }
        lock (mem) mem[key] = result;
        return result;
    }

    /// <summary>How LRCLIB is asked again when it hiccups (HttpRetry); a lookup that still could not be made is not remembered as "no lyrics".</summary>
    public RetryPolicy Retry { get; init; } = RetryPolicy.Quick;

    /// <summary>One song's lookup: whether some question to LRCLIB got no real answer (then "none found" is not to be believed).</summary>
    sealed class Ask { public bool Unanswered; public System.Runtime.CompilerServices.StrongBox<Lyrics?>? Exact; }

    async Task<Lyrics?> Fetch(Track t, JellyfinAccount? account, string file, CancellationToken ct, Ask? ask = null)
    {
        ask ??= new Ask();
        // LRCLIB's synced lyrics for this exact length first: Jellyfin's own .lrc files can be off (Lovegame's ran 0.5-1.5 s late with a line 10 s
        // early, owner 5 Nov); Jellyfin's next, then LRCLIB's looser search
        var l = await FromLrclibExact(t, ct, ask) is { Synced: true } exact ? exact : await FromJellyfin(t, account, ct) ?? await FromLrclib(t, ct, ask);
        // F18.1: no synced lyrics yet: NetEase's, when it has them for the same recording
        if (l is not { Synced: true } && await FromNetease(t, ct, ask) is { } ne) l = ne;
        if (l is null && ask.Unanswered) return null;
        try
        {
            File.WriteAllText(file, l is null ? "#none" : l.Source + "\t2\n" + string.Join("\n", l.Lines.Select(x => l.Synced ? $"[{Stamp(x.TimeMs)}]{x.Text}" : x.Text)));
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

    /// <summary>LRCLIB's lyrics for this artist, title, album and length (LRCLIB only answers when the length is within 2 s); asked once per lookup.</summary>
    async Task<Lyrics?> FromLrclibExact(Track t, CancellationToken ct, Ask ask)
    {
        if (ask.Exact is { } done) return done.Value;
        var title = t.Title.Trim(); var artist = t.Artist.Trim();
        Lyrics? found = null;
        if (title.Length > 0)
        {
            static string Enc(string s) => Uri.EscapeDataString(s);
            var lead = LeadArtist(artist) is { Length: > 0 } l ? l : artist;
            var url = $"{Lrclib}/get?track_name={Enc(title)}&artist_name={Enc(lead)}" + (t.Album.Length > 0 ? $"&album_name={Enc(t.Album)}" : "") + (t.DurationMs > 0 ? $"&duration={t.DurationMs / 1000}" : "");
            if (await Http(url, ct, ask) is { } b && ParseJson(b) is { } hit) found = FromJson(hit);
        }
        ask.Exact = new(found);
        return found;
    }

    async Task<Lyrics?> FromLrclib(Track t, CancellationToken ct, Ask ask)
    {
        var title = t.Title.Trim(); var artist = t.Artist.Trim();
        if (title.Length == 0) return null;
        static string Enc(string s) => Uri.EscapeDataString(s);
        var lead = LeadArtist(artist) is { Length: > 0 } l ? l : artist;
        if (await FromLrclibExact(t, ct, ask) is { } hit) return hit;
        // no exact match (another album/edition, or length off): search, and take the closest-length hit that has lyrics
        if (await Http($"{Lrclib}/search?track_name={Enc(StripFeat(title))}&artist_name={Enc(lead)}", ct, ask) is not { } q || ParseJson(q) is not JsonArray arr) return null;
        var hits = arr.OfType<JsonObject>().Where(h => Has(h, "syncedLyrics") || Has(h, "plainLyrics")).ToList();
        var best = hits.MinBy(h => (t.DurationMs > 0 ? Math.Abs((h["duration"]?.GetValue<double>() ?? 0) - t.DurationMs / 1000.0) : 0) + (Has(h, "syncedLyrics") ? 0 : 5));
        if (best is null) return null;
        if (t.DurationMs > 0 && Math.Abs((best["duration"]?.GetValue<double>() ?? 0) - t.DurationMs / 1000.0) > 20) return null; // a different recording
        return FromJson(best);
    }

    async Task<Lyrics?> FromNetease(Track t, CancellationToken ct, Ask ask)
    {
        if (t.Title.Trim().Length == 0 || t.Artist.Trim().Length == 0) return null;
        if (await Http(LyricsNetease.SearchUrl(t), ct, ask) is not { } b || ParseJson(b) is not { } found || LyricsNetease.Pick(found, t) is not { } id) return null;
        if (await Http(LyricsNetease.LyricUrl(id), ct, ask) is not { } lb || ParseJson(lb) is not { } lj || LyricsNetease.Lrc(lj) is not { } lrc) return null;
        return Parse(lrc, "NetEase") is { Synced: true } l ? l : null;
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

    /// <summary>The body of a good answer, else null. [ask] notes an answer that says nothing about the song (no connection, a timeout, a 5xx or 429 still
    /// there after asking again), as opposed to a 404 ("no such song").</summary>
    async Task<string?> Http(string url, CancellationToken ct, Ask ask)
    {
        try
        {
            using var res = await HttpRetry.SendAsync(http, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("FLACie/1.0 (web music player)");
                return req;
            }, Retry, ct);
            if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync(ct);
            if (HttpRetry.Transient(res.StatusCode)) ask.Unanswered = true;
            return null;
        }
        catch (Exception) { ask.Unanswered = true; return null; }
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
