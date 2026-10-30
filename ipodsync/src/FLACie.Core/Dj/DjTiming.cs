namespace FLACie.Core;

/// <summary>When the DJ's clip starts, as a place in the song ([ClipAtMs]), and how long the song is held back under it ([SongDelayMs]).
/// (0, 0) is how it always was: the clip and the song start together.</summary>
public sealed record DjTimingPlan(int ClipAtMs, int SongDelayMs)
{
    public static readonly DjTimingPlan Now = new(0, 0);
}

/// <summary>
/// Times the DJ like a radio presenter: talk over the song's intro and finish before the singing (docs/dj-timing-report.md). Pure; the Android app has the same
/// rule in Kotlin (playback/DjTiming.kt) with the same tests, so keep the two the same.
/// <list type="bullet">
/// <item>Intro or clip length not known: the clip starts with the song, as before.</item>
/// <item>The clip fits the intro: it starts with the song. It used to start late so it ended just before the vocals, but over a long intro that sounded like
/// the DJ talking at random ten seconds into the song (owner, 30 Oct).</item>
/// <item>The clip is longer: it starts with the song, and the song is held back (playing silently, then from its start) just enough for the line to end
/// <see cref="MarginMs"/> before the vocals, never more than <see cref="MaxSongDelayMs"/>. The clip is never cut off.</item>
/// </list>
/// </summary>
public static class DjTiming
{
    public const int MarginMs = 500, MaxSongDelayMs = 1500;
    /// <summary>How far into a song a line may still hold it back: later, the song has been heard, and holding it would silence it and start it again.</summary>
    public const int HoldWithinMs = 300;
    /// <summary>The voice's speaking rate, for a clip whose length is not known yet and for the writer's word target. Not measured: Cloudflare's clips were not
    /// to hand, so this is the rate the server's cost estimate already uses.</summary>
    public const double WordsPerSecond = 2.6;
    /// <summary>The quiet at the two ends of a clip.</summary>
    public const int ClipPadMs = 300;
    /// <summary>The fewest words worth asking the writer for: a shorter intro gets the lead-in instead.</summary>
    public const int MinWords = 6;

    public static DjTimingPlan Plan(int? introMs, int? clipMs)
    {
        if (introMs is not { } intro || clipMs is not { } clip || intro < 0 || clip <= 0) return DjTimingPlan.Now;
        if (clip + MarginMs <= intro) return DjTimingPlan.Now;
        return new(0, Math.Min(MaxSongDelayMs, clip + MarginMs - intro));
    }

    /// <summary>[plan] for a line that starts [intoSongMs] into its song (after a skip, or a clip slow to arrive): past <see cref="HoldWithinMs"/> the song
    /// has been heard and is not held back (that silenced it and started it again: "the song goes quiet and it just starts playing again").</summary>
    public static DjTimingPlan Started(DjTimingPlan plan, long intoSongMs) => intoSongMs > HoldWithinMs ? plan with { SongDelayMs = 0 } : plan;

    /// <summary>"Talk before the song": the clip at once, and the song held back (silently, then from its start) until [clipMs] and the margin have passed.
    /// No cap here, unlike <see cref="Plan"/>: the person chose to wait.</summary>
    public static DjTimingPlan Before(int clipMs) => new(0, Math.Max(0, clipMs) + MarginMs);

    /// <summary>How long [text] takes to say, roughly.</summary>
    public static int EstimateMs(string text) => (int)Math.Round(DjText.WordCount(text) / WordsPerSecond * 1000) + ClipPadMs;

    /// <summary>How many words fit an intro of [introMs], ending <see cref="MarginMs"/> before the vocals; 0 when no target is wanted (not known, too short for a
    /// line, or long enough for any line).</summary>
    public static int TargetWords(int? introMs)
    {
        if (introMs is not { } i) return 0;
        var words = (int)Math.Floor((i - MarginMs - ClipPadMs) / 1000.0 * WordsPerSecond);
        return words < MinWords || words >= DjWriter.MaxWords ? 0 : words;
    }
}
