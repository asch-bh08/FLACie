namespace FLACie.Core;

/// <summary>How long a song plays before the singing starts: [Ms] from the start of the song, how sure that is ([Confidence], 0-1) and where it came from
/// ([Source]: "lyrics" for the first line of synced lyrics, "levels" for the first big lift in the level envelope, "none" when neither said).
/// A song looked at and not known has <see cref="DjIntro.None"/>, so it is not looked at again.</summary>
public sealed record SongIntro(int Ms, double Confidence, string Source);

/// <summary>
/// Works out a song's intro, for the DJ to talk over it and stop before the vocals (docs/dj-timing-report.md). Pure, so it is tested once.
/// <para>Synced lyrics win: the first sung line's time, less <see cref="LyricsLead"/> (LRC times are often a touch late), with confidence 0.9.</para>
/// <para>Otherwise the level envelope: the song in one-second steps, and the first second from <see cref="EarliestS"/> to <see cref="LatestS"/> where the next
/// four seconds are each loud (at least <see cref="LoudShare"/> of the song's loud level, its 90th percentile second) and everything before is clearly quieter (at most <see cref="QuietShare"/> of
/// those four). The lift is placed at the first frame that reaches halfway between the two, less <see cref="LevelsLead"/>, so the guess errs early. Its
/// confidence grows with the gap: an intro at 60% of the loud part's level is the least that counts (0.5), at 20% or less it is 1.</para>
/// Anything below <see cref="MinConfidence"/> is not used, and the DJ talks as it did before.
/// </summary>
public static class DjIntro
{
    public const double MinConfidence = 0.5;
    public const int LyricsLead = 200, LevelsLead = 500;
    public const int EarliestS = 3, LatestS = 40;
    public const double LoudShare = 0.8, QuietShare = 0.6;
    /// <summary>A song looked at whose intro could not be told.</summary>
    public static readonly SongIntro None = new(0, 0, "none");

    /// <summary>From synced lyrics: the first line with words in it. Null when there are none, they are not synced, or the line comes after a minute (a mistake, or a long instrumental).</summary>
    public static SongIntro? FromLyrics(Lyrics? l)
    {
        if (l is not { Synced: true }) return null;
        var first = l.Lines.FirstOrDefault(x => x.TimeMs >= 0 && x.Text.Any(char.IsLetter));
        if (first is null || first.TimeMs > 60_000) return null;
        return new SongIntro((int)Math.Max(0, first.TimeMs - LyricsLead), 0.9, "lyrics");
    }

    /// <summary>From the level envelope (0-1 each, <paramref name="secondsPerLevel"/> apart). Null when there is no clear lift after a quieter start.</summary>
    public static SongIntro? FromLevels(ReadOnlySpan<float> levels, double secondsPerLevel)
    {
        if (levels.Length == 0 || secondsPerLevel <= 0) return null;
        var per = Math.Max(1, (int)Math.Round(1 / secondsPerLevel));
        var secs = levels.Length / per;
        if (secs < EarliestS + 4) return null;
        var m = new double[secs];
        for (var k = 0; k < secs; k++) { var s = 0.0; for (var i = 0; i < per; i++) s += levels[k * per + i]; m[k] = s / per; }
        var sorted = m.OrderBy(x => x).ToArray();
        var loud = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .9))];
        if (loud < 1e-6) return null;
        var before = 0.0;
        for (var k = 0; k < Math.Min(LatestS, secs - 4); k++)
        {
            before += m[k];
            var at = k + 1;
            if (at < EarliestS) continue;
            var after = (m[at] + m[at + 1] + m[at + 2] + m[at + 3]) / 4;
            var quiet = before / at;
            if (Math.Min(Math.Min(m[at], m[at + 1]), Math.Min(m[at + 2], m[at + 3])) < LoudShare * loud || quiet > QuietShare * after) continue;
            // where in the second before and the one after the level first reaches halfway up
            var half = (quiet + after) / 2;
            var frame = at * per;
            for (var i = Math.Max(0, (at - 1) * per); i < Math.Min(levels.Length, (at + 1) * per); i++) if (levels[i] >= half) { frame = i; break; }
            var ms = (int)Math.Max(0, frame * secondsPerLevel * 1000 - LevelsLead);
            var conf = Math.Clamp((1 - quiet / after - 0.4) / 0.4 * 0.5 + 0.5, 0, 1);
            return new SongIntro(ms, Math.Round(conf, 2), "levels");
        }
        return null;
    }

    /// <summary>The best guess: synced lyrics first, then the levels, else <see cref="None"/>.</summary>
    public static SongIntro Best(Lyrics? lyrics, ReadOnlySpan<float> levels, double secondsPerLevel) =>
        FromLyrics(lyrics) ?? FromLevels(levels, secondsPerLevel) ?? None;

    /// <summary>The intro the DJ may time itself by, in ms; null when not known or not sure enough.</summary>
    public static int? Usable(SongIntro? i) => i is { Confidence: >= MinConfidence } ? i.Ms : null;
}
