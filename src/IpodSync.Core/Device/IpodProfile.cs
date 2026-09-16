using System.Text;

namespace IpodSync.Core.Device;

/// <summary>How the device stores its library.</summary>
public enum IpodDbFormat
{
    Unknown,
    /// <summary>Plain <c>iTunesDB</c> (iPod 1G–5G, mini, nano 1G–3G, classic).</summary>
    ItunesDb,
    /// <summary>zlib-compressed <c>iTunesCDB</c> (nano 4G onwards, classic 2009).</summary>
    ItunesCdb,
    /// <summary>iPod shuffle: a flat <c>iTunesSD</c> list, no iTunesDB.</summary>
    ShuffleSd,
    /// <summary>iOS (iPod touch/iPhone): <c>iTunes_Control</c> plus MediaLibrary.sqlitedb.</summary>
    IosMediaLibrary,
}

/// <summary>Which checksum the firmware expects over the database. Named after the header
/// offset each one lives at, the way libgpod names them.</summary>
public enum IpodSignature
{
    /// <summary>Nothing to sign (everything before the 2007/2008 models).</summary>
    None,
    /// <summary>hash58 only: HMAC-SHA1 keyed from the FirewireGuid (nano 4G, classic 2008).</summary>
    Hash58,
    /// <summary>hash58 + hash72 (nano 5G, classic 2009): hash72 is AES-CBC over the SHA-1.</summary>
    Hash58AndHash72,
    /// <summary>hashAB (nano 6G/7G, shuffle 4G): not implemented — see COMPATIBILITY.md.</summary>
    HashAb,
    /// <summary>The header asks for a scheme this build doesn't know.</summary>
    Unknown,
}

/// <summary>What this app can do with a particular device, worked out from the device's own
/// files (never from a guess about the model).</summary>
public sealed record IpodProfile(
    string Root,
    IpodDbFormat Format,
    IpodSignature Signature,
    int SchemeField,
    bool HasSqliteBundle,
    bool HasArtworkDb,
    string? ModelNumber,
    string? ModelName,
    string? Serial,
    string? FirewireGuid,
    bool CanRead,
    bool CanWrite,
    string Summary,
    IReadOnlyList<string> Notes);

/// <summary>
/// Inspects a mounted device and reports what it is and what is supported. This is the single
/// place that decides "we can write to this one", so an unfamiliar iPod is refused before the
/// write pipeline is ever asked to sign something it doesn't understand.
/// </summary>
public static class IpodProfiler
{
    public static IpodProfile Inspect(string root)
    {
        var notes = new List<string>();
        string control = Path.Combine(root, "iPod_Control");
        string iosControl = Path.Combine(root, "iTunes_Control");
        bool ios = !Directory.Exists(control) && Directory.Exists(iosControl);
        string itunes = Path.Combine(ios ? iosControl : control, "iTunes");

        string plain = Path.Combine(itunes, "iTunesDB");
        string packed = Path.Combine(itunes, "iTunesCDB");
        string sd = Path.Combine(itunes, "iTunesSD");
        string itlp = Path.Combine(itunes, "iTunes Library.itlp");
        string mediaLibrary = Path.Combine(itunes, "MediaLibrary.sqlitedb");

        var (model, serial, fwid) = ReadSysInfo(ios ? iosControl : control);
        string? modelName = model is null ? null : IpodModels.Name(model);
        bool hasItlp = File.Exists(Path.Combine(itlp, "Library.itdb"));
        bool hasArt = File.Exists(Path.Combine(Path.GetDirectoryName(itunes)!, "Artwork", "ArtworkDB"));

        IpodDbFormat format =
            File.Exists(mediaLibrary) || (ios && !File.Exists(plain) && !File.Exists(packed)) ? IpodDbFormat.IosMediaLibrary
            : File.Exists(packed) ? IpodDbFormat.ItunesCdb
            : File.Exists(plain) ? IpodDbFormat.ItunesDb
            : File.Exists(sd) ? IpodDbFormat.ShuffleSd
            : IpodDbFormat.Unknown;

        var signature = IpodSignature.None;
        int scheme = 0;
        if (format is IpodDbFormat.ItunesCdb or IpodDbFormat.ItunesDb)
        {
            string dbPath = format == IpodDbFormat.ItunesCdb ? packed : plain;
            try
            {
                byte[] head = ReadHead(dbPath, 0x100);
                (signature, scheme) = ReadSignatureScheme(head);
            }
            catch (IOException) { notes.Add("the database couldn't be read to check its signature"); }
        }

        bool canRead = format is IpodDbFormat.ItunesCdb or IpodDbFormat.ItunesDb;
        bool canWrite = canRead && signature is IpodSignature.None or IpodSignature.Hash58 or IpodSignature.Hash58AndHash72;

        string summary = format switch
        {
            IpodDbFormat.IosMediaLibrary => "iPod touch / iOS device — a different protocol and a different library format; not supported.",
            IpodDbFormat.ShuffleSd => "iPod shuffle — its library is a flat iTunesSD list, which this app doesn't read or write yet.",
            IpodDbFormat.Unknown => "No iPod library found here.",
            _ when signature == IpodSignature.HashAb => "This iPod signs its database with hashAB (nano 6G/7G, shuffle 4G). Reading works; writing is refused because that signature can't be produced.",
            _ when signature == IpodSignature.Unknown => $"This iPod asks for signature scheme {scheme}, which this build doesn't know. Reading works; writing is refused.",
            _ => signature switch
            {
                IpodSignature.None => "Classic iPod with an unsigned database — reading and writing supported.",
                IpodSignature.Hash58 => "Signed with hash58 (FirewireGuid) — reading and writing supported.",
                _ => "Signed with hash58 + hash72 — reading and writing supported.",
            },
        };

        if (format == IpodDbFormat.IosMediaLibrary)
            notes.Add("iOS devices don't mount as a disk; iTunes talks to them over a pairing/AFC protocol, and the music library is Apple's own SQLite database.");
        if (signature is IpodSignature.HashAb)
            notes.Add("hashAB has never been reimplemented in open source — libgpod loads a binary blob for it.");
        if (canWrite && !hasItlp && format == IpodDbFormat.ItunesCdb)
            notes.Add("no SQLite library bundle on this device: only the classic database is written.");
        if (canWrite && !hasArt)
            notes.Add("no artwork database on this device: cover art can't be set.");

        return new IpodProfile(root, format, signature, scheme, hasItlp, hasArt, model, modelName, serial, fwid,
            canRead, canWrite, summary, notes);
    }

    /// <summary>mhbd's scheme field (+0x30) says which checksum the firmware expects; the
    /// signature fields themselves confirm it. Unknown values are reported, never assumed.</summary>
    public static (IpodSignature Signature, int Scheme) ReadSignatureScheme(byte[] header)
    {
        if (header.Length < 0xA0 || Encoding.ASCII.GetString(header, 0, 4) != "mhbd") return (IpodSignature.None, 0);
        int scheme = BitConverter.ToUInt16(header, 0x30);
        bool hash58 = !AllZero(header, 0x58, 20);
        bool hash72 = header.Length >= 0xA0 && !AllZero(header, 0x72, 46);
        return scheme switch
        {
            0 => (IpodSignature.None, scheme),
            1 or 2 when hash72 => (IpodSignature.Hash58AndHash72, scheme),
            1 or 2 when hash58 => (IpodSignature.Hash58, scheme),
            1 or 2 => (IpodSignature.None, scheme),
            3 => (IpodSignature.HashAb, scheme),
            _ => (IpodSignature.Unknown, scheme),
        };
    }

    private static bool AllZero(byte[] b, int offset, int length)
    {
        for (int i = offset; i < offset + length && i < b.Length; i++) if (b[i] != 0) return false;
        return true;
    }

    private static byte[] ReadHead(string path, int count)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] buffer = new byte[count];
        int read = fs.Read(buffer, 0, count);
        return read == count ? buffer : buffer[..Math.Max(read, 0)];
    }

    /// <summary>SysInfo is "Key: value" text; SysInfoExtended is a plist. Both are optional —
    /// SysInfoExtended only appears once iTunes has talked to the device.</summary>
    private static (string? Model, string? Serial, string? Firewire) ReadSysInfo(string controlDir)
    {
        string? model = null, serial = null, fw = null;
        string sysInfo = Path.Combine(controlDir, "Device", "SysInfo");
        try
        {
            if (File.Exists(sysInfo))
                foreach (var line in File.ReadLines(sysInfo))
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string key = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
                    if (key.Equals("ModelNumStr", StringComparison.OrdinalIgnoreCase)) model = value;
                    else if (key.Equals("pszSerialNumber", StringComparison.OrdinalIgnoreCase) || key.Equals("SerialNumber", StringComparison.OrdinalIgnoreCase)) serial = value;
                    else if (key.Equals("FirewireGuid", StringComparison.OrdinalIgnoreCase)) fw = value;
                }
        }
        catch (IOException) { }

        string extended = Path.Combine(controlDir, "Device", "SysInfoExtended");
        try
        {
            if (File.Exists(extended))
            {
                string xml = File.ReadAllText(extended);
                model ??= PlistString(xml, "ModelNumber");
                serial ??= PlistString(xml, "SerialNumber");
                fw ??= PlistString(xml, "FireWireGUID");
            }
        }
        catch (IOException) { }
        return (model, serial, fw);
    }

    /// <summary>Pulls one &lt;key&gt;name&lt;/key&gt;&lt;string&gt;value&lt;/string&gt; pair out of a plist,
    /// without taking a dependency on a plist parser for two fields.</summary>
    private static string? PlistString(string xml, string key)
    {
        int k = xml.IndexOf($"<key>{key}</key>", StringComparison.OrdinalIgnoreCase);
        if (k < 0) return null;
        int open = xml.IndexOf("<string>", k, StringComparison.OrdinalIgnoreCase);
        int close = open < 0 ? -1 : xml.IndexOf("</string>", open, StringComparison.OrdinalIgnoreCase);
        return open < 0 || close < 0 ? null : xml[(open + 8)..close].Trim();
    }
}
