using IpodSync.Core.ItunesDb;
using IpodSync.Core.Jellyfin;
using IpodSync.Core.LocalLibrary;

namespace IpodSync.Core.Listen;

/// <summary>Where a merged track actually lives, so a caller (Blazor page or a remote API
/// client) knows how to resolve playback for it.</summary>
public enum TrackSource { Local, Ipod, Jellyfin }

/// <summary>One song in the merged "Listen" view. SourceId is whatever the original source
/// keys it by (a full file path for Local, the numeric CDB track id for iPod, the Jellyfin
/// item id for Jellyfin) -- enough to resolve a stream URL given the caller's own context
/// (which folder/device/server that source id belongs to).</summary>
public sealed record PlayableTrack(TrackSource Source, string SourceId, string Title, string? Artist, string? Album, TimeSpan Duration);

/// <summary>
/// Merges the three "just play my music" sources into one deduplicated list. Kept independent
/// of any host/UI concern (no URLs, no HTTP) so it can be called both from a Blazor page in
/// process and from a JSON API endpoint for remote clients (see IpodSync.Web's /api/listen).
/// </summary>
public static class ListenLibrary
{
    /// <summary>
    /// Same song in more than one source collapses to one entry, keyed by normalized
    /// title+artist. Order matters: sources are folded in offline-capable-first (Local, then
    /// iPod, then Jellyfin last), so when a duplicate is found the entry that doesn't depend
    /// on network access wins and Jellyfin never silently overrides a copy you already have.
    /// </summary>
    public static List<PlayableTrack> Merge(IEnumerable<LocalTrack>? local, IEnumerable<Track>? ipod, IEnumerable<JellyfinItem>? jellyfin)
    {
        var claimed = new HashSet<string>();
        var result = new List<PlayableTrack>();

        // Checked against a snapshot taken BEFORE this source's own tracks are folded in, so two
        // tracks that share a title+artist WITHIN the same source (two different "Interlude"
        // tracks on one album is a real, common case) are never compared against each other and
        // both survive -- only a lower-priority source's track that matches a HIGHER-priority
        // source's is treated as the same song and dropped.
        void AddSource(IEnumerable<PlayableTrack>? items)
        {
            if (items is null) return;
            var alreadyClaimed = new HashSet<string>(claimed);
            foreach (var t in items)
            {
                string key = Key(t.Title, t.Artist);
                if (!alreadyClaimed.Contains(key)) result.Add(t);
                claimed.Add(key);
            }
        }

        AddSource(local?.Select(t => new PlayableTrack(TrackSource.Local, t.Path, t.DisplayTitle, t.Artist, t.Album, t.Duration)));
        AddSource(ipod?.Where(t => t.Title is not null).Select(t => new PlayableTrack(TrackSource.Ipod, t.Id.ToString(), t.Title!, t.Artist, t.Album, t.Duration)));
        AddSource(jellyfin?.Select(t => new PlayableTrack(TrackSource.Jellyfin, t.Id, t.Name, t.Artist, t.Album, t.Duration)));

        return result;
    }

    private static string Key(string title, string? artist) => $"{title.Trim().ToLowerInvariant()}|{(artist ?? "").Trim().ToLowerInvariant()}";
}
