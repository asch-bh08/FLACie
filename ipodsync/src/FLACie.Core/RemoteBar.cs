namespace FLACie.Core;

/// <summary>Which other player the "Listening on" strip shows.</summary>
public static class RemoteBar
{
    /// <summary>
    /// A player that is playing; when none is, the one the strip was last showing ([sticky], a session id) if it is still on a song: paused from the strip, it stays
    /// with its Play button until that player stops or another takes over (owner, 5 Nov: pausing made the strip vanish, so it could not be resumed).
    /// </summary>
    public static ConnectSession? Pick(IEnumerable<ConnectSession> sessions, string? sticky)
    {
        var others = sessions.Where(s => !s.IsSelf && s.Controllable && s.ItemId is not null).ToList();
        return others.FirstOrDefault(s => !s.Paused) ?? (sticky is null ? null : others.FirstOrDefault(s => s.Id == sticky));
    }
}
