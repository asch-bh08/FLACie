using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>One picture the Cover Art Archive holds for a release.</summary>
public sealed record CaaImage(string Id, string Thumb, string Full, bool Front, bool Back, string[] Types);

/// <summary>
/// The Cover Art Archive (pictures of MusicBrainz releases): the list of pictures for a release (or, when the release has none, for its release group) and the picture
/// itself. The list goes through the MusicBrainz gate (one request a second, one User-Agent for all of FLACie's MusicBrainz traffic); pictures are fetched only for what a
/// person looks at. Nothing is kept as someone's cover without them choosing it, and nothing is ever written into the song files or to Jellyfin.
/// </summary>
public sealed class CoverArtArchiveClient(HttpClient http, string userAgent, MusicBrainzGate gate)
{
    public const long MaxBytes = 12 * 1024 * 1024;
    public int RequestsMade { get; private set; }

    public static IReadOnlyList<CaaImage> Parse(string json)
    {
        var list = new List<CaaImage>();
        if (JsonNode.Parse(json)?["images"] is not JsonArray arr) return list;
        foreach (var o in arr.OfType<JsonObject>())
        {
            var id = o["id"]?.ToString(); var full = o["image"]?.GetValue<string>();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(full)) continue;
            var th = o["thumbnails"] as JsonObject;
            string? Pick(params string[] keys) => keys.Select(k => th?[k]?.GetValue<string>()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            var types = (o["types"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToArray() ?? [];
            list.Add(new CaaImage(id, Https(Pick("250", "small") ?? full), Https(Pick("1200", "large", "500") ?? full), o["front"]?.GetValue<bool>() ?? false, o["back"]?.GetValue<bool>() ?? false, types));
        }
        // the front cover first
        return list.OrderByDescending(i => i.Front).ThenBy(i => i.Back).ToList();
    }

    static string Https(string url) => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[7..] : url;

    /// <summary>The release's pictures; when it has none (404), those of its release group. Empty when neither has any.</summary>
    public async Task<IReadOnlyList<CaaImage>> ListAsync(string? releaseMbid, string? releaseGroupMbid, CancellationToken ct = default)
    {
        foreach (var url in new[] { releaseMbid is { Length: > 0 } r ? $"https://coverartarchive.org/release/{r}" : null, releaseGroupMbid is { Length: > 0 } g ? $"https://coverartarchive.org/release-group/{g}" : null })
        {
            if (url is null) continue;
            var (code, body) = await gate.RunAsync(async () =>
            {
                RequestsMade++;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", userAgent); req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await http.SendAsync(req, ct);
                return ((int)res.StatusCode, res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync(ct) : "");
            }, interactive: true, ct);
            if (code == 200) { var l = Parse(body); if (l.Count > 0) return l; }
            else if (code != 404) throw new HttpRequestException($"Cover Art Archive answered {code}");
        }
        return [];
    }

    /// <summary>One picture's bytes (redirects followed), checked to be a JPEG, PNG or WebP of a sensible size; null when it is not.</summary>
    public async Task<byte[]?> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(Https(url), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return null;   // (http is upgraded above; nothing else, such as file: or ftp:, is ever fetched)
        using var req = new HttpRequestMessage(HttpMethod.Get, u);
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode || res.Content.Headers.ContentLength > MaxBytes) return null;
        await using var s = await res.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920]; int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0) { ms.Write(buf, 0, n); if (ms.Length > MaxBytes) return null; }
        var bytes = ms.ToArray();
        return Sniff(bytes) is null ? null : bytes;
    }

    /// <summary>"image/jpeg", "image/png" or "image/webp" by the first bytes; null for anything else (an error page, a script).</summary>
    public static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }
}

/// <summary>
/// Covers a person chose to replace a cover with. The picture behind an art key never changes (browsers keep it for good), so a replacement gets a NEW key ("ov" + a hash of
/// the chosen bytes and time): every place that shows the old key asks [Map] for what to show instead, and the choice can be undone. The original cover and the song files are never touched.
/// </summary>
public sealed class CoverOverrides
{
    /// <summary>Set at start-up so the places that draw covers (a Razor component, the player) can ask without being handed the store.</summary>
    public static CoverOverrides? Current { get; set; }

    readonly string dir; readonly object lk = new();
    Dictionary<string, Entry>? map;
    sealed record Entry(string Key, string Mime, string Source, DateTime ChosenUtc);

    public CoverOverrides(string dir) { this.dir = dir; }

    Dictionary<string, Entry> Load()
    {
        if (map is not null) return map;
        try { var f = Path.Combine(dir, "index.json"); map = File.Exists(f) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(f)) ?? [] : []; }
        catch (Exception) { map = []; }
        return map;
    }

    void Save() { try { Directory.CreateDirectory(dir); var f = Path.Combine(dir, "index.json"); File.WriteAllText(f + ".tmp", JsonSerializer.Serialize(map)); File.Move(f + ".tmp", f, true); } catch (Exception) { } }

    /// <summary>The key to show for [artKey]: the replacement's key, or [artKey] itself when nobody replaced it (and null stays null).</summary>
    public static string? Map(string? artKey) => artKey is null ? null : Current?.Replacement(artKey) ?? artKey;
    public string? Replacement(string artKey) { lock (lk) return Load().TryGetValue(artKey, out var e) ? e.Key : null; }
    public bool Has(string artKey) { lock (lk) return Load().ContainsKey(artKey); }

    /// <summary>Keeps [bytes] (checked to be a picture) as the cover for [artKey]; returns the new key, or null when they are not a picture.</summary>
    public string? Set(string artKey, byte[] bytes, string source)
    {
        var mime = CoverArtArchiveClient.Sniff(bytes); if (mime is null) return null;
        var key = "ov" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(artKey + "|" + Convert.ToHexString(SHA1.HashData(bytes)) + "|" + DateTime.UtcNow.Ticks))).ToLowerInvariant()[..20];
        lock (lk)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, key + ".img"), bytes);
            if (Load().TryGetValue(artKey, out var old)) TryDelete(old.Key);
            Load()[artKey] = new Entry(key, mime, source, DateTime.UtcNow);
            Save();
        }
        return key;
    }

    public void Clear(string artKey) { lock (lk) { if (Load().Remove(artKey, out var e)) { TryDelete(e.Key); Save(); } } }

    void TryDelete(string key) { try { File.Delete(Path.Combine(dir, key + ".img")); } catch (Exception) { } }

    /// <summary>The file and type behind a replacement key; null for any other key (and for a key that looks like a path trick).</summary>
    public (string File, string Mime)? Find(string key)
    {
        if (!key.StartsWith("ov") || key.Length > 40 || !key.All(char.IsAsciiLetterOrDigit)) return null;
        lock (lk)
        {
            var e = Load().Values.FirstOrDefault(x => x.Key == key); if (e is null) return null;
            var f = Path.Combine(dir, key + ".img");
            return File.Exists(f) ? (f, e.Mime) : null;
        }
    }
}
