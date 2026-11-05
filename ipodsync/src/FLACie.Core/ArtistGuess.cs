using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// F1: the artist of a song whose file has no artist tag (137 of the owner's, e.g. Eminem/Singles/Eminem_Lose_Yourself.mp3.mp3), from the file and folder
/// names: "Artist - Title" in the file name, "Artist - Album" in a folder, a folder whose name starts the file name, else the folder above the album.
/// Folders that are no one's name (Singles, Favourites, Unknown artist, CD 1, music) give nothing. Null when nothing fits.
/// </summary>
public static partial class ArtistGuess
{
    static readonly HashSet<string> NoOne = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown", "unknown artist", "unknown artists", "unknown album", "various", "various artists", "va", "singles", "single", "favourites", "favorites",
        "music", "my music", "media", "library", "downloads", "download", "mp3", "flac", "songs", "tracks", "albums", "compilations", "misc", "other", "new",
        "mnt", "home", "data", "srv", "volume1", "share", "storage", "itunes", "itunes media", "playlists",
    };

    [GeneratedRegex(@"^(?:cd|disc|disk)\s*\d+$|^\d+$", RegexOptions.IgnoreCase)] private static partial Regex DiscRx();
    [GeneratedRegex(@"\s+[-–—]\s+")] private static partial Regex Dash();
    [GeneratedRegex(@"^\d{1,3}[\s._-]+")] private static partial Regex TrackNo();

    /// <summary>The artist a folder names: the part before " - " ("Jessie J - Who You Are [Deluxe Edition]"), or the whole name; null for Singles and the like.</summary>
    public static string? FromFolder(string folder)
    {
        var f = Tidy(folder);
        if (Dash().Split(f, 2) is [var left, _]) f = left.Trim();
        return f.Length == 0 || NoOne.Contains(f) || DiscRx().IsMatch(f) ? null : f;
    }

    public static string? FromPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var file = TrackNo().Replace(Tidy(StripExtensions(parts[^1])), "");
        var folders = parts[..^1].Reverse().ToList(); // nearest first
        // "Corona - The Rhythm of the Night.mp3"
        if (Dash().Split(file, 2) is [var fa, _] && fa.Trim() is { Length: > 0 } fileArtist && !NoOne.Contains(fileArtist) && !DiscRx().IsMatch(fileArtist)) return fileArtist;
        // "Jessie J - Who You Are [Deluxe Edition]/03 Price Tag.flac"
        foreach (var f in folders) if (Dash().IsMatch(Tidy(f)) && FromFolder(f) is { } a) return a;
        // "Eminem/Singles/Eminem Lose Yourself": a folder whose name starts the file name
        foreach (var f in folders)
            if (FromFolder(f) is { } a && file.StartsWith(a + " ", StringComparison.OrdinalIgnoreCase)) return a;
        // "The Prodigy/Fat of the Land/01 Smack My Bitch Up.flac": the folder above the album; a song loose in the artist's folder takes that one
        var named = folders.Select(FromFolder).ToList();
        if (named.Count >= 2 && named[0] is not null && named[1] is { } artist) return artist;
        if (named.Count >= 1 && named[0] is null) return named.Skip(1).FirstOrDefault(n => n is not null);
        return null;
    }

    static string StripExtensions(string name)
    {
        while (Path.GetExtension(name) is { Length: > 1 and <= 5 } ext && ext.Skip(1).All(char.IsLetterOrDigit)) name = name[..^ext.Length];
        return name;
    }

    static string Tidy(string s) => Regex.Replace(s.Replace('_', ' '), @"\s+", " ").Trim();
}
