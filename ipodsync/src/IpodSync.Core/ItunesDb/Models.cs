namespace IpodSync.Core.ItunesDb;

/// <summary>Everything we could read out of an iTunesDB.</summary>
public sealed class ItunesDatabase
{
    public int Version { get; set; }

    /// <summary>True when the source was an iTunesCDB (zlib-compressed).</summary>
    public bool WasCompressed { get; set; }
    public ulong LibraryPersistentId { get; set; }
    public List<Track> Tracks { get; } = new();
    public List<Playlist> Playlists { get; } = new();

    /// <summary>Chunk magics we walked past without decoding. Useful for spotting
    /// model-specific structures we do not handle yet.</summary>
    public HashSet<string> UnknownChunks { get; } = new();

    public Playlist? MasterPlaylist => Playlists.FirstOrDefault(p => p.IsMaster);

    /// <summary>Album list (mhla) entries by list id.</summary>
    public Dictionary<uint, ListEntity> Albums { get; } = new();

    /// <summary>Artist list (mhli) entries by list id.</summary>
    public Dictionary<uint, ListEntity> Artists { get; } = new();
}

/// <summary>An mhia (album) or mhii (artist) entry. PersistentId is the id the
/// SQLite library uses for the same album/artist.</summary>
public sealed class ListEntity
{
    public uint Id { get; set; }
    public ulong PersistentId { get; set; }
    public string? Album { get; set; }
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
}

public sealed class Track
{
    /// <summary>Unique within the DB. Playlist entries reference this.</summary>
    public uint Id { get; set; }

    /// <summary>64-bit persistent id (dbid). Stable across syncs; this is what we
    /// key our own manifest against, not Id.</summary>
    public ulong PersistentId { get; set; }

    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Genre { get; set; }
    public string? Composer { get; set; }
    public string? Comment { get; set; }
    public string? FileTypeDescription { get; set; }

    /// <summary>As stored on device, colon-separated: ":iPod_Control:Music:F00:ABCD.mp3"</summary>
    public string? Location { get; set; }

    public int TrackNumber { get; set; }
    public int TotalTracks { get; set; }
    public int DiscNumber { get; set; }
    public int TotalDiscs { get; set; }
    public int Year { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int SizeBytes { get; set; }
    public int LengthMs { get; set; }
    public int PlayCount { get; set; }

    /// <summary>Stored 0-100 in steps of 20. This is the 0-5 star value.</summary>
    public int Stars { get; set; }

    public bool Compilation { get; set; }

    /// <summary>mhit +0x120 / +0x1E0: ids of this track's album (mhia) and artist (mhii).</summary>
    public uint AlbumListId { get; set; }
    public uint ArtistListId { get; set; }
    /// <summary>Resolved from the lists; 0 when the link doesn't resolve.</summary>
    public ulong AlbumPersistentId { get; set; }
    public ulong ArtistPersistentId { get; set; }
    public bool HasArtwork { get; set; }
    public uint ArtworkId { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>Device-relative path with OS separators, e.g. "iPod_Control/Music/F00/ABCD.mp3"</summary>
    public string? RelativePath => Location is null
        ? null
        : Location.TrimStart(':').Replace(':', '/');

    public TimeSpan Duration => TimeSpan.FromMilliseconds(LengthMs);

    public override string ToString() =>
        $"{Artist ?? "?"} - {Title ?? "?"}" + (Album is null ? "" : $" [{Album}]");
}

public sealed class Playlist
{
    public string? Name { get; set; }
    public ulong PersistentId { get; set; }

    /// <summary>The master playlist is the device's entire library. Every iPod has
    /// exactly one and it is not user-visible as a playlist.</summary>
    public bool IsMaster { get; set; }

    public bool IsPodcast { get; set; }

    /// <summary>True when iTunes stored smart-playlist rules (mhod 50/51). We
    /// preserve these bytes verbatim on write so smart playlists survive us.</summary>
    public bool IsSmart { get; set; }

    public DateTimeOffset? Created { get; set; }

    /// <summary>Track ids in playlist order. Resolve against ItunesDatabase.Tracks.</summary>
    public List<uint> TrackIds { get; } = new();

    public override string ToString() =>
        $"{Name ?? "?"} ({TrackIds.Count} tracks)" + (IsMaster ? " [master]" : "") + (IsSmart ? " [smart]" : "");
}
