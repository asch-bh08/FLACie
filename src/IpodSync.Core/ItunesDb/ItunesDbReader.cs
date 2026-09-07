using System.Text;
using static IpodSync.Core.ItunesDb.BinaryIo;

namespace IpodSync.Core.ItunesDb;

/// <summary>
/// Reads the iTunesDB binary format.
///
/// The format is a tree of chunks. Every chunk starts with a 4-byte ASCII magic
/// and a 32-bit header length; most also carry a 32-bit total length covering
/// their children. All integers are little-endian.
///
/// Deliberately tolerant: we navigate purely by the length fields and decode only
/// the fields we are confident about, recording any magic we did not recognise in
/// <see cref="ItunesDatabase.UnknownChunks"/>. Field offsets drifted slightly
/// across iPod generations, so anything past a chunk's declared header length is
/// treated as absent rather than misread.
/// </summary>
public static class ItunesDbReader
{
    /// <summary>Mac OS classic epoch: timestamps are seconds since 1904-01-01 UTC.</summary>
    private static readonly DateTimeOffset MacEpoch = new(1904, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static ItunesDatabase Read(string path) => Read(File.ReadAllBytes(path));

    public static ItunesDatabase Read(byte[] d)
    {
        if (d.Length < 0x20 || Magic(d, 0) != "mhbd")
            throw new InvalidDataException(
                $"Not an iTunesDB: expected 'mhbd' magic, found '{(d.Length >= 4 ? Magic(d, 0) : "<empty>")}'.");

        int compressedLength = d.Length;
        d = Inflate(d);

        var db = new ItunesDatabase { WasCompressed = d.Length != compressedLength };
        int hdrLen = I32(d, 0x04);
        db.Version = I32(d, 0x10);
        int numChildren = I32(d, 0x14);
        db.LibraryPersistentId = U64(d, 0x18);

        // The meaning of an mhsd's type number drifted between DB versions (at
        // version 115 type 4 holds albums and type 3 holds the playlists, which is
        // not what older documentation says), so dispatch on the magic of the list
        // chunk inside it instead. A DB may carry several playlist lists, some of
        // them duplicates, so collect and de-duplicate rather than trusting one.
        var playlistSets = new List<List<Playlist>>();

        int pos = hdrLen;
        for (int i = 0; i < numChildren && pos + 0x10 <= d.Length; i++)
        {
            string magic = Magic(d, pos);
            if (magic != "mhsd") { db.UnknownChunks.Add(magic); break; }

            int sHdr = I32(d, pos + 0x04);
            int sTotal = I32(d, pos + 0x08);
            int sType = I32(d, pos + 0x0C);
            if (sHdr <= 0 || sTotal <= 0) break;

            int inner = pos + sHdr;
            switch (Magic(d, inner))
            {
                case "mhlt":
                    ReadTrackList(d, inner, db);
                    break;
                case "mhlp":
                    playlistSets.Add(ReadPlaylistList(d, inner, db));
                    break;
                default:
                    db.UnknownChunks.Add($"{Magic(d, inner)} (mhsd type {sType})");
                    break;
            }
            pos += sTotal;
        }

        var seen = new HashSet<ulong>();
        foreach (var p in playlistSets.SelectMany(s => s))
            if (p.PersistentId == 0 || seen.Add(p.PersistentId))
                db.Playlists.Add(p);

        return db;
    }

    private static void ReadTrackList(byte[] d, int start, ItunesDatabase db)
    {
        if (Magic(d, start) != "mhlt") { db.UnknownChunks.Add(Magic(d, start)); return; }
        int hdrLen = I32(d, start + 0x04);
        int count = I32(d, start + 0x08);

        int pos = start + hdrLen;
        for (int i = 0; i < count && pos + 0x0C <= d.Length; i++)
        {
            if (Magic(d, pos) != "mhit") { db.UnknownChunks.Add(Magic(d, pos)); break; }
            int total = I32(d, pos + 0x08);
            if (total <= 0) break;
            db.Tracks.Add(ReadTrack(d, pos, db));
            pos += total;
        }
    }

    private static Track ReadTrack(byte[] d, int p, ItunesDatabase db)
    {
        int hdrLen = I32(d, p + 0x04);
        int numMhods = I32(d, p + 0x0C);

        var t = new Track
        {
            Id           = (uint)Fld(d, p, 0x10, hdrLen),
            SizeBytes    = Fld(d, p, 0x24, hdrLen),
            LengthMs     = Fld(d, p, 0x28, hdrLen),
            TrackNumber  = Fld(d, p, 0x2C, hdrLen),
            TotalTracks  = Fld(d, p, 0x30, hdrLen),
            Year         = Fld(d, p, 0x34, hdrLen),
            Bitrate      = Fld(d, p, 0x38, hdrLen),
            PlayCount    = Fld(d, p, 0x50, hdrLen),
            DiscNumber   = Fld(d, p, 0x5C, hdrLen),
            TotalDiscs   = Fld(d, p, 0x60, hdrLen),
            // Sample rate lives in the high 16 bits of a fixed-point word.
            SampleRate   = Fld(d, p, 0x3C, hdrLen) >> 16,
            LastModified = MacTime(Fld(d, p, 0x20, hdrLen)),
            LastPlayed   = MacTime(Fld(d, p, 0x58, hdrLen)),
        };

        if (0x1C + 4 <= hdrLen)
        {
            t.Compilation = d[p + 0x1C + 2] != 0;
            t.Stars = d[p + 0x1C + 3] / 20;   // stored 0-100 in steps of 20
        }
        if (0x70 + 8 <= hdrLen) t.PersistentId = U64(d, p + 0x70);

        int pos = p + hdrLen;
        for (int i = 0; i < numMhods && pos + 0x10 <= d.Length; i++)
        {
            if (Magic(d, pos) != "mhod") { db.UnknownChunks.Add(Magic(d, pos)); break; }
            int total = I32(d, pos + 0x08);
            if (total <= 0) break;

            var type = (MhodType)I32(d, pos + 0x0C);
            string? s = ReadMhodString(d, pos);
            switch (type)
            {
                case MhodType.Title:       t.Title = s; break;
                case MhodType.Location:    t.Location = s; break;
                case MhodType.Album:       t.Album = s; break;
                case MhodType.Artist:      t.Artist = s; break;
                case MhodType.AlbumArtist: t.AlbumArtist = s; break;
                case MhodType.Genre:       t.Genre = s; break;
                case MhodType.Composer:    t.Composer = s; break;
                case MhodType.Comment:     t.Comment = s; break;
                case MhodType.FileType:    t.FileTypeDescription = s; break;
            }
            pos += total;
        }
        return t;
    }

    private static List<Playlist> ReadPlaylistList(byte[] d, int start, ItunesDatabase db)
    {
        var result = new List<Playlist>();
        if (Magic(d, start) != "mhlp") { db.UnknownChunks.Add(Magic(d, start)); return result; }

        int hdrLen = I32(d, start + 0x04);
        int count = I32(d, start + 0x08);

        int pos = start + hdrLen;
        for (int i = 0; i < count && pos + 0x0C <= d.Length; i++)
        {
            if (Magic(d, pos) != "mhyp") { db.UnknownChunks.Add(Magic(d, pos)); break; }
            int total = I32(d, pos + 0x08);
            if (total <= 0) break;
            result.Add(ReadPlaylist(d, pos, db));
            pos += total;
        }
        return result;
    }

    private static Playlist ReadPlaylist(byte[] d, int p, ItunesDatabase db)
    {
        int hdrLen = I32(d, p + 0x04);
        int numMhods = I32(d, p + 0x0C);
        int numItems = I32(d, p + 0x10);

        var pl = new Playlist
        {
            IsMaster = hdrLen > 0x14 && d[p + 0x14] != 0,
            Created  = MacTime(Fld(d, p, 0x18, hdrLen)),
        };
        if (0x1C + 8 <= hdrLen) pl.PersistentId = U64(d, p + 0x1C);

        // Children are the string/rule mhods followed by one mhip per track, but
        // we dispatch on magic rather than trusting the order.
        int pos = p + hdrLen;
        for (int i = 0; i < numMhods + numItems && pos + 0x10 <= d.Length; i++)
        {
            string magic = Magic(d, pos);
            int total = I32(d, pos + 0x08);
            if (total <= 0) break;

            if (magic == "mhod")
            {
                var type = (MhodType)I32(d, pos + 0x0C);
                if (type == MhodType.Title) pl.Name = ReadMhodString(d, pos);
                else if (type is MhodType.SmartPlaylistData or MhodType.SmartPlaylistRules) pl.IsSmart = true;
            }
            else if (magic == "mhip")
            {
                int ihdr = I32(d, pos + 0x04);
                if (0x18 + 4 <= ihdr) pl.TrackIds.Add((uint)I32(d, pos + 0x18));
            }
            else
            {
                db.UnknownChunks.Add(magic);
                break;
            }
            pos += total;
        }
        return pl;
    }

    /// <summary>
    /// String mhods store, after the header: position, byte length, a word that is
    /// commonly documented as an encoding flag, a reserved word, then the bytes.
    /// </summary>
    private static string? ReadMhodString(byte[] d, int p)
    {
        int hdrLen = I32(d, p + 0x04);
        int total = I32(d, p + 0x08);
        int dataStart = p + hdrLen + 16;
        if (dataStart > d.Length || p + hdrLen + 8 > d.Length) return null;

        int len = I32(d, p + hdrLen + 4);

        // Clamp to what the chunk actually claims to hold.
        int available = Math.Min(d.Length - dataStart, p + total - dataStart);
        if (len <= 0 || available <= 0) return null;
        len = Math.Min(len, available);

        return DecodeString(d, dataStart, len);
    }

    /// <summary>
    /// Picks the text encoding from the bytes themselves. The word following the
    /// byte length is often described as an encoding flag, but real devices set it
    /// to 1 while storing UTF-16LE, so trusting it renders every string with a gap
    /// between characters.
    /// </summary>
    private static string DecodeString(byte[] d, int start, int len)
    {
        // A UTF-16 payload always occupies an even number of bytes.
        if ((len & 1) != 0) return Encoding.UTF8.GetString(d, start, len);

        // Latin text in UTF-16LE leaves a NUL in every second byte.
        int probe = Math.Min(len, 32);
        for (int i = 1; i < probe; i += 2)
            if (d[start + i] == 0) return Encoding.Unicode.GetString(d, start, len);

        // No NULs: either genuine UTF-8, or UTF-16 holding non-Latin characters.
        // Strict UTF-8 will reject the latter, so use it as the discriminator.
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(d, start, len); }
        catch (DecoderFallbackException) { return Encoding.Unicode.GetString(d, start, len); }
    }

    // --- primitives -------------------------------------------------------
    // Magic/I32/U64/Inflate come from BinaryIo (shared with the raw chunk tree
    // the writer round-trips through) via the `using static` import above.

    /// <summary>Read a field only if the chunk's declared header is long enough to contain it.</summary>
    private static int Fld(byte[] d, int chunk, int off, int hdrLen) =>
        off + 4 <= hdrLen ? I32(d, chunk + off) : 0;

    private static DateTimeOffset? MacTime(int seconds) =>
        seconds <= 0 ? null : MacEpoch.AddSeconds(seconds);
}
