using System.Security.Cryptography;

namespace IpodSync.Core.Device;

/// <summary>
/// The only code path that puts bytes onto a device. Wraps a multi-file write
/// (iTunesCDB + the SQLite bundle files + new audio files) so that:
///
///  1. the whole <c>iPod_Control/iTunes/</c> directory is backed up first, and the
///     backup is SHA-1 verified file-by-file against the device before anything is
///     written (a backup that doesn't match the device is not a backup);
///  2. every file written is read back and compared against the intended bytes;
///  3. on any failure — or when the caller's post-write verification fails —
///     <see cref="Restore"/> puts every database file back from the backup and
///     verifies the restore, leaving the device in its pre-write state.
///
/// New audio files copied into <c>iPod_Control/Music</c> are not deleted on
/// restore: an orphaned audio file is harmless, a deleted file is not (HANDOFF
/// safety rule). They are listed in the log instead.
/// </summary>
public sealed class DeviceWriteTransaction
{
    private readonly string _itunesDir;
    private readonly List<string> _writtenRel = [];   // relative to the iTunes dir
    private readonly List<string> _audioCopied = [];  // absolute device paths
    public string BackupDir { get; }
    public List<string> Log { get; } = [];

    private DeviceWriteTransaction(string itunesDir, string backupDir)
    {
        _itunesDir = itunesDir;
        BackupDir = backupDir;
    }

    /// <summary>Back up <paramref name="itunesDir"/> into
    /// <c>&lt;backupRoot&gt;/&lt;label&gt;-yyyyMMdd-HHmmss/iTunes</c> and verify it.</summary>
    public static DeviceWriteTransaction Begin(string itunesDir, string backupRoot, string label)
    {
        string dir = Path.Combine(backupRoot, $"{label}-{DateTime.Now:yyyyMMdd-HHmmss}");
        if (Directory.Exists(dir)) dir += "-" + Guid.NewGuid().ToString("N")[..6];
        string backup = Path.Combine(dir, "iTunes");
        CopyDir(itunesDir, backup);

        var tx = new DeviceWriteTransaction(itunesDir, backup);
        int files = 0;
        foreach (var file in Directory.EnumerateFiles(itunesDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(itunesDir, file);
            string copy = Path.Combine(backup, rel);
            if (!File.Exists(copy) || Sha1(file) != Sha1(copy))
                throw new IOException($"backup verification failed for {rel}; nothing was written");
            files++;
        }
        tx.Log.Add($"backup      {files} files -> {backup} (SHA-1 verified)");
        return tx;
    }

    /// <summary>Write <paramref name="bytes"/> to a file under the iTunes dir and
    /// read it back. Throws (after which the caller must <see cref="Restore"/>) if
    /// the device doesn't hold exactly those bytes.</summary>
    public void WriteDatabaseFile(string relPath, byte[] bytes)
    {
        string dest = Path.Combine(_itunesDir, relPath);
        _writtenRel.Add(relPath);
        File.WriteAllBytes(dest, bytes);
        byte[] back = File.ReadAllBytes(dest);
        if (!back.AsSpan().SequenceEqual(bytes))
            throw new IOException($"read-back mismatch after writing {relPath}");
        Log.Add($"wrote       {relPath}  {bytes.Length:N0} bytes, SHA-1 {Convert.ToHexString(SHA1.HashData(bytes))[..12]}.. read back identical");
    }

    public void CopyAudioFile(string source, string deviceDest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(deviceDest)!);
        File.Copy(source, deviceDest, overwrite: false);
        _audioCopied.Add(deviceDest);
        if (Sha1(source) != Sha1(deviceDest))
            throw new IOException($"read-back mismatch after copying audio to {deviceDest}");
        Log.Add($"copied      {deviceDest}  {new FileInfo(deviceDest).Length:N0} bytes, read back identical");
    }

    /// <summary>Restore every database file this transaction wrote from the backup
    /// and verify each restored file matches the backup byte-for-byte.</summary>
    public bool Restore()
    {
        bool ok = true;
        foreach (var rel in _writtenRel.Distinct())
        {
            string src = Path.Combine(BackupDir, rel);
            string dest = Path.Combine(_itunesDir, rel);
            try
            {
                if (File.Exists(src))
                {
                    File.Copy(src, dest, overwrite: true);
                    bool same = Sha1(src) == Sha1(dest);
                    ok &= same;
                    Log.Add($"RESTORED    {rel} from backup{(same ? " (verified)" : " -- VERIFY FAILED")}");
                }
                else
                {
                    Log.Add($"RESTORE     {rel} did not exist before the write; left in place for inspection");
                    ok = false;
                }
            }
            catch (Exception ex) { ok = false; Log.Add($"RESTORE FAILED {rel}: {ex.Message}"); }
        }
        foreach (var a in _audioCopied)
            Log.Add($"note        audio file {a} was copied and is now unreferenced (left in place; harmless)");
        return ok;
    }

    public static string Sha1(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA1.HashData(fs));
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDir(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }
}
