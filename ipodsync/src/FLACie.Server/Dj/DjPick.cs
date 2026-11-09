using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// F11: which of several songs that match is meant. The album version before edits, live versions, remixes and demos (owner, 9 Nov), then own plays, then popularity, then how close the title is to what was said
/// (Corona's The Rhythm of the Night over DeBarge's Rhythm of the Night). The others are kept for <see cref="Life"/> so "not that one" plays the next.
/// </summary>
public sealed class DjPick(Func<DateTime> now)
{
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(1);

    sealed record Left(DateTime At, IReadOnlyList<Track> Others);
    readonly ConcurrentDictionary<string, Left> left = new();

    /// <summary>The matches, best first.</summary>
    public static IReadOnlyList<Track> Rank(IDjAskUser u, IEnumerable<Track> matches, string said)
    {
        var top = u.MostPlayed(500);
        var plays = new Dictionary<string, int>();
        for (var i = 0; i < top.Count; i++) plays.TryAdd(top[i].Path, top.Count - i);
        var key = Matching.NormTitle(said);
        // the album version first, whatever was played before: a demo the DJ once picked wrongly must not keep winning (owner, 9 Nov)
        return matches.DistinctBy(t => t.Path)
            .OrderBy(VersionRank)
            .ThenByDescending(t => plays.GetValueOrDefault(t.Path))
            .ThenByDescending(u.Popularity)
            .ThenBy(t => Distance(Matching.NormTitle(t.Title), key))
            .ToList();
    }

    /// <summary>Songs in the library that share the title (with or without "The", and whatever its brackets or " - " note say), for ranking alongside
    /// what the matcher found: "Out of Space (Demo)", "Out of Space - Audio Bullys Remix" and the live one are all versions of "Out of Space".</summary>
    public static IEnumerable<Track> SameTitle(IDjAskUser u, Track t)
    {
        var k = VersionKey(t.Title);
        return u.Songs.Where(x => VersionKey(x.Title) == k);
    }

    /// <summary>The title with every bracket and " - " note gone, so all versions of a song share it.</summary>
    public static string VersionKey(string title) => Core(Plain(title));

    /// <summary>The title as said aloud: "Out of Space (Demo)" is "Out of Space".</summary>
    public static string Plain(string title) { var s = Regex.Replace(title.Split(" - ")[0], @"\s*[(\[][^)\]]*[)\]]", " ").Trim(); return s.Length > 0 ? s : title.Trim(); }

    /// <summary>The words in a title's brackets and after its " - ".</summary>
    static string Extras(string title)
    {
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        var parts = Regex.Matches(dash < 0 ? title : title[..dash], @"[(\[]([^)\]]*)[)\]]").Select(m => m.Groups[1].Value).ToList();
        if (dash >= 0) parts.Add(title[(dash + 3)..]);
        return string.Join(" ", parts).Trim();
    }

    static readonly Regex OddWord = new(@"\b(demo|demos|karaoke|instrumental|a cappella|acapella|sped|slowed|nightcore|cover|tribute|rehearsal)\b", RegexOptions.IgnoreCase);
    static readonly Regex Remix = new(@"\b(remix|remixed|remixes|mix|rework|bootleg|vip|dub)\b", RegexOptions.IgnoreCase);
    static readonly Regex Live = new(@"\b(live|acoustic|unplugged|session|sessions)\b", RegexOptions.IgnoreCase);
    static readonly Regex Edit = new(@"\b(edit|single version|radio version|short version|extended)\b", RegexOptions.IgnoreCase);
    static readonly Regex OtherAlbum = new(@"\b(demos?|karaoke|tribute|rarities)\b", RegexOptions.IgnoreCase);
    static readonly Regex RemixAlbum = new(@"\b(remix|remixes|remixed)\b", RegexOptions.IgnoreCase);
    static readonly Regex LiveAlbum = new(@"^live\b|\blive (?:at|in|from|on)\b|\blive$|[(\[]live[)\]]|\bunplugged\b|\bsessions?\b", RegexOptions.IgnoreCase);

    /// <summary>How far a version is from the album one: 0 the album version, 1 an edit, 2 live or acoustic, 3 a remix, 4 a demo, karaoke and such.
    /// Read from the title's brackets and " - " note, and from the album's name ("Live at Brixton", "Demos and Rarities").</summary>
    public static int VersionRank(Track t)
    {
        var x = Extras(t.Title);
        // a dance record's "(Original Mix)" is the song itself
        var r = Regex.IsMatch(x, @"^\s*original(?: (?:mix|version))?\s*$", RegexOptions.IgnoreCase) ? 0 : OddWord.IsMatch(x) ? 4 : Remix.IsMatch(x) ? 3 : Live.IsMatch(x) ? 2 : Edit.IsMatch(x) ? 1 : 0;
        var a = OtherAlbum.IsMatch(t.Album) ? 4 : RemixAlbum.IsMatch(t.Album) ? 3 : LiveAlbum.IsMatch(t.Album) ? 2 : 0;
        return Math.Max(r, a);
    }

    /// <summary>Which version this is, said aloud after "the other one": "the live version", "the Audio Bullys Remix", "the demo", "the album version".</summary>
    public static string VersionName(Track t)
    {
        var x = Extras(t.Title);
        return VersionRank(t) switch
        {
            4 => Regex.IsMatch(x + " " + t.Album, @"\bdemos?\b", RegexOptions.IgnoreCase) ? "the demo" : x.Length > 0 ? $"the {x.ToLowerInvariant()} version" : $"the one from {t.Album}",
            3 => Remix.IsMatch(x) && !Regex.IsMatch(x, @"^\s*remix\s*$", RegexOptions.IgnoreCase) ? $"the {x}" : "the remix",
            2 => Regex.IsMatch(x, @"\bacoustic\b", RegexOptions.IgnoreCase) ? "the acoustic version" : "the live version",
            1 => $"the {x.ToLowerInvariant()}",
            _ => t.Album.Length > 0 ? $"the album version, from {t.Album}" : "the album version",
        };
    }

    static string Core(string title) { var n = Matching.NormTitle(title); return n.StartsWith("the ") ? n[4..] : n; }

    /// <summary>What versions of one song share: the title without "The" and its extras ("The Rhythm of the Night" and "Rhythm of the Night").</summary>
    public static string TitleKey(string title) => Core(WebCatalog.BaseTitle(title));

    /// <summary>Keeps the matches not played, for "the other one".</summary>
    public void Keep(string account, IReadOnlyList<Track> others)
    {
        if (others.Count == 0) left.TryRemove(account, out _);
        else left[account] = new(now(), others);
    }

    /// <summary>The next match after "not that one" ([artist]: "the Corona one"), or null when none is left or it's been over a minute.</summary>
    public Track? Other(string account, string? artist = null)
    {
        if (!left.TryGetValue(account, out var l) || now() - l.At > Life) return null;
        var pick = artist is { Length: > 0 } a ? l.Others.FirstOrDefault(t => Matching.NormTitle(t.Artist).Contains(Matching.NormTitle(a))) : l.Others.FirstOrDefault();
        if (pick is null) return null;
        Keep(account, l.Others.Where(t => t != pick).ToList());
        return pick;
    }

    static int Distance(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0]; d[0] = i;
            for (var j = 1; j <= b.Length; j++) { var t = d[j]; d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1)); prev = t; }
        }
        return d[b.Length];
    }
}
