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
    /// <summary>Null on models that only use hash58 (nano 3G/4G, iPod classic): those have no
    /// hash72 field and no SQLite bundle to sign.</summary>
    public Hash72.DeviceKey? Key72 { get; }
    public byte[] FirewireId { get; }
    public Device.IpodSignature Scheme { get; }
    public List<string> Evidence { get; } = [];

    /// <summary>Set only for hashAB devices, and only when the user's external signer has
    /// reproduced the signature already on the device (see <see cref="ExternalHashAbSigner"/>).</summary>
    public ExternalHashAbSigner? HashAbSigner { get; private init; }

    private DeviceSigning(Hash72.DeviceKey? key72, byte[] firewireId, Device.IpodSignature scheme)
    {
        Key72 = key72;
        FirewireId = firewireId;
        Scheme = scheme;
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

        // What does this model actually expect? nano 3G/4G and the classics sign with hash58
        // alone; the nano 5G adds hash72 (and the signed Locations.itdb.cbk). hashAB devices
        // (nano 6G/7G, shuffle 4G) are refused here rather than written with a wrong signature.
        var (scheme, schemeField) = Device.IpodProfiler.ReadSignatureScheme(cdb);
        if (scheme is Device.IpodSignature.HashAb)
        {
            // hashAB can't be computed here (white-box AES; see HashAb). A signer the user
            // supplies is accepted only after it reproduces this device's own signature.
            var external = ExternalHashAbSigner.Resolve(cdb, firewireCandidates, problems, evidence);
            if (external is null)
            {
                problems.Add("this iPod signs its database with hashAB (nano 6G/7G, shuffle 4G) -- see COMPATIBILITY.md");
                return null;
            }
            byte[] anyId = [];
            foreach (var c in firewireCandidates) { try { anyId = Hash58.ParseFirewireGuid(c); break; } catch { } }
            var signerAb = new DeviceSigning(null, anyId, scheme) { HashAbSigner = external };
            signerAb.Evidence.AddRange(evidence);
            return signerAb;
        }
        if (scheme is Device.IpodSignature.Unknown)
        {
            problems.Add($"this iPod asks for database signature scheme {schemeField}, which this build does not know");
            return null;
        }
        bool needsHash72 = scheme is Device.IpodSignature.Hash58AndHash72;

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
        if (key is null && needsHash72) { problems.Add("no valid hash72 signature found to recover this device's signing key from"); return null; }
        if (!needsHash72) evidence.Add("this model signs with hash58 only (no hash72 field)");

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

        var s = new DeviceSigning(needsHash72 ? key : null, fw, scheme);
        s.Evidence.AddRange(evidence);
        return s;
    }

    /// <summary>Returns a copy of the database file with fresh hash72 (where the model has one)
    /// then hash58.</summary>
    public byte[] SignDatabase(byte[] file)
    {
        if (HashAbSigner is { } ab) return ab.Sign(file);
        byte[] copy = (byte[])file.Clone();
        if (Key72 is { } key72) Hash72.Generate(Hash72.DatabaseSha1(copy), key72).CopyTo(copy, 0x72);
        if (FirewireId.Length > 0) Hash58.Compute(FirewireId, Hash58.ZeroedForHash(copy)).CopyTo(copy, 0x58);
        return copy;
    }

    public List<string> VerifyDatabase(byte[] file)
    {
        var problems = new List<string>();
        if (HashAbSigner is { } ab)
        {
            if (!ab.Verify(file)) problems.Add("CDB hashAB does not validate");
            return problems;
        }
        if (Key72 is { } key72 && !Hash72.Generate(Hash72.DatabaseSha1(file), key72).AsSpan().SequenceEqual(file.AsSpan(0x72, 46)))
            problems.Add("CDB hash72 does not validate");
        if (FirewireId.Length > 0 && !Hash58.Verify(FirewireId, file))
            problems.Add("CDB hash58 does not validate");
        return problems;
    }

    /// <summary>Only devices with hash72 have a signed Locations.itdb.cbk.</summary>
    public bool CanSignCbk => Key72 is not null;

    public byte[] BuildCbk(byte[] locations) => Hash72.BuildCbk(locations,
        Key72 ?? throw new InvalidOperationException("this model has no hash72 key, so it has no signed Locations.itdb.cbk"));

    public List<string> VerifyCbk(byte[] locations, byte[] cbk)
    {
        var problems = new List<string>();
        if (!BuildCbk(locations).AsSpan().SequenceEqual(cbk)) problems.Add("Locations.itdb.cbk does not match Locations.itdb signed with the device key");
        return problems;
    }
}
