using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.Jellyfin;

public sealed record PlaylistSyncReport(string PlaylistName, int Matched, int Unmatched, string Action);

/// <summary>
/// Pushes an iPod playlist's membership into a same-named Jellyfin playlist, by
/// looking up each track's title/artist in the Jellyfin library. This only ever
/// adds items to Jellyfin; it never touches the iPod or the local library scan,
/// and it never deletes anything from Jellyfin -- an existing playlist with the
/// same name gets missing tracks appended, not replaced or trimmed. Matching
/// removals is deliberately out of scope for a first pass.
/// </summary>
public static class JellyfinPlaylistSync
{
    public static async Task<PlaylistSyncReport> SyncAsync(
        JellyfinClient client, string userId, Playlist playlist, IReadOnlyList<Track> allTracks, CancellationToken ct = default)
    {
        var byId = allTracks.ToDictionary(t => t.Id);
        var itemIds = new List<string>();
        int unmatched = 0;

        foreach (var trackId in playlist.TrackIds)
        {
            if (!byId.TryGetValue(trackId, out var track) || track.Title is null) { unmatched++; continue; }
            string? itemId = await client.FindAudioItemIdAsync(track.Title, track.Artist, ct);
            if (itemId is not null) itemIds.Add(itemId);
            else unmatched++;
        }

        string name = playlist.Name ?? "Untitled";
        var existing = await client.GetPlaylistsAsync(userId, ct);
        var match = existing.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        string action;
        if (match is null)
        {
            await client.CreatePlaylistAsync(userId, name, itemIds, ct);
            action = "created";
        }
        else
        {
            await client.AddToPlaylistAsync(match.Id, userId, itemIds, ct);
            action = "updated (tracks added; removals are not synced)";
        }

        return new PlaylistSyncReport(name, itemIds.Count, unmatched, action);
    }
}
