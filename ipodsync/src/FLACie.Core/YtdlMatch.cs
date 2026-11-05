using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// F7: remix-aware matching of a YouTube or SoundCloud upload to the song asked for. A remix keeps its name ("Dark Horse (Kryd Hoodtrap Remix)") and only
/// an upload with that same remix matches; an original never takes a remix, and a remix never takes the original. The artist is the song's lead artist or,
/// for a remix, the remixer (who often uploads it); the length has to be within 8 seconds when both are known. The yt-dlp service (tools/ytdl/server.py)
/// uses the same rules.
/// </summary>
public static partial class RemixMatch
{
    [GeneratedRegex(@"[(\[]([^)\]]*\b(?:remix|mix|edit|bootleg|vip|rework|flip|refix|dub)\b[^)\]]*)[)\]]|\s[-–]\s([^-–()\[\]]*\b(?:remix|mix|edit|bootleg|vip|rework|flip|refix|dub)\b[^-–()\[\]]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex RemixRx();
    [GeneratedRegex(@"\b(?:remix|remixed|mix|bootleg|vip|rework|flip|refix)\b", RegexOptions.IgnoreCase)] private static partial Regex RemixWord();
    [GeneratedRegex(@"^\s*(?:official\s+)?(?:radio|album|single|original|extended)\s+(?:edit|mix|version)\s*$", RegexOptions.IgnoreCase)] private static partial Regex PlainVersion();

    /// <summary>The remix's name, lower case ("kryd hoodtrap remix"), or null when the title is no remix (a radio edit or original mix is none).</summary>
    public static string? Remix(string title)
    {
        foreach (Match m in RemixRx().Matches(title ?? ""))
        {
            var r = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
            if (r.Length > 0 && !PlainVersion().IsMatch(r)) return string.Join(' ', Matching.SearchWords(r));
        }
        return null;
    }

    /// <summary>The title without its remix and feat. parts, for comparing the song itself.</summary>
    public static string Core(string title) => Matching.NormTitle(RemixRx().Replace(WebCatalog.BaseTitle(title ?? ""), " "));

    public static bool Matches(string wantArtist, string wantTitle, int wantSec, string gotTitle, string gotUploader, int gotSec)
    {
        var want = Remix(wantTitle); var got = Remix(gotTitle);
        if (want is null ? got is not null || RemixWord().IsMatch(gotTitle) : got != want) return false;
        var core = Core(wantTitle);
        if (core.Length == 0 || !(" " + Core(gotTitle) + " ").Contains(" " + core + " ")) return false;
        var hay = Matching.NormTitle(gotTitle + " " + gotUploader);
        var lead = Matching.NormTitle(Matching.PrimaryArtist(wantArtist));
        var remixer = want is null ? "" : string.Join(' ', want.Split(' ').TakeWhile(w => !RemixWord().IsMatch(w)).Take(1));
        if (!(lead.Length > 0 && hay.Contains(lead)) && !(remixer.Length > 0 && hay.Contains(remixer))) return false;
        return wantSec <= 0 || gotSec <= 0 || Math.Abs(wantSec - gotSec) <= 8;
    }
}
