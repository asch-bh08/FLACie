using System.Net;
using System.Text;
using SMBLibrary;
using SMBLibrary.Client;
using SmbFileAttributes = SMBLibrary.FileAttributes;

namespace FLACie.Core;

/// <summary>A NAS login: the SMB share that holds music and, as an account, the profile file.</summary>
public sealed record NasAccount(string Host, string Share, string Folder, string User, string Password, string Domain = "")
{
    /// <summary>The same smb:// form the Android app stores, so playlists and favourites match across devices.</summary>
    public string Url(string relPath) => "smb://" + Host + "/" + Seg(Share) + "/" + string.Join('/', relPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Seg));
    static string Seg(string s) => Uri.EscapeDataString(s);
    public string ProfilePath => ".flacie/profile-" + new string((User.Length == 0 ? "guest" : User).ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray()) + ".json";
}

/// <summary>Reads a share over SMB2/3: lists music, reads ranges for streaming, reads and writes the profile file.
/// Each call opens its own connection; SMBLibrary's client isn't safe to share between threads.</summary>
public static class NasClient
{
    static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase) { "mp3", "flac", "m4a", "aac", "ogg", "opus", "wav", "aiff", "aif", "alac", "wma", "ape", "wv" };
    static readonly string[] CoverNames = ["cover", "folder", "front", "album", "albumart", "artwork"];
    static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp" };

    public static bool IsAudio(string name) => AudioExt.Contains(Path.GetExtension(name).TrimStart('.'));

    sealed class Conn : IDisposable
    {
        public readonly SMB2Client Client = new();
        public ISMBFileStore Store = null!;
        public void Dispose() { try { Store?.Disconnect(); } catch { } try { Client.Logoff(); } catch { } Client.Disconnect(); }
    }

    static Conn Open(NasAccount a)
    {
        var c = new Conn();
        var ip = IPAddress.TryParse(a.Host, out var parsed) ? parsed : Dns.GetHostAddresses(a.Host).First(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        if (!c.Client.Connect(ip, SMBTransportType.DirectTCPTransport)) { c.Dispose(); throw new IOException($"Can't reach {a.Host}"); }
        // no username: an anonymous login (what most open shares expect), then the "guest" account
        var status = a.User.Length == 0 ? c.Client.Login(new AnonymousNtlm()) : c.Client.Login(a.Domain, a.User, a.Password);
        if (status != NTStatus.STATUS_SUCCESS && a.User.Length == 0) status = c.Client.Login(a.Domain, "guest", "", AuthenticationMethod.NTLMv2);
        if (status != NTStatus.STATUS_SUCCESS) { c.Dispose(); throw new UnauthorizedAccessException("The NAS didn't accept that login."); }
        c.Store = c.Client.TreeConnect(a.Share, out status);
        if (status != NTStatus.STATUS_SUCCESS) { c.Dispose(); throw new IOException($"No share called \"{a.Share}\" on {a.Host}"); }
        return c;
    }

    static string Win(string rel) => rel.Replace('/', '\\').Trim('\\');

    static List<FileDirectoryInformation> List(Conn c, string rel)
    {
        var st = c.Store.CreateFile(out var handle, out _, Win(rel), AccessMask.GENERIC_READ, SmbFileAttributes.Directory, ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
        if (st != NTStatus.STATUS_SUCCESS) return [];
        try
        {
            c.Store.QueryDirectory(out var entries, handle, "*", FileInformationClass.FileDirectoryInformation);
            return entries?.OfType<FileDirectoryInformation>().Where(e => e.FileName is not "." and not "..").ToList() ?? [];
        }
        finally { c.Store.CloseFile(handle); }
    }

    /// <summary>Checks the login and that the music folder exists.</summary>
    public static void Test(NasAccount a)
    {
        using var c = Open(a);
        if (a.Folder.Length > 0 && List(c, a.Folder).Count == 0) throw new IOException($"Nothing found in \"{a.Folder}\"");
    }

    /// <summary>Every song under the music folder. Folder layout gives the credits: Artist/Album/file, and a loose
    /// "X - Y" file is credited by name (swapped when Y is one of the artist folders).</summary>
    public static List<Track> Scan(NasAccount a)
    {
        using var c = Open(a);
        var root = a.Folder.Trim('/');
        var top = List(c, root);
        var artists = top.Where(e => (e.FileAttributes & SmbFileAttributes.Directory) != 0).Select(e => Matching.PrimaryArtist(e.FileName)).ToHashSet();
        var outList = new List<Track>();
        void Walk(string rel, List<FileDirectoryInformation> entries, int depth, string? artist, string? album)
        {
            foreach (var e in entries)
            {
                var path = rel.Length == 0 ? e.FileName : rel + "/" + e.FileName;
                if ((e.FileAttributes & SmbFileAttributes.Directory) != 0)
                {
                    if (depth < 6) Walk(path, List(c, path), depth + 1, depth == 0 ? e.FileName : artist, depth == 1 ? e.FileName : depth == 0 ? null : album);
                    continue;
                }
                if (!IsAudio(e.FileName)) continue;
                var name = Path.GetFileNameWithoutExtension(e.FileName);
                string title = name, credit = artist ?? "";
                if (album is null && name.Contains(" - "))
                {
                    var p = name.Split(" - ", 2).Select(s => s.Trim()).ToArray();
                    if (p.All(s => s.Length > 0))
                    {
                        (credit, title) = artists.Contains(Matching.PrimaryArtist(p[1])) && !artists.Contains(Matching.PrimaryArtist(p[0])) ? (p[1], p[0]) : (p[0], p[1]);
                    }
                }
                else title = CleanTitle(name, artist, album);
                // "2Pac - Me Against the World - 09 - Dear Mama" / "09 Dear Mama": the album order
                var no = System.Text.RegularExpressions.Regex.Match(name, @"(?:^|\s-\s)(\d{1,3})\s*[-. ]");
                var trackNo = no.Success ? int.Parse(no.Groups[1].Value) : 0;
                var folder = rel;
                outList.Add(new Track(a.Url(path), title, credit, album ?? "", credit, trackNo, 0, 0, 0, "nf" + Convert.ToBase64String(Encoding.UTF8.GetBytes(folder + "\n" + credit + "\n" + (album ?? "") + "\n" + (album is null ? title : ""))).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                    e.LastWriteTime.ToUniversalTime().Ticks / 10_000 - 62_135_596_800_000, TrackSource.Nas));
            }
        }
        Walk(root, top, 0, null, null);
        return outList;
    }

    /// <summary>"01 - Artist - Album - 03 - Title" style names down to the title.</summary>
    static string CleanTitle(string name, string? artist, string? album)
    {
        // "2Pac - Me Against the World - 01 - Intro": whatever follows the track number
        var numbered = System.Text.RegularExpressions.Regex.Match(name, @"(?:^|\s-\s)\d{1,3}\s*[-.]\s+(.+)$");
        if (numbered.Success && numbered.Groups[1].Value.Trim().Length > 0) return numbered.Groups[1].Value.Trim();
        var t = name;
        foreach (var p in new[] { artist, album }) if (!string.IsNullOrEmpty(p)) t = t.Replace(p + " - ", "", StringComparison.OrdinalIgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s*\d{1,3}\s*[-.)_ ]\s*", "");
        return t.Trim().Length == 0 ? name : t.Trim();
    }

    /// <summary>The path inside the share for an smb:// URL from <see cref="NasAccount.Url"/>.</summary>
    public static string RelPath(NasAccount a, string url)
    {
        var u = new Uri(url);
        var segs = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        return string.Join('/', segs.Skip(1));
    }

    public static long Size(NasAccount a, string rel)
    {
        using var c = Open(a);
        var st = c.Store.CreateFile(out var h, out _, Win(rel), AccessMask.GENERIC_READ, SmbFileAttributes.Normal, ShareAccess.Read, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
        if (st != NTStatus.STATUS_SUCCESS) throw new FileNotFoundException(rel);
        try { c.Store.GetFileInformation(out var info, h, FileInformationClass.FileStandardInformation); return ((FileStandardInformation)info).EndOfFile; }
        finally { c.Store.CloseFile(h); }
    }

    /// <summary>Copies bytes [offset, offset+count) of a file to <paramref name="output"/> (range requests for streaming).</summary>
    public static async Task CopyRangeAsync(NasAccount a, string rel, long offset, long count, Stream output, CancellationToken ct)
    {
        using var c = Open(a);
        var st = c.Store.CreateFile(out var h, out _, Win(rel), AccessMask.GENERIC_READ, SmbFileAttributes.Normal, ShareAccess.Read, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
        if (st != NTStatus.STATUS_SUCCESS) throw new FileNotFoundException(rel);
        try
        {
            var chunk = (int)Math.Min(c.Client.MaxReadSize, 1 << 20);
            while (count > 0 && !ct.IsCancellationRequested)
            {
                st = c.Store.ReadFile(out var data, h, offset, (int)Math.Min(chunk, count));
                if (st != NTStatus.STATUS_SUCCESS && st != NTStatus.STATUS_END_OF_FILE) throw new IOException($"Read failed: {st}");
                if (data is null || data.Length == 0) break;
                await output.WriteAsync(data, ct);
                offset += data.Length; count -= data.Length;
            }
        }
        finally { c.Store.CloseFile(h); }
    }

    public static byte[]? ReadAll(NasAccount a, string rel, long max = 15_000_000)
    {
        try
        {
            var size = Size(a, rel);
            if (size > max) return null;
            using var ms = new MemoryStream();
            CopyRangeAsync(a, rel, 0, size, ms, CancellationToken.None).GetAwaiter().GetResult();
            return ms.ToArray();
        }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>The album folder's own cover (cover.jpg, folder.jpg...; else its largest image).</summary>
    public static byte[]? FolderCover(NasAccount a, string folderRel)
    {
        using var c = Open(a);
        var images = List(c, folderRel).Where(e => (e.FileAttributes & SmbFileAttributes.Directory) == 0 && ImageExt.Contains(Path.GetExtension(e.FileName).TrimStart('.'))).ToList();
        var pick = images.FirstOrDefault(e => CoverNames.Contains(Path.GetFileNameWithoutExtension(e.FileName).ToLowerInvariant())) ?? images.MaxBy(e => e.EndOfFile);
        c.Dispose();
        return pick is null ? null : ReadAll(a, (folderRel.Length == 0 ? "" : folderRel + "/") + pick.FileName);
    }

    /// <summary>The cover embedded in the folder's first song (FLAC and MP3 keep it near the start, so only the first few
    /// megabytes are read).</summary>
    public static byte[]? EmbeddedCover(NasAccount a, string folderRel)
    {
        string? first;
        using (var c = Open(a)) first = List(c, folderRel).Where(e => (e.FileAttributes & SmbFileAttributes.Directory) == 0 && IsAudio(e.FileName)).Select(e => e.FileName).Order().FirstOrDefault();
        if (first is null) return null;
        var rel = (folderRel.Length == 0 ? "" : folderRel + "/") + first;
        var size = Size(a, rel);
        using var head = new MemoryStream();
        CopyRangeAsync(a, rel, 0, Math.Min(size, 6_000_000), head, CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            using var f = TagLib.File.Create(new MemoryFile(first, head.ToArray()), TagLib.ReadStyle.None);
            return f.Tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)?.Data.Data ?? f.Tag.Pictures.FirstOrDefault()?.Data.Data;
        }
        catch { return null; }
    }

    /// <summary>The start of a file, for TagLib (tags and pictures live there; the rest is never read).</summary>
    sealed class MemoryFile(string name, byte[] data) : TagLib.File.IFileAbstraction
    {
        public string Name => name;
        public Stream ReadStream { get; } = new MemoryStream(data, writable: false);
        public Stream WriteStream => throw new NotSupportedException();
        public void CloseStream(Stream stream) { }
    }

    public static string? ReadText(NasAccount a, string rel) { var b = ReadAll(a, rel, 20_000_000); return b is null ? null : Encoding.UTF8.GetString(b); }

    /// <summary>Writes a small text file, creating its folder (the profile in .flacie/).</summary>
    public static void WriteText(NasAccount a, string rel, string text)
    {
        using var c = Open(a);
        var dir = rel.Contains('/') ? rel[..rel.LastIndexOf('/')] : "";
        if (dir.Length > 0)
        {
            var ds = c.Store.CreateFile(out var dh, out _, Win(dir), AccessMask.GENERIC_READ, SmbFileAttributes.Directory, ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN_IF, CreateOptions.FILE_DIRECTORY_FILE, null);
            if (ds == NTStatus.STATUS_SUCCESS) c.Store.CloseFile(dh);
        }
        var st = c.Store.CreateFile(out var h, out _, Win(rel), AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, SmbFileAttributes.Normal, ShareAccess.None,
            CreateDisposition.FILE_OVERWRITE_IF, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (st != NTStatus.STATUS_SUCCESS) throw new IOException($"Can't write {rel}: {st}");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            for (var off = 0; off < bytes.Length;)
            {
                var n = Math.Min(bytes.Length - off, (int)c.Client.MaxWriteSize);
                st = c.Store.WriteFile(out var written, h, off, bytes.AsSpan(off, n).ToArray());
                if (st != NTStatus.STATUS_SUCCESS) throw new IOException($"Write failed: {st}");
                off += written;
            }
        }
        finally { c.Store.CloseFile(h); }
    }
}
