using System.Text;
using System.Text.Json;
using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.LocalLibrary;

/// <summary>
/// Folder / NAS → iPod sync planning. Read-only: produces a plan and change-set ops;
/// the verified write pipeline does the writing, in batches.
///
/// - The sync manifest lives off the device (HANDOFF: "never store our state on the
///   iPod"), keyed by the device library's persistent id: source file (relative
///   path, size, mtime) → track persistent id on the device.
/// - A source file with no manifest entry is first matched against what is already
///   on the iPod (normalised title + artist, duration within 2.5 s) so music the
///   device already holds — e.g. synced by iTunes — is adopted into the manifest
///   instead of being copied a second time.
/// - Removing device tracks whose source disappeared is opt-in, and only ever
///   touches tracks the manifest says this sync added or adopted.
/// </summary>
public static class FolderSync
{
    public static readonly string[] AudioExtensions =
        [".mp3", ".m4a", ".m4b", ".aac", ".wav", ".aif", ".aiff", ".flac", ".ogg", ".oga", ".opus", ".wma", ".ape", ".wv"];

    public sealed class ManifestEntry
    {
        public string RelativePath { get; set; } = "";
        public long Size { get; set; }
        public long MtimeTicks { get; set; }
        public ulong PersistentId { get; set; }
        /// <summary>"added" (copied by ipodsync) or "adopted" (already on the device).</summary>
        public string Origin { get; set; } = "added";
    }

    public sealed class Manifest
    {
        public ulong LibraryId { get; set; }
        public string SourceFolder { get; set; } = "";
        public List<ManifestEntry> Entries { get; set; } = [];

        public static string PathFor(ulong libraryId) => System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("IPODSYNC_MANIFEST_DIR")
                ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ipodsync", "manifests"),
            $"{libraryId:X16}.json");

        public static Manifest Load(ulong libraryId, string folder)
        {
            string p = PathFor(libraryId);
            if (File.Exists(p))
            {
                var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(p));
                if (m is not null && string.Equals(m.SourceFolder, folder, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return new Manifest { LibraryId = libraryId, SourceFolder = folder };
        }

        public void Save()
        {
            string p = PathFor(LibraryId);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, p, overwrite: true);
        }
    }

    public sealed record SourceFile(string Path, string RelativePath, long Size, long MtimeTicks, string? Title, string? Artist, string? Album, double Seconds);

    public sealed class Plan
    {
        public List<SourceFile> Unchanged { get; } = [];
        public List<(SourceFile File, Track Track)> Adopt { get; } = [];
        public List<SourceFile> Add { get; } = [];
        public List<(ManifestEntry Entry, Track Track)> RemoveCandidates { get; } = [];
        public List<string> Unreadable { get; } = [];
        /// <summary>Source files skipped because another file in the folder is the same
        /// song (same title/artist/duration); the better copy is added.</summary>
        public List<(SourceFile Skipped, SourceFile Kept)> SourceDuplicates { get; } = [];
        public long AddBytes => Add.Sum(f => f.Size);
    }

    public static List<SourceFile> Scan(string folder, Action<int>? progress = null)
    {
        var list = new List<SourceFile>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(folder, "*", opts))
        {
            if (!AudioExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            var fi = new FileInfo(file);
            string? title = null, artist = null, album = null;
            double seconds = 0;
            try
            {
                using var tf = TagLib.File.Create(file);
                title = Nz(tf.Tag.Title); artist = Nz(tf.Tag.FirstPerformer) ?? Nz(tf.Tag.FirstAlbumArtist); album = Nz(tf.Tag.Album);
                seconds = tf.Properties.Duration.TotalSeconds;
            }
            catch { /* unreadable tags: matched by file name only */ }
            list.Add(new SourceFile(file, System.IO.Path.GetRelativePath(folder, file), fi.Length, fi.LastWriteTimeUtc.Ticks,
                title ?? System.IO.Path.GetFileNameWithoutExtension(file), artist, album, seconds));
            progress?.Invoke(list.Count);
        }
        return list.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Plan MakePlan(IReadOnlyList<SourceFile> files, ItunesDatabase device, Manifest manifest, bool removeMissing)
    {
        var plan = new Plan();
        var byPid = device.Tracks.ToDictionary(t => t.PersistentId);
        var byRel = manifest.Entries.GroupBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<ulong>(manifest.Entries.Where(e => byPid.ContainsKey(e.PersistentId)).Select(e => e.PersistentId));
        var byKey = device.Tracks.GroupBy(t => Key(t.Title, t.Artist)).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var f in files)
        {
            if (byRel.TryGetValue(f.RelativePath, out var e) && byPid.ContainsKey(e.PersistentId))
            {
                plan.Unchanged.Add(f);
                continue;
            }
            var match = byKey.TryGetValue(Key(f.Title, f.Artist), out var candidates)
                ? candidates.FirstOrDefault(t => !claimed.Contains(t.PersistentId) && (f.Seconds <= 0 || Math.Abs(t.LengthMs / 1000.0 - f.Seconds) <= 2.5))
                : null;
            // Fallback for sources whose tags put the artist inside the title (e.g.
            // "NOTION - CHRYSTAL - THE DAYS [NOTION REMIX]" vs "CHRYSTAL; NotioN - The Days
            // (NOTION Remix)"): same duration within 1.5 s AND both the device title and its
            // first artist appear in the source's title/artist/file name.
            match ??= device.Tracks.FirstOrDefault(t => !claimed.Contains(t.PersistentId) && f.Seconds > 0 &&
                Math.Abs(t.LengthMs / 1000.0 - f.Seconds) <= 1.5 && LooseMatch(f, t));
            if (match is not null)
            {
                plan.Adopt.Add((f, match));
                claimed.Add(match.PersistentId);
            }
            else plan.Add.Add(f);
        }

        // The same song twice in the source (e.g. a FLAC and a lossy export): keep one,
        // preferring lossless, then larger files.
        // A song that was adopted (already on the iPod) also covers its other copies.
        bool Same(SourceFile a, SourceFile b) => Key(a.Title, a.Artist) == Key(b.Title, b.Artist) && a.Seconds > 0 && b.Seconds > 0 && Math.Abs(a.Seconds - b.Seconds) <= 2.5;
        var kept = new List<SourceFile>();
        foreach (var f in plan.Add)
        {
            var adopted = plan.Adopt.FirstOrDefault(a => Same(a.File, f));
            if (adopted.File is not null) { plan.SourceDuplicates.Add((f, adopted.File)); continue; }
            int i = kept.FindIndex(k => Same(k, f));
            if (i < 0) { kept.Add(f); continue; }
            var (better, worse) = Rank(f).CompareTo(Rank(kept[i])) > 0 ? (f, kept[i]) : (kept[i], f);
            kept[i] = better;
            plan.SourceDuplicates.Add((worse, better));
        }
        plan.Add.Clear();
        plan.Add.AddRange(kept);

        if (removeMissing)
        {
            var present = files.Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var e in manifest.Entries.Where(e => !present.Contains(e.RelativePath)))
                if (byPid.TryGetValue(e.PersistentId, out var t)) plan.RemoveCandidates.Add((e, t));
        }
        return plan;
    }

    private static bool LooseMatch(SourceFile f, Track t)
    {
        string haystack = Key(f.Title + " " + f.Artist + " " + System.IO.Path.GetFileNameWithoutExtension(f.Path), null).TrimEnd('|');
        var parts = Key(t.Title, t.Artist).Split('|');
        return parts[0].Length >= 4 && parts[1].Length >= 3 && haystack.Contains(parts[0]) && haystack.Contains(parts[1]);
    }

    private static (int, long) Rank(SourceFile f)
    {
        string ext = System.IO.Path.GetExtension(f.Path).ToLowerInvariant();
        int lossless = ext is ".flac" or ".wav" or ".aif" or ".aiff" or ".ape" or ".wv" ? 1 : 0;
        return (lossless, f.Size);
    }

    /// <summary>Normalised match key: lower-case letters and digits of title and
    /// artist, ignoring a trailing "(feat. …)" and anything bracketed.</summary>
    public static string Key(string? title, string? artist)
    {
        static string Norm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder();
            int depth = 0;
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                if (ch is '(' or '[') { depth++; continue; }
                if (ch is ')' or ']') { depth = Math.Max(0, depth - 1); continue; }
                if (depth == 0 && char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }
        // Artist lists are written differently between sources ("A; B", "A & B", "A, B",
        // "A feat. B"): compare only the first artist named.
        string? first = artist is null ? null
            : System.Text.RegularExpressions.Regex.Split(artist, @"\s*(?:;|,|&|/| x | feat\.? | ft\.? | featuring | and )\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase)[0];
        return Norm(title) + "|" + Norm(first);
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
