using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>What ffprobe read of a file through the file mover. <see cref="Gone"/>: the file mover says there is no such file.</summary>
public sealed record ProbeFacts(string Codec, int SampleRate, int BitDepth, long Size, long DurationMs, bool Gone = false);

/// <summary>What the Soulseek fetch of an upgrade filed: the playable track, its path under the file mover's root, and the size the peer said it is
/// (the bytes on disk must match it before the old file is retired).</summary>
public sealed record UpgradeCopy(Track Track, string File, long ExpectedBytes);

/// <summary>The parts of the file mover that smart downloads need beyond filing: whether it can retire a file (an older one can't), retiring one into
/// <c>.flacie-replaced/</c>, and reading a file's real facts. The upgrade worker's tests use a fake.</summary>
public interface IFileMover
{
    Task<bool> CanRetireAsync(CancellationToken ct = default);
    Task<bool> RetireAsync(string path, CancellationToken ct = default);
    /// <summary>Null when the file mover could not be asked or could not read the file; <see cref="ProbeFacts.Gone"/> when the file is not there.</summary>
    Task<ProbeFacts?> ProbeAsync(string path, CancellationToken ct = default);
    /// <summary>F23.4: empties the holding folder of files moved there over [days] ago; false when it could not.</summary>
    Task<bool> PurgeHeldAsync(int days, CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>The real file mover (ipodsync/tools/filemove/server.py), at the address and key in the person's download services.</summary>
public sealed class FileMoverClient(HttpClient http, string url, string key) : IFileMover
{
    string Base => url.TrimEnd('/');

    // what each file mover answered to GET /health, kept ten minutes: asked before every quick download and every upgrade
    static readonly ConcurrentDictionary<string, (long At, bool Retire)> Health = new();

    HttpRequestMessage Req(HttpMethod m, string path, JsonObject? body = null)
    {
        var r = new HttpRequestMessage(m, Base + path);
        r.Headers.TryAddWithoutValidation("X-Api-Key", key);
        if (body is not null) r.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return r;
    }

    /// <summary>True when this file mover lists "retire" in GET /health. A file mover from before smart downloads has no /health at all (404), so it
    /// answers false and nothing that needs a file moved aside runs: no upgrades, and no racing of sources whose copies can't be stopped.</summary>
    public async Task<bool> CanRetireAsync(CancellationToken ct = default)
    {
        if (Base.Length == 0) return false;
        var now = Environment.TickCount64;
        if (Health.TryGetValue(Base, out var h) && now - h.At < 600_000) return h.Retire;
        var ok = false;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(10));
            using var req = Req(HttpMethod.Get, "/health");
            using var res = await http.SendAsync(req, limit.Token);
            if (res.IsSuccessStatusCode && JsonNode.Parse(await res.Content.ReadAsStringAsync(limit.Token))?["routes"] is JsonArray routes)
                ok = routes.Any(r => r is JsonValue v && v.TryGetValue<string>(out var s) && s == "retire");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }   // unreachable just now: asked again next time, nothing remembered
        Health[Base] = (now, ok);
        return ok;
    }

    public async Task<bool> RetireAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var req = Req(HttpMethod.Post, "/retire", new JsonObject { ["path"] = path });
            using var res = await http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    public async Task<bool> PurgeHeldAsync(int days, CancellationToken ct = default)
    {
        try
        {
            using var req = Req(HttpMethod.Post, "/purge", new JsonObject { ["days"] = days });
            using var res = await http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    public async Task<ProbeFacts?> ProbeAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var req = Req(HttpMethod.Post, "/probe", new JsonObject { ["path"] = path });
            using var res = await http.SendAsync(req, ct);
            if ((int)res.StatusCode == 404) return new ProbeFacts("", 0, 0, 0, 0, Gone: true);
            if (!res.IsSuccessStatusCode || JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) is not JsonObject j) return null;
            int I(string k) => j[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
            long L(string k) => j[k] is JsonValue v && v.TryGetValue<long>(out var i) ? i : 0;
            return new ProbeFacts(j["codec"]?.GetValue<string>() ?? "", I("sampleRate"), I("bitDepth"), L("size"), L("durationMs"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }
}
