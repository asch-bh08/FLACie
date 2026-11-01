namespace FLACie.Core;

/// <summary>
/// One source in a quick download's race: Soulseek's normal copies, Soulseek's Hi-Res copies, YouTube, an open source. [run] finds, downloads and
/// files the song and returns it playable (null when this source had nothing). A Hi-Res source says how its transfer is going with
/// <see cref="Report"/>, so the race can tell whether it is worth a few seconds' wait once a quicker copy is ready.
/// </summary>
public sealed class RaceEntrant(string source, bool hiRes, Func<RaceEntrant, CancellationToken, Task<Track?>> run)
{
    /// <summary>The source's tag for the job's label (soulseek, ytdl ...). The open sources' runner sets it to whichever of them had the song.</summary>
    public string Source { get; set; } = source;
    public bool HiRes { get; } = hiRes;
    internal Func<RaceEntrant, CancellationToken, Task<Track?>> Run => run;

    /// <summary>The time in milliseconds (a test sets its own).</summary>
    public Func<long> Clock { get; set; } = () => Environment.TickCount64;

    readonly object gate = new();
    long firstAt = -1, firstBytes, bytes, total; double told;

    /// <summary>The transfer has [bytes] of [total]. [bytesPerSecond] when the source knows its own speed; otherwise it is measured from these reports
    /// (the bytes gained since the first report that had any, over the time since).</summary>
    public void Report(long bytes, long total, double? bytesPerSecond = null)
    {
        lock (gate)
        {
            var now = Clock();
            if (firstAt < 0 || bytes < this.bytes) { firstAt = now; firstBytes = bytes; }
            this.bytes = bytes; this.total = total; told = bytesPerSecond ?? 0;
        }
    }

    public double BytesPerSecond
    {
        get
        {
            lock (gate)
            {
                if (told > 0) return told;
                var ms = Clock() - firstAt;
                return firstAt < 0 || ms < 500 || bytes <= firstBytes ? 0 : (bytes - firstBytes) * 1000.0 / ms;
            }
        }
    }

    public long Remaining { get { lock (gate) return Math.Max(0, total - bytes); } }
}

/// <summary>The copy that plays: the source that delivered it, and whether it is Hi-Res.</summary>
public sealed record RaceWin(Track Track, string Source, bool HiRes);

/// <summary>
/// The race of a quick download (spec section 1). Every source starts at once. The first copy that is filed and playable is held; if a Hi-Res transfer
/// is under way whose estimate is within the grace (<see cref="SmartDownloads.WaitForHiRes"/>), it is waited for, at most the grace; otherwise the
/// quick copy plays. A Hi-Res copy that arrives first plays at once. The sources that lost are cancelled.
/// <para>
/// Some sources can't be stopped once they have started filing: the YouTube service and the file mover's fetch carry on by themselves. Their copy is
/// still handed to [stray] when it lands, so the caller can retire it instead of leaving a second copy of the song in the library. A late copy at the
/// very path of the one that plays (the YouTube service answers "already there" for a file of the same name) is never handed back.
/// </para>
/// </summary>
public static class DownloadRace
{
    public static async Task<RaceWin?> RunAsync(IReadOnlyList<RaceEntrant> entrants, Action<RaceWin>? stray, CancellationToken ct, int graceMs = SmartDownloads.GraceMs)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var runs = entrants.Select(e => (E: e, T: Guard(e, race.Token))).ToList();
        var seen = new HashSet<Task<Track?>>();
        RaceWin? quick = null; long deadline = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var (e, t) in runs)
                {
                    if (!t.IsCompleted || !seen.Add(t) || t.Result is not { } track) continue;
                    var copy = new RaceWin(track, e.Source, e.HiRes);
                    if (copy.HiRes) { if (quick is not null) Hand(stray, quick, copy); return Settle(copy); }
                    if (quick is null) { quick = copy; deadline = Environment.TickCount64 + graceMs; }
                    else Hand(stray, copy, quick);
                }
                var live = runs.Where(r => !r.T.IsCompleted).ToList();
                if (quick is not null)
                {
                    var left = deadline - Environment.TickCount64;
                    // a Hi-Res transfer is worth waiting for only while it is still expected inside what is left of the grace
                    if (left <= 0 || !live.Any(r => r.E.HiRes && SmartDownloads.WaitForHiRes(r.E.Remaining, r.E.BytesPerSecond, (int)left))) return Settle(quick);
                }
                if (live.Count == 0) return quick is null ? null : Settle(quick);
                await Task.WhenAny(live.Select(r => (Task)r.T).Append(Task.Delay(quick is null ? 1_000 : 100, ct)));
            }
        }
        finally { race.Cancel(); }

        RaceWin Settle(RaceWin win)
        {
            race.Cancel();
            // the losers are cancelled; one that files its copy anyway (it could not be stopped) is handed back to be retired
            foreach (var (e, t) in runs.Where(r => !seen.Contains(r.T)))
                _ = t.ContinueWith(done => { if (done.IsCompletedSuccessfully && done.Result is { } late) Hand(stray, new RaceWin(late, e.Source, e.HiRes), win); }, TaskScheduler.Default);
            return win;
        }
    }

    static void Hand(Action<RaceWin>? stray, RaceWin late, RaceWin winner)
    {
        if (stray is null || late.Track.Path == winner.Track.Path) return;
        try { stray(late); } catch (Exception) { }
    }

    /// <summary>A source that throws (a service that is down, a timeout) counts as having nothing, like the old one-by-one order did. A real cancel stays a cancel.</summary>
    static async Task<Track?> Guard(RaceEntrant e, CancellationToken ct)
    {
        await Task.Yield();
        try { return await e.Run(e, ct); }
        catch (OperationCanceledException) { return null; }
        catch (Exception) { return null; }
    }
}
