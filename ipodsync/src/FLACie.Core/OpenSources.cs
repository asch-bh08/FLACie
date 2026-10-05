using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>A file one of the open sources offers for a song: a plain https URL the file mover can fetch. <see cref="Source"/> is the tag shown
/// with the download ("archive", "jamendo", "audius").</summary>
public sealed record OpenHit(string Source, string Label, string Url, string Ext, long DurationSec);

/// <summary>Supplementary download sources that only offer music which is free to fetch: the Internet Archive's curated live-music and netlabel
/// collections, Jamendo (only tracks whose artist allows downloads) and Audius. They look for a match at the same time and the first one that
/// has the song wins; the rest are cancelled. They never download anything themselves (that happens only if Soulseek found nothing), so
/// they cannot slow or block the Soulseek/Lidarr path.</summary>
public static class OpenSources
{
    /// <summary>Hosts the file mover is allowed to be pointed at. Anything else a source returns is ignored.</summary>
    static readonly string[] AllowedHostSuffixes = ["archive.org", "jamendo.com", "audius.co", "audius.org"];

    /// <summary>Audius serves from whichever discovery node api.audius.co names, so any https host is fine for it.</summary>
    public static bool Allowed(OpenHit hit) => hit.Source == "audius" ? Uri.TryCreate(hit.Url, UriKind.Absolute, out var a) && a.Scheme == Uri.UriSchemeHttps : AllowedHost(hit.Url);

    static bool AllowedHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        AllowedHostSuffixes.Any(s => u.Host == s || u.Host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));

    /// <summary>Looks in every enabled source at once and takes the hit of the highest-priority source that has the song (settings order): the
    /// first-ranked source's answer is waited for, and only if it has nothing the next one's (already running) is used. Null when nobody has it
    /// or the time ran out.</summary>
    public static async Task<OpenHit?> FindAsync(HttpClient http, string artist, string title, int durationSec, OpenSourceSettings cfg, TimeSpan budget, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        var running = new List<Task<OpenHit?>>();
        foreach (var id in cfg.Active)
            running.Add(id switch
            {
                OpenSourceSettings.ArchiveId => Guard(() => ArchiveOrg.FindAsync(http, artist, title, durationSec, cts.Token)),
                OpenSourceSettings.AudiusId => Guard(() => Audius.FindAsync(http, artist, title, durationSec, cts.Token)),
                _ => Guard(() => Jamendo.FindAsync(http, cfg.JamendoId, artist, title, durationSec, cts.Token)),
            });
        try
        {
            foreach (var task in running)
                if (await task is { } hit && Allowed(hit)) return hit;
            return null;
        }
        finally { cts.Cancel(); }
    }

    static async Task<OpenHit?> Guard(Func<Task<OpenHit?>> f)
    {
        try { return await f(); }
        catch (Exception) { return null; }
    }

    /// <summary>Same song: the title (feat. and punctuation ignored) equal, the artist's lead name equal, and a length within 8 seconds when both are known.</summary>
    public static bool Matches(string wantArtist, string wantTitle, int wantSec, string gotArtist, string gotTitle, int gotSec)
    {
        if (Matching.NormTitle(wantTitle) != Matching.NormTitle(gotTitle)) return false;
        var a = Matching.PrimaryArtist(wantArtist); var b = Matching.PrimaryArtist(gotArtist);
        if (a.Length == 0 || b.Length == 0 || !(a == b || a.Contains(b) || b.Contains(a))) return false;
        return wantSec <= 0 || gotSec <= 0 || Math.Abs(wantSec - gotSec) <= 8;
    }

    static async Task<JsonNode?> GetJson(HttpClient http, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "FLACie/1.0 (personal music player)");
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    static int Sec(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<double>(out var d) ? (int)d : v.TryGetValue<string>(out var s) ? ParseLength(s) : 0) : 0;
    /// <summary>"213.5", "3:33" or "01:03:33" to seconds.</summary>
    internal static int ParseLength(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return (int)d;
        var p = s.Split(':'); var total = 0;
        foreach (var x in p) { if (!int.TryParse(x, out var n)) return 0; total = total * 60 + n; }
        return total;
    }

    /// <summary>Internet Archive, restricted to the curated music collections (the Live Music Archive "etree" and the "netlabels" releases), never a
    /// general search: items by the artist in those collections, then the file whose title is the song (FLAC preferred, else MP3).</summary>
    public static class ArchiveOrg
    {
        public static async Task<OpenHit?> FindAsync(HttpClient http, string artist, string title, int durationSec, CancellationToken ct)
        {
            var who = Lucene(Matching.PrimaryArtist(artist));
            if (who.Length == 0) return null;
            var q = $"(collection:etree OR collection:netlabels) AND (mediatype:etree OR mediatype:audio) AND creator:({who})";
            var url = $"https://archive.org/advancedsearch.php?q={Uri.EscapeDataString(q)}&fl%5B%5D=identifier&fl%5B%5D=creator&sort%5B%5D=downloads+desc&rows=6&output=json";
            var docs = (await GetJson(http, url, ct))?["response"]?["docs"] as JsonArray;
            if (docs is null) return null;
            foreach (var d in docs)
            {
                if (d?["identifier"]?.GetValue<string>() is not { Length: > 0 } id) continue;
                var files = (await GetJson(http, $"https://archive.org/metadata/{Uri.EscapeDataString(id)}/files", ct))?["result"] as JsonArray;
                if (files is null) continue;
                OpenHit? best = null; var bestRank = 0;
                foreach (var f in files)
                {
                    var name = f?["name"]?.GetValue<string>(); var fmt = f?["format"]?.GetValue<string>() ?? "";
                    var ft = f?["title"]?.GetValue<string>();
                    if (name is null || ft is null) continue;
                    var rank = fmt.Equals("Flac", StringComparison.OrdinalIgnoreCase) || fmt.Equals("24bit Flac", StringComparison.OrdinalIgnoreCase) ? 3
                        : fmt.Contains("MP3", StringComparison.OrdinalIgnoreCase) && !fmt.Contains("64Kbps") ? 2 : 0;
                    if (rank <= bestRank) continue;
                    if (!Matches(artist, title, durationSec, artist, ft, Sec(f?["length"]))) continue;
                    best = new OpenHit("archive", "the Internet Archive", $"https://archive.org/download/{Uri.EscapeDataString(id)}/{string.Join('/', name.Split('/').Select(Uri.EscapeDataString))}",
                        name[(name.LastIndexOf('.') + 1)..].ToLowerInvariant(), Sec(f?["length"]));
                    bestRank = rank;
                }
                if (best is not null) return best;
            }
            return null;
        }

        static string Lucene(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"[^\p{L}\p{N} ]", " ").Trim();
    }

    /// <summary>Jamendo, only tracks the artist allows to be downloaded (<c>audiodownload_allowed</c>). Needs a free client id (FLACIE_JAMENDO_CLIENT_ID).</summary>
    public static class Jamendo
    {
        public static async Task<OpenHit?> FindAsync(HttpClient http, string clientId, string artist, string title, int durationSec, CancellationToken ct)
        {
            var url = $"https://api.jamendo.com/v3.0/tracks/?client_id={Uri.EscapeDataString(clientId)}&format=json&limit=10" +
                      $"&namesearch={Uri.EscapeDataString(Matching.NormTitle(title))}&artist_name={Uri.EscapeDataString(Matching.PrimaryArtist(artist))}";
            var results = (await GetJson(http, url, ct))?["results"] as JsonArray;
            if (results is null) return null;
            foreach (var r in results)
            {
                // the filter is asked for in the query and checked again: a track that does not say so explicitly is not downloaded
                if (r?["audiodownload_allowed"] is not JsonValue ok || !(ok.TryGetValue<bool>(out var b) && b)) continue;
                if (r["audiodownload"]?.GetValue<string>() is not { Length: > 0 } dl) continue;
                if (!Matches(artist, title, durationSec, r["artist_name"]?.GetValue<string>() ?? "", r["name"]?.GetValue<string>() ?? "", Sec(r["duration"]))) continue;
                return new OpenHit("jamendo", "Jamendo", dl, "mp3", Sec(r["duration"]));
            }
            return null;
        }
    }

    /// <summary>Audius: the public REST API, no key. A host is picked from api.audius.co, tracks are found by search and fetched from the stream endpoint.</summary>
    public static class Audius
    {
        public static async Task<OpenHit?> FindAsync(HttpClient http, string artist, string title, int durationSec, CancellationToken ct)
        {
            var hosts = (await GetJson(http, "https://api.audius.co", ct))?["data"] as JsonArray;
            var host = hosts?.Select(h => h?.GetValue<string>()).FirstOrDefault(h => !string.IsNullOrEmpty(h) && h!.StartsWith("https://"));
            if (host is null) return null;
            var q = $"{Matching.PrimaryArtist(artist)} {Matching.NormTitle(title)}";
            var results = (await GetJson(http, $"{host.TrimEnd('/')}/v1/tracks/search?query={Uri.EscapeDataString(q)}&app_name=FLACie&limit=10", ct))?["data"] as JsonArray;
            if (results is null) return null;
            foreach (var r in results)
            {
                if (r?["id"]?.GetValue<string>() is not { Length: > 0 } id) continue;
                if (r["is_streamable"] is JsonValue s && s.TryGetValue<bool>(out var streamable) && !streamable) continue;
                if (!Matches(artist, title, durationSec, r["user"]?["name"]?.GetValue<string>() ?? "", r["title"]?.GetValue<string>() ?? "", Sec(r["duration"]))) continue;
                return new OpenHit("audius", "Audius", $"{host.TrimEnd('/')}/v1/tracks/{id}/stream?app_name=FLACie", "mp3", Sec(r["duration"]));
            }
            return null;
        }
    }
}

/// <summary>Which open sources are on and in what order they are preferred, kept in the profile's <c>services.opensources</c> (the Jamendo client id in
/// <c>services.jamendo.id</c>), so the phone, the Windows app and the web page share one setup. Jamendo only counts as on with a client id.</summary>
public sealed record OpenSourceSettings(bool Archive, bool Audius, bool Jamendo, string JamendoId, IReadOnlyList<string> Order)
{
    public const string ArchiveId = "archive", AudiusId = "audius", JamendoSourceId = "jamendo";
    public static readonly string[] AllIds = [ArchiveId, AudiusId, JamendoSourceId];
    public static OpenSourceSettings Default => new(true, true, true, "", AllIds);

    /// <summary>The saved order with anything unknown dropped and anything missing appended, so every source appears exactly once.</summary>
    public IReadOnlyList<string> FullOrder => Order.Where(AllIds.Contains).Distinct().Concat(AllIds.Except(Order)).ToList();
    public bool IsOn(string id) => id switch { ArchiveId => Archive, AudiusId => Audius, _ => Jamendo };
    /// <summary>The sources that will actually be searched, best first.</summary>
    public IEnumerable<string> Active => FullOrder.Where(id => IsOn(id) && (id != JamendoSourceId || JamendoId.Length > 0));
    public bool Any => Active.Any();

    public static OpenSourceSettings From(JsonObject? services, string? fallbackJamendoId = null)
    {
        var o = services?["opensources"] as JsonObject;
        bool B(string k) => o?[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : true;
        var order = (o?["order"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() ?? [];
        var id = services?["jamendo"]?["id"] is JsonValue j && j.TryGetValue<string>(out var s) ? s.Trim() : "";
        return new(B(ArchiveId), B(AudiusId), B(JamendoSourceId), id.Length > 0 ? id : (fallbackJamendoId ?? "").Trim(), order);
    }

    public void WriteTo(JsonObject services)
    {
        services["opensources"] = new JsonObject
        {
            [ArchiveId] = Archive, [AudiusId] = Audius, [JamendoSourceId] = Jamendo,
            ["order"] = new JsonArray(FullOrder.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
        };
        if (JamendoId.Length > 0) services["jamendo"] = new JsonObject { ["id"] = JamendoId }; else services.Remove("jamendo");
    }
}
