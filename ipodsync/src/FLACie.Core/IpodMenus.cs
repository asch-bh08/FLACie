namespace FLACie.Core;

/// <summary>What the Playback and About pages show (the page reads these from the player and the library).</summary>
public sealed record IpodPlayback(bool Shuffle, int Repeat, double Speed, int SleepMinutes, int FadeSeconds, bool EvenVolume, int Songs, int Playlists, string Version);

/// <summary>
/// The menu trees of the iPod mode, as the Android emulator builds them per model family, as plain rows (see <see cref="IpodRow"/>): ids starting with "m:" open a menu or act,
/// "cyc:" cycles a setting, "tgl:" toggles one. Pure and unit-tested; the page gives the rows meaning.
/// </summary>
public static class IpodMenus
{
    public const string RootTitle = "iPod";
    static readonly string[] RepeatNames = ["Off", "All", "One"];

    /// <summary>The main menu of a family (hidden entries left out, "Now Playing" only while something is queued, as on the real device).</summary>
    public static IReadOnlyList<IpodRow> Root(IpodFamily family, IReadOnlySet<string> hidden, bool nowPlaying, bool anyMusic = true)
    {
        var all = RootAll(family);
        var rows = new List<IpodRow>();
        if (!anyMusic) rows.Add(new IpodRow("No Music", "m:none", false));
        rows.AddRange(all.Where(r => !hidden.Contains(r.Label) || IpodPrefs.FixedMenu.Contains(r.Label)));
        if (nowPlaying) rows.Add(new IpodRow("Now Playing", "m:np"));
        return rows;
    }

    /// <summary>Every entry the family's main menu can hold (what Settings &gt; Main Menu lists, minus the fixed ones).</summary>
    public static IReadOnlyList<IpodRow> RootAll(IpodFamily family) => family switch
    {
        IpodFamily.Mono => [new("Playlists", "m:pl"), new("Browse", "m:browse"), new("Extras", "m:extras"), new("Settings", "m:settings"), new("Shuffle Songs", "m:shuffle", false)],
        IpodFamily.Video => [new("Music", "m:music"), new("Cover Flow", "m:cover"), new("Extras", "m:extras"), new("Settings", "m:settings"), new("Shuffle Songs", "m:shuffle", false)],
        _ => [new("Music", "m:music"), new("Cover Flow", "m:cover"), new("Extras", "m:extras"), new("Settings", "m:settings"), new("Shuffle Songs", "m:shuffle", false)],
    };

    public static string MusicTitle(IpodFamily family) => family == IpodFamily.Mono ? "Browse" : "Music";

    public static IReadOnlyList<IpodRow> Music(IpodFamily family) => family == IpodFamily.Mono
        ? [new("Artists", "m:artists"), new("Albums", "m:albums"), new("Songs", "m:songs"), new("Genres", "m:genres")]
        : [new("Playlists", "m:pl"), new("Artists", "m:artists"), new("Albums", "m:albums"), new("Songs", "m:songs"), new("Genres", "m:genres")];

    /// <summary>Extras: the clock and the stopwatch of the iPod OS, and the voice memos (there are none on the web).</summary>
    public static IReadOnlyList<IpodRow> Extras() => [new("Clock", "m:clock", false), new("Stopwatch", "m:stopwatch", false), new("Voice Memos", "m:memos")];

    public static IReadOnlyList<IpodRow> Settings() =>
    [
        new("Appearance", "m:appearance"), new("Playback", "m:playback"), new("Controls", "m:controls"), new("Main Menu", "m:mainmenu"), new("About", "m:about"), new("Account", "m:account", false), new("FLACie Settings", "m:flacie", false), new("Reset Settings", "m:reset"),
    ];

    public static IReadOnlyList<IpodRow> Appearance(IpodPrefs p) =>
    [
        new("Model", "m:models", Value: p.CurrentModel.Name), new("Colour", "m:colors", Value: p.CurrentColor.Name), new("Look", "cyc:look", false, IpodPrefs.LookNames[p.Look]),
        new("Display", "cyc:clock", false, p.Clock ? "Time in Title" : "Title"),
    ];

    /// <summary>The model picker: the four groups, like the Android picker.</summary>
    public static IReadOnlyList<IpodRow> ModelGroups() => IpodModels.Groups.Select(g => new IpodRow(g == IpodModels.Classic ? "iPod / classic" : g == IpodModels.TouchGroup ? "iPod touch" : g == IpodModels.Mini ? "iPod mini" : "iPod nano", "m:group:" + g)).ToList();

    public static string GroupTitle(string group) => ModelGroups().First(r => r.Id == "m:group:" + group).Label;

    public static IReadOnlyList<IpodRow> Models(string group, string currentId) =>
        IpodModels.InGroup(group).Select(m => new IpodRow(m.Name, "set:model:" + m.Id, false, m.Year.ToString(), m.Id == currentId)).ToList();

    public static IReadOnlyList<IpodRow> Colors(IpodModel m, int current) =>
        m.Colors.Select((c, i) => new IpodRow(c.Name, "set:color:" + i, false, null, i == current)).ToList();

    public static IReadOnlyList<IpodRow> PlaybackPage(IpodPlayback s, IpodPrefs p) =>
    [
        new("Shuffle", "cyc:shuffle", false, s.Shuffle ? "Songs" : "Off"),
        new("Repeat", "cyc:repeat", false, RepeatNames[Math.Clamp(s.Repeat, 0, 2)]),
        new("Speed", "cyc:speed", false, s.Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x"),
        new("Even Out Volume", "cyc:level", false, s.EvenVolume ? "On" : "Off"),
        new("Fade Between Songs", "cyc:fade", false, s.FadeSeconds <= 0 ? "Off" : s.FadeSeconds + " s"),
        new("Volume Limit", "cyc:volumeLimit", false, p.VolumeLimit == 100 ? "Off" : p.VolumeLimit + "%"),
        new("Sleep Timer", "cyc:sleep", false, s.SleepMinutes <= 0 ? "Off" : s.SleepMinutes + " min"),
    ];

    public static IReadOnlyList<IpodRow> Controls(IpodPrefs p) =>
    [
        new("Wheel Speed", "cyc:wheelSpeed", false, IpodPrefs.SpeedNames[p.WheelSpeed]),
        new("Wheel Accel", "cyc:wheelAccel", false, IpodPrefs.AccelNames[p.WheelAccel]),
        new("Clicker", "cyc:clicker", false, IpodPrefs.ClickerNames[p.Clicker]),
        new("Click Volume", "cyc:clickVolume", false, IpodPrefs.ClickVolumeNames[p.ClickVolume]),
    ];

    public static IReadOnlyList<IpodRow> About(IpodPlayback s) =>
    [
        new("Songs", "m:none", false, s.Songs.ToString()), new("Playlists", "m:none", false, s.Playlists.ToString()), new("Voice Memos", "m:none", false, "0"), new("Version", "m:none", false, s.Version),
    ];

    /// <summary>Main Menu: a tick per entry the family's main menu can show; the fixed ones are not listed.</summary>
    public static IReadOnlyList<IpodRow> MainMenu(IpodFamily family, IReadOnlySet<string> hidden) =>
        RootAll(family).Where(r => !IpodPrefs.FixedMenu.Contains(r.Label)).Select(r => new IpodRow(r.Label, "tgl:menu:" + r.Label, false, null, !hidden.Contains(r.Label))).ToList();

    public static IReadOnlyList<IpodRow> Reset() => [new("Cancel", "m:cancel", false), new("Reset", "m:doreset", false)];
}
