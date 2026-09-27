using System.Security.Cryptography;

namespace IpodSync.Core.Signing;

/// <summary>
/// The "hash72" signature used by the iPod nano 5G on <c>Locations.itdb.cbk</c>
/// (and present, alongside hash58, in the header of iTunes-written iTunesCDBs).
///
/// A 46-byte signature is <c>01 00</c> + 12 random bytes + AES-128-CBC(key, iv,
/// sha1 ‖ random) over 32 bytes, no padding. The AES key is a fixed constant;
/// the (iv, random) pair is per-device and is recovered from any signature iTunes
/// already wrote for that device. The algorithm and constant are as published by
/// Chris Lee (hash_generate/hash_extract, WTFPL) and used by libgpod; this is an
/// independent C# implementation of that description, not a port of libgpod code.
///
/// Nothing here is trusted by construction: <see cref="TryExtract"/> only
/// succeeds when re-encrypting with the recovered IV reproduces the *whole*
/// signature, and callers additionally prove regeneration reproduces an existing
/// iTunes-written signature byte-for-byte before signing anything new.
/// </summary>
public static class Hash72
{
    private static readonly byte[] AesKey =
        [0x61, 0x8c, 0xa1, 0x0d, 0xc7, 0xf5, 0x7f, 0xd3, 0xb4, 0x72, 0x3e, 0x08, 0x15, 0x74, 0x63, 0xd7];

    public sealed record DeviceKey(byte[] Iv, byte[] Random)
    {
        public bool SameAs(DeviceKey other) => Iv.AsSpan().SequenceEqual(other.Iv) && Random.AsSpan().SequenceEqual(other.Random);
    }

    public static byte[] Generate(ReadOnlySpan<byte> sha1, DeviceKey key)
    {
        if (sha1.Length != 20) throw new ArgumentException("sha1 must be 20 bytes");
        byte[] plain = new byte[32];
        sha1.CopyTo(plain);
        key.Random.CopyTo(plain, 20);

        using var aes = Aes.Create();
        aes.Key = AesKey;
        byte[] cipher = aes.EncryptCbc(plain, key.Iv, PaddingMode.None);

        byte[] sig = new byte[46];
        sig[0] = 0x01; sig[1] = 0x00;
        key.Random.CopyTo(sig, 2);
        cipher.CopyTo(sig, 14);
        return sig;
    }

    /// <summary>Recover the device's (iv, random) from a signature and the SHA-1 it
    /// signs. Returns null unless regenerating with the recovered pair reproduces the
    /// signature exactly (which validates the key, the SHA-1 recipe and the IV).</summary>
    public static DeviceKey? TryExtract(ReadOnlySpan<byte> signature, ReadOnlySpan<byte> sha1)
    {
        if (signature.Length != 46 || sha1.Length != 20 || signature[0] != 0x01 || signature[1] != 0x00) return null;
        byte[] random = signature.Slice(2, 12).ToArray();
        byte[] plain = new byte[32];
        sha1.CopyTo(plain);
        random.CopyTo(plain, 20);

        // CBC: C1 = E(P1 xor IV)  =>  IV = D(C1) xor P1.
        using var aes = Aes.Create();
        aes.Key = AesKey;
        byte[] d1 = aes.DecryptEcb(signature.Slice(14, 16).ToArray(), PaddingMode.None);
        byte[] iv = new byte[16];
        for (int i = 0; i < 16; i++) iv[i] = (byte)(d1[i] ^ plain[i]);

        var key = new DeviceKey(iv, random);
        return Generate(sha1, key).AsSpan().SequenceEqual(signature) ? key : null;
    }

    // ------------------------------------------------------------------ Locations.itdb.cbk

    public const int CbkHeaderSize = 46;

    /// <summary>cbk = signature(46) ‖ SHA1(block SHA-1s)(20) ‖ SHA-1 of each full
    /// 1024-byte block of Locations.itdb (a trailing partial block is not hashed).</summary>
    public static (byte[] FinalSha1, byte[] BlockSha1s) CbkBody(byte[] locations)
    {
        int blocks = locations.Length / 1024;
        byte[] sha1s = new byte[blocks * 20];
        for (int b = 0; b < blocks; b++)
            SHA1.HashData(locations.AsSpan(b * 1024, 1024), sha1s.AsSpan(b * 20, 20));
        return (SHA1.HashData(sha1s), sha1s);
    }

    public static byte[] BuildCbk(byte[] locations, DeviceKey key)
    {
        var (final, sha1s) = CbkBody(locations);
        return [.. Generate(final, key), .. final, .. sha1s];
    }

    /// <summary>Checks an existing cbk against Locations.itdb and recovers the device
    /// key from its signature. Problems are returned, never thrown.</summary>
    public static (DeviceKey? Key, List<string> Problems) VerifyCbk(byte[] locations, byte[] cbk)
    {
        var problems = new List<string>();
        var (final, sha1s) = CbkBody(locations);
        if (cbk.Length != CbkHeaderSize + 20 + sha1s.Length)
            problems.Add($"cbk length {cbk.Length} != expected {CbkHeaderSize + 20 + sha1s.Length} for a {locations.Length}-byte Locations.itdb");
        else
        {
            if (!cbk.AsSpan(CbkHeaderSize, 20).SequenceEqual(final)) problems.Add("cbk final SHA-1 does not match Locations.itdb");
            if (!cbk.AsSpan(CbkHeaderSize + 20).SequenceEqual(sha1s)) problems.Add("cbk block SHA-1s do not match Locations.itdb");
        }
        var key = cbk.Length >= CbkHeaderSize ? TryExtract(cbk.AsSpan(0, CbkHeaderSize), final) : null;
        if (key is null) problems.Add("cbk signature does not validate against its SHA-1 (key/recipe mismatch)");
        return (key, problems);
    }

    // ------------------------------------------------------------------ iTunesCDB / iTunesDB header

    /// <summary>SHA-1 of a database file as signed by hash72: the whole file with the
    /// mhbd header's db id (0x18, 8 bytes), hash58 (0x58, 20) and hash72 (0x72, 46)
    /// zeroed.</summary>
    public static byte[] DatabaseSha1(byte[] file)
    {
        byte[] copy = (byte[])file.Clone();
        Array.Clear(copy, 0x18, 8);
        Array.Clear(copy, 0x58, 20);
        Array.Clear(copy, 0x72, 46);
        return SHA1.HashData(copy);
    }

    public static DeviceKey? ExtractFromDatabase(byte[] file) =>
        file.Length < 0xA0 ? null : TryExtract(file.AsSpan(0x72, 46), DatabaseSha1(file));
}
