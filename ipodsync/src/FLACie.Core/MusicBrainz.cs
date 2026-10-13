using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>One recording MusicBrainz offers for a song, with where it appears.</summary>
public sealed record MbCandidate(string Mbid, string Title, string Artist, string Album, string? ReleaseMbid, string? ReleaseGroupMbid, int? Year, int? LengthMs, string? Country, int ApiScore)
{
    public string Url => "https://musicbrainz.org/recording/" + Mbid;
}

/// <summary>How well a MusicBrainz candidate fits a song of the library: a number from 0 to 100 that the person sees. Nothing is ever applied because of it (the person chooses).</summary>
public static class MbMatching
{
    /// <summary>At or above this the first candidate is offered as "probably this one"; below it the list is shown without a favourite.</summary>
    public const int Likely = 80;

    public static double Score(Track t, MbCandidate c)
    {
        double s = 0;
        var tt = Matching.NormTitle(t.Title); var ct = Matching.NormTitle(c.Title);
        if (tt.Length > 0 && tt == ct) s += 40; else if (tt.Length > 0 && ct.Length > 0 && (tt.Contains(ct) || ct.Contains(tt))) s += 20;
        var ta = Matching.PrimaryArtist(t.Artist.Length > 0 ? t.Artist : t.AlbumArtist); var ca = Matching.PrimaryArtist(c.Artist);
        if (ta.Length > 0 && ta == ca) s += 30; else if (ta.Length > 0 && ca.Length > 0 && (ta.Contains(ca) || ca.Contains(ta))) s += 15;
        var tb = Matching.AlbumNorm(t.Album); var cb = Matching.AlbumNorm(c.Album);
        if (tb.Length > 0 && tb == cb) s += 15; else if (tb.Length > 0 && cb.Length > 0 && (tb.Contains(cb) || cb.Contains(tb))) s += 7;
        if (t.DurationMs > 0 && c.LengthMs is { } l && l > 0)
        {
            var d = Math.Abs(t.DurationMs - l);
            s += d <= 3_000 ? 15 : d <= 10_000 ? 8 : 0;
        }
        return s;
    }

    /// <summary>The candidates best first (ties keep MusicBrainz's own order, then its score).</summary>
    public static IReadOnlyList<(MbCandidate Candidate, double Score)> Rank(Track t, IEnumerable<MbCandidate> candidates) =>
        candidates.Select((c, i) => (c, s: Score(t, c), i)).OrderByDescending(x => x.s).ThenByDescending(x => x.c.ApiScore).ThenBy(x => x.i).Select(x => (x.c, x.s)).ToList();
}

/// <summary>
/// MusicBrainz asks for about one request a second from one address, answered by 503 beyond that, and a descriptive User-Agent. All the app's MusicBrainz traffic goes through
/// one of these: requests are spaced by [minInterval], those a person is waiting for go before background ones, and a "retry later" answer is honoured. Time and waiting are
/// injected so the rules are tested without sleeping.
/// </summary>
public sealed class MusicBrainzGate(TimeSpan? minInterval = null, Func<DateTime>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    readonly TimeSpan gap = minInterval ?? TimeSpan.FromMilliseconds(1100);
    readonly Func<DateTime> clock = now ?? (() => DateTime.UtcNow);
    readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? ((d, ct) => Task.Delay(d, ct));
    readonly object lk = new();
    readonly LinkedList<(TaskCompletionSource Go, bool Interactive)> queue = new();
    bool busy; DateTime next = DateTime.MinValue;
    public int Requests { get; private set; }

    /// <summary>Runs [work] when it is its turn and at least the gap after the last request began. [retryAfter] (from a 503/429 answer) pushes the next turn out.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> work, bool interactive, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lk)
        {
            if (interactive)
            {
                // ahead of every background request, behind earlier interactive ones
                var at = queue.First; while (at is not null && at.Value.Interactive) at = at.Next;
                if (at is null) queue.AddLast((tcs, true)); else queue.AddBefore(at, (tcs, true));
            }
            else queue.AddLast((tcs, false));
            Pump();
        }
        using (ct.Register(() => tcs.TrySetCanceled(ct))) await tcs.Task;
        try
        {
            var due = next - clock();
            if (due > TimeSpan.Zero) await wait(due, ct);
            lock (lk) { next = clock() + gap; Requests++; }
            return await work();
        }
        finally { lock (lk) { busy = false; Pump(); } }
    }

    /// <summary>The server said "not before this long": nobody asks again until then.</summary>
    public void Pause(TimeSpan d) { lock (lk) { var until = clock() + d; if (until > next) next = until; } }

    void Pump()
    {
        if (busy) return;
        while (queue.First is { } n)
        {
            queue.RemoveFirst();
            if (n.Value.Go.TrySetResult()) { busy = true; return; }   // (a request cancelled while it waited is skipped)
        }
    }
}

/// <summary>Looks recordings up on MusicBrainz for the Info panel. Never called at launch or in bulk: only when a person asks about a song.</summary>
public sealed class MusicBrainzClient(HttpClient http, string userAgent, string? cacheDir = null, MusicBrainzGate? gate = null, Func<DateTime>? now = null)
{
    public const string DefaultContact = "https://github.com/asch-bh08/FLACie";
    public static string UserAgentFor(string version, string? contact = null) => $"FLACie/{version} ( {(string.IsNullOrWhiteSpace(contact) ? DefaultContact : contact.Trim())} )";

    readonly MusicBrainzGate gate = gate ?? new MusicBrainzGate();
    readonly Func<DateTime> clock = now ?? (() => DateTime.UtcNow);
    public TimeSpan CacheFor { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan CacheNoneFor { get; init; } = TimeSpan.FromDays(7);
    public int RequestsMade { get; private set; }

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The Lucene query for a song: its title and lead artist (and album when there is one).</summary>
    public static string QueryFor(Track t)
    {
        var parts = new List<string> { "recording:" + Quote(t.Title) };
        var artist = t.Artist.Length > 0 ? t.Artist : t.AlbumArtist;
        if (artist.Length > 0) parts.Add("artist:" + Quote(artist));
        if (t.Album.Length > 0) parts.Add("release:" + Quote(t.Album));
        return string.Join(" AND ", parts);
    }

    public async Task<IReadOnlyList<MbCandidate>> SearchRecordingAsync(Track t, bool interactive = true, CancellationToken ct = default)
    {
        if (t.Title.Trim().Length == 0) return [];
        var query = QueryFor(t);
        if (Cached(query) is { } hit) return hit;
        var url = "https://musicbrainz.org/ws/2/recording?query=" + Uri.EscapeDataString(query) + "&fmt=json&limit=8";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (code, body, retry) = await gate.RunAsync(async () =>
            {
                RequestsMade++;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await http.SendAsync(req, ct);
                return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct), RetryAfter(res));
            }, interactive, ct);
            if (code == 200)
            {
                var list = Parse(body);
                Store(query, list);
                return list;
            }
            // too many requests / busy: wait as long as the server says (at most a minute), then try again; any other answer is a failure
            if (code is 503 or 429 && attempt < 2) { gate.Pause(retry ?? TimeSpan.FromSeconds(2 + attempt * 3)); continue; }
            throw new HttpRequestException($"MusicBrainz answered {code}");
        }
        throw new HttpRequestException("MusicBrainz is busy");
    }

    static TimeSpan? RetryAfter(HttpResponseMessage r) =>
        r.Headers.RetryAfter?.Delta is { } d ? TimeSpan.FromSeconds(Math.Min(60, d.TotalSeconds)) : null;

    public static IReadOnlyList<MbCandidate> Parse(string json)
    {
        var list = new List<MbCandidate>();
        if (JsonNode.Parse(json)?["recordings"] is not JsonArray arr) return list;
        foreach (var r in arr.OfType<JsonObject>())
        {
            var id = r["id"]?.GetValue<string>(); if (string.IsNullOrEmpty(id)) continue;
            var artist = string.Concat((r["artist-credit"] as JsonArray)?.OfType<JsonObject>().Select(c => (c["name"]?.GetValue<string>() ?? c["artist"]?["name"]?.GetValue<string>() ?? "") + (c["joinphrase"]?.GetValue<string>() ?? "")) ?? []);
            var rel = (r["releases"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            int? year = null;
            if (rel?["date"]?.GetValue<string>() is { Length: >= 4 } d && int.TryParse(d[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var y)) year = y;
            list.Add(new MbCandidate(id, r["title"]?.GetValue<string>() ?? "", artist, rel?["title"]?.GetValue<string>() ?? "", rel?["id"]?.GetValue<string>(),
                rel?["release-group"]?["id"]?.GetValue<string>(), year, r["length"]?.GetValue<int>(), rel?["country"]?.GetValue<string>(), r["score"]?.GetValue<int>() ?? 0));
        }
        return list;
    }

    // ---- the disk cache: one small file per query; a "nothing found" is remembered too, for less time ----
    string? CacheFile(string query) => cacheDir is null ? null : Path.Combine(cacheDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(query))).ToLowerInvariant() + ".json");

    sealed record Entry(DateTime SavedUtc, List<MbCandidate> Hits);

    IReadOnlyList<MbCandidate>? Cached(string query)
    {
        try
        {
            if (CacheFile(query) is not { } f || !File.Exists(f)) return null;
            var e = JsonSerializer.Deserialize<Entry>(File.ReadAllText(f));
            if (e is null) return null;
            var ttl = e.Hits.Count == 0 ? CacheNoneFor : CacheFor;
            return clock() - e.SavedUtc <= ttl ? e.Hits : null;
        }
        catch (Exception) { return null; }
    }

    void Store(string query, IReadOnlyList<MbCandidate> hits)
    {
        try
        {
            if (CacheFile(query) is not { } f) return;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            var tmp = f + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Entry(clock(), hits.ToList())));
            File.Move(tmp, f, true);
        }
        catch (Exception) { }
    }
}

/// <summary>What the person chose as the MusicBrainz match for a song. Kept beside the library, never written into the file or into Jellyfin.</summary>
public sealed record MbChoice(string Mbid, string Title, string Artist, string Album, string? ReleaseMbid, string? ReleaseGroupMbid, int? Year, DateTime ChosenUtc);

public sealed class MbOverlay(string file)
{
    readonly object lk = new();
    Dictionary<string, MbChoice>? map;

    Dictionary<string, MbChoice> Load()
    {
        if (map is not null) return map;
        try { map = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, MbChoice>>(File.ReadAllText(file)) ?? [] : []; }
        catch (Exception) { map = []; }
        return map;
    }

    public static string KeyOf(Track t) => t.JellyfinId is { Length: > 0 } id ? id : SpeedPolicy.KeyOf(t.Path);
    public MbChoice? Get(Track t) { lock (lk) return Load().GetValueOrDefault(KeyOf(t)); }

    public void Set(Track t, MbCandidate c)
    {
        lock (lk) { Load()[KeyOf(t)] = new MbChoice(c.Mbid, c.Title, c.Artist, c.Album, c.ReleaseMbid, c.ReleaseGroupMbid, c.Year, DateTime.UtcNow); Save(); }
    }

    public void Clear(Track t) { lock (lk) { if (Load().Remove(KeyOf(t))) Save(); } }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(map)); File.Move(tmp, file, true);
        }
        catch (Exception) { }
    }
}
