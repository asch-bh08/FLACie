namespace IpodSync.Core.Signing;

/// <summary>
/// Signs databases for one specific device, using only key material that has been
/// proven against signatures iTunes itself wrote for that device:
///
/// - hash72 (iv, random) pair: recovered from the device's own
///   <c>Locations.itdb.cbk</c> (or its CDB header, or a reference database with the
///   same library id), accepted only if regenerating reproduces that signature.
/// - FirewireGuid for hash58: accepted only if it reproduces the hash58 of the
///   device's current CDB or of a reference database with the same library id
///   (e.g. the original iTunes-written CDB in a backup).
///
/// No key material is stored on the device or in the repository.
/// </summary>
public sealed class DeviceSigning
{
    public Hash72.DeviceKey Key72 { get; }
    public byte[] FirewireId { get; }
    public List<string> Evidence { get; } = [];

    private DeviceSigning(Hash72.DeviceKey key72, byte[] firewireId)
    {
        Key72 = key72;
        FirewireId = firewireId;
    }

    /// <summary>True when the header declares a signature scheme (so writes must be signed).</summary>
    public static bool RequiresSigning(byte[] databaseFile) =>
        databaseFile.Length >= 0xA0 && BitConverter.ToUInt16(databaseFile, 0x30) != 0;

    public static ulong LibraryId(byte[] databaseFile) => BitConverter.ToUInt64(databaseFile, 0x18);

    public static DeviceSigning? Resolve(string itunesDir, string databaseFileName,
        IEnumerable<string> firewireCandidates, IEnumerable<string> referenceDatabases, List<string> problems)
    {
        byte[] cdb = File.ReadAllBytes(Path.Combine(itunesDir, databaseFileName));
        ulong libId = LibraryId(cdb);
        var evidence = new List<string>();

        var references = new List<(string Path, byte[] Bytes)>();
        foreach (var r in referenceDatabases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                byte[] b = File.ReadAllBytes(r);
                if (b.Length >= 0xA0 && LibraryId(b) == libId) references.Add((r, b));
            }
            catch { /* unreadable reference: ignore */ }
        }

        // ---- hash72 key ----
        Hash72.DeviceKey? key = null;
        string cbkPath = Path.Combine(itunesDir, "iTunes Library.itlp", "Locations.itdb.cbk");
        string locPath = Path.Combine(itunesDir, "iTunes Library.itlp", "Locations.itdb");
        if (File.Exists(cbkPath) && File.Exists(locPath))
        {
            var (k, p) = Hash72.VerifyCbk(File.ReadAllBytes(locPath), File.ReadAllBytes(cbkPath));
            if (k is not null && p.Count == 0) { key = k; evidence.Add("hash72 key recovered from the device's valid Locations.itdb.cbk"); }
        }
        if (key is null && Hash72.ExtractFromDatabase(cdb) is { } k2) { key = k2; evidence.Add("hash72 key recovered from the device CDB header"); }
        foreach (var (path, bytes) in references)
        {
            if (key is not null) break;
            if (Hash72.ExtractFromDatabase(bytes) is { } k3) { key = k3; evidence.Add($"hash72 key recovered from reference {path}"); }
        }
        if (key is null) { problems.Add("no valid hash72 signature found to recover this device's signing key from"); return null; }

        // ---- FirewireGuid for hash58 ----
        byte[]? fw = null;
        foreach (var cand in firewireCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byte[] id;
            try { id = Hash58.ParseFirewireGuid(cand); } catch { continue; }
            if (Hash58.Verify(id, cdb)) { fw = id; evidence.Add("FirewireGuid reproduces the device CDB's hash58"); break; }
            var match = references.FirstOrDefault(r => Hash58.Verify(id, r.Bytes));
            if (match.Path is not null) { fw = id; evidence.Add($"FirewireGuid reproduces the hash58 of reference {match.Path}"); break; }
        }
        if (fw is null) { problems.Add("no FirewireGuid candidate reproduces an existing hash58 for this library"); return null; }

        var s = new DeviceSigning(key, fw);
        s.Evidence.AddRange(evidence);
        return s;
    }

    /// <summary>Returns a copy of the database file with fresh hash72 then hash58.</summary>
    public byte[] SignDatabase(byte[] file)
    {
        byte[] copy = (byte[])file.Clone();
        Hash72.Generate(Hash72.DatabaseSha1(copy), Key72).CopyTo(copy, 0x72);
        Hash58.Compute(FirewireId, Hash58.ZeroedForHash(copy)).CopyTo(copy, 0x58);
        return copy;
    }

    public List<string> VerifyDatabase(byte[] file)
    {
        var problems = new List<string>();
        if (!Hash72.Generate(Hash72.DatabaseSha1(file), Key72).AsSpan().SequenceEqual(file.AsSpan(0x72, 46)))
            problems.Add("CDB hash72 does not validate");
        if (!Hash58.Verify(FirewireId, file))
            problems.Add("CDB hash58 does not validate");
        return problems;
    }

    public byte[] BuildCbk(byte[] locations) => Hash72.BuildCbk(locations, Key72);

    public List<string> VerifyCbk(byte[] locations, byte[] cbk)
    {
        var problems = new List<string>();
        if (!BuildCbk(locations).AsSpan().SequenceEqual(cbk)) problems.Add("Locations.itdb.cbk does not match Locations.itdb signed with the device key");
        return problems;
    }
}
