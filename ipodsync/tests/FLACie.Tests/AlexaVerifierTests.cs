using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FLACie.Server.Alexa;
using Xunit;

namespace FLACie.Tests;

/// <summary>Alexa request verification (cert URL, cert chain, signature, timestamp, skill id) with a self-made CA, plus the per-key rate limiter.</summary>
public class AlexaVerifierTests
{
    const string GoodUrl = "https://s3.amazonaws.com/echo.api/echo-api-cert-7.pem";
    const string Skill = "amzn1.ask.skill.test-1234";
    static readonly DateTimeOffset Now = new(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    sealed class Pki : IDisposable
    {
        public X509Certificate2 Root, Leaf; public RSA LeafKey = RSA.Create(2048);
        readonly RSA rootKey = RSA.Create(2048);
        public Pki(string san = "echo-api.amazon.com", DateTimeOffset? from = null, DateTimeOffset? to = null)
        {
            var rr = new CertificateRequest("CN=Test Root " + Guid.NewGuid().ToString("N")[..6], rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rr.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rr.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            Root = rr.CreateSelfSigned(Now.AddYears(-1), Now.AddYears(5));
            var lr = new CertificateRequest("CN=echo-api.amazon.com", LeafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var b = new SubjectAlternativeNameBuilder(); b.AddDnsName(san);
            lr.CertificateExtensions.Add(b.Build());
            lr.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            lr.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            Leaf = lr.Create(Root, from ?? Now.AddDays(-30), to ?? Now.AddDays(30), RandomNumberGenerator.GetBytes(8));
        }
        public byte[] Pem(bool withRoot = true) => Encoding.ASCII.GetBytes(Leaf.ExportCertificatePem() + "\n" + (withRoot ? Root.ExportCertificatePem() + "\n" : ""));
        public string Sign(byte[] body, HashAlgorithmName? h = null) => Convert.ToBase64String(LeafKey.SignData(body, h ?? HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        public X509Certificate2Collection Roots() => [Root];
        public void Dispose() { Root.Dispose(); Leaf.Dispose(); LeafKey.Dispose(); rootKey.Dispose(); }
    }

    sealed class FakeSource(byte[]? pem) : IAlexaCertSource
    {
        public int Calls; public Exception? Throw; public byte[]? Pem = pem;
        public Task<byte[]> FetchAsync(Uri url, CancellationToken ct) { Calls++; if (Throw != null) throw Throw; return Task.FromResult(Pem!); }
    }

    static byte[] Body(DateTimeOffset? ts = null, string skill = Skill, bool sessionOnly = false)
    {
        var t = (ts ?? Now).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var app = $"{{\"application\":{{\"applicationId\":\"{skill}\"}}}}";
        return Encoding.UTF8.GetBytes(sessionOnly
            ? $"{{\"version\":\"1.0\",\"session\":{app},\"request\":{{\"type\":\"LaunchRequest\",\"timestamp\":\"{t}\"}}}}"
            : $"{{\"version\":\"1.0\",\"context\":{{\"System\":{app}}},\"request\":{{\"type\":\"LaunchRequest\",\"timestamp\":\"{t}\"}}}}");
    }

    static AlexaRequestVerifier Make(Pki p, FakeSource src, string skills = Skill, Func<DateTimeOffset>? clock = null) =>
        new(src, skills, clock ?? (() => Now), p.Roots());

    static async Task<AlexaVerification> Run(Pki p, byte[]? body = null, string url = GoodUrl, string skills = Skill, Func<DateTimeOffset>? clock = null, FakeSource? src = null)
    {
        body ??= Body();
        return await Make(p, src ?? new FakeSource(p.Pem()), skills, clock).VerifyAsync(url, p.Sign(body), null, body);
    }

    [Fact]
    public async Task Happy_path_returns_parsed_request()
    {
        using var p = new Pki();
        var r = await Run(p);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal("LaunchRequest", (string?)r.Request!["request"]!["type"]);
    }

    [Fact]
    public async Task Happy_path_with_leaf_only_chain_and_trusted_leaf_issuer()
    {
        using var p = new Pki();
        var r = await Make(p, new FakeSource(p.Pem(withRoot: false))).VerifyAsync(GoodUrl, p.Sign(Body()), null, Body());
        Assert.True(r.Ok, r.Reason);
    }

    [Fact]
    public async Task Session_application_id_is_accepted()
    {
        using var p = new Pki();
        var r = await Run(p, Body(sessionOnly: true));
        Assert.True(r.Ok, r.Reason);
    }

    [Fact]
    public async Task Tampered_body_fails()
    {
        using var p = new Pki();
        var body = Body();
        var sig = p.Sign(body);
        var tampered = (byte[])body.Clone(); tampered[^3] ^= 1;
        var r = await Make(p, new FakeSource(p.Pem())).VerifyAsync(GoodUrl, sig, null, tampered);
        Assert.False(r.Ok); Assert.Null(r.Request);
    }

    [Fact]
    public async Task Garbage_signature_fails()
    {
        using var p = new Pki();
        var v = Make(p, new FakeSource(p.Pem()));
        Assert.False((await v.VerifyAsync(GoodUrl, "not base64!!", null, Body())).Ok);
        Assert.False((await v.VerifyAsync(GoodUrl, Convert.ToBase64String(new byte[256]), null, Body())).Ok);
    }

    [Fact]
    public async Task Signature_from_a_different_key_fails()
    {
        using var p = new Pki(); using var other = new Pki();
        var body = Body();
        var r = await Make(p, new FakeSource(p.Pem())).VerifyAsync(GoodUrl, other.Sign(body), null, body);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Expired_leaf_fails()
    {
        using var p = new Pki(from: Now.AddDays(-60), to: Now.AddDays(-1));
        Assert.False((await Run(p)).Ok);
    }

    [Fact]
    public async Task Not_yet_valid_leaf_fails()
    {
        using var p = new Pki(from: Now.AddDays(1), to: Now.AddDays(60));
        Assert.False((await Run(p)).Ok);
    }

    [Fact]
    public async Task Wrong_san_fails()
    {
        using var p = new Pki(san: "evil.example.com");
        Assert.False((await Run(p)).Ok);
    }

    [Fact]
    public async Task Untrusted_root_fails()
    {
        using var served = new Pki(); using var trusted = new Pki();
        var body = Body();
        var v = new AlexaRequestVerifier(new FakeSource(served.Pem()), Skill, () => Now, trusted.Roots());
        Assert.False((await v.VerifyAsync(GoodUrl, served.Sign(body), null, body)).Ok);
    }

    [Theory]
    [InlineData("http://s3.amazonaws.com/echo.api/echo-api-cert.pem")]
    [InlineData("https://s3.amazonaws.com.evil.com/echo.api/echo-api-cert.pem")]
    [InlineData("https://evil.com/s3.amazonaws.com/echo.api/echo-api-cert.pem")]
    [InlineData("https://s3.amazonaws.com/Echo.api/echo-api-cert.pem")]
    [InlineData("https://s3.amazonaws.com/echo.api/../evil/cert.pem")]
    [InlineData("https://s3.amazonaws.com/echo.api/%2e%2e/evil/cert.pem")]
    [InlineData("https://s3.amazonaws.com/evil/echo.api/cert.pem")]
    [InlineData("https://s3.amazonaws.com:8443/echo.api/echo-api-cert.pem")]
    [InlineData("https://s3.amazonaws.com@evil.com/echo.api/echo-api-cert.pem")]
    [InlineData("https://evil.com@s3.amazonaws.com/echo.api/echo-api-cert.pem")]
    [InlineData("ftp://s3.amazonaws.com/echo.api/echo-api-cert.pem")]
    [InlineData("/echo.api/echo-api-cert.pem")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Bad_cert_urls_are_rejected_without_fetching(string? url)
    {
        using var p = new Pki();
        var src = new FakeSource(p.Pem());
        var body = Body();
        var r = await Make(p, src).VerifyAsync(url, p.Sign(body), null, body);
        Assert.False(r.Ok);
        Assert.Equal(0, src.Calls);
    }

    [Theory]
    [InlineData("https://S3.AmazonAWS.com/echo.api/echo-api-cert.pem")]
    [InlineData("https://s3.amazonaws.com:443/echo.api/echo-api-cert.pem")]
    [InlineData("https://s3.amazonaws.com/echo.api/../echo.api/echo-api-cert.pem")]
    public async Task Good_cert_url_variants_are_accepted(string url)
    {
        using var p = new Pki();
        var r = await Run(p, url: url);
        Assert.True(r.Ok, r.Reason);
    }

    [Theory]
    [InlineData(150)] [InlineData(149)] [InlineData(-149)] [InlineData(0)]
    public async Task Timestamp_inside_tolerance_ok(int ageSeconds)
    {
        using var p = new Pki();
        var r = await Run(p, Body(Now.AddSeconds(-ageSeconds)));
        Assert.True(r.Ok, r.Reason);
    }

    [Theory]
    [InlineData(151)] [InlineData(-151)] [InlineData(86400)]
    public async Task Timestamp_outside_tolerance_rejected(int ageSeconds)
    {
        using var p = new Pki();
        Assert.False((await Run(p, Body(Now.AddSeconds(-ageSeconds)))).Ok);
    }

    [Fact]
    public async Task Missing_or_unparseable_timestamp_rejected()
    {
        using var p = new Pki();
        var none = Encoding.UTF8.GetBytes($"{{\"context\":{{\"System\":{{\"application\":{{\"applicationId\":\"{Skill}\"}}}}}},\"request\":{{\"type\":\"X\"}}}}");
        var junk = Encoding.UTF8.GetBytes($"{{\"context\":{{\"System\":{{\"application\":{{\"applicationId\":\"{Skill}\"}}}}}},\"request\":{{\"timestamp\":\"yesterday\"}}}}");
        Assert.False((await Run(p, none)).Ok);
        Assert.False((await Run(p, junk)).Ok);
    }

    [Fact]
    public async Task Wrong_skill_id_rejected()
    {
        using var p = new Pki();
        Assert.False((await Run(p, Body(skill: "amzn1.ask.skill.someone-else"))).Ok);
    }

    [Fact]
    public async Task Comma_separated_skill_ids_accepted()
    {
        using var p = new Pki();
        var r = await Run(p, skills: " amzn1.ask.skill.a , " + Skill + ",amzn1.ask.skill.b");
        Assert.True(r.Ok, r.Reason);
    }

    [Theory]
    [InlineData("")] [InlineData("  ")] [InlineData(" , ,")]
    public async Task Empty_configured_skill_id_fails_closed(string skills)
    {
        using var p = new Pki();
        Assert.False((await Run(p, skills: skills)).Ok);
    }

    [Fact]
    public async Task Missing_headers_rejected()
    {
        using var p = new Pki();
        var v = Make(p, new FakeSource(p.Pem()));
        Assert.False((await v.VerifyAsync(GoodUrl, null, null, Body())).Ok);
        Assert.False((await v.VerifyAsync(GoodUrl, "", "  ", Body())).Ok);
        Assert.False((await v.VerifyAsync(null, p.Sign(Body()), null, Body())).Ok);
    }

    [Fact]
    public async Task Empty_and_oversized_body_rejected()
    {
        using var p = new Pki();
        var v = Make(p, new FakeSource(p.Pem()));
        Assert.False((await v.VerifyAsync(GoodUrl, "AAAA", null, [])).Ok);
        var big = new byte[1024 * 1024 + 1];
        Assert.False((await v.VerifyAsync(GoodUrl, p.Sign(big), null, big)).Ok);
    }

    [Fact]
    public async Task Non_json_body_with_valid_signature_rejected()
    {
        using var p = new Pki();
        var body = Encoding.UTF8.GetBytes("not json at all");
        Assert.False((await Run(p, body)).Ok);
    }

    [Fact]
    public async Task Legacy_sha1_used_only_when_sha256_header_absent()
    {
        using var p = new Pki();
        var body = Body();
        var v = Make(p, new FakeSource(p.Pem()));
        var legacy = p.Sign(body, HashAlgorithmName.SHA1);
        var ok = await v.VerifyAsync(GoodUrl, null, legacy, body);
        Assert.True(ok.Ok, ok.Reason);
        // a present but wrong Signature-256 must not fall back to a good legacy one
        var bad = await v.VerifyAsync(GoodUrl, Convert.ToBase64String(new byte[256]), legacy, body);
        Assert.False(bad.Ok);
        // a sha1 signature in the sha256 header is not accepted
        Assert.False((await v.VerifyAsync(GoodUrl, legacy, legacy, body)).Ok);
        // a good sha256 header wins even if the legacy one is garbage
        Assert.True((await v.VerifyAsync(GoodUrl, p.Sign(body), "garbage", body)).Ok);
    }

    [Fact]
    public async Task Cert_fetch_failure_rejected_and_retried_only_after_thirty_seconds()
    {
        using var p = new Pki();
        var src = new FakeSource(p.Pem()) { Throw = new HttpRequestException("boom") };
        var t = Now;
        var v = Make(p, src, clock: () => t);
        var body = Body(ts: t);
        Assert.False((await v.VerifyAsync(GoodUrl, p.Sign(body), null, body)).Ok);
        src.Throw = null;
        Assert.False((await v.VerifyAsync(GoodUrl, p.Sign(body), null, body)).Ok);   // inside the 30 seconds: not asked again
        Assert.Equal(1, src.Calls);
        t += TimeSpan.FromSeconds(31);
        var fresh = Body(ts: t);
        Assert.True((await v.VerifyAsync(GoodUrl, p.Sign(fresh), null, fresh)).Ok);
        Assert.Equal(2, src.Calls);
    }

    [Fact]
    public async Task Oversized_or_garbage_cert_rejected()
    {
        using var p = new Pki();
        Assert.False((await Run(p, src: new FakeSource(new byte[64 * 1024 + 1]))).Ok);
        Assert.False((await Run(p, src: new FakeSource(Encoding.ASCII.GetBytes("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----")))).Ok);
        Assert.False((await Run(p, src: new FakeSource([]))).Ok);
    }

    [Fact]
    public async Task Cache_hit_does_not_refetch()
    {
        using var p = new Pki();
        var src = new FakeSource(p.Pem());
        var v = Make(p, src);
        for (var i = 0; i < 3; i++) { var b = Body(); Assert.True((await v.VerifyAsync(GoodUrl, p.Sign(b), null, b)).Ok); }
        Assert.Equal(1, src.Calls);
    }

    [Fact]
    public async Task Cached_cert_is_refetched_after_leaf_expires()
    {
        using var p = new Pki(to: Now.AddDays(1));
        var src = new FakeSource(p.Pem());
        var t = Now;
        var v = Make(p, src, clock: () => t);
        var b = Body(); Assert.True((await v.VerifyAsync(GoodUrl, p.Sign(b), null, b)).Ok);
        t = Now.AddDays(2);
        var b2 = Body(t); Assert.False((await v.VerifyAsync(GoodUrl, p.Sign(b2), null, b2)).Ok);
        Assert.Equal(2, src.Calls);
    }

    [Fact]
    public async Task Cache_is_bounded_to_twenty_entries()
    {
        using var p = new Pki();
        var src = new FakeSource(p.Pem());
        var v = Make(p, src);
        var b = Body();
        for (var i = 0; i < 25; i++) Assert.True((await v.VerifyAsync($"https://s3.amazonaws.com/echo.api/c{i}.pem", p.Sign(b), null, b)).Ok);
        Assert.Equal(25, src.Calls);
        Assert.True(v.CacheCount <= 20, v.CacheCount.ToString());
    }

    [Fact]
    public async Task Reasons_never_leak_body_or_cert_material()
    {
        using var p = new Pki(san: "evil.example.com");
        var body = Body(skill: "SECRET-MARKER");
        var r = await Make(p, new FakeSource(p.Pem())).VerifyAsync(GoodUrl, p.Sign(body), null, body);
        Assert.False(r.Ok);
        Assert.DoesNotContain("SECRET-MARKER", r.Reason);
        Assert.DoesNotContain("evil.example.com", r.Reason);
        Assert.True(r.Reason.Length < 40);
    }

    // ---- rate limiter ----

    [Fact]
    public void Limiter_allows_up_to_limit_then_blocks()
    {
        var t = Now; var l = new AlexaRateLimiter(() => t);
        for (var i = 0; i < 30; i++) Assert.True(l.TryTake("a"));
        Assert.False(l.TryTake("a"));
        Assert.True(l.TryTake("b"));
    }

    [Fact]
    public void Limiter_window_resets_after_a_minute()
    {
        var t = Now; var l = new AlexaRateLimiter(() => t, perMinute: 2);
        Assert.True(l.TryTake("a")); Assert.True(l.TryTake("a")); Assert.False(l.TryTake("a"));
        t = t.AddSeconds(59); Assert.False(l.TryTake("a"));
        t = t.AddSeconds(2); Assert.True(l.TryTake("a"));
    }

    [Fact]
    public void Limiter_memory_is_bounded()
    {
        var t = Now; var l = new AlexaRateLimiter(() => t);
        for (var i = 0; i < 6000; i++) Assert.True(l.TryTake("k" + i));
        Assert.True(l.KeyCount <= 5000, l.KeyCount.ToString());
        t = t.AddMinutes(2);
        Assert.True(l.TryTake("fresh"));
        Assert.Equal(1, l.KeyCount);
    }

    [Fact]
    public void Limiter_null_or_empty_key_shares_one_bucket()
    {
        var l = new AlexaRateLimiter(() => Now, perMinute: 1);
        Assert.True(l.TryTake(null!)); Assert.False(l.TryTake(""));
    }
}

public class AlexaVerifierFloodTests
{
    sealed class CountingSource : IAlexaCertSource { public int Calls; public Task<byte[]> FetchAsync(Uri url, CancellationToken ct) { Calls++; throw new HttpRequestException("no"); } }

    [Fact] public async Task AFailedCertificateAddressIsNotFetchedAgainWithinThirtySeconds()
    {
        var src = new CountingSource(); var t = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var v = new AlexaRequestVerifier(src, "amzn1.ask.skill.x", () => t);
        var body = System.Text.Encoding.UTF8.GetBytes("{}"); var sig = Convert.ToBase64String(new byte[256]);
        for (var i = 0; i < 5; i++) Assert.Equal("cert-fetch-failed", (await v.VerifyAsync("https://s3.amazonaws.com/echo.api/x.pem", sig, null, body)).Reason);
        Assert.Equal(1, src.Calls);
        t += TimeSpan.FromSeconds(31); await v.VerifyAsync("https://s3.amazonaws.com/echo.api/x.pem", sig, null, body);
        Assert.Equal(2, src.Calls);
    }
}

public class AlexaCertUrlQueryTests
{
    [Fact] public async Task ACertificateAddressWithAQueryOrFragmentIsRefused()
    {
        var v = new AlexaRequestVerifier(new NeverSource(), "amzn1.ask.skill.x");
        var body = System.Text.Encoding.UTF8.GetBytes("{}"); var sig = Convert.ToBase64String(new byte[256]);
        foreach (var url in new[] { "https://s3.amazonaws.com/echo.api/echo-api-cert.pem?x=1", "https://s3.amazonaws.com/echo.api/echo-api-cert.pem#f" })
            Assert.Equal("bad-cert-url", (await v.VerifyAsync(url, sig, null, body)).Reason);
    }
    sealed class NeverSource : IAlexaCertSource { public Task<byte[]> FetchAsync(Uri url, CancellationToken ct) => throw new InvalidOperationException("must not be fetched"); }
}
