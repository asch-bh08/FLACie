using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Itlp;
using IpodSync.Core.Signing;

/// <summary>
/// The one write path for both databases. Dry run by default; with commit=true it
/// follows HANDOFF.md's rules exactly: prove everything off-device (CDB in memory,
/// SQLite on a staged copy, signatures regenerated and re-validated), back up and
/// verify the backup, write, read back, then re-verify the device from scratch —
/// and restore from the backup if anything about the result is not as proven.
/// </summary>
static class WritePipeline
{
    public sealed class Options
    {
        public required string Root { get; init; }
        public ChangeSet? Changes { get; init; }
        public bool Commit { get; init; }
        public string BackupRoot { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "ipod-backups");
        public string Label { get; init; } = "applyedits";
        /// <summary>Re-sign the current CDB even when no edit changes it.</summary>
        public bool ResignCdb { get; init; }
        public bool AllowUnsigned { get; init; }
        public IReadOnlyList<string> FirewireCandidates { get; init; } = [];
        public IReadOnlyList<string> SigningReferences { get; init; } = [];
    }

    public static int Run(Options o)
    {
        var log = new List<string>();
        void Say(string s) { Console.WriteLine(s); log.Add(s); }

        string itunesDir = Directory.Exists(Path.Combine(o.Root, "iPod_Control"))
            ? Path.GetDirectoryName(IpodDevice.Open(o.Root).ItunesDbPath)!
            : throw new DirectoryNotFoundException($"{o.Root} is not an iPod root (no iPod_Control)");
        string cdbName = File.Exists(Path.Combine(itunesDir, "iTunesDB")) ? "iTunesDB" : "iTunesCDB";
        string cdbPath = Path.Combine(itunesDir, cdbName);
        string itlpDir = Path.Combine(itunesDir, "iTunes Library.itlp");
        bool hasItlp = File.Exists(Path.Combine(itlpDir, "Library.itdb"));
        string deviceRoot = Path.GetDirectoryName(Path.GetDirectoryName(itunesDir))!;

        // ---------------------------------------------------------------- 1. CDB, in memory
        byte[] originalCdb = File.ReadAllBytes(cdbPath);
        string originalCdbSha = DeviceWriteTransaction.Sha1(cdbPath);
        var before = ItunesDbReader.Read(originalCdb);
        ItunesDatabase after = before;
        ApplyReport? report = null;
        bool ok = true;

        if (o.Changes?.Ops is { Count: > 0 })
        {
            report = EditApplier.Apply(originalCdb, o.Changes);
            Say($"CDB         {cdbPath}");
            Say($"tracks      {report.TracksBefore} -> {report.TracksAfter}");
            Say($"playlists   {report.PlaylistsBefore} -> {report.PlaylistsAfter}");
            foreach (var op in report.Ops) Say($"  [{(op.Ok ? "ok" : "FAIL")}] {op.Op}: {op.Detail}");
            Say($"reader re-parse     {(report.Parseable ? "PASS" : "FAIL")}");
            Say($"idempotent write    {(report.Idempotent ? "PASS" : "FAIL")}");
            foreach (var p in report.Problems) Say($"  - {p}");
            foreach (var fc in report.FileCopies) Say($"new file    {fc.Source} -> {fc.DestRel}");
            if (!report.AllOk) ok = false;
            else after = ItunesDbReader.Read(report.ModifiedInflated);
        }
        else Say($"CDB         {cdbPath} (content unchanged)");

        // ---------------------------------------------------------------- 1b. signatures
        DeviceSigning? signer = null;
        if (DeviceSigning.RequiresSigning(originalCdb))
        {
            var problems = new List<string>();
            signer = DeviceSigning.Resolve(itunesDir, cdbName, o.FirewireCandidates, o.SigningReferences, problems);
            if (signer is null)
            {
                foreach (var p in problems) Say($"  SIGNING: {p}");
                if (o.AllowUnsigned) Say("signing     UNAVAILABLE -- continuing unsigned because --allow-unsigned was given");
                else { Say("signing     UNAVAILABLE -- refusing (pass --firewire-guid / --signing-reference, or --allow-unsigned)"); ok = false; }
            }
            else
            {
                Say("signing     device key material proven:");
                foreach (var e in signer.Evidence) Say($"  - {e}");
                var current = signer.VerifyDatabase(originalCdb);
                Say($"current CDB signatures {(current.Count == 0 ? "valid" : "STALE (" + string.Join("; ", current) + ")")}");
            }
        }

        byte[]? cdbToWrite = report?.ModifiedOnDisk ?? (o.ResignCdb ? originalCdb : null);
        if (ok && cdbToWrite is not null && signer is not null)
        {
            byte[] unsigned = cdbToWrite;
            byte[] signedCdb = signer.SignDatabase(unsigned);
            var sigProblems = signer.VerifyDatabase(signedCdb);
            // Signing may only change the two signature fields.
            bool onlySigBytes = signedCdb.Length == unsigned.Length && Enumerable.Range(0, signedCdb.Length)
                .All(i => signedCdb[i] == unsigned[i] || (i >= 0x58 && i < 0x6C) || (i >= 0x72 && i < 0xA0));
            Say($"CDB signed          {(sigProblems.Count == 0 && onlySigBytes ? "PASS" : "FAIL")} (hash72 + hash58 regenerated; only signature bytes differ: {onlySigBytes})");
            foreach (var p in sigProblems) Say($"  - {p}");
            ok &= sigProblems.Count == 0 && onlySigBytes;
            cdbToWrite = signedCdb;
        }
        if (cdbToWrite is not null && cdbToWrite.AsSpan().SequenceEqual(originalCdb)) cdbToWrite = null;

        // ---------------------------------------------------------------- 2. SQLite, on a staged copy
        string? staged = null;
        var changedBundleFiles = new List<string>();
        var deviceBundleSha = new Dictionary<string, string>();
        if (!hasItlp) Say("SQLite      no iTunes Library.itlp on this device; CDB only");
        else if (ok)
        {
            var diffBefore = ItlpCompare.Compare(itlpDir, before);
            staged = ItlpSync.Stage(itlpDir, Path.Combine(Path.GetTempPath(), "ipodsync-stage"));
            foreach (var f in ItlpSync.BundleFiles)
                if (File.Exists(Path.Combine(itlpDir, f))) deviceBundleSha[f] = DeviceWriteTransaction.Sha1(Path.Combine(itlpDir, f));

            var hashesBefore = ItlpSync.TableHashes(staged);
            var now = DateTimeOffset.UtcNow;
            Say($"SQLite      staged copy {staged}");
            var touched = new HashSet<string>();
            var trackSync = ItlpTrackSync.Sync(staged, after, now);
            foreach (var a in trackSync.Actions) Say($"  sqlite: {a}");
            foreach (var p in trackSync.Problems) Say($"  SQLITE PROBLEM: {p}");
            touched.UnionWith(trackSync.TouchedTables);
            ok &= trackSync.Ok;
            if (trackSync.Ok)
            {
                var sync = ItlpSync.SyncPlaylists(staged, after, now);
                foreach (var a in sync.Actions) Say($"  sqlite: {a}");
                foreach (var p in sync.Problems) Say($"  SQLITE PROBLEM: {p}");
                if (sync.Actions.Count + trackSync.Actions.Count == 0) Say("  sqlite: no changes needed");
                touched.UnionWith(sync.TouchedTables);
                ok &= sync.Ok;
            }

            var checks = VerifyStaged(staged, after, diffBefore, hashesBefore, touched);
            foreach (var c in checks.Lines) Say(c);
            ok &= checks.Ok;

            // Locations.itdb is covered by the hash72-signed cbk: rebuild it if it changed.
            string stagedLoc = Path.Combine(staged, "Locations.itdb");
            if (File.Exists(stagedLoc) && deviceBundleSha.TryGetValue("Locations.itdb", out var locSha) && DeviceWriteTransaction.Sha1(stagedLoc) != locSha)
            {
                if (signer is null) { Say("Locations.itdb changed but no signing key is available to rebuild Locations.itdb.cbk -- refusing"); ok = false; }
                else
                {
                    byte[] cbk = signer.BuildCbk(File.ReadAllBytes(stagedLoc));
                    File.WriteAllBytes(Path.Combine(staged, "Locations.itdb.cbk"), cbk);
                    var cbkProblems = Hash72.VerifyCbk(File.ReadAllBytes(stagedLoc), cbk).Problems;
                    Say($"cbk rebuilt         {(cbkProblems.Count == 0 ? "PASS" : "FAIL")} ({cbk.Length} bytes)");
                    ok &= cbkProblems.Count == 0;
                }
            }

            foreach (var f in ItlpSync.BundleFiles)
            {
                string s = Path.Combine(staged, f);
                if (File.Exists(s) && deviceBundleSha.TryGetValue(f, out var dsha) && DeviceWriteTransaction.Sha1(s) != dsha)
                    changedBundleFiles.Add(f);
            }
            Say($"bundle files that will change: {(changedBundleFiles.Count == 0 ? "none" : string.Join(", ", changedBundleFiles))}");
        }
        Say($"CDB will change     {(cdbToWrite is null ? "no" : "yes")}");

        if (!o.Commit)
        {
            Say(ok ? "DRY RUN: all checks passed. Nothing written. Re-run with --yes to write to the device."
                   : "DRY RUN: checks did NOT all pass. Nothing written (and --yes would refuse).");
            return ok ? 0 : 1;
        }
        if (!ok) { Say("refusing to write: not all checks passed."); return 1; }
        if (cdbToWrite is null && changedBundleFiles.Count == 0 && (report?.FileCopies.Count ?? 0) == 0) { Say("nothing to write."); return 0; }

        // ---------------------------------------------------------------- 3. write
        if (DeviceWriteTransaction.Sha1(cdbPath) != originalCdbSha ||
            deviceBundleSha.Any(kv => DeviceWriteTransaction.Sha1(Path.Combine(itlpDir, kv.Key)) != kv.Value))
        {
            Say("refusing to write: device database changed while the change-set was being prepared.");
            return 1;
        }

        var tx = DeviceWriteTransaction.Begin(itunesDir, o.BackupRoot, o.Label);
        Say(tx.Log[^1]);
        bool restoreNeeded;
        try
        {
            // Audio first (harmless if orphaned), then the SQLite bundle, then the CDB.
            foreach (var (src, destRel) in report?.FileCopies ?? [])
                tx.CopyAudioFile(src, Path.Combine(deviceRoot, destRel.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var f in changedBundleFiles)
                tx.WriteDatabaseFile(Path.Combine("iTunes Library.itlp", f), File.ReadAllBytes(Path.Combine(staged!, f)));
            if (cdbToWrite is not null) tx.WriteDatabaseFile(cdbName, cdbToWrite);
            foreach (var l in tx.Log.Skip(1)) Say(l);

            // ------------------------------------------------------------ 4. verify from the device
            var post = VerifyDevice(itunesDir, cdbName, cdbToWrite, staged, changedBundleFiles, after, deviceRoot, signer);
            foreach (var l in post.Lines) Say(l);
            restoreNeeded = !post.Ok;
            // Test hook: proves the restore path end to end on a fake device root.
            if (Environment.GetEnvironmentVariable("IPODSYNC_FAULT_INJECT") == "postverify")
            {
                Say("FAULT INJECTED: treating post-write verification as failed (IPODSYNC_FAULT_INJECT=postverify)");
                restoreNeeded = true;
            }
        }
        catch (Exception ex)
        {
            Say($"WRITE ERROR: {ex.Message}");
            restoreNeeded = true;
        }

        if (restoreNeeded)
        {
            Say("Post-write verification failed -> restoring the pre-write backup.");
            int n = tx.Log.Count;
            bool restored = tx.Restore();
            foreach (var l in tx.Log.Skip(n)) Say(l);
            Say(restored ? $"Device restored to its pre-write state (backup {tx.BackupDir})."
                         : $"RESTORE INCOMPLETE -- device state uncertain; backup is {tx.BackupDir}");
            WriteLog(tx.BackupDir, log);
            return 1;
        }

        Say($"WRITE VERIFIED. Backup of the pre-write state: {tx.BackupDir}");
        WriteLog(tx.BackupDir, log);
        return 0;
    }

    private sealed record Checks(bool Ok, List<string> Lines);

    private static Checks VerifyStaged(string staged, ItunesDatabase after, ItlpCompare.Diff diffBefore,
        Dictionary<string, string> hashesBefore, HashSet<string> touched)
    {
        var lines = new List<string>();
        bool ok = true;

        var integrity = ItlpSync.IntegrityCheck(staged);
        lines.Add($"sqlite integrity    {(integrity.Count == 0 ? "PASS" : "FAIL")}");
        foreach (var p in integrity) lines.Add("  - " + p);
        ok &= integrity.Count == 0;

        var hashesAfter = ItlpSync.TableHashes(staged);
        var changed = hashesAfter.Where(kv => !hashesBefore.TryGetValue(kv.Key, out var h) || h != kv.Value).Select(kv => kv.Key).ToList();
        var unexpected = changed.Where(t => !touched.Contains(t)).ToList();
        lines.Add($"untouched tables    {(unexpected.Count == 0 ? "PASS" : "FAIL")} ({changed.Count} table(s) changed: {string.Join(", ", changed)})");
        foreach (var t in unexpected) lines.Add($"  - table {t} changed but the sync did not declare touching it");
        ok &= unexpected.Count == 0 && hashesAfter.Count == hashesBefore.Count;

        var diffAfter = ItlpCompare.Compare(staged, after);
        lines.Add($"playlists in sync   {(diffAfter.PlaylistsInSync ? "PASS" : "FAIL")}");
        foreach (var (section, l) in diffAfter.Sections().Skip(3)) foreach (var x in l) lines.Add($"  - {section}: {x}");
        ok &= diffAfter.PlaylistsInSync;

        lines.Add($"tracks in sync      {(diffAfter.TracksInSync ? "PASS" : "FAIL")} ({TrackLines(diffBefore).Count()} difference(s) before, {TrackLines(diffAfter).Count()} after)");
        foreach (var x in TrackLines(diffAfter)) lines.Add("  - " + x);
        ok &= diffAfter.TracksInSync;
        return new Checks(ok, lines);
    }

    private static IEnumerable<string> TrackLines(ItlpCompare.Diff d) =>
        d.TracksOnlyInCdb.Concat(d.ItemsOnlyInSqlite).Concat(d.TrackFieldMismatches);

    private static Checks VerifyDevice(string itunesDir, string cdbName, byte[]? cdbWritten, string? staged,
        List<string> changedBundleFiles, ItunesDatabase expected, string deviceRoot, DeviceSigning? signer)
    {
        var lines = new List<string>();
        bool ok = true;
        void Check(string name, bool pass, string detail = "")
        {
            lines.Add($"device {name,-22} {(pass ? "PASS" : "FAIL")}{(detail == "" ? "" : "  " + detail)}");
            ok &= pass;
        }

        // CDB: exact bytes, parses, identity round-trip, signatures, same counts as proven in memory.
        byte[] cdb = File.ReadAllBytes(Path.Combine(itunesDir, cdbName));
        if (cdbWritten is not null) Check("CDB bytes", cdb.AsSpan().SequenceEqual(cdbWritten));
        ItunesDatabase? reread = null;
        try { reread = ItunesDbReader.Read(cdb); Check("CDB re-read", true, $"{reread.Tracks.Count} tracks, {reread.Playlists.Count} playlists"); }
        catch (Exception ex) { Check("CDB re-read", false, ex.Message); }
        var rt = RoundTrip.Run(cdb);
        Check("CDB round-trip", rt.StructureMatches && rt.CompressionRoundTripOk != false, "byte-identical");
        if (signer is not null && cdbWritten is not null)
        {
            var sig = signer.VerifyDatabase(cdb);
            Check("CDB signatures", sig.Count == 0, sig.Count == 0 ? "hash72 + hash58 valid" : string.Join("; ", sig));
        }
        if (reread is not null)
        {
            Check("track count", reread.Tracks.Count == expected.Tracks.Count, $"{reread.Tracks.Count} (expected {expected.Tracks.Count})");
            Check("playlist count", reread.Playlists.Count == expected.Playlists.Count, $"{reread.Playlists.Count} (expected {expected.Playlists.Count})");
            var missing = reread.Tracks.Where(t => t.RelativePath is not null && !File.Exists(Path.Combine(deviceRoot, t.RelativePath))).ToList();
            Check("audio files present", missing.Count == 0, missing.Count == 0 ? $"all {reread.Tracks.Count} referenced files exist" : $"{missing.Count} missing, e.g. {missing[0].RelativePath}");
        }

        // SQLite: device bytes == proven staged bytes; then check a fresh copy read back off the device.
        string itlp = Path.Combine(itunesDir, "iTunes Library.itlp");
        if (staged is not null)
        {
            foreach (var f in changedBundleFiles)
                Check($"{f} bytes", DeviceWriteTransaction.Sha1(Path.Combine(itlp, f)) == DeviceWriteTransaction.Sha1(Path.Combine(staged, f)));
            string readBack = ItlpSync.Stage(itlp, Path.Combine(Path.GetTempPath(), "ipodsync-verify"));
            var integrity = ItlpSync.IntegrityCheck(readBack);
            Check("sqlite integrity", integrity.Count == 0, string.Join("; ", integrity));
            string loc = Path.Combine(readBack, "Locations.itdb"), cbk = loc + ".cbk";
            if (File.Exists(loc) && File.Exists(cbk))
            {
                var cbkProblems = Hash72.VerifyCbk(File.ReadAllBytes(loc), File.ReadAllBytes(cbk)).Problems;
                Check("Locations cbk valid", cbkProblems.Count == 0, string.Join("; ", cbkProblems));
            }
            if (reread is not null)
            {
                var diff = ItlpCompare.Compare(readBack, reread);
                Check("playlists in sync", diff.PlaylistsInSync);
                Check("tracks in sync", diff.TracksInSync);
            }
        }
        return new Checks(ok, lines);
    }

    private static void WriteLog(string backupDir, List<string> log)
    {
        try { File.WriteAllLines(Path.Combine(Path.GetDirectoryName(backupDir)!, "write-log.txt"), log); }
        catch { /* the console already has it */ }
    }
}
