namespace FLACie.Core;

/// <summary>How a song is fetched. <see cref="Quick"/>: every source at once and the first playable copy plays (a Hi-Res copy only a little behind
/// still wins, see <see cref="SmartDownloads.GraceMs"/>); used for a click on Download, Alexa, a skip to a song that isn't here yet and the next song
/// or two of the queue. <see cref="HiRes"/>: Hi-Res copies only, falling back to quick when there is none; used for songs further down the queue,
/// Autoplay's look-ahead, imports and charts, where nobody is waiting to hear the song this minute.</summary>
public enum DownloadMode { Quick, HiRes }

/// <summary>
/// The plain rules behind smart downloads (docs/superpowers/specs/2026-11-01-smart-downloads-design.md). The owner asked for "the quickest possible
/// solution" (most of the time YouTube), "but if it's only a small difference in speed then choose Hi-Res", and for anything that arrived below
/// Hi-Res to be upgraded in the background "no matter how long it takes", the old file kept "until Hi-Res is verified and downloaded".
/// </summary>
public static class SmartDownloads
{
    /// <summary>How much longer a Hi-Res copy may take than the quick copy and still be the one that plays. The owner's examples: YouTube ready at
    /// 10 s and Hi-Res at 15 s, Hi-Res plays; Hi-Res at 20 s, YouTube plays now and the song is upgraded later.</summary>
    public const int GraceMs = 7_000;

    /// <summary>What filing a finished Soulseek file takes (the file mover's move into the library), added to the transfer's own estimate.</summary>
    public const int FilingMs = 2_000;

    /// <summary>A song at least this many places down the queue is fetched in Hi-Res: there is time, and it saves an upgrade later.</summary>
    public const int HiResPlacesAhead = 3;

    /// <summary>How long until a Hi-Res transfer is playable: its remaining bytes at its measured speed, plus the filing. Null while no speed has been
    /// measured (a peer that has not started sending can't be promised to anybody).</summary>
    public static long? EstimateMs(long remainingBytes, double bytesPerSecond, int filingMs = FilingMs) =>
        bytesPerSecond <= 0 ? null : (long)Math.Ceiling(Math.Max(0, remainingBytes) * 1000.0 / bytesPerSecond) + filingMs;

    /// <summary>The quick copy is ready: true when this Hi-Res transfer is estimated to be playable within the grace, so it is waited for (never longer
    /// than the grace itself, which the race enforces). A small file on a fast peer can win however late it started.</summary>
    public static bool WaitForHiRes(long remainingBytes, double bytesPerSecond, int graceMs = GraceMs) =>
        EstimateMs(remainingBytes, bytesPerSecond) is { } ms && ms <= graceMs;

    /// <summary>Quick or Hi-Res for a song [placesAhead] places down the queue (1 = the next song, 0 = the one being skipped to now).</summary>
    public static DownloadMode ModeFor(int placesAhead) => placesAhead >= HiResPlacesAhead ? DownloadMode.HiRes : DownloadMode.Quick;

    /// <summary>How long a song that found no Hi-Res copy waits before the next look, after [attempts] tries: an hour, six hours, a day, then every week,
    /// for as long as the song is in the library (a rare record can turn up on Soulseek months later).</summary>
    public static TimeSpan RetryAfter(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromHours(1),
        2 => TimeSpan.FromHours(6),
        3 => TimeSpan.FromDays(1),
        _ => TimeSpan.FromDays(7),
    };

    /// <summary>Really Hi-Res, from the file's own facts (ffprobe): lossless, and more than 16 bits or more than 48 kHz. A 16-bit 48 kHz file is
    /// CD-like and stays on the upgrade list.</summary>
    public static bool IsHiRes(string codec, int bitDepth, int sampleRate) =>
        MediaInfo.LosslessCodecs.Contains(codec.ToLowerInvariant()) && (bitDepth > 16 || sampleRate > 48_000);

    /// <summary>The longest a new copy may differ in length from the old one and still be the same recording.</summary>
    public const int LengthToleranceMs = 3_000;

    /// <summary>
    /// Whether a newly filed copy may replace the old file: the whole transfer arrived (the bytes on disk are the bytes the peer said), it is the same
    /// recording (its length within 3 s of the old file's), and it is really Hi-Res (or, for the lossless step, at least lossless). Null when it passes,
    /// otherwise the reason, which goes in the log. Nothing is swapped on a guess: an old file whose length can't be read fails too.
    /// </summary>
    public static string? Verify(long expectedBytes, long gotBytes, long oldMs, long newMs, string codec, int bitDepth, int sampleRate, bool wantHiRes = true)
    {
        if (expectedBytes <= 0 || gotBytes != expectedBytes) return $"the transfer was incomplete ({gotBytes:N0} of {expectedBytes:N0} bytes)";
        if (oldMs <= 0 || newMs <= 0) return "the length of the old or the new file could not be read";
        if (Math.Abs(oldMs - newMs) > LengthToleranceMs) return $"the new copy is {Math.Abs(oldMs - newMs) / 1000.0:0.#} s longer or shorter than the old one, so it may be another recording";
        if (wantHiRes && !IsHiRes(codec, bitDepth, sampleRate)) return $"the new copy is not really Hi-Res ({codec} {bitDepth}-bit {sampleRate / 1000.0:0.#} kHz)";
        if (!wantHiRes && !MediaInfo.LosslessCodecs.Contains(codec.ToLowerInvariant())) return $"the new copy is not lossless ({codec})";
        return null;
    }
}
