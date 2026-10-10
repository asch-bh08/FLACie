using FLACie.Core;

namespace FLACie.Server;

/// <summary>Closes listening runs that have been quiet for ten minutes (one summary notification each).</summary>
public sealed class PlayTrackerSweeper(PlayTracker plays, DownloadDigest downloads) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { while (await timer.WaitForNextTickAsync(stop)) { try { plays.Sweep(); downloads.Sweep(); } catch (Exception) { } } } catch (OperationCanceledException) { }
    }
}
