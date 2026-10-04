using FLACie.Core;

namespace FLACie.Server;

/// <summary>Who may open the admin dashboard: the accounts listed in FLACIE_ADMINS (comma separated user names), or, when that is not set, the administrators of the Jellyfin server.</summary>
public static class AdminAccess
{
    public static bool Is(UserSession s, IConfiguration conf)
    {
        var list = (conf["FLACIE_ADMINS"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return list.Length > 0 ? list.Contains(s.Jellyfin?.UserName ?? s.DisplayName, StringComparer.OrdinalIgnoreCase) : s.IsAdmin;
    }
}
