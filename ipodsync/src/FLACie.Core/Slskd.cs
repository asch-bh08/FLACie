using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FLACie.Core;

public sealed record SlskdFile(string Username, string Filename, long Size, bool HasFreeUploadSlot, int? BitRate, long UploadSpeed = 0, int QueueLength = 0, int LengthSec = 0)
{
    public string Leaf => Filename.Replace('\\', '/').Split('/').Last();
    public string Folder => Filename.Replace('\\', '/') is var f && f.Contains('/') ? f[..f.LastIndexOf('/')] : "";
    public bool Lossless => new[] { "flac", "alac", "wav", "aiff", "ape", "wv" }.Contains(Leaf[(Leaf.LastIndexOf('.') + 1)..].ToLowerInvariant());
}

/// <summary>One file's transfer as slskd reports it. <see cref="Done"/>: true succeeded, false failed, null still going.</summary>
public sealed record SlskdTransfer(string Id, long Bytes, bool? Done);

/// <summary>
/// Talks to slskd (a Soulseek daemon with a REST API, auth by its X-API-Key); a port of the Android SlskdClient with the same
/// peer ranking: the typed artist in the path, no unrequested remix/live/cover, a length close to the real song, lossless
/// whenever any peer has it, then a free upload slot, a short queue and a fast uploader.
/// </summary>
public sealed class SlskdClient(HttpClient http, string url, string apiKey)
{
    static readonly SemaphoreSlim SearchGate = new(1, 1);
    static long lastSearchAt;
    static readonly string[] AudioExt = ["mp3", "flac", "m4a", "aac", "ogg", "opus", "wav", "aiff", "aif", "alac", "wma", "ape", "wv"];
    string Base => url.TrimEnd('/');

    HttpRequestMessage Req(HttpMethod m, string path, JsonNode? body = null)
    {
        var r = new HttpRequestMessage(m, Base + path);
        r.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        if (body is not null) r.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return r;
    }

    async Task<string> Send(HttpMethod m, string path, JsonNode? body = null, CancellationToken ct = default)
    {
        using var req = Req(m, path, body);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new IOException($"HTTP {(int)res.StatusCode}");
        return await res.Content.ReadAsStringAsync(ct);
    }

    public async Task<(bool Ok, string? Message)> TestAsync(CancellationToken ct = default)
    {
        try { var j = JsonNode.Parse(await Send(HttpMethod.Get, "/api/v0/application", null, ct)); return (true, j?["version"]?["current"]?.GetValue<string>()); }
        catch (Exception e) { return (false, e.Message); }
    }

    /// <summary>Every decent-quality audio file peers offer for the query (lossless or at least 256 kbps). Stops the search once
    /// enough peers answered and a short grace period passed, or at the timeout.</summary>
    public async Task<List<SlskdFile>> SearchAsync(string query, int timeoutMs = 15_000, int enough = 40, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var text = Regex.Replace(Regex.Replace(query, @"[^\p{L}\p{N}' ]", " "), @"\s+", " ").Trim();
        // milliseconds: slskd hands the number straight to Soulseek.NET (its docs say seconds, but 15 ended every search after 15ms)
        var body = new JsonObject { ["id"] = id, ["searchText"] = text, ["searchTimeout"] = Math.Max(5_000, timeoutMs), ["responseLimit"] = 200, ["fileLimit"] = 20_000 };
        // searches are spaced at least 2s apart, whoever starts them, so a big album can't fire a burst at the Soulseek server
        await SearchGate.WaitAsync(ct);
        try
        {
            var wait = Interlocked.Read(ref lastSearchAt) + 2_000 - Environment.TickCount64;
            if (wait > 0) await Task.Delay((int)wait, ct);
            Interlocked.Exchange(ref lastSearchAt, Environment.TickCount64);
        }
        finally { SearchGate.Release(); }
        // slskd takes one search request at a time (429 for another)
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try { await Send(HttpMethod.Post, "/api/v0/searches", body, ct); break; }
            catch (IOException e) { if (e.Message != "HTTP 429" || attempt == 7) throw; await Task.Delay(400 + attempt * 200, ct); }
        }
        var start = Environment.TickCount64; long firstSeen = 0;
        while (Environment.TickCount64 - start < timeoutMs + 2_000)
        {
            await Task.Delay(500, ct);
            JsonNode? s; try { s = JsonNode.Parse(await Send(HttpMethod.Get, $"/api/v0/searches/{id}", null, ct)); } catch (IOException) { continue; } catch (HttpRequestException) { continue; }
            if (s?["isComplete"]?.GetValue<bool>() == true || (s?["state"]?.GetValue<string>() ?? "").Contains("Completed")) break;
            var n = s?["responseCount"]?.GetValue<int>() ?? 0;
            if (n > 0 && firstSeen == 0) firstSeen = Environment.TickCount64;
            var since = firstSeen == 0 ? 0 : Environment.TickCount64 - firstSeen;
            if (n >= enough || (n >= 5 && since > 2_500) || (n > 0 && since > 5_000))
            {
                try { await Send(HttpMethod.Put, $"/api/v0/searches/{id}", null, ct); } catch { }
                await WaitComplete(id, ct);
                break;
            }
        }
        JsonNode? json;
        try { json = JsonNode.Parse(await Send(HttpMethod.Get, $"/api/v0/searches/{id}?includeResponses=true", null, ct)); } catch (Exception) { return []; }
        try { await Send(HttpMethod.Delete, $"/api/v0/searches/{id}", null, ct); } catch { }
        return Parse(json?["responses"] as JsonArray ?? []);
    }

    async Task WaitComplete(string id, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            try { var s = JsonNode.Parse(await Send(HttpMethod.Get, $"/api/v0/searches/{id}", null, ct)); if (s?["isComplete"]?.GetValue<bool>() == true || (s?["state"]?.GetValue<string>() ?? "").Contains("Completed")) return; } catch (IOException) { }
            await Task.Delay(250, ct);
        }
    }

    static List<SlskdFile> Parse(JsonArray responses)
    {
        var res = new List<SlskdFile>();
        foreach (var r in responses.OfType<JsonObject>())
        {
            if (r["files"] is not JsonArray files) continue;
            foreach (var f in files.OfType<JsonObject>())
            {
                var name = f["filename"]?.GetValue<string>() ?? "";
                var leaf = name.Replace('\\', '/').Split('/').Last();
                if (!AudioExt.Contains(leaf[(leaf.LastIndexOf('.') + 1)..].ToLowerInvariant())) continue;
                int? bitRate = f["bitRate"] is JsonValue b && b.TryGetValue<int>(out var br) ? br : null;
                if (bitRate is < 256) continue;
                res.Add(new SlskdFile(r["username"]?.GetValue<string>() ?? "", name, f["size"]?.GetValue<long>() ?? 0, r["hasFreeUploadSlot"]?.GetValue<bool>() ?? false, bitRate,
                    r["uploadSpeed"]?.GetValue<long>() ?? 0, r["queueLength"]?.GetValue<int>() ?? 0, f["length"] is JsonValue l && l.TryGetValue<int>(out var len) ? len : 0));
            }
        }
        return res;
    }

    // ---- ranking ----

    static readonly string[] Variants = ["remix", "live", "instrumental", "karaoke", "acapella", "cover", "sped", "slowed", "nightcore", "8d", "reverb", "edit", "demo", "mix"];

    static List<string> Tokens(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Split(' ').Where(t => t.Length > 0 && t != "the").ToList();

    static int PeerScore(SlskdFile f) => (f.HasFreeUploadSlot ? 500 : 0) - Math.Min(50, f.QueueLength) * 8 + (int)Math.Min(400, f.UploadSpeed / 50_000);

    /// <summary>The best files for one song, best first, at most one per peer (so falling through to the next tries someone else).</summary>
    public static List<SlskdFile> RankSong(IEnumerable<SlskdFile> files, string artist, string title, int durationSec = 0)
    {
        var titleWords = Tokens(WebCatalog.BaseTitle(title));
        if (titleWords.Count == 0) return [];
        var artistWords = Tokens(Matching.PrimaryArtist(artist));
        var wanted = Tokens($"{artist} {title}").ToHashSet();
        var ok = files.Where(f =>
        {
            var leaf = Tokens(f.Leaf[..(f.Leaf.LastIndexOf('.') is var d && d > 0 ? d : f.Leaf.Length)]).ToHashSet();
            return titleWords.All(leaf.Contains) && !Variants.Any(v => leaf.Contains(v) && !wanted.Contains(v)) &&
                   (durationSec == 0 || f.LengthSec == 0 || Math.Abs(f.LengthSec - durationSec) <= 12) && f.Size > 1_000_000;
        }).ToList();
        // the typed artist somewhere in the path (file or folder) rules out the same title by somebody else
        var byArtist = ok.Where(f => { var p = Tokens(f.Filename).ToHashSet(); return artistWords.All(p.Contains); }).ToList();
        var matched = byArtist.Count > 0 ? byArtist : artistWords.Count == 0 ? ok : [];
        // lossless whenever anyone has it, however slow; MP3/AAC (320 kbps) only when no peer has a lossless copy at all
        var pool = matched.Where(f => f.Lossless).ToList();
        if (pool.Count == 0) pool = matched.Where(f => (f.BitRate ?? 0) >= 320).ToList();
        return pool.OrderByDescending(PeerScore).DistinctBy(f => f.Username).ToList();
    }

    /// <summary>An album as whole folders from single peers: each candidate folder with, per tracklist entry, its best file.</summary>
    public static List<SlskdFile?[]> RankAlbumFolders(IEnumerable<SlskdFile> files, string artist, string album, IReadOnlyList<string> titles)
    {
        var artistWords = Tokens(Matching.PrimaryArtist(artist)).ToHashSet();
        var albumWords = Tokens(Regex.Replace(album, @"\s*[(\[][^)\]]*[)\]]", "")).ToHashSet();
        var folders = files.GroupBy(f => f.Username + "\0" + f.Folder).Select(g => g.ToList()).Where(fs =>
        {
            var path = Tokens(fs[0].Filename).ToHashSet();
            return (artistWords.Count > 0 && artistWords.All(path.Contains)) || (albumWords.Count > 0 && albumWords.All(path.Contains));
        }).ToList();
        var need = Math.Max(1, (int)(titles.Count * 0.6));
        var scored = new List<(SlskdFile?[] Picks, int Score)>();
        foreach (var fs in folders)
        {
            // longest titles pick first, so "Love Song" takes its file before "Love" can; then the shortest matching name is the closest
            var used = new HashSet<SlskdFile>();
            var picks = new SlskdFile?[titles.Count];
            foreach (var i in Enumerable.Range(0, titles.Count).OrderByDescending(i => Tokens(WebCatalog.BaseTitle(titles[i])).Count))
            {
                var tw = Tokens(WebCatalog.BaseTitle(titles[i]));
                if (tw.Count == 0) continue;
                var pick = fs.Where(f => !used.Contains(f) && tw.All(Tokens(f.Leaf[..(f.Leaf.LastIndexOf('.') is var d && d > 0 ? d : f.Leaf.Length)]).ToHashSet().Contains))
                    .OrderBy(f => f.Leaf.Length).ThenBy(f => f.Lossless ? 0 : 1).FirstOrDefault();
                if (pick is not null) { picks[i] = pick; used.Add(pick); }
            }
            var found = picks.Count(p => p is not null);
            var lossless = picks.All(p => p is null || p.Lossless);
            if (found >= need) scored.Add((picks, (lossless ? 100_000 : 0) + found * 100 + PeerScore(fs[0])));
        }
        return scored.OrderByDescending(s => s.Score).Select(s => s.Picks).ToList();
    }

    // ---- downloads ----

    static string Enc(string s) => Uri.EscapeDataString(s);

    /// <summary>Queues files from one peer in a single request; slskd writes each into its downloads directory once complete.</summary>
    public async Task DownloadAsync(IReadOnlyList<SlskdFile> files, CancellationToken ct = default)
    {
        if (files.Count == 0) return;
        var body = new JsonArray(files.Select(f => (JsonNode)new JsonObject { ["filename"] = f.Filename, ["size"] = f.Size }).ToArray());
        await Send(HttpMethod.Post, $"/api/v0/transfers/downloads/{Enc(files[0].Username)}", body, ct);
    }

    /// <summary>Current state of each of a peer's transfers, by remote filename.</summary>
    public async Task<Dictionary<string, SlskdTransfer>> TransfersAsync(string username, CancellationToken ct = default)
    {
        var res = new Dictionary<string, SlskdTransfer>();
        JsonNode? j; try { j = JsonNode.Parse(await Send(HttpMethod.Get, $"/api/v0/transfers/downloads/{Enc(username)}", null, ct)); } catch (Exception) { return res; }
        foreach (var dir in j?["directories"] as JsonArray ?? [])
            foreach (var f in dir?["files"] as JsonArray ?? [])
            {
                var state = f?["state"]?.GetValue<string>() ?? "";
                bool? done = state.Contains("Succeeded", StringComparison.OrdinalIgnoreCase) ? true
                    : new[] { "Errored", "Cancelled", "Rejected", "TimedOut" }.Any(x => state.Contains(x, StringComparison.OrdinalIgnoreCase)) ? false : null;
                res[f?["filename"]?.GetValue<string>() ?? ""] = new SlskdTransfer(f?["id"]?.GetValue<string>() ?? "", f?["bytesTransferred"]?.GetValue<long>() ?? 0, done);
            }
        return res;
    }

    /// <summary>Stops a transfer and clears it from slskd's list (a stalled peer is dropped for the next one).</summary>
    public async Task CancelAsync(string username, string id) { try { await Send(HttpMethod.Delete, $"/api/v0/transfers/downloads/{Enc(username)}/{Enc(id)}?remove=true"); } catch { } }
}
