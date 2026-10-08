using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Who may open the admin dashboard (server settings, downloads, every signed-in user).
/// Only an account of the Jellyfin this server is locked to (FLACIE_JELLYFIN_URL) counts by plain user name: a name alone proves nothing when anyone may point the
/// login at a Jellyfin of their own, where they can call themselves "admin". On a server that is not locked, list accounts as
/// <c>name@jellyfin-host:port</c> (or <c>name@nas-host</c> for a NAS sign-in) in FLACIE_ADMINS. With no list, the administrators of the locked Jellyfin are the admins.
/// </summary>
public static class AdminAccess
{
    public static bool Is(UserSession s, IConfiguration conf)
    {
        var list = (conf["FLACIE_ADMINS"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // hosted by the Windows app (loopback only, one person): their own Jellyfin is the one that counts
        var local = conf["FLACIE_PARENT_PID"] is { Length: > 0 };
        var locked = conf["FLACIE_JELLYFIN_URL"] is { Length: > 0 } f ? JellyfinClient.Normalise(f) : null;
        if (s.Kind == "jellyfin" && s.Jellyfin is { } j)
        {
            // when the server is locked to one Jellyfin every sign-in went to it (the cookie is signed by this server), whatever address the account was saved under (an older sign-in may hold the LAN, Tailscale or public address of the same Jellyfin)
            var onLocked = local || locked is not null;
            if (list.Length == 0) return onLocked && s.IsAdmin;
            return (onLocked && list.Contains(j.UserName, StringComparer.OrdinalIgnoreCase)) || list.Contains($"{j.UserName}@{Authority(j.Server)}", StringComparer.OrdinalIgnoreCase);
        }
        if (s.Kind == "nas" && s.Nas is { } n && n.User.Length > 0) return list.Contains($"{n.User}@{n.Host}", StringComparer.OrdinalIgnoreCase);
        return false;
    }

    static string Authority(string server) => Uri.TryCreate(JellyfinClient.Normalise(server), UriKind.Absolute, out var u) ? u.Authority : server;
}
