namespace FLACie.Server.Dj;

/// <summary>Makes the always-the-same "talk to the DJ" replies ahead in FLACie's own voice (see <see cref="DjService.WarmRepliesAsync"/>): a minute after
/// the server starts, then every hour, so a voice chosen since gets them too. Replies already made are kept, so most runs make nothing.</summary>
public sealed class DjWarmService(DjService dj, ILogger<DjWarmService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { if (await dj.WarmRepliesAsync() is > 0 and var n) log.LogInformation("DJ: made {Count} replies ahead in the voice", n); }
            catch (Exception e) { log.LogWarning("DJ replies not made ahead: {Kind}", e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromHours(1), stop); } catch (OperationCanceledException) { return; }
        }
    }
}
