using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>A song's own album, found in the catalog for a file whose album tag is really a playlist's name.</summary>
public sealed record AlbumFix(string Album, string AlbumArtist, string? ArtUrl, int Year);

/// <summary>
/// Songs filed under a playlist's name instead of their album (owner, 13 Nov: "Grenade" showed "Pop Megahits" and "Airplanes" "2010s bops", with those
/// playlists' pictures: files from a ripped mix). The server looks each one up in the catalog (VersionNames-style, in the background) and the library
/// shows the song's own album, artist and cover over the tags; the files are not changed. Real albums, an artist's own best-of, remixes and other
/// versions keep what they have.
/// </summary>
public static partial class AlbumFixes
{
    /// <summary>The fix for a file (by its file path, else its stream path), null when there is none.</summary>
    public static Func<string, AlbumFix?>? Lookup { get; set; }

    [GeneratedRegex(@"\b(?:bangers?|bops|hits|megahits|throwbacks?|party|playlist|mix(?:tape)?|vibes|anthems|classics|essentials|favou?rites|top\s*\d+|\d0'?s|\d{4}'?s|songs|tunes|bangerz|summer|workout|gym|chill)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlaylistName();

    /// <summary>Songs whose album looks like a playlist: a playlist-like name ("2010s Bangers - 100 Hits", "Pop Megahits", "party bangers") held by one
    /// song alone or by songs of several artists. One artist's own album of several songs ("Greatest Hits" by Queen) is a real album; a remix or other
    /// version keeps its album.</summary>
    public static List<Track> Candidates(IEnumerable<Track> songs)
    {
        var list = songs.ToList();
        var byAlbum = list.GroupBy(t => Matching.AlbumNorm(t.Album)).ToDictionary(g => g.Key, g => g.ToList());
        return list.Where(t =>
        {
            if (t.Album.Length == 0 || !PlaylistName().IsMatch(t.Album) || Matching.Recording(t.Title).Length > 0) return false;
            var g = byAlbum[Matching.AlbumNorm(t.Album)];
            return g.Count == 1 || g.Select(x => Matching.PrimaryArtist(x.Artist)).Distinct().Count() > 1;
        }).ToList();
    }

    /// <summary>The songs with their fixes over them (the same list when there are none).</summary>
    public static IReadOnlyList<Track> Apply(IReadOnlyList<Track> tracks)
    {
        if (Lookup is not { } look) return tracks;
        List<Track>? result = null;
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            if (look(t.FilePath ?? t.Path) is not { } f) continue;
            result ??= [.. tracks];
            result[i] = t with { Album = f.Album, AlbumArtist = f.AlbumArtist, Year = f.Year > 0 ? f.Year : t.Year, ArtKey = f.ArtUrl is { } u ? Art.ExternalKey(u) : t.ArtKey };
        }
        return result ?? tracks;
    }
}
