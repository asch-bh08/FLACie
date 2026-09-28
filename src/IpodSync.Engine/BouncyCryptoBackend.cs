using System.Security.Cryptography;
using IpodSync.Core.Crypto;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace IpodSync.Engine;

/// <summary>Managed crypto for the NativeAOT Android library (the BCL's would need OpenSSL). <see cref="SelfTest"/>
/// checks it against published test vectors before the engine will write anything.</summary>
public sealed class BouncyCryptoBackend : ICryptoBackend
{
    public IHasher Create(HashAlgorithmName alg) => new Hasher(alg == HashAlgorithmName.SHA1 ? new Sha1Digest()
        : alg == HashAlgorithmName.SHA256 ? new Sha256Digest() : throw new NotSupportedException(alg.Name));

    public byte[] HmacSha1(byte[] key, ReadOnlySpan<byte> data)
    {
        var mac = new HMac(new Sha1Digest());
        mac.Init(new KeyParameter(key));
        mac.BlockUpdate(data);
        var o = new byte[mac.GetMacSize()];
        mac.DoFinal(o, 0);
        return o;
    }

    public byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plain)
    {
        if (plain.Length % 16 != 0) throw new ArgumentException("no padding: length must be a multiple of 16");
        var aes = new AesEngine();
        aes.Init(true, new KeyParameter(key));
        var o = new byte[plain.Length];
        var prev = (byte[])iv.Clone();
        var block = new byte[16];
        for (int off = 0; off < plain.Length; off += 16)
        {
            for (int i = 0; i < 16; i++) block[i] = (byte)(plain[off + i] ^ prev[i]);
            aes.ProcessBlock(block, 0, o, off);
            Array.Copy(o, off, prev, 0, 16);
        }
        return o;
    }

    public byte[] AesEcbDecrypt(byte[] key, byte[] cipher)
    {
        if (cipher.Length % 16 != 0) throw new ArgumentException("no padding: length must be a multiple of 16");
        var aes = new AesEngine();
        aes.Init(false, new KeyParameter(key));
        var o = new byte[cipher.Length];
        for (int off = 0; off < cipher.Length; off += 16) aes.ProcessBlock(cipher, off, o, off);
        return o;
    }

    sealed class Hasher(IDigest d) : IHasher
    {
        public void AppendData(ReadOnlySpan<byte> data) => d.BlockUpdate(data);
        public byte[] GetHashAndReset() { var o = new byte[d.GetDigestSize()]; d.DoFinal(o, 0); return o; }
        public void Dispose() { }
    }

    /// <summary>Known-answer tests: FIPS 180 ("abc"), RFC 2202 HMAC-SHA1 case 2, FIPS 197 / SP 800-38A AES-128.
    /// Returns the failures (empty = all good).</summary>
    public static List<string> SelfTest(ICryptoBackend c)
    {
        var fails = new List<string>();
        void Check(string name, byte[] got, string hex) { if (!Convert.ToHexString(got).Equals(hex, StringComparison.OrdinalIgnoreCase)) fails.Add(name); }
        byte[] abc = "abc"u8.ToArray();
        using (var h = c.Create(HashAlgorithmName.SHA1)) { h.AppendData(abc); Check("sha1", h.GetHashAndReset(), "A9993E364706816ABA3E25717850C26C9CD0D89D"); }
        using (var h = c.Create(HashAlgorithmName.SHA256)) { h.AppendData(abc); Check("sha256", h.GetHashAndReset(), "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"); }
        Check("hmac-sha1", c.HmacSha1("Jefe"u8.ToArray(), "what do ya want for nothing?"u8), "EFFCDF6AE5EB2FA2D27416D5F184DF9C259A7C79");
        byte[] k = Convert.FromHexString("2B7E151628AED2A6ABF7158809CF4F3C");
        byte[] iv = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        byte[] p = Convert.FromHexString("6BC1BEE22E409F96E93D7E117393172AAE2D8A571E03AC9C9EB76FAC45AF8E51");
        Check("aes-cbc", c.AesCbcEncrypt(k, iv, p), "7649ABAC8119B246CEE98E9B12E9197D5086CB9B507219EE95DB113A917678B2");
        Check("aes-ecb-dec", c.AesEcbDecrypt(k, Convert.FromHexString("3AD77BB40D7A3660A89ECAF32466EF97")), "6BC1BEE22E409F96E93D7E117393172A");
        return fails;
    }
}
