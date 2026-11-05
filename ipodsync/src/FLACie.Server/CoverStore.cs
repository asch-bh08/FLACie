using System.Security.Cryptography;
using System.Text;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// The covers kept on disk, one file per (cover key, variant). A Jellyfin cover's key carries its image tag, so a cover is fetched once and never again unless
/// Jellyfin's tag for it changes (a new key). There is no fixed cap: everything stays while the data volume has room, and only when its free space falls below
/// <see cref="MinFreeBytes"/> (the larger of 2 GB and 10 % of the volume) are the least recently used covers deleted until there is room again.
/// </summary>
public sealed class CoverStore
{
    public const string Row = "s240", Full = "full", Other = "v3";
    static readonly string[] Exts = [".webp", ".jpg", ".png", ".gif", ".img"];
    readonly string dir;
    int saves;

    /// <summary>Free and total bytes of the volume the covers are on (replaceable for tests).</summary>
    public Func<(long Free, long Total)> Space { get; set; }
    public CoverStore(string dir)
    {
        this.dir = Directory.CreateDirectory(dir).FullName;
        Space = () => { try { var d = new DriveInfo(Path.GetPathRoot(this.dir)!); return (d.AvailableFreeSpace, d.TotalSize); } catch (Exception) { return (long.MaxValue, long.MaxValue); } };
    }

    /// <summary>The least free space worth keeping on the volume (2 GB): below it or below 10 % of the volume, the least recently used covers are deleted.</summary>
    public long MinFloorBytes { get; set; } = 2L << 30;
    /// <summary>The share of the volume that is kept free (10 %).</summary>
    public int MinFreePercent { get; set; } = 10;
    public long MinFreeBytes { get { var (_, total) = Space(); return Math.Max(MinFloorBytes, total / 100 * MinFreePercent); } }
    /// <summary>A cover saved or looked at in the last few minutes is never evicted: it is on its way to a page (or is the one just fetched for the request being answered).</summary>
    public static readonly TimeSpan Protected = TimeSpan.FromMinutes(5);

    /// <summary>One file per key: the files are not tied to the lookup rules (owner, 2 Nov: tying them made every catalog cover be fetched again at once and
    /// Deezer's limit left many missing). A cover found wrongly is deleted on its own; browsers are told by the address (CoverOverrides.CatalogRules).</summary>
    public static string Name(string key) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
    static string Ext(string contentType) => contentType switch { "image/webp" => ".webp", "image/jpeg" => ".jpg", "image/png" => ".png", "image/gif" => ".gif", _ => ".img" };
    public static string TypeOf(string path) => Path.GetExtension(path) switch { ".webp" => "image/webp", ".png" => "image/png", ".gif" => "image/gif", _ => "image/jpeg" };

    /// <summary>The saved cover, if there is one (its use is noted, so the least recently used can be told apart).</summary>
    public string? Find(string key, string variant)
    {
        var stem = Path.Combine(dir, Name(key) + "." + variant);
        foreach (var e in Exts)
        {
            var f = stem + e;
            if (!File.Exists(f)) continue;
            try { if (DateTime.UtcNow - File.GetLastAccessTimeUtc(f) > TimeSpan.FromMinutes(10)) File.SetLastAccessTimeUtc(f, DateTime.UtcNow); } catch (Exception) { }
            return f;
        }
        return null;
    }

    public async Task<string> SaveAsync(string key, string variant, byte[] bytes, string contentType)
    {
        var file = Path.Combine(dir, Name(key) + "." + variant + Ext(contentType));
        var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        File.Move(tmp, file, true);
        if (Interlocked.Increment(ref saves) % 25 == 0) EvictIfLow();   // (what was just saved is protected: see Protected)
        return file;
    }

    IEnumerable<FileInfo> Files() => new DirectoryInfo(dir).EnumerateFiles().Where(f => !f.Name.EndsWith(".tmp", StringComparison.Ordinal));

    public (int Count, long Bytes) Size() { var n = 0; long b = 0; foreach (var f in Files()) { n++; b += f.Length; } return (n, b); }

    public int Clear() { var n = 0; foreach (var f in Files().ToList()) try { f.Delete(); n++; } catch (Exception) { } return n; }

    /// <summary>When the volume's free space is below the minimum, deletes the least recently used covers until it is 25 % above it. Returns how many went.</summary>
    public int EvictIfLow()
    {
        var (free, _) = Space();
        var min = MinFreeBytes;
        if (free >= min) return 0;
        var want = (long)(min * 1.25) - free;   // bytes to give back
        var n = 0; long freed = 0;
        var fresh = DateTime.UtcNow - Protected;
        foreach (var f in Files().OrderBy(f => f.LastAccessTimeUtc).ThenBy(f => f.LastWriteTimeUtc).ToList())
        {
            if (freed >= want) break;
            if (f.LastAccessTimeUtc > fresh) continue;
            try { var len = f.Length; f.Delete(); freed += len; n++; } catch (Exception) { }
        }
        return n;
    }
}

/// <summary>What the cover address answers when every source came up empty (F1).</summary>
public enum CoverMiss { Generated, Fallback, TryAgain }

/// <summary>
/// F1: the last link of the cover chain, so a song never shows a blank: a picture made from the artist and title on the app's colours (an SVG, the same
/// every time for the same song). Not saved: when a real cover turns up later it takes over.
/// </summary>
public static class GeneratedCover
{
    /// <summary>Kept briefly by the browser, so a real cover found later shows within the hour.</summary>
    public const string CacheControl = "private, max-age=3600";
    /// <summary>The PNG's sides: rows and tiles, and the player (?s=full).</summary>
    public const int RowPx = 240, FullPx = 1000;

    // the app's dark sleeve colours, one picked by the song's name
    static readonly (string From, string To)[] Colours =
        [("#1f2a44", "#3d5a80"), ("#2b193d", "#6b3fa0"), ("#13322b", "#2a9d8f"), ("#3a1c1c", "#c0392b"), ("#2d2a16", "#b5942a"), ("#1b263b", "#e07a5f")];

    /// <summary>A cover that is gone for good gets a made one, unless the key carries a fallback picture; a lookup cut short is tried again.</summary>
    public static CoverMiss Answer(bool transient, bool hasFallback) => transient ? CoverMiss.TryAgain : hasFallback ? CoverMiss.Fallback : CoverMiss.Generated;

    /// <summary>Whether a missing cover for [key] gets a made one: a song's cover key (catalog "it", Jellyfin "jf", NAS "nf"), never an outside picture's address.</summary>
    public static bool ForKey(string key) => key.StartsWith("it", StringComparison.Ordinal) || key.StartsWith("jf", StringComparison.Ordinal) || key.StartsWith("nf", StringComparison.Ordinal);

    /// <summary>The artist and title a cover key names (a catalog key, or a NAS folder key); empty when it names none (a Jellyfin key).</summary>
    public static (string Artist, string Title) Names(string key)
    {
        if (Art.ParseLookupKey(key) is var (a, al, t)) return (a, t.Length > 0 ? t : al);
        if (key.StartsWith("nf"))
        {
            try
            {
                var b = key[2..].Replace('-', '+').Replace('_', '/');
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b + new string('=', (4 - b.Length % 4) % 4))).Split('\n');
                if (parts.Length >= 4) return (parts[1], parts[3].Length > 0 ? parts[3] : parts[2]);
            }
            catch (FormatException) { }
        }
        return ("", "");
    }

    /// <summary>The two colours of the fade, picked by the song's name (the same for the SVG and the PNG).</summary>
    public static (string From, string To) ColoursFor(string artist, string title) => Colours[(int)((uint)StableHash(artist + "\n" + title) % (uint)Colours.Length)];
    /// <summary>The big letters: the first letters of the artist's first two words (the title's when there is no artist).</summary>
    public static string Initials(string artist, string title) =>
        string.Concat((artist.Length > 0 ? artist : title).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    public static string Svg(string artist, string title)
    {
        var (from, to) = ColoursFor(artist, title);
        var initials = Initials(artist, title);
        static string X(string s) => System.Security.SecurityElement.Escape(s.Length > 40 ? s[..39] + "…" : s);
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 240 240\" width=\"240\" height=\"240\">");
        sb.Append($"<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"{from}\"/><stop offset=\"1\" stop-color=\"{to}\"/></linearGradient></defs>");
        sb.Append("<rect width=\"240\" height=\"240\" fill=\"url(#g)\"/>");
        sb.Append($"<text x=\"120\" y=\"130\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"84\" font-weight=\"700\" fill=\"#ffffff\" fill-opacity=\"0.9\">{X(initials.Length > 0 ? initials : "♪")}</text>");
        if (title.Length > 0) sb.Append($"<text x=\"16\" y=\"196\" font-family=\"sans-serif\" font-size=\"17\" font-weight=\"600\" fill=\"#ffffff\">{X(title)}</text>");
        if (artist.Length > 0) sb.Append($"<text x=\"16\" y=\"220\" font-family=\"sans-serif\" font-size=\"14\" fill=\"#ffffff\" fill-opacity=\"0.75\">{X(artist)}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    static int StableHash(string s) { var h = 17; foreach (var c in s) h = unchecked(h * 31 + c); return h; }
}
