using System.Security.Cryptography;

namespace IpodSync.Core.Signing;

/// <summary>
/// The "hash58" iTunesDB/iTunesCDB header signature (mhbd +0x58, 20 bytes;
/// hashing_scheme +0x30 == 1). HMAC-SHA1 over the database with a 64-byte key
/// derived from the device's FirewireGuid. The derivation (LCM of each FWID byte
/// pair, looked up through the AES S-box and inverse S-box, SHA-1 with an 18-byte
/// constant) is as published by wtbw and documented in libgpod's BSD-licensed
/// itdb_hash58.c; this is an independent C# implementation.
///
/// The FirewireGuid of a nano 5G is its USB serial number (e.g. Windows device
/// instance id USB\VID_05AC&amp;PID_1265\&lt;16 hex digits&gt;). Callers must prove the
/// implementation reproduces an existing iTunes-written hash58 for the device
/// before using it to sign anything (see <see cref="Verify"/>).
/// </summary>
public static class Hash58
{
    private static readonly byte[] Fixed =
        [0x67, 0x23, 0xFE, 0x30, 0x45, 0x33, 0xF8, 0x90, 0x99, 0x21, 0x07, 0xC1, 0xD0, 0x12, 0xB2, 0xA1, 0x07, 0x81];

    public static byte[] ParseFirewireGuid(string hex)
    {
        hex = hex.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length != 16) throw new ArgumentException("FirewireGuid must be 16 hex digits");
        return Convert.FromHexString(hex);
    }

    public static byte[] Compute(byte[] firewireId, ReadOnlySpan<byte> zeroedDatabase)
    {
        byte[] y = new byte[16];
        for (int i = 0; i < 4; i++)
        {
            int a = firewireId[i * 2], b = firewireId[i * 2 + 1];
            int l = Lcm(a, b);
            byte hi = (byte)((l >> 8) & 0xFF), lo = (byte)(l & 0xFF);
            y[i * 4] = SBox[hi];
            y[i * 4 + 1] = InvSBox[hi];
            y[i * 4 + 2] = SBox[lo];
            y[i * 4 + 3] = InvSBox[lo];
        }
        byte[] key = new byte[64];
        SHA1.HashData([.. Fixed, .. y]).CopyTo(key, 0);
        return HMACSHA1.HashData(key, zeroedDatabase);
    }

    /// <summary>The database bytes hash58 covers: the file with db id (0x18, 8),
    /// unk 0x32 (20) and hash58 (0x58, 20) zeroed. hash72 is left as stored, which
    /// means a writer must sign hash72 first and hash58 last.</summary>
    public static byte[] ZeroedForHash(byte[] file)
    {
        byte[] copy = (byte[])file.Clone();
        Array.Clear(copy, 0x18, 8);
        Array.Clear(copy, 0x32, 20);
        Array.Clear(copy, 0x58, 20);
        return copy;
    }

    public static bool Verify(byte[] firewireId, byte[] file) =>
        Compute(firewireId, ZeroedForHash(file)).AsSpan().SequenceEqual(file.AsSpan(0x58, 20));

    /// <summary>Spot-checks the generated S-boxes against FIPS-197 values.</summary>
    public static bool SelfTest() =>
        SBox[0x00] == 0x63 && SBox[0x01] == 0x7C && SBox[0x53] == 0xED && SBox[0xFF] == 0x16 &&
        InvSBox[0x00] == 0x52 && InvSBox[0x01] == 0x09 && InvSBox[0xFF] == 0x7D;

    private static int Lcm(int a, int b)
    {
        if (a == 0 || b == 0) return 1;
        int x = a, y = b;
        while (y != 0) (x, y) = (y, x % y);
        return a * b / x;
    }

    // AES forward and inverse S-boxes (FIPS-197).
    private static readonly byte[] SBox = BuildSBox(out InvSBoxStore);
    private static readonly byte[] InvSBoxStore;
    private static byte[] InvSBox => InvSBoxStore;

    private static byte[] BuildSBox(out byte[] inv)
    {
        byte[] s = new byte[256];
        inv = new byte[256];
        byte p = 1, q = 1;
        do
        {
            p = (byte)(p ^ (p << 1) ^ ((p & 0x80) != 0 ? 0x1B : 0));
            q ^= (byte)(q << 1); q ^= (byte)(q << 2); q ^= (byte)(q << 4);
            if ((q & 0x80) != 0) q ^= 0x09;
            byte x = (byte)(q ^ Rotl(q, 1) ^ Rotl(q, 2) ^ Rotl(q, 3) ^ Rotl(q, 4) ^ 0x63);
            s[p] = x;
        } while (p != 1);
        s[0] = 0x63;
        for (int i = 0; i < 256; i++) inv[s[i]] = (byte)i;
        return s;
    }

    private static byte Rotl(byte v, int n) => (byte)((v << n) | (v >> (8 - n)));
}
