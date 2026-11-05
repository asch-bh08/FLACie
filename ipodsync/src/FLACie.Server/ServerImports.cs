using FLACie.Core;

namespace FLACie.Server;

/// <summary>F26.6: the import jobs behind playlists, so a playlist page lists every song of its import and links to its downloads.</summary>
public sealed class ServerImports(UserStateStore states) : IImports
{
    public (string JobId, string Name, IReadOnlyList<ImportSong> Songs)? ForPlaylist(UserSession s, string playlistId) => Find(states.For(s).Imports, playlistId);

    public (string Name, string? PlaylistId, IReadOnlyList<ImportSong> Songs)? ForJob(UserSession s, string jobId) => OfJob(states.For(s).Imports, jobId);

    /// <summary>The newest import with a list that made [playlistId].</summary>
    public static (string JobId, string Name, IReadOnlyList<ImportSong> Songs)? Find(IEnumerable<ImportJob> jobs, string playlistId)
    {
        foreach (var j in jobs.OrderByDescending(j => j.Created))
            if (j.Lists.FirstOrDefault(l => l.PlaylistId == playlistId) is { } l) return (j.Id, l.Name, Songs(l.Items));
        return null;
    }

    /// <summary>All songs of an import job, every list in turn.</summary>
    public static (string Name, string? PlaylistId, IReadOnlyList<ImportSong> Songs)? OfJob(IEnumerable<ImportJob> jobs, string jobId) =>
        jobs.FirstOrDefault(j => j.Id == jobId) is { } j
            ? (j.Lists.Count == 1 ? j.Lists[0].Name : j.File, j.Lists.FirstOrDefault(l => l.PlaylistId is not null)?.PlaylistId, Songs(j.Lists.SelectMany(l => l.Items)))
            : null;

    static List<ImportSong> Songs(IEnumerable<ImportItem> items) => items.Select(i => new ImportSong(i.Artist, i.Title, i.Album, i.State, i.Message)).ToList();
}
