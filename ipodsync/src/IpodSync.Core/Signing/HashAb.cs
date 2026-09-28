using System.Diagnostics;
using System.Security.Cryptography;

namespace IpodSync.Core.Signing;

/// <summary>
/// hashAB — the signature the iPod nano 6G/7G (and shuffle 4G, iPhone 4-era devices) expect.
///
/// It is NOT implemented here, and it can't be: the algorithm is white-box AES whose key is
/// inseparable from a large embedded table. libgpod doesn't implement it either — it dlopens a
/// closed binary (<c>libhashab.so</c>) that someone extracted from Apple's code, and other
/// projects ship the same thing recompiled to WebAssembly. Shipping that blob isn't something
/// this project will do.
///
/// What is here instead: the parts that ARE known (which bytes are hashed, where the signature
/// lives), and a hook for an external signer the user supplies themselves. The hook is only ever
/// trusted after it reproduces, byte for byte, the signature iTunes already wrote on that very
/// device — the same proof rule the hash58 and hash72 paths use. See COMPATIBILITY.md.
/// </summary>
public static class HashAb
{
    /// <summary>Where the 57-byte signature sits in the mhbd header.</summary>
    public const int Offset = 0xAB;
    public const int Length = 57;

    public static bool Present(byte[] databaseFile) =>
        databaseFile.Length >= Offset + Length && databaseFile.Skip(Offset).Take(Length).Any(b => b != 0);

    /// <summary>The SHA-1 an external signer is given: the whole database with db_id, hash58,
    /// hash72 and hashAB zeroed and the scheme field set to 3 (as libgpod's itdb_hashAB.c does).</summary>
    public static byte[] DatabaseSha1(byte[] databaseFile)
    {
        byte[] copy = (byte[])databaseFile.Clone();
        Array.Clear(copy, 0x18, 8);
        Array.Clear(copy, 0x58, 20);
        Array.Clear(copy, 0x72, 46);
        if (copy.Length >= Offset + Length) Array.Clear(copy, Offset, Length);
        BitConverter.TryWriteBytes(copy.AsSpan(0x30, 2), (ushort)3);
        return IpodSync.Core.Crypto.CryptoPrimitives.Sha1(copy);
    }
}

/// <summary>
/// Runs a user-supplied hashAB signer. The contract is deliberately tiny, so a few lines of
/// Python around any existing implementation satisfies it:
///
///   &lt;program&gt; &lt;sha1-hex (40 chars)&gt; &lt;firewire-guid-hex&gt;   →  stdout: 114 hex chars (57 bytes)
///
/// Point <c>IPODSYNC_HASHAB_SIGNER</c> at the program. It is used only if it reproduces the
/// signature already on the device; otherwise writing is refused.
/// </summary>
public sealed class ExternalHashAbSigner
{
    private readonly string _program;
    private readonly string _firewireHex;

    private ExternalHashAbSigner(string program, string firewireHex)
    {
        _program = program;
        _firewireHex = firewireHex;
    }

    public static string? ConfiguredProgram => Environment.GetEnvironmentVariable("IPODSYNC_HASHAB_SIGNER") is { Length: > 0 } p ? p : null;

    /// <summary>Returns a signer only when the configured program reproduces
    /// <paramref name="deviceDatabase"/>'s own hashAB. Adds a line to <paramref name="problems"/> otherwise.</summary>
    public static ExternalHashAbSigner? Resolve(byte[] deviceDatabase, IEnumerable<string> firewireCandidates, List<string> problems, List<string> evidence)
    {
        if (ConfiguredProgram is not { } program)
        {
            problems.Add("hashAB signing needs an external signer: set IPODSYNC_HASHAB_SIGNER (see COMPATIBILITY.md)");
            return null;
        }
        if (!HashAb.Present(deviceDatabase))
        {
            problems.Add("this database has no hashAB signature to check an external signer against, so the signer can't be trusted");
            return null;
        }
        byte[] expected = deviceDatabase.Skip(HashAb.Offset).Take(HashAb.Length).ToArray();
        string sha1 = Convert.ToHexString(HashAb.DatabaseSha1(deviceDatabase));

        foreach (var candidate in firewireCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byte[]? produced;
            try { produced = new ExternalHashAbSigner(program, candidate).Run(sha1, candidate); }
            catch (Exception ex) { problems.Add($"hashAB signer failed: {ex.Message}"); return null; }
            if (produced is not null && produced.AsSpan().SequenceEqual(expected))
            {
                evidence.Add($"external hashAB signer reproduced this device's own signature ({Path.GetFileName(program)})");
                return new ExternalHashAbSigner(program, candidate);
            }
        }
        problems.Add("the external hashAB signer did not reproduce this device's existing signature, so it is not trusted");
        return null;
    }

    /// <summary>Signs a database copy in place and returns it.</summary>
    public byte[] Sign(byte[] file)
    {
        byte[] copy = (byte[])file.Clone();
        BitConverter.TryWriteBytes(copy.AsSpan(0x30, 2), (ushort)3);
        byte[] signature = Run(Convert.ToHexString(HashAb.DatabaseSha1(copy)), _firewireHex)
            ?? throw new InvalidOperationException("the hashAB signer returned nothing");
        signature.CopyTo(copy, HashAb.Offset);
        return copy;
    }

    public bool Verify(byte[] file)
    {
        try
        {
            byte[]? produced = Run(Convert.ToHexString(HashAb.DatabaseSha1(file)), _firewireHex);
            return produced is not null && produced.AsSpan().SequenceEqual(file.AsSpan(HashAb.Offset, HashAb.Length));
        }
        catch { return false; }
    }

    private byte[]? Run(string sha1Hex, string firewireHex)
    {
        var psi = new ProcessStartInfo(_program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add(sha1Hex);
        psi.ArgumentList.Add(firewireHex);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"could not start {_program}");
        string output = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(TimeSpan.FromSeconds(30))) { try { p.Kill(true); } catch { } throw new TimeoutException("the hashAB signer timed out"); }
        if (p.ExitCode != 0) throw new InvalidOperationException($"the hashAB signer exited with {p.ExitCode}: {error.Trim()}");

        string hex = new([.. output.Where(Uri.IsHexDigit)]);
        if (hex.Length != HashAb.Length * 2) throw new InvalidOperationException($"the hashAB signer returned {hex.Length / 2} bytes, expected {HashAb.Length}");
        return Convert.FromHexString(hex);
    }
}
