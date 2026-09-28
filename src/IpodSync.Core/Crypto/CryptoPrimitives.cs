using System.Security.Cryptography;

namespace IpodSync.Core.Crypto;

/// <summary>An incremental hash (the subset of <see cref="IncrementalHash"/> the engine uses).</summary>
public interface IHasher : IDisposable
{
    void AppendData(ReadOnlySpan<byte> data);
    byte[] GetHashAndReset();
}

/// <summary>The cryptographic primitives the engine needs: SHA-1 / SHA-256 (file identity, hashAB/hash72 inputs,
/// cbk block hashes), HMAC-SHA1 (hash58) and single-shot AES-128 (hash72).</summary>
public interface ICryptoBackend
{
    IHasher Create(HashAlgorithmName alg);
    byte[] HmacSha1(byte[] key, ReadOnlySpan<byte> data);
    byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plain);
    byte[] AesEcbDecrypt(byte[] key, byte[] cipher);
}

/// <summary>
/// Every hash / HMAC / AES call in the engine goes through here. The default backend is .NET's own
/// System.Security.Cryptography, so the Windows app, IpodSync.Web, the CLI and the MAUI Android app behave exactly as
/// before. The one exception is the engine built as a NativeAOT library for ipodplayer on Android
/// (IpodSync.Engine): there the BCL's crypto would need an OpenSSL that Android doesn't provide to native code, so it
/// installs a managed BouncyCastle backend -- checked byte-for-byte against this default by the engine's self-test.
/// </summary>
public static class CryptoPrimitives
{
    public static ICryptoBackend Backend { get; set; } = new BclCryptoBackend();

    public static byte[] Sha1(ReadOnlySpan<byte> data) => Hash(HashAlgorithmName.SHA1, data);
    public static int Sha1(ReadOnlySpan<byte> data, Span<byte> destination) { var h = Sha1(data); h.CopyTo(destination); return h.Length; }
    public static byte[] Sha1(Stream data) => Hash(HashAlgorithmName.SHA1, data);
    public static byte[] Sha256(ReadOnlySpan<byte> data) => Hash(HashAlgorithmName.SHA256, data);
    public static byte[] Sha256(Stream data) => Hash(HashAlgorithmName.SHA256, data);
    public static IHasher CreateSha256() => Backend.Create(HashAlgorithmName.SHA256);
    public static byte[] HmacSha1(byte[] key, ReadOnlySpan<byte> data) => Backend.HmacSha1(key, data);
    public static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plain) => Backend.AesCbcEncrypt(key, iv, plain);
    public static byte[] AesEcbDecrypt(byte[] key, byte[] cipher) => Backend.AesEcbDecrypt(key, cipher);

    static byte[] Hash(HashAlgorithmName alg, ReadOnlySpan<byte> data)
    {
        using var h = Backend.Create(alg);
        h.AppendData(data);
        return h.GetHashAndReset();
    }

    static byte[] Hash(HashAlgorithmName alg, Stream data)
    {
        using var h = Backend.Create(alg);
        var buf = new byte[81920];
        int n;
        while ((n = data.Read(buf, 0, buf.Length)) > 0) h.AppendData(buf.AsSpan(0, n));
        return h.GetHashAndReset();
    }
}

/// <summary>The default: .NET's System.Security.Cryptography, exactly the calls the engine made before.</summary>
public sealed class BclCryptoBackend : ICryptoBackend
{
    public IHasher Create(HashAlgorithmName alg) => new Bcl(IncrementalHash.CreateHash(alg));
    public byte[] HmacSha1(byte[] key, ReadOnlySpan<byte> data) => HMACSHA1.HashData(key, data);
    public byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(plain, iv, PaddingMode.None);
    }
    public byte[] AesEcbDecrypt(byte[] key, byte[] cipher)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptEcb(cipher, PaddingMode.None);
    }

    sealed class Bcl(IncrementalHash h) : IHasher
    {
        public void AppendData(ReadOnlySpan<byte> data) => h.AppendData(data);
        public byte[] GetHashAndReset() => h.GetHashAndReset();
        public void Dispose() => h.Dispose();
    }
}
