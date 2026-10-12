namespace FLACie.Core;

public enum DownloadTagState { None, Downloading, Recent, Failed }

/// <summary>What the small download tag under a song says. <see cref="AtMs"/> is when it finished (Recent), failed (Failed) or 0 (Downloading).</summary>
public readonly record struct DownloadTagStatus(DownloadTagState State, string Message = "", long AtMs = 0)
{
    public static readonly DownloadTagStatus None = new(DownloadTagState.None);

    /// <summary>The whole line as shown ("Recently downloaded · 1s ago", "Downloading · Searching Soulseek...", "Couldn't download"), or null when there is nothing to say
    /// (no state, or a recent download that has just aged out of the window).</summary>
    public string? Text(long nowMs) => State switch
    {
        DownloadTagState.Downloading => Message.Length > 0 ? DownloadTags.DownloadingWord + " · " + Message : DownloadTags.DownloadingWord,
        DownloadTagState.Recent => DownloadTags.AgeLabel(nowMs, AtMs) is { } age ? DownloadTags.RecentWord + " · " + age : null,
        DownloadTagState.Failed => DownloadTags.FailedText,
        _ => null,
    };
}

/// <summary>
/// "Recently downloaded": the one rule web and phone share. A song is recent for <see cref="RecentWindowMs"/> after its download finished, then it is a normal library
/// song. The wording and the age label are the same on both sides (the phone has its own copy of <see cref="AgeLabel"/>, unit-tested the same way).
/// </summary>
public static class DownloadTags
{
    /// <summary>60 minutes.</summary>
    public const long RecentWindowMs = 60L * 60 * 1000;
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMilliseconds(RecentWindowMs);
    /// <summary>A report whose finish time is in the future by more than this is not believed (a client clock must not make a song "recent" for longer).</summary>
    public const long FutureSlackMs = 60_000;

    public const string RecentWord = "Recently downloaded";
    public const string DownloadingWord = "Downloading";
    public const string FailedText = "Couldn't download";

    /// <summary>
    /// How long ago a download finished: "just now" (under 5 s), "Ns ago" (under a minute), "N min ago" (under an hour). Null when it finished more than
    /// <see cref="RecentWindowMs"/> ago, or lies in the future by more than a minute (not believable): then the song carries no tag.
    /// </summary>
    public static string? AgeLabel(long nowMs, long finishedMs)
    {
        var age = nowMs - finishedMs;
        if (age > RecentWindowMs || age < -FutureSlackMs) return null;
        if (age < 5_000) return "just now";
        if (age < 60_000) return (age / 1000) + "s ago";
        return (age / 60_000) + " min ago";
    }

    /// <summary>True while a download that finished at <paramref name="finishedMs"/> counts as recent.</summary>
    public static bool IsRecent(long nowMs, long finishedMs) => AgeLabel(nowMs, finishedMs) is not null;

    /// <summary>
    /// Picks what a song's tag says. Order: a download running now (its stage message) wins; then the newest finished download or, for a "new download" song (a
    /// Cloud track), the time it was added, as long as that is within the window; then a failure within the window. A failed try never hides a success.
    /// </summary>
    /// <param name="runningMessage">The stage message of a download running for this song, or null.</param>
    /// <param name="doneAtMs">When the newest successful download of this song finished (0 = none).</param>
    /// <param name="failedAtMs">When the newest failed try finished (0 = none).</param>
    /// <param name="cloudAddedMs">For a "new download" song, when it was added (0 = not one).</param>
    public static DownloadTagStatus Resolve(string? runningMessage, long doneAtMs, long failedAtMs, long cloudAddedMs, long nowMs)
    {
        if (runningMessage is not null) return new(DownloadTagState.Downloading, runningMessage.Trim());
        var at = Math.Max(doneAtMs, cloudAddedMs);
        if (at > 0 && IsRecent(nowMs, at)) return new(DownloadTagState.Recent, "", at);
        if (failedAtMs > 0 && IsRecent(nowMs, failedAtMs)) return new(DownloadTagState.Failed, "", failedAtMs);
        return DownloadTagStatus.None;
    }

    /// <summary>The report's finish time as the server accepts it: never before 10 minutes ago, never in the future.</summary>
    public static long ClampReported(long finishedMs, long nowMs) => Math.Clamp(finishedMs <= 0 ? nowMs : finishedMs, nowMs - 10 * 60_000, nowMs);
}
