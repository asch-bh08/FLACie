using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace FLACie.Server.Alexa;

/// <summary>Outcome of verifying one Alexa request. Reason is a short fixed string (never body, token or certificate contents); Request is the parsed body when Ok.</summary>
public record AlexaVerification(bool Ok, string Reason, JsonNode? Request);

/// <summary>Where the signing certificate chain (PEM) comes from. Injectable so tests never touch the network.</summary>
public interface IAlexaCertSource { Task<byte[]> FetchAsync(Uri url, CancellationToken ct); }

/// <summary>Production source: plain HTTPS GET, 5 s timeout, at most 64 KB read. Pass the "catalog" client from IHttpClientFactory, or none.</summary>
public sealed class HttpAlexaCertSource(HttpClient? http = null) : IAlexaCertSource
{
    readonly HttpClient http = http ?? new HttpClient();
    public async Task<byte[]> FetchAsync(Uri url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > AlexaRequestVerifier.MaxCertBytes) throw new InvalidDataException("cert too large");
        await using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
        var ms = new MemoryStream(); var buf = new byte[8192]; int n;
        while ((n = await s.ReadAsync(buf, cts.Token)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > AlexaRequestVerifier.MaxCertBytes) throw new InvalidDataException("cert too large");
        }
        return ms.ToArray();
    }
}

/// <summary>
/// Verifies a request really came from Alexa, as Amazon documents: signing cert URL rules, cert chain (fetched, cached until the leaf expires), SAN echo-api.amazon.com,
/// RSA signature over the raw body (Signature-256 preferred, legacy Signature only when that header is absent), timestamp within 150 s, and the skill id.
/// An empty configured skill id rejects everything (fail closed). skillIds may be comma separated. extraRoots replaces the system trust store (tests).
/// </summary>
public sealed class AlexaRequestVerifier(IAlexaCertSource source, string? skillIds, Func<DateTimeOffset>? clock = null, X509Certificate2Collection? extraRoots = null)
{
    public const int MaxBodyBytes = 1024 * 1024, MaxCertBytes = 64 * 1024, MaxCacheEntries = 20;
    static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(150);
    readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    readonly HashSet<string> skills = new((skillIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, Cached> cache = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> failedFetch = new();

    sealed record Cached(X509Certificate2 Leaf, X509Certificate2Collection Chain, long Seq);
    long seq;

    /// <summary>Entries currently cached (for tests).</summary>
    public int CacheCount => cache.Count;

    static AlexaVerification Fail(string why) => new(false, why, null);

    public async Task<AlexaVerification> VerifyAsync(string? certChainUrl, string? signature256, string? signatureLegacy, byte[] body, CancellationToken ct = default)
    {
        if (skills.Count == 0) return Fail("skill-not-configured");
        if (body is null || body.Length == 0 || body.Length > MaxBodyBytes) return Fail("bad-body-size");
        if (!TryCertUrl(certChainUrl, out var url)) return Fail("bad-cert-url");

        // Signature-256 wins; the legacy SHA-1 header counts only when Signature-256 is absent.
        var useNew = !string.IsNullOrWhiteSpace(signature256);
        var sigText = useNew ? signature256 : signatureLegacy;
        if (string.IsNullOrWhiteSpace(sigText)) return Fail("missing-signature");
        byte[] sig;
        try { sig = Convert.FromBase64String(sigText.Trim()); } catch (FormatException) { return Fail("bad-signature"); }

        var (leaf, why) = await GetLeafAsync(url!, ct);
        if (leaf is null) return Fail(why);

        bool good;
        try
        {
            using var rsa = leaf.GetRSAPublicKey();
            if (rsa is null) return Fail("bad-cert");
            good = rsa.VerifyData(body, sig, useNew ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException) { good = false; }
        if (!good) return Fail("bad-signature");

        JsonNode? json;
        try { json = JsonNode.Parse(body); } catch (Exception) { return Fail("bad-json"); }
        if (json is not JsonObject) return Fail("bad-json");

        if (!DateTimeOffset.TryParse((string?)(json["request"] as JsonObject)?["timestamp"] is { } ts ? ts : null, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var stamp)) return Fail("bad-timestamp");
        if ((now() - stamp).Duration() > Tolerance) return Fail("stale-timestamp");

        var ids = new[] { Id(json["context"]?["System"]), Id(json["session"]) }.Where(i => i != null).ToList();
        if (ids.Count == 0 || ids.Any(i => !skills.Contains(i!))) return Fail("wrong-skill");

        return new AlexaVerification(true, "ok", json);
    }

    static string? Id(JsonNode? n) { try { return (string?)n?["application"]?["applicationId"]; } catch (Exception) { return null; } }

    /// <summary>https, host s3.amazonaws.com, no userinfo, port 443, path under /echo.api/ (case-sensitive, after ../ is resolved; no escapes).</summary>
    static bool TryCertUrl(string? s, out Uri? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(s) || !Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps || u.UserInfo.Length > 0 || u.Port != 443) return false;
        if (!string.Equals(u.Host, "s3.amazonaws.com", StringComparison.OrdinalIgnoreCase)) return false;
        var p = u.AbsolutePath;
        if (p.Contains('%') || p.Contains('\\') || !p.StartsWith("/echo.api/", StringComparison.Ordinal)) return false;
        url = u; return true;
    }

    async Task<(X509Certificate2?, string)> GetLeafAsync(Uri url, CancellationToken ct)
    {
        var key = url.AbsoluteUri;
        if (cache.TryGetValue(key, out var c))
        {
            if (CheckChain(c.Leaf, c.Chain) is null) return (c.Leaf, "");
            cache.TryRemove(key, out _);
        }
        // a certificate address that could not be fetched is not asked for again for 30 seconds (a flood of made-up addresses must not become a flood of downloads)
        if (failedFetch.TryGetValue(key, out var failedAt) && now() - failedAt < TimeSpan.FromSeconds(30)) return (null, "cert-fetch-failed");
        byte[] pem;
        try { pem = await source.FetchAsync(url, ct); }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            if (failedFetch.Count > 200) failedFetch.Clear();
            failedFetch[key] = now(); return (null, "cert-fetch-failed");
        }
        if (pem is null || pem.Length == 0 || pem.Length > MaxCertBytes) return (null, "cert-size");
        var certs = new X509Certificate2Collection();
        try
        {
            var text = Encoding.ASCII.GetString(pem).AsSpan();
            while (PemEncoding.TryFind(text, out var f))
            {
                if (text[f.Label].ToString() == "CERTIFICATE") certs.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(text[f.Base64Data].ToString())));
                text = text[f.Location.End..];
            }
        }
        catch (Exception) { return (null, "bad-cert"); }
        if (certs.Count == 0) return (null, "bad-cert");
        var leaf = certs[0];
        var err = CheckChain(leaf, certs);
        if (err != null) return (null, err);
        Store(key, new Cached(leaf, certs, Interlocked.Increment(ref seq)));
        return (leaf, "");
    }

    void Store(string key, Cached c)
    {
        var t = now();
        foreach (var k in cache.Where(kv => kv.Value.Leaf.NotAfter.ToUniversalTime() <= t.UtcDateTime).Select(kv => kv.Key).ToList()) cache.TryRemove(k, out _);
        while (cache.Count >= MaxCacheEntries && cache.Count > 0)
            cache.TryRemove(cache.MinBy(kv => kv.Value.Seq).Key, out _);
        cache[key] = c;
    }

    /// <summary>Null when the leaf is in date, names echo-api.amazon.com and chains to a trusted root; otherwise a short reason.</summary>
    string? CheckChain(X509Certificate2 leaf, X509Certificate2Collection all)
    {
        var t = now().UtcDateTime;
        if (t < leaf.NotBefore.ToUniversalTime() || t > leaf.NotAfter.ToUniversalTime()) return "cert-expired";
        var san = leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (san is null || !san.EnumerateDnsNames().Any(d => string.Equals(d, "echo-api.amazon.com", StringComparison.OrdinalIgnoreCase))) return "cert-san";
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = t;
        if (extraRoots != null)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(extraRoots);
        }
        for (var i = 1; i < all.Count; i++) chain.ChainPolicy.ExtraStore.Add(all[i]);
        try { return chain.Build(leaf) ? null : "cert-untrusted"; } catch (Exception) { return "cert-untrusted"; }
    }
}

/// <summary>Fixed one-minute window per key (default 30 requests); stale keys are dropped and at most 5000 are kept, so a flood of keys cannot grow memory.</summary>
public sealed class AlexaRateLimiter(Func<DateTimeOffset>? clock = null, int perMinute = 30, int maxKeys = 5000)
{
    static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    readonly Dictionary<string, (DateTimeOffset Start, int Count)> keys = [];
    readonly object gate = new();

    /// <summary>Keys currently tracked (for tests).</summary>
    public int KeyCount { get { lock (gate) return keys.Count; } }

    public bool TryTake(string key)
    {
        key ??= "";
        lock (gate)
        {
            var t = now();
            if (keys.TryGetValue(key, out var w) && t - w.Start < Window)
            {
                if (w.Count >= perMinute) return false;
                keys[key] = (w.Start, w.Count + 1); return true;
            }
            if (!keys.ContainsKey(key) && keys.Count >= maxKeys)
            {
                foreach (var k in keys.Where(kv => t - kv.Value.Start >= Window).Select(kv => kv.Key).ToList()) keys.Remove(k);
                if (keys.Count >= maxKeys) keys.Remove(keys.MinBy(kv => kv.Value.Start).Key);
            }
            keys[key] = (t, 1);
            return true;
        }
    }
}
