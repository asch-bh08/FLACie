using System.Security.Cryptography;
using System.Text;

namespace FLACie.Server.Alexa;

public enum SignedUrlResult { OK, Expired, BadSignature }

/// <summary>
/// Short-lived signed public stream URLs for Alexa (the speaker cannot send headers, so the URL itself carries the proof).
/// Path: /alexa/stream/{kind}/{itemId}.{format}?u={8 hex of the user key hash}&amp;e={expiry unix seconds}&amp;s={HMAC-SHA256 base64url}.
/// No media-server token is ever in it. The HMAC covers kind, the user hash (derived from the user key), item, format and expiry.
/// </summary>
public sealed class AlexaUrlSigner(byte[] secret, Func<DateTime> clock)
{
    /// <summary>Short stable hash of an opaque user key; the server maps the u= value back to the user by hashing its known users.</summary>
    public static string UserHash(string userKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userKey)), 0, 4).ToLowerInvariant();

    long Now() => new DateTimeOffset(clock().ToUniversalTime()).ToUnixTimeSeconds();

    string Mac(string kind, string u, string itemId, string format, long e) =>
        Convert.ToBase64String(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"{kind}|{u}|{itemId}|{format}|{e}"))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string Sign(string kind, string userKey, string itemId, string format, TimeSpan ttl)
    {
        var u = UserHash(userKey); var e = Now() + (long)ttl.TotalSeconds;
        return $"/alexa/stream/{kind}/{itemId}.{format}?u={u}&e={e}&s={Mac(kind, u, itemId, format, e)}";
    }

    /// <summary>Signature is checked first (constant time), so a tampered or unsigned URL never learns whether it was also expired.</summary>
    public SignedUrlResult Verify(string kind, string itemId, string format, string u, long e, string s)
    {
        if (string.IsNullOrEmpty(s) || u is null) return SignedUrlResult.BadSignature;
        var want = Encoding.ASCII.GetBytes(Mac(kind, u, itemId, format, e));
        var got = Encoding.UTF8.GetBytes(s);
        if (got.Length != want.Length || !CryptographicOperations.FixedTimeEquals(got, want)) return SignedUrlResult.BadSignature;
        return Now() > e ? SignedUrlResult.Expired : SignedUrlResult.OK;
    }

    /// <summary>32 random bytes kept in a file in the data dir (written to a temp file and moved into place, so a crash never leaves half a key).</summary>
    public static byte[] LoadOrCreateSecret(string path)
    {
        try { if (File.Exists(path)) { var b = File.ReadAllBytes(path); if (b.Length == 32) return b; } } catch { }
        var key = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllBytes(tmp, key);
        File.Move(tmp, path, overwrite: true);
        return key;
    }
}
