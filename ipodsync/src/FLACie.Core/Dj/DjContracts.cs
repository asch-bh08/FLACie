namespace FLACie.Core;

/// <summary>Where a song is playing, for the DJ's "Where" ticks. Android Auto counts as <see cref="Auto"/>, not <see cref="Phone"/>, while the phone is in the car.</summary>
public enum DjSurface { Phone, Auto, Web, Windows }

/// <summary>How often the DJ speaks: Rare is about 1 song in 7, Often about 1 in 3.</summary>
public enum DjChatty { Rare, Often }

/// <summary>One person's DJ settings. Kept on the server (per account), so the phone, the car, the web and the Windows app all follow them.</summary>
public sealed class DjSettings
{
    public bool Enabled { get; set; }
    public DjChatty Chatty { get; set; } = DjChatty.Often;
    public bool Phone { get; set; } = true;
    public bool Auto { get; set; } = true;
    public bool Web { get; set; } = true;
    public bool Windows { get; set; } = true;
    /// <summary>Say the line with the device's own voice when Cloudflare can't make a clip.</summary>
    public bool Backup { get; set; } = true;
    /// <summary>Per-playlist switch: true = DJ on for that playlist, false = off; no entry = follow the rest. Key: the Jellyfin playlist id when it has one, else the app's own id.</summary>
    public Dictionary<string, bool> Playlists { get; set; } = new();

    public bool On(DjSurface s) => s switch { DjSurface.Phone => Phone, DjSurface.Auto => Auto, DjSurface.Web => Web, _ => Windows };
}

/// <summary>Which switch wins, top to bottom: the Now Playing "this queue" switch, then the playlist's switch, then On/Off with this device's "Where" tick.
/// The Android app mirrors this in Kotlin (playback/DjRules.kt); keep the two the same.</summary>
public static class DjRules
{
    public static bool EffectiveOn(DjSettings s, DjSurface surface, string? playlistId, bool? queueDj) =>
        queueDj ?? (playlistId is { Length: > 0 } && s.Playlists.TryGetValue(playlistId, out var p) ? p : s.Enabled && s.On(surface));
}

/// <summary>A song as the DJ needs it. Clients send what they have; blank fields are never spoken.</summary>
public sealed record DjSong(string Title, string Artist, string Album = "", string AlbumArtist = "", int Year = 0, string Genre = "",
    long DurationMs = 0, string Path = "", string? JellyfinId = null, int TrackNo = 0, int DiscNo = 0);

/// <summary>
/// Asked when a song starts, about the song after it. [QueueDj] is the Now Playing "DJ for this queue" switch (null = untouched);
/// [PlaylistId] is the playlist the queue came from (same key as <see cref="DjSettings.Playlists"/>). [PrevPlayedOut]: the previous song is expected to
/// play to its end (false after a skip). [SkippedRecently]: a song was skipped within 10 s in the last two changes. [Why]: the recommender's reason, if any.
/// </summary>
public sealed record DjRequest(DjSurface Surface, DjSong Next, DjSong? Prev = null, bool PrevPlayedOut = true, bool SkippedRecently = false,
    string? PlaylistId = null, bool? QueueDj = null, int? LocalHour = null, int? Weekday = null, string? Why = null);

/// <summary>What the DJ says before a song. [Text] is spoken (years in words), [Caption] is the written form for screens.
/// [ClipUrl] is a server-relative MP3 (null when no clip can be made); [Backup] = say [Text] with the device's own voice instead.</summary>
public sealed record DjCue(string Text, string Caption, string? ClipUrl, bool Backup);

/// <summary>The DJ, as the players see it. Implemented by the server (FLACie.Server/Dj); absent in apps with no server.</summary>
public interface IDjService
{
    DjSettings Settings(UserSession s);
    void Save(UserSession s, DjSettings settings);
    /// <summary>Decides whether the DJ speaks before [req.Next] and, if so, starts making the clip. Asking again about the same songs gives the same answer.</summary>
    Task<DjCue?> PrepareAsync(UserSession s, DjRequest req, CancellationToken ct = default);
    /// <summary>Cloudflare is set up, so clips can be made.</summary>
    bool VoiceReady { get; }
}
