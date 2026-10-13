namespace FLACie.Core;

/// <summary>What the sleep timer waits for.</summary>
public enum SleepMode { Off, Minutes, EndOfTrack, EndOfAlbum }

/// <summary>The sleep timer's arithmetic, apart from the player (the same rules as the Android app's SleepPlan.kt).</summary>
public static class SleepPlan
{
    /// <summary>The volume falls to silence over this long before the timer fires.</summary>
    public const long FadeMs = 30_000;
    public const int ExtendMinutes = 15;
    /// <summary>The extend offer applies only in the last stretch of a running timer.</summary>
    public const long ExtendWindowMs = 120_000;

    /// <summary>Gain 1..0 for the time left: full until the fade starts, then a smooth (cosine) fall.</summary>
    public static double FadeGain(long remainingMs, long fadeMs = FadeMs)
    {
        if (fadeMs <= 0 || remainingMs >= fadeMs) return 1;
        if (remainingMs <= 0) return 0;
        var x = (double)remainingMs / fadeMs;
        return Math.Clamp(0.5 - 0.5 * Math.Cos(Math.PI * x), 0, 1);
    }

    public static long? TrackRemaining(long positionMs, long durationMs) => durationMs <= 0 ? null : Math.Max(0, durationMs - positionMs);

    /// <summary>True when the song at <paramref name="index"/> is the last of its album in the play order: the next one is another album, or there is none.</summary>
    public static bool AlbumEndsAfter(IReadOnlyList<Track> playOrder, int index)
    {
        if (index < 0 || index >= playOrder.Count) return true;
        if (index + 1 >= playOrder.Count) return true;
        var cur = playOrder[index];
        if (string.IsNullOrWhiteSpace(cur.Album)) return true;   // no album name: every song is its own "album"
        return cur.AlbumKey != playOrder[index + 1].AlbumKey;
    }

    /// <summary>Time left until the end of the album: the rest of this song plus the following songs of the same album; null when a length is unknown.</summary>
    public static long? AlbumRemaining(IReadOnlyList<Track> playOrder, int index, long positionMs)
    {
        if (index < 0 || index >= playOrder.Count) return null;
        var left = TrackRemaining(positionMs, playOrder[index].DurationMs);
        if (left is null) return null;
        var i = index;
        while (!AlbumEndsAfter(playOrder, i)) { i++; var d = playOrder[i].DurationMs; if (d <= 0) return null; left += d; }
        return left;
    }

    /// <summary>The timer with more minutes: counted from what is left (or from now when it had run out), never shorter than before.</summary>
    public static DateTime Extend(DateTime endsAtUtc, DateTime nowUtc, int addMinutes = ExtendMinutes) => (endsAtUtc > nowUtc ? endsAtUtc : nowUtc).AddMinutes(addMinutes);

    public static bool CanExtend(SleepMode mode, long? remainingMs) => mode != SleepMode.Off && remainingMs is { } r && r <= ExtendWindowMs;

    public static string Label(SleepMode mode) => mode switch
    {
        SleepMode.EndOfTrack => "End of this song", SleepMode.EndOfAlbum => "End of this album", SleepMode.Minutes => "Timer", _ => "Off",
    };

    /// <summary>"14 min left", or "45 s left" in the last minute.</summary>
    public static string FormatLeft(long ms)
    {
        var s = (Math.Max(0, ms) + 999) / 1000;
        return s >= 60 ? $"{(s + 59) / 60} min left" : $"{s} s left";
    }
}

/// <summary>Playback speed rules (the same as the Android app's SpeedPolicy.kt): the steps, which items are spoken word, the place remembered in long items.</summary>
public static class SpeedPolicy
{
    public const double Min = 0.5, Max = 3.0;
    public static readonly double[] Steps = [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];
    public const long LongItemMs = 20 * 60_000L, FinishedMs = 15_000, MinResumeMs = 5_000;

    public static double Clamp(double s) => double.IsNaN(s) ? 1 : Math.Clamp(s, Min, Max);

    public static double Step(double s, bool up)
    {
        var c = Clamp(s);
        return up ? Steps.FirstOrDefault(x => x > c + 0.001, Steps[^1]) : Steps.LastOrDefault(x => x < c - 0.001, Steps[0]);
    }

    /// <summary>"1x", "1.25x", "0.5x".</summary>
    public static string Label(double s)
    {
        var c = Clamp(s);
        var t = Math.Abs(c - Math.Round(c)) < 0.001 ? ((int)Math.Round(c)).ToString() : c.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return t + "x";
    }

    static readonly string[] SpokenWords = ["audiobook", "audio book", "audio-book", "podcast", "spoken", "speech", "lecture", "sermon", "radio play", "radio drama", "hörbuch", "hoerbuch", "story", "stories", "comedy", "talk", "interview", "news"];

    public static bool IsSpoken(string? genre) => !string.IsNullOrWhiteSpace(genre) && SpokenWords.Any(w => genre.Contains(w, StringComparison.OrdinalIgnoreCase));
    public static bool IsLong(long durationMs) => durationMs >= LongItemMs;
    public static bool ShowLongItemControls(string? genre, long durationMs, double speed) => IsSpoken(genre) || IsLong(durationMs) || Math.Abs(speed - 1) > 0.001;

    /// <summary>The 32-hex Jellyfin item id when the path has one (stable across address changes), else the path.</summary>
    public static string KeyOf(string path)
    {
        var m = System.Text.RegularExpressions.Regex.Match(path, "/Audio/([0-9a-fA-F]{32})/");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : path;
    }

    public static long ResumeAt(long? savedMs, long durationMs)
    {
        if (savedMs is not { } s || !IsLong(durationMs) || s < MinResumeMs) return 0;
        if (durationMs > 0 && s >= durationMs - FinishedMs) return 0;
        return s;
    }

    public static bool ShouldSavePlace(long positionMs, long durationMs) => IsLong(durationMs) && positionMs >= MinResumeMs && positionMs < durationMs - FinishedMs;
}

/// <summary>One number per item, the newest kept when over the cap; saved as "key=value" lines (the same format as the Android app's ItemMemory).</summary>
public sealed class ItemMemory(int cap = 400)
{
    readonly LinkedList<(string Key, double Value)> items = new();
    public int Count => items.Count;
    public double? Get(string key) { foreach (var (k, v) in items) if (k == key) return v; return null; }

    public void Set(string key, double? value)
    {
        for (var n = items.First; n is not null; n = n.Next) if (n.Value.Key == key) { items.Remove(n); break; }
        if (value is null) return;
        items.AddLast((key, value.Value));
        while (items.Count > cap) items.RemoveFirst();
    }

    public string Encode() => string.Join("\n", items.Select(i => i.Key.Replace("\n", " ").Replace("=", "%3D") + "=" + i.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    public static ItemMemory Decode(string? text, int cap = 400)
    {
        var m = new ItemMemory(cap);
        if (string.IsNullOrWhiteSpace(text)) return m;
        foreach (var line in text.Split('\n'))
        {
            var i = line.LastIndexOf('=');
            if (i <= 0 || !double.TryParse(line[(i + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
            m.Set(line[..i].Replace("%3D", "="), v);
        }
        return m;
    }
}

/// <summary>How a screen reader says a time (the same wording as the Android seek bar: "1 minute 37 seconds of 5 minutes 2 seconds").</summary>
public static class TimeSpeech
{
    public static string Spoken(double seconds)
    {
        var total = (long)Math.Max(0, double.IsNaN(seconds) || double.IsInfinity(seconds) ? 0 : seconds);
        var h = total / 3600; var m = total % 3600 / 60; var s = total % 60;
        var parts = new List<string>();
        if (h > 0) parts.Add(h == 1 ? "1 hour" : $"{h} hours");
        if (m > 0) parts.Add(m == 1 ? "1 minute" : $"{m} minutes");
        if (s > 0 || parts.Count == 0) parts.Add(s == 1 ? "1 second" : $"{s} seconds");
        return string.Join(" ", parts);
    }

    public static string Position(double positionSeconds, double durationSeconds) => durationSeconds > 0 ? Spoken(positionSeconds) + " of " + Spoken(durationSeconds) : Spoken(positionSeconds);
}
