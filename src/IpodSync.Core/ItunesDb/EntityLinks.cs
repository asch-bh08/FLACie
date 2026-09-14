using static IpodSync.Core.ItunesDb.BinaryIo;

namespace IpodSync.Core.ItunesDb;

/// <summary>
/// Keeps a track's album (mhla/mhia) and artist (mhli/mhii) list links correct.
/// Findings from every track on a real nano 5G (OVERNIGHT-STATUS.md):
///
/// - mhit +0x120 = album list id, +0x1E0 = artist list id; the linked entries'
///   persistent ids (+0x14) are exactly the SQLite library's album.pid / artist.pid.
/// - An album is keyed by its name and its artist entity (album artist, else track
///   artist); mhia carries mhods 200 album, 201 artist, 202 album artist, plus the
///   persistent id of a track for its artwork at +0x20. Album-less tracks link to a
///   nameless per-artist mhia (only mhod 201).
/// - An artist entry (mhii) carries mhod 300 name and, when it differs, 301 sort name.
/// - List ids share one counter with track ids (and mhit +0x1F4, always id + 3), so
///   new ids are allocated above all of them.
///
/// New entries clone a real sibling's header, then patch id / persistent id /
/// artwork track; strings use the same mhod layout every real entry uses.
/// </summary>
public static class EntityLinks
{
    public static RawChunk? AlbumList(RawChunk root) => ListOf(root, "mhla");
    public static RawChunk? ArtistList(RawChunk root) => ListOf(root, "mhli");

    private static RawChunk? ListOf(RawChunk root, string magic) =>
        root.Children.SelectMany(s => s.Children).FirstOrDefault(c => c.Magic == magic);

    public static uint NextId(RawChunk root)
    {
        uint max = 0;
        foreach (var t in RawChunkNavigation.TrackChunks(root))
        {
            max = Math.Max(max, (uint)TrackFields.GetId(t));
            if (t.Header.Length >= 0x1F8) max = Math.Max(max, (uint)I32(t.Header, 0x1F4));
        }
        foreach (var list in new[] { AlbumList(root), ArtistList(root) })
            foreach (var e in list?.Children ?? [])
                if (e.Header.Length >= 0x14) max = Math.Max(max, (uint)I32(e.Header, 0x10));
        return max + 1;
    }

    /// <summary>Points the track at the album and artist entries its strings call for,
    /// creating entries when none exist. No-op for databases without the lists.</summary>
    public static void Relink(RawChunk root, RawChunk mhit)
    {
        if (mhit.Header.Length < 0x1E4) return;
        var albums = AlbumList(root);
        var artists = ArtistList(root);
        if (albums is null || artists is null) return;

        string? title = Str(mhit, MhodType.Title), artist = Str(mhit, MhodType.Artist);
        string? album = Str(mhit, MhodType.Album), albumArtist = Str(mhit, MhodType.AlbumArtist);
        string? entityArtist = albumArtist ?? artist;

        uint artistId = 0;
        if (entityArtist is not null) artistId = FindOrCreateArtist(root, artists, entityArtist);
        uint albumId = FindOrCreateAlbum(root, albums, string.IsNullOrEmpty(album) ? null : album, artist, albumArtist, TrackFields.GetPersistentId(mhit));

        WriteI32(mhit.Header, 0x120, (int)albumId);
        WriteI32(mhit.Header, 0x1E0, (int)artistId);
    }

    private static uint FindOrCreateArtist(RawChunk root, RawChunk artists, string name)
    {
        foreach (var e in artists.Children.Where(c => c.Magic == "mhii"))
            if (EntryStr(e, 300) == name) return (uint)I32(e.Header, 0x10);

        var template = artists.Children.FirstOrDefault(c => c.Magic == "mhii")
            ?? throw new InvalidOperationException("no existing artist entry to base a new one on");
        var entry = new RawChunk { Magic = "mhii", Header = (byte[])template.Header.Clone() };
        uint id = NextId(root);
        WriteI32(entry.Header, 0x10, (int)id);
        WriteU64(entry.Header, 0x14, FreshPid(artists));
        entry.Children.Add(LibraryMutation.BuildStringMhod((MhodType)300, name));
        string sort = SortName(name);
        if (sort != name) entry.Children.Add(LibraryMutation.BuildStringMhod((MhodType)301, sort));
        artists.Children.Add(entry);
        return id;
    }

    private static uint FindOrCreateAlbum(RawChunk root, RawChunk albums, string? album, string? artist, string? albumArtist, ulong trackPid)
    {
        string? entityArtist = albumArtist ?? artist;
        foreach (var e in albums.Children.Where(c => c.Magic == "mhia"))
        {
            string? eAlbum = EntryStr(e, 200);
            string? eArtist = EntryStr(e, 202) ?? EntryStr(e, 201);
            if (eAlbum == album && eArtist == entityArtist) return (uint)I32(e.Header, 0x10);
        }

        var template = albums.Children.FirstOrDefault(c => c.Magic == "mhia" && c.Header.Length >= 0x28)
            ?? throw new InvalidOperationException("no existing album entry to base a new one on");
        var entry = new RawChunk { Magic = "mhia", Header = (byte[])template.Header.Clone() };
        uint id = NextId(root);
        WriteI32(entry.Header, 0x10, (int)id);
        WriteU64(entry.Header, 0x14, FreshPid(albums));
        WriteU64(entry.Header, 0x20, trackPid);
        if (album is not null) entry.Children.Add(LibraryMutation.BuildStringMhod((MhodType)200, album));
        if (artist is not null) entry.Children.Add(LibraryMutation.BuildStringMhod((MhodType)201, artist));
        if (albumArtist is not null && album is not null) entry.Children.Add(LibraryMutation.BuildStringMhod((MhodType)202, albumArtist));
        albums.Children.Add(entry);
        return id;
    }

    private static ulong FreshPid(RawChunk list)
    {
        var used = list.Children.Where(c => c.Header.Length >= 0x1C).Select(c => U64(c.Header, 0x14)).ToHashSet();
        while (true)
        {
            ulong pid = (ulong)Random.Shared.NextInt64(long.MinValue, long.MaxValue);
            if (pid != 0 && !used.Contains(pid)) return pid;
        }
    }

    // Same rule the SQLite sort columns follow (Itlp.ItlpSorting.SortName).
    private static string SortName(string s) => Itlp.ItlpSorting.SortName(s) ?? s;

    private static string? Str(RawChunk mhit, MhodType type) => EntryStr(mhit, (int)type);

    internal static string? EntryStr(RawChunk entry, int type)
    {
        var m = entry.Children.FirstOrDefault(c => c.Magic == "mhod" && I32(c.Header, 0x0C) == type);
        if (m is null || m.Payload.Length < 16) return null;
        int len = I32(m.Payload, 4);
        if (len < 0 || 16 + len > m.Payload.Length) return null;
        var bytes = m.Payload.AsSpan(16, len);
        return len % 2 == 1 ? System.Text.Encoding.UTF8.GetString(bytes) : System.Text.Encoding.Unicode.GetString(bytes);
    }
}
