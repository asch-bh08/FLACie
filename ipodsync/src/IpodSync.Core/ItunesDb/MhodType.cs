namespace IpodSync.Core.ItunesDb;

/// <summary>
/// mhod ("data object") subtypes. Types 1-15 and 21+ carry strings; 50/51 carry
/// smart-playlist rule blobs; 52 is a playlist index; 100 is playlist column info.
/// </summary>
public enum MhodType
{
    Title = 1,
    Location = 2,
    Album = 3,
    Artist = 4,
    Genre = 5,
    FileType = 6,
    EqSetting = 7,
    Comment = 8,
    Category = 9,
    Composer = 12,
    Grouping = 13,
    Description = 14,
    PodcastEnclosureUrl = 15,
    PodcastRssUrl = 16,
    ChapterData = 17,
    Subtitle = 18,
    Show = 19,
    EpisodeNumber = 20,
    TvNetwork = 21,
    AlbumArtist = 22,
    ArtistSort = 23,
    KeywordsPodcast = 24,
    TitleSort = 27,
    AlbumSort = 28,
    AlbumArtistSort = 29,
    ComposerSort = 30,
    ShowSort = 31,
    SmartPlaylistData = 50,
    SmartPlaylistRules = 51,
    LibraryPlaylistIndex = 52,
    PlaylistColumnInfo = 100,
}
