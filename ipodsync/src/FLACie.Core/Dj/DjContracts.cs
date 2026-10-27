namespace FLACie.Core;

/// <summary>Where a song is playing, for the DJ's "Where" ticks. Android Auto counts as <see cref="Auto"/>, not <see cref="Phone"/>, while the phone is in the car.</summary>
public enum DjSurface { Phone, Auto, Web, Windows }

/// <summary>How often the DJ speaks: Rare is about 1 song in 7, Often about 1 in 3, Every before every song it may talk over (it uses the most of the daily cap).</summary>
public enum DjChatty { Rare, Often, Every }

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
    /// <summary>The old "backup voice" switch, kept so older apps can still send it. It no longer does anything: the DJ keeps to one voice, and when that voice
    /// can't make a clip it stays quiet rather than switch to the device's own (see <see cref="DeviceVoice"/>).</summary>
    public bool Backup { get; set; } = true;
    /// <summary>The person chose the device's own voice (Android TextToSpeech, the browser's speech) as the DJ's voice: every line is said by it and no clip
    /// is made. Off (the default): only the server's voice, never the device's.</summary>
    public bool DeviceVoice { get; set; }
    /// <summary>How loud the DJ's voice is, 0-100 % of the music's volume.</summary>
    public int Volume { get; set; } = DefaultVolume;
    /// <summary>How loud the music stays under the DJ, 0-100 %. 20 % since 2026-10-27 (it was 30 %, and the voice got lost in busy songs).</summary>
    public int Duck { get; set; } = DefaultDuck;
    public const int DefaultVolume = 90, DefaultDuck = 20, OldDefaultDuck = 30;
    /// <summary>1 once <see cref="Duck"/> has been looked at after the default changed: someone still on the old 30 % was moved to the new default then
    /// (<see cref="MoveOldDuck"/>), and a 30 % chosen after that is kept.</summary>
    public int LevelsVersion { get; set; }
    /// <summary>Show the DJ's words on screen while it talks (Now Playing and the player bar).</summary>
    public bool Captions { get; set; } = true;
    /// <summary>A short soft swoosh just before the DJ speaks.</summary>
    public bool Cue { get; set; } = true;

    /// <summary>Someone still on the old "Music under the DJ" default moves to the new one, once. True when it changed anything.</summary>
    public static bool MoveOldDuck(DjSettings s)
    {
        if (s.LevelsVersion >= 1) return false;
        s.LevelsVersion = 1;
        if (s.Duck == OldDefaultDuck) s.Duck = DefaultDuck;
        return true;
    }
    /// <summary>Per song, by <see cref="Matching.MatchKey(string, string)"/> (title and artist, so it holds on every device and path): true = always introduce it,
    /// false = never talk over it; no entry = the usual rules.</summary>
    public Dictionary<string, bool> Songs { get; set; } = new();
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

    /// <summary>A song's key in <see cref="DjSettings.Songs"/>: its match key (title and artist); "" when it has no title.</summary>
    public static string SongKey(string title, string artist) => title.Trim().Length == 0 ? "" : Matching.MatchKey(title, artist);

    /// <summary>The person's choice for this song: true = always introduce it, false = never talk over it, null = the usual rules.</summary>
    public static bool? SongChoice(DjSettings s, string title, string artist) =>
        SongKey(title, artist) is { Length: > 0 } k && s.Songs is { } songs && songs.TryGetValue(k, out var v) ? v : null;

    /// <summary>Sets or (null) clears the choice for a song.</summary>
    public static void SetSong(DjSettings s, string title, string artist, bool? choice)
    {
        var k = SongKey(title, artist);
        if (k.Length == 0) return;
        s.Songs ??= new();
        if (choice is { } c) s.Songs[k] = c; else s.Songs.Remove(k);
    }

    public static int Clamp(int pc) => Math.Clamp(pc, 0, 100);
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
    /// <summary>The player had (or was waiting for) a line before [song] and did not say it, and [why] (timing, a skip): written to the log, so a quiet DJ can be read there.</summary>
    void NotSaid(UserSession s, string song, string why) { }
}
