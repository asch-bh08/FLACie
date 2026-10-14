using FLACie.Core;

namespace FLACie.Server;

/// <summary>What the iPod settings rows do to the player (shared by the wheel iPods and the touch iPods): each press goes to the next choice of that setting.</summary>
public static class IpodActions
{
    static readonly double[] SpeedSteps = [0.75, 1, 1.25, 1.5, 2, 3, 0.5];

    /// <summary>Cycles a player setting ("shuffle", "repeat", "speed", "level", "fade", "sleep") or one of the iPod's own (<see cref="IpodPrefs.Cycle"/>); false when the name means nothing.</summary>
    public static async Task<bool> CycleAsync(string name, IpodPrefs prefs, PlayerState player)
    {
        switch (name)
        {
            case "shuffle": player.ToggleShuffle(); return true;
            case "repeat": player.CycleRepeat(); return true;
            case "speed": { var i = (Array.FindIndex(SpeedSteps, s => Math.Abs(s - player.Speed) < 0.01) + 1) % SpeedSteps.Length; await player.SetSpeedAsync(SpeedSteps[i]); return true; }
            case "level": await player.SetLevelVolumeAsync(!player.LevelVolume); return true;
            case "fade": { var ch = FadeBetween.Choices.ToList(); await player.SetFadeSecsAsync(ch[(Math.Max(0, ch.IndexOf(player.FadeSecs)) + 1) % ch.Count]); return true; }
            case "sleep":
                prefs.SleepChoice = player.SleepMode == SleepMode.Off ? 1 : (prefs.SleepChoice + 1) % IpodPrefs.SleepMinutes.Length;
                if (prefs.SleepChoice == 0) await player.SetSleepAsync(SleepMode.Off); else await player.SetSleepAsync(SleepMode.Minutes, IpodPrefs.SleepMinutes[prefs.SleepChoice]);
                return true;
            default:
                if (!prefs.Cycle(name)) return false;
                if (name == "volumeLimit" && player.Volume > prefs.VolumeLimit / 100.0) await player.SetVolumeAsync(prefs.VolumeLimit / 100.0);
                return true;
        }
    }

    /// <summary>The Playback / About figures the settings rows show.</summary>
    public static IpodPlayback Info(PlayerState player, IpodPrefs prefs, Library lib, int playlists) =>
        new(player.Shuffle, player.Repeat, player.Speed, player.SleepMode == SleepMode.Off ? 0 : IpodPrefs.SleepMinutes[Math.Clamp(prefs.SleepChoice, 1, IpodPrefs.SleepMinutes.Length - 1)],
            player.FadeSecs, player.LevelVolume, lib.Songs.Count, playlists, AppInfo.Version);
}
