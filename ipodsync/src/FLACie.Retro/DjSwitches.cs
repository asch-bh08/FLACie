using FLACie.Core;

namespace FLACie.Server;

/// <summary>The DJ's switches as the web pages change them (Settings, a playlist's chip). Which switch wins is <see cref="DjRules"/> in FLACie.Core.</summary>
public static class DjSwitches
{
    /// <summary>A playlist's key in <see cref="DjSettings.Playlists"/>: its Jellyfin id when it has one (the phone finds the same switch), else the app's own id.</summary>
    public static string KeyOf(Playlist p) => p.JellyfinId is { Length: > 0 } j ? j : p.Id;

    /// <summary>A copy to change and then save (the service's own object is never changed in place).</summary>
    public static DjSettings Copy(DjSettings s) => new()
    {
        Enabled = s.Enabled, Chatty = s.Chatty, Phone = s.Phone, Auto = s.Auto, Web = s.Web, Windows = s.Windows, Backup = s.Backup, TalkAfterSkips = s.TalkAfterSkips, Style = s.Style, Playlists = new(s.Playlists),
        DeviceVoice = s.DeviceVoice, Volume = s.Volume, Duck = s.Duck, Songs = new(s.Songs ?? new()), LevelsVersion = s.LevelsVersion, Captions = s.Captions, Cue = s.Cue,
    };

    /// <summary>A playlist's own switch: true (On), false (Off) or null (no entry: it follows the rest, "Auto").</summary>
    public static bool? OfPlaylist(DjSettings s, string key) => s.Playlists.TryGetValue(key, out var on) ? on : null;

    /// <summary>Sets a playlist's switch (null takes its entry away).</summary>
    public static void SetPlaylist(DjSettings s, string key, bool? on) { if (on is { } v) s.Playlists[key] = v; else s.Playlists.Remove(key); }

    /// <summary>The playlist chip's next choice, one tap each: Auto, On, Off, then Auto again.</summary>
    public static bool? NextChoice(bool? now) => now switch { null => true, true => false, false => null };

    public static string Label(bool? on) => on switch { true => "On", false => "Off", null => "Auto" };

    /// <summary>
    /// The hour (0-23) and weekday (0 = Sunday) where the listener is, for the DJ's time-of-day lines: UTC less the browser's offset ([browserOffsetMinutes] as
    /// Date.getTimezoneOffset gives it: minutes, positive west of Greenwich, so UTC+1 is -60). Without one (not read yet, or nonsense), this server's own clock.
    /// </summary>
    public static (int Hour, int Weekday) LocalTime(DateTime utcNow, int? browserOffsetMinutes)
    {
        var t = browserOffsetMinutes is int m and >= -18 * 60 and <= 18 * 60 ? utcNow.AddMinutes(-m) : utcNow.ToLocalTime();
        return (t.Hour, (int)t.DayOfWeek);
    }

    /// <summary>The "Where" quick presets; choosing one also turns the DJ on.</summary>
    public enum Preset { None, Everywhere, OnlyInTheCar, OnlyOnWeb }

    public static void Apply(DjSettings s, Preset p)
    {
        switch (p)
        {
            case Preset.Everywhere: s.Phone = s.Auto = s.Web = s.Windows = true; break;
            case Preset.OnlyInTheCar: s.Phone = s.Web = s.Windows = false; s.Auto = true; break;
            case Preset.OnlyOnWeb: s.Phone = s.Auto = s.Windows = false; s.Web = true; break;
        }
        // choosing where is choosing to have it, as on the phone
        if (p != Preset.None) s.Enabled = true;
    }

    /// <summary>The preset the ticks match, if any (shown as chosen).</summary>
    public static Preset PresetOf(DjSettings s) => (s.Phone, s.Auto, s.Web, s.Windows) switch
    {
        (true, true, true, true) => Preset.Everywhere,
        (false, true, false, false) => Preset.OnlyInTheCar,
        (false, false, true, false) => Preset.OnlyOnWeb,
        _ => Preset.None,
    };
}
