using FLACie.Core;

namespace FLACie.Server;

/// <summary>Every 20 seconds, looks at each signed-in account's profile again and folds in what the phone (or another device) changed: see
/// <see cref="UserSession.RefreshProfileAsync"/>.</summary>
public sealed class ProfileSyncService(SessionStore sessions, JellyfinClient jf, ILogger<ProfileSyncService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var prio = JellyfinGate.Use(GatePriority.Background);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { break; }
            foreach (var s in sessions.Active.ToList())
                try { if (await s.RefreshProfileAsync(jf, ct)) log.LogInformation("profile of {user} updated from another device", s.DisplayName); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { log.LogDebug(e, "profile refresh failed"); }
        }
    }
}
