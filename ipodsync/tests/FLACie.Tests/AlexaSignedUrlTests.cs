using System.Web;
using FLACie.Server.Alexa;
using Xunit;

namespace FLACie.Tests;

/// <summary>Short-lived signed stream URLs for Alexa: roundtrip, expiry, tampering, secret persistence.</summary>
public class AlexaSignedUrlTests
{
    static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    static readonly byte[] Secret = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    const string Item = "0123456789abcdef0123456789abcdef";

    sealed class Clock { public DateTime Now = T0; }

    static (string path, string u, long e, string s) Parse(string url)
    {
        var q = HttpUtility.ParseQueryString(url[(url.IndexOf('?') + 1)..]);
        return (url[..url.IndexOf('?')], q["u"]!, long.Parse(q["e"]!), q["s"]!);
    }

    static (AlexaUrlSigner signer, Clock clock) Make(byte[]? secret = null) { var c = new Clock(); return (new AlexaUrlSigner(secret ?? Secret, () => c.Now), c); }

    [Fact]
    public void Roundtrip_verifies_ok()
    {
        var (sg, _) = Make();
        var url = sg.Sign("track", "user-1", Item, "flac", TimeSpan.FromMinutes(5));
        var (path, u, e, s) = Parse(url);
        Assert.Equal($"/alexa/stream/track/{Item}.flac", path);
        Assert.Equal(AlexaUrlSigner.UserHash("user-1"), u);
        Assert.Equal(SignedUrlResult.OK, sg.Verify("track", Item, "flac", u, e, s));
    }

    [Fact]
    public void Expiry_boundary_is_inclusive_then_expired()
    {
        var (sg, c) = Make();
        var (_, u, e, s) = Parse(sg.Sign("track", "user-1", Item, "mp3", TimeSpan.FromSeconds(60)));
        c.Now = DateTimeOffset.FromUnixTimeSeconds(e).UtcDateTime;
        Assert.Equal(SignedUrlResult.OK, sg.Verify("track", Item, "mp3", u, e, s));
        c.Now = c.Now.AddSeconds(1);
        Assert.Equal(SignedUrlResult.Expired, sg.Verify("track", Item, "mp3", u, e, s));
    }

    [Fact]
    public void Tampering_with_any_field_is_bad_signature()
    {
        var (sg, _) = Make();
        var (_, u, e, s) = Parse(sg.Sign("track", "user-1", Item, "flac", TimeSpan.FromMinutes(5)));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("album", Item, "flac", u, e, s));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("track", Item[..^1] + "0", "flac", u, e, s));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("track", Item, "mp3", u, e, s));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("track", Item, "flac", AlexaUrlSigner.UserHash("user-2"), e, s));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("track", Item, "flac", u, e + 1000, s));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("track", Item, "flac", u, e, s[..^1] + (s[^1] == 'A' ? 'B' : 'A')));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("track", Item, "flac", u, e, ""));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("track", Item, "flac", u, e, null!));
    }

    [Fact]
    public void Field_separator_cannot_be_smuggled()
    {
        var (sg, _) = Make();
        var (_, u, e, s) = Parse(sg.Sign("tr", "user-1", "ack|x", "flac", TimeSpan.FromMinutes(5)));
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("tr|ack", "x", "flac", u, e, s));
    }

    [Fact]
    public void Wrong_secret_is_bad_signature()
    {
        var (a, _) = Make();
        var (b, _) = Make(new byte[32]);
        var (_, u, e, s) = Parse(a.Sign("track", "user-1", Item, "flac", TimeSpan.FromMinutes(5)));
        Assert.Equal(SignedUrlResult.BadSignature, b.Verify("track", Item, "flac", u, e, s));
    }

    [Fact]
    public void Expired_and_tampered_reports_bad_signature_not_expired()
    {
        var (sg, c) = Make();
        var (_, u, e, s) = Parse(sg.Sign("track", "user-1", Item, "flac", TimeSpan.FromSeconds(1)));
        c.Now = T0.AddHours(1);
        Assert.Equal(SignedUrlResult.BadSignature, sg.Verify("track", Item, "flac", u, e, s + "x"));
    }

    [Fact]
    public void Url_is_short_clean_and_has_no_user_key()
    {
        var (sg, _) = Make();
        var url = sg.Sign("track", "a-very-long-opaque-user-id-0123456789", Item, "flac", TimeSpan.FromHours(1));
        Assert.True(url.Length < 300, url.Length.ToString());
        var (_, u, _, s) = Parse(url);
        Assert.DoesNotContain("opaque", url);
        Assert.Equal(8, u.Length);
        Assert.DoesNotContain('+', s); Assert.DoesNotContain('/', s); Assert.DoesNotContain('=', s);
    }

    [Fact]
    public void Signature_alphabet_is_url_safe_across_many_signs()
    {
        var (sg, _) = Make();
        for (var i = 0; i < 200; i++)
        {
            var (_, _, _, s) = Parse(sg.Sign("track", "u" + i, Item + i, "flac", TimeSpan.FromMinutes(1)));
            Assert.Matches("^[A-Za-z0-9_-]+$", s);
        }
    }

    [Fact]
    public void UserHash_is_stable_and_distinguishes_users()
    {
        Assert.Equal(AlexaUrlSigner.UserHash("a"), AlexaUrlSigner.UserHash("a"));
        Assert.NotEqual(AlexaUrlSigner.UserHash("a"), AlexaUrlSigner.UserHash("b"));
    }

    [Fact]
    public void Secret_is_created_once_and_reused()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "alexa.key");
        var a = AlexaUrlSigner.LoadOrCreateSecret(path);
        var b = AlexaUrlSigner.LoadOrCreateSecret(path);
        Assert.Equal(32, a.Length);
        Assert.Equal(a, b);
        Assert.NotEqual(new byte[32], a);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        var url = new AlexaUrlSigner(a, () => T0).Sign("track", "u", Item, "flac", TimeSpan.FromMinutes(1));
        var (_, u, e, s) = Parse(url);
        Assert.Equal(SignedUrlResult.OK, new AlexaUrlSigner(b, () => T0).Verify("track", Item, "flac", u, e, s));
    }

    [Fact]
    public void Secret_of_wrong_size_on_disk_is_replaced()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "alexa.key");
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Equal(32, AlexaUrlSigner.LoadOrCreateSecret(path).Length);
    }
}
