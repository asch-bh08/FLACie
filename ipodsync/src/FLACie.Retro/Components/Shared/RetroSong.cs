using FLACie.Core;

namespace FLACie.Server.Components.Shared;

/// <summary>What the page has read about the song playing, for the listening scenes' displays: its lyrics (Lyrics is null until they arrive, or when there are
/// none) with the key of the song they belong to, and what the file is. Made again only when one of them changes.</summary>
public sealed record RetroSong(Lyrics? Lyrics, string? SongKey, MediaInfo? Info);
