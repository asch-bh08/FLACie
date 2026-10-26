namespace FLACie.Core;

/// <summary>Where a song is playing, for the DJ's "Where" ticks. Android Auto counts as <see cref="Auto"/>, not <see cref="Phone"/>, while the phone is in the car.</summary>
public enum DjSurface { Phone, Auto, Web, Windows }

/// <summary>How often the DJ speaks: Rare is about 1 song in 7, Often about 1 in 3.</summary>
public enum DjChatty { Rare, Often }

/// <summary>How the DJ sounds: Classic radio (the default), Chill (softer, a little less often) or Hype (livelier, a little more often).</summary>
public enum DjStyle { Classic, Chill, Hype }

/// <summary>What the next song is to this listener, from their own listening (TasteProfile): one they love (hearted, or finished three times lately), one they
/// have never played, or not known. Only ever a fact: never guessed from the recommender's words.</summary>
public enum DjBond { Unknown, Loved, NewFind }

/// <summary>More that the server knows for certain about the next song: MusicBrainz (what kind of release, the year it first came out, the label; blank when
/// not known) and how it feels (<see cref="Vibe"/>, with the previous song's for "slowing it down"). Never spoken when missing.</summary>
public sealed record DjExtra(string ReleaseType = "", int OriginalYear = 0, string Label = "", Vibe? Vibe = null, Vibe? PrevVibe = null);

/// <summary>One person's DJ settings. Kept on the server (per account), so the phone, the car, the web and the Windows app all follow them.</summary>
public sealed class DjSettings
{
    public bool Enabled { get; set; }
    public DjChatty Chatty { get; set; } = DjChatty.Often;
    /// <summary>The tone of the lines and, a little, how often it talks. Classic for everyone who had the DJ before this setting.</summary>
    public DjStyle Style { get; set; } = DjStyle.Classic;
    public bool Phone { get; set; } = true;
    public bool Auto { get; set; } = true;
    public bool Web { get; set; } = true;
    public bool Windows { get; set; } = true;
    /// <summary>Say the line with the device's own voice when Cloudflare can't make a clip.</summary>
    public bool Backup { get; set; } = true;
    /// <summary>Keep talking when the listener skips: a skip no longer silences the next two songs (off: the DJ keeps quiet while skipping goes on).</summary>
    public bool TalkAfterSkips { get; set; } = true;
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
/// [Bond]: loved or a new find, when the client knows it for certain (the server fills it from the account's listening when it is Unknown).
/// </summary>
public sealed record DjRequest(DjSurface Surface, DjSong Next, DjSong? Prev = null, bool PrevPlayedOut = true, bool SkippedRecently = false,
    string? PlaylistId = null, bool? QueueDj = null, int? LocalHour = null, int? Weekday = null, string? Why = null, DjBond Bond = DjBond.Unknown);

/// <summary>What the DJ says before a song. [Text] is spoken (years in words), [Caption] is the written form for screens.
/// [ClipUrl] is a server-relative MP3 (null when no clip can be made); [Backup] = say [Text] with the device's own voice instead.
/// [IntroMs]: how long the song plays before the singing, when the server is sure enough (<see cref="DjIntro.Usable"/>), for <see cref="DjTiming"/>; null = not known.</summary>
public sealed record DjCue(string Text, string Caption, string? ClipUrl, bool Backup, int? IntroMs = null);

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
