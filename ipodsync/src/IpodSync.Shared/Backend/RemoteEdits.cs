using IpodSync.Core.Sync;

namespace IpodSync.Shared.Backend;

/// <summary>The remote-edit rules (<see cref="IpodSync.Core.Sync.RemoteEdits"/>, in Core so the headless engine
/// library can use them too) applied to an <see cref="IIpodSyncBackend"/>.</summary>
public static class RemoteEdits
{
    public static System.Text.Json.JsonSerializerOptions Json => IpodSync.Core.Sync.RemoteEdits.Json;

    public static Task<(int Status, object Body)> HandleAsync(IIpodSyncBackend backend, string root, bool commit, string? confirm, string body, CancellationToken ct) =>
        IpodSync.Core.Sync.RemoteEdits.HandleAsync(
            (changes, c, log, t) => backend.RunChangesAsync(root, changes, c, "remote", log, t),
            backend.Capabilities.CanWrite ? null : backend.Capabilities.ReadOnlyReason ?? "This host can't write to the iPod.",
            root, commit, confirm, body, ct);
}
