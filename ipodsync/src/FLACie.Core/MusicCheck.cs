using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// Whether a song or a list found online is music (owner, 13 Nov: searching "Dreamer" found a YouTube list of nine clips of the 2005 film, and opening it
/// downloaded them). Anything FLACie fetches by itself (a list someone opened or played, Autoplay) must pass; a person fetching by hand is not stopped.
/// Film and TV clips, trailers, episodes, gameplay and the like are not songs; a song that is merely called "Movie" or "Scene Stealer" is.
/// </summary>
public static partial class MusicCheck
{
    [GeneratedRegex(@"\b(?:movie\s*clips?|movieclips|full\s+movie|official\s+trailer|trailer|teaser|full\s+episode|episode\s*\d|season\s*\d|s\d{1,2}\s*e\d{1,2}|gameplay|walkthrough|let'?s\s+play|behind\s+the\s+scenes|interview|documentary|reaction|unboxing|podcast\s+episode|deleted\s+scene)\b|\(\d{4}\)\s*HD\b|\(\d+\s*/\s*\d+\)\s*movie", RegexOptions.IgnoreCase)]
    private static partial Regex NotMusic();
    [GeneratedRegex(@"\b(?:movie|film|trailers?|episodes?|season|clips|scenes|tv\s+show|series|gameplay|documentary)\b|\(\d{4}\)\s*-\s*movie", RegexOptions.IgnoreCase)]
    private static partial Regex NotMusicList();

    public static bool LooksLikeSong(string title, string artist) => !NotMusic().IsMatch(title ?? "") && !NotMusic().IsMatch(artist ?? "");

    /// <summary>A list is music when its name does not say film, TV or clips and (once its songs are read) most of them look like songs.</summary>
    public static bool LooksLikeMusic(WebPlaylist l)
    {
        if (NotMusicList().IsMatch(l.Name) && !Regex.IsMatch(l.Name, @"\b(?:soundtrack|ost|songs?|music|score|theme)\b", RegexOptions.IgnoreCase)) return false;
        if (l.Tracks.Count == 0) return true;
        return l.Tracks.Count(t => LooksLikeSong(t.Title, t.Artist)) >= l.Tracks.Count * 0.6;
    }
}
