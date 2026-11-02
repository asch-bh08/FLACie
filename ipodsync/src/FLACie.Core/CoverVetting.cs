namespace FLACie.Core;

/// <summary>
/// Which of a Jellyfin song's cover keys can be trusted (the same rule as the Android app's CoverVetting.kt). A song's own picture ("jf" + its own item id) is always right.
/// A song without one points at its album's picture (the AlbumId item), and on a real Jellyfin AlbumId is not reliable: files of many different albums (or with no album at
/// all) end up under one AlbumId, and all of them then show that one album's picture (on the owner's library 1,807 of 10,325 songs). A fallback is therefore kept only for
/// songs that really belong to the album the picture is of, which the library itself can tell: among the songs that point at one album picture there must be exactly one album
/// name (not blank, not "Unknown"), and only the songs with that name keep it. Every other song gets the catalog lookup (its own artist / album / title) instead of another album's cover.
/// </summary>
public static class CoverVetting
{
    static bool Blank(string albumNorm) => albumNorm.Length == 0 || albumNorm is "unknown" or "unknownalbum" or "notitle" or "untitled";

    /// <summary>The key's item id when it is a Jellyfin picture key ("jf" + id [+ "~" + tag]), else null.</summary>
    static string? JellyfinKeyId(string? key) => key is { Length: > 2 } && key.StartsWith("jf", StringComparison.Ordinal) ? key[2..].Split('~')[0].Replace("-", "").ToLowerInvariant() : null;

    static bool IsOwn(Track t, string id) => string.Equals((t.JellyfinId ?? "").Replace("-", ""), id, StringComparison.OrdinalIgnoreCase);

    /// <summary>The same songs with the doubtful album fallbacks replaced (the list itself when nothing changes).</summary>
    static string? FolderOf(Track t) => t.FilePath is { Length: > 0 } p && p.LastIndexOf('/') is > 0 and var i ? p[..i] : null;

    public static IReadOnlyList<Track> Vet(IReadOnlyList<Track> tracks)
    {
        // album picture item -> the album names of the songs that point at it as a fallback
        Dictionary<string, HashSet<string>>? groups = null;
        foreach (var t in tracks)
        {
            if (t.Source != TrackSource.Jellyfin || JellyfinKeyId(t.ArtKey) is not { } id || IsOwn(t, id)) continue;
            (groups ??= []).TryAdd(id, []);
            groups[id].Add(Matching.AlbumNorm(t.Album));
        }
        if (groups is null) return tracks;
        // the album every fallback picture is of: the one non-blank name its songs share, or none when they disagree
        var owner = new Dictionary<string, string>();
        foreach (var (id, names) in groups) { var real = names.Where(n => !Blank(n)).ToList(); if (real.Count == 1) owner[id] = real[0]; }
        // the folders holding more than one album, counting every song in them (also those with a picture of their own): a playlist folder Jellyfin made one
        // album of. A song there without its own picture was the only one pointing at the "album" picture, so nothing disagreed (owner, 2 Nov: Wham! with Sia's)
        var folderNames = new Dictionary<string, HashSet<string>>();
        foreach (var t in tracks)
            if (t.Source == TrackSource.Jellyfin && FolderOf(t) is { } f && Matching.AlbumNorm(t.Album) is var n && !Blank(n)) { folderNames.TryAdd(f, []); folderNames[f].Add(n); }
        bool Mixed(Track t) => FolderOf(t) is { } f && folderNames.TryGetValue(f, out var names) && names.Count > 1;
        var changed = false;
        var result = new List<Track>(tracks.Count);
        foreach (var t in tracks)
        {
            if (t.Source == TrackSource.Jellyfin && JellyfinKeyId(t.ArtKey) is { } id && !IsOwn(t, id) && (!(owner.TryGetValue(id, out var o) && o == Matching.AlbumNorm(t.Album)) || Mixed(t)))
            // (a song in a playlist folder: by the song itself, its album tag is often a compilation's: George Michael's Careless Whisper got a Wham! cover)
            { result.Add(t with { ArtKey = Mixed(t) || groups[id].Count(n => !Blank(n)) > 1 ? Art.LookupKey(t.Artist, "", t.Title) : Art.LookupKey(t.Artist, t.Album, t.Title) }); changed = true; }
            else result.Add(t);
        }
        return changed ? result : tracks;
    }
}
