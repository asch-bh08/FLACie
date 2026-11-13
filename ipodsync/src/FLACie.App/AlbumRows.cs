using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// An album page with its whole tracklist (owner, 13 Nov: Random Access Memories showed 3 of its 13 songs): the catalog's songs in order, the ones the
/// library has as normal rows, the others greyed in their place ("Not downloaded yet", or how far their download has got). A song is the album's own
/// when its title is the same once "(feat. ...)" and punctuation are set aside; songs the catalog doesn't list (a bonus track) follow at the end.
/// </summary>
public static class AlbumRows
{
    public static (List<Track> Playable, List<WaitingSong> Waiting) Build(IReadOnlyList<CatalogSong> catalog, IReadOnlyList<Track> have, Func<string, string, DownloadJob?> job)
    {
        if (catalog.Count == 0) return (have.ToList(), []);
        static string Key(string title) => Matching.NormTitle(WebCatalog.BaseTitle(title));
        var mine = have.GroupBy(t => Key(t.Title)).ToDictionary(g => g.Key, g => g.First());
        var (play, wait) = ImportRows.BuildWeb(catalog.Select(c => new WebTrack(c.Title, c.Artist)).ToList(), (a, t) => mine.GetValueOrDefault(Key(t)), job);
        var placed = play.Select(t => t.Path).ToHashSet();
        play.AddRange(have.Where(t => !placed.Contains(t.Path)));
        return (play, wait);
    }
}
