using System.Text;
using System.Text.RegularExpressions;
using FLACie.Core;

namespace FLACie.Server.Alexa;

public enum MatchKind { Track, Album, Artist, Playlist }
public enum Decision { Confident, Ambiguous, NotFound }

/// <summary>What was asked for. Raw is the whole phrase when nothing in it named the kind (so "stand by me" is tried as a title and as "stand" by "me"); the other fields are set by words like "album" or by slots.</summary>
public sealed record VoiceQuery(string? Title, string? Artist, string? Album, string? Playlist, string? Genre, string? Decade, string? Raw, string? SimilarTo = null);

public sealed record VoiceMatch(MatchKind Kind, object Item, double Confidence, string Spoken);
public sealed record MatchResult(IReadOnlyList<VoiceMatch> Ranked, Decision Decision);

/// <summary>Turns what Alexa heard into a <see cref="VoiceQuery"/>.</summary>
public static partial class VoiceParser
{
    static readonly string[] Genres = ["hip hop", "hip-hop", "r&b", "rnb", "new wave", "post rock", "rock", "pop", "jazz", "rap", "metal", "punk", "indie", "alternative", "soul", "blues", "country", "classical", "electronic", "dance", "folk", "reggae", "grunge", "techno", "house", "funk", "disco", "ambient", "soundtrack"];
    const string Dec = @"(?:(?:19|20)?[0-9]0'?s|nineties|eighties|seventies|sixties|fifties|noughties|aughts)";
    static string GenreAlt => string.Join("|", Genres.Select(Regex.Escape));

    [GeneratedRegex(@"^(?:(?:hey |ok |okay )?alexa[, ]*|please |can you |could you |would you |will you |i want to hear |i want to listen to |i would like to hear |i'd like to hear |ask flacie player to |ask flacie to |tell flacie to |ask flacie |flacie |play |put on |listen to |start playing |start )+", RegexOptions.IgnoreCase)] private static partial Regex Lead();
    [GeneratedRegex(@"^(?:something|stuff|music|songs?|anything|artists?|bands?)\s+(?:like|similar to)\s+(.+)$|^(?:similar to|more like)\s+(.+)$")] private static partial Regex Similar();
    [GeneratedRegex(@"^(?:my |the )?(.+?)\s+playlist$|^(?:my |the )?playlist\s+(.+)$")] private static partial Regex PlaylistRx();
    [GeneratedRegex(@"^(?:the\s+)?album\s+(.+?)(?:\s+by\s+(.+))?$|^(.+?)\s+album(?:\s+by\s+(.+))?$")] private static partial Regex AlbumRx();
    [GeneratedRegex(@"^(?:the\s+)?song\s+(.+?)(?:\s+by\s+(.+))?$")] private static partial Regex SongRx();
    [GeneratedRegex(@"^(?:songs?|music|tracks?|something|anything|stuff)\s+by\s+(.+)$")] private static partial Regex ByArtistRx();
    [GeneratedRegex(@"^(.+?)\s+by\s+(.+)$")] private static partial Regex TitleByRx();

    public static VoiceQuery FromRaw(string utterance)
    {
        var s = (utterance ?? "").Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[.,!?;]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        for (var again = true; again;) { var t = Lead().Replace(s, "").Trim(); again = t != s; s = t; }
        if (s.Length == 0) return new(null, null, null, null, null, null, "");

        if (Similar().Match(s) is { Success: true } sm) return new(null, null, null, null, null, null, null, Clean(sm.Groups[1].Success ? sm.Groups[1].Value : sm.Groups[2].Value));
        if (PlaylistRx().Match(s) is { Success: true } pm) return new(null, null, null, Clean(pm.Groups[1].Success ? pm.Groups[1].Value : pm.Groups[2].Value), null, null, null);
        if (AlbumRx().Match(s) is { Success: true } am)
            return am.Groups[1].Success ? new(null, Opt(am.Groups[2]), Clean(am.Groups[1].Value), null, null, null, null) : new(null, Opt(am.Groups[4]), Clean(am.Groups[3].Value), null, null, null, null);
        if (SongRx().Match(s) is { Success: true } gm) return new(Clean(gm.Groups[1].Value), Opt(gm.Groups[2]), null, null, null, null, null);
        if (ByArtistRx().Match(s) is { Success: true } bm) return new(null, Clean(bm.Groups[1].Value), null, null, null, null, null);

        var body = Regex.Replace(s, @"^(?:me |us )?(?:some |a few )?", "");
        var dg = Regex.Match(body, $@"^(?:(?:songs|music|hits|tracks|stuff)\s+)?(?:from\s+)?(?:the\s+)?(?<d>{Dec})(?:\s+(?:(?:songs|music|hits)\s+)?(?<g>{GenreAlt}))?$");
        if (dg.Success) return new(null, null, null, null, dg.Groups["g"].Success ? dg.Groups["g"].Value : null, NormalizeDecade(dg.Groups["d"].Value), null);
        var gd = Regex.Match(body, $@"^(?<g>{GenreAlt})(?:\s+(?:songs|music|hits|tracks))?(?:\s+from\s+(?:the\s+)?(?<d>{Dec}))?$");
        if (gd.Success) return new(null, null, null, null, gd.Groups["g"].Value, gd.Groups["d"].Success ? NormalizeDecade(gd.Groups["d"].Value) : null, null);

        if (TitleByRx().Match(s) is { Success: true } tm) return new(Clean(tm.Groups[1].Value), Clean(tm.Groups[2].Value), null, null, null, null, s);
        return new(null, null, null, null, null, null, s);
    }

    static string? Opt(System.Text.RegularExpressions.Group g) => g.Success && g.Value.Trim().Length > 0 ? Clean(g.Value) : null;
    static string Clean(string s) => s.Trim();

    /// <summary>For the slots a skill's interaction model fills in (kept as spoken, only trimmed; the decade is normalised).</summary>
    public static VoiceQuery FromSlots(string? title, string? artist, string? album, string? playlist, string? genre, string? decade, string? similarTo = null)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return new(T(title), T(artist), T(album), T(playlist), T(genre), NormalizeDecade(decade), null, T(similarTo));
    }

    public static string? NormalizeDecade(string? d)
    {
        if (string.IsNullOrWhiteSpace(d)) return null;
        d = d.Trim().ToLowerInvariant().Replace("'", "").Replace("the ", "");
        switch (d)
        {
            case "nineties": return "1990s"; case "eighties": return "1980s"; case "seventies": return "1970s"; case "sixties": return "1960s"; case "fifties": return "1950s";
            case "noughties" or "aughts": return "2000s";
        }
        var m = Regex.Match(d, @"^(\d{4}|\d{2})s$");
        if (!m.Success) return null;
        if (m.Groups[1].Length == 4) return d;
        var n = int.Parse(m.Groups[1].Value);
        return (n >= 30 ? 1900 + n : 2000 + n) + "s";
    }
}

/// <summary>Spoken-form aliases for the content catalog ("x t c" for XTC), so Alexa's recogniser has the way people say a name.</summary>
public static class AliasGenerator
{
    static readonly string[] DigitWords = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

    public static IReadOnlyList<string> AliasesFor(string name, MatchKind kind)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(name)) return result;
        var original = name.Trim().ToLowerInvariant();
        void Add(string? s)
        {
            if (s is null) return;
            s = Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");
            if (s.Length == 0 || s == original || result.Contains(s) || result.Count >= 8) return;
            result.Add(s);
        }

        var bases = new List<string> { name };
        var tagless = Phonetic.StripTags(name); if (tagless != name) bases.Add(tagless);
        var bare = Phonetic.StripBrackets(name); if (bare.Length > 0 && bare != tagless) bases.Add(bare);
        if (kind == MatchKind.Playlist) { var noYear = Regex.Replace(tagless, @"\s*\b(?:19|20)\d{2}\b\s*$", "").Trim(); if (noYear.Length > 0 && noYear != tagless) bases.Add(noYear); }

        foreach (var b in bases)
        {
            var words = Phonetic.Words(b, tags: false);   // the plain spoken words: digits are spelled in Words, so build the digit forms separately
            var spoken = Plain(b);
            Add(spoken);
            if (spoken.StartsWith("the ", StringComparison.Ordinal) && spoken.Length > 4) Add(spoken[4..]);
            // acronym tokens spoken letter by letter: XTC = x t c, AC/DC = a c d c
            var tokens = Regex.Split(b, @"[\s/\-.]+").Where(t => t.Length > 0).ToList();
            if (tokens.Any(t => t.Length is >= 2 and <= 5 && t.All(char.IsLetter) && t == t.ToUpperInvariant()))
                Add(string.Join(' ', tokens.Select(t => t.Length is >= 2 and <= 5 && t.All(char.IsLetter) && t == t.ToUpperInvariant() ? string.Join(' ', t.ToLowerInvariant().ToCharArray()) : Plain(t))));
            // digits spoken: "blink 182" -> "blink one eight two"; and number words as digits: "two door" -> "2 door"
            if (spoken.Any(char.IsDigit)) Add(Regex.Replace(spoken, @"\d", m => " " + DigitWords[m.Value[0] - '0'] + " "));
            var asDigits = Regex.Replace(spoken, @"\b(zero|one|two|three|four|five|six|seven|eight|nine|ten)\b", m => Array.IndexOf(DigitWords, m.Value).ToString());
            if (asDigits != spoken) Add(asDigits);
            // "believin'" is also said "believing"
            if (Regex.IsMatch(b, @"in'(?:\s|$)", RegexOptions.IgnoreCase)) Add(Regex.Replace(spoken, @"in\b", "ing"));
            if (words.Count > 0 && kind == MatchKind.Artist) { }
        }
        // 2Pac: "2pac" -> "two pac"
        if (Regex.IsMatch(original, @"\d[a-z]|[a-z]\d")) { var split = Regex.Replace(Plain(name), @"(?<=\d)(?=[a-z])|(?<=[a-z])(?=\d)", " "); Add(Regex.Replace(split, @"\d", m => " " + DigitWords[m.Value[0] - '0'] + " ")); }
        return result;
    }

    /// <summary>Lower-case, no accents, "&amp;"/"+"/"n" = "and", punctuation gone (apostrophes dropped, "!" between letters = i, dots and dashes are spaces).</summary>
    static string Plain(string s)
    {
        s = Regex.Replace(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", "");
        s = Regex.Replace(s, @"(?<=\p{L})!(?=\p{L})", "i");
        s = s.Replace("&", " and ").Replace("+", " and ").Replace("'", "").Replace("’", "");
        s = Regex.Replace(s, @"[^\p{L}\p{N}]+", " ").Trim();
        s = Regex.Replace(s, @"\bn\b", "and");
        return s;
    }
}

/// <summary>
/// Finds what was asked for in one person's library. Everything is indexed once in memory (no calls to Jellyfin or the files): names are folded (<see cref="Phonetic"/>),
/// a trigram index picks the few candidates worth scoring, and a query on 10,000 songs takes a few milliseconds.
/// </summary>
public sealed class VoiceMatcher
{
    public const double ConfidentThreshold = 0.82;
    public const double AskFloor = 0.62;
    public const double Margin = 0.06;

    sealed class Ent
    {
        public MatchKind Kind; public object Item = null!; public string Display = "";
        public Form[] Names = []; public Form[] Composites = []; public Form Artist; public string TitleFold = "", ArtistFold = "";
    }

    readonly List<Ent> ents = [];
    readonly Dictionary<string, List<int>> grams = [];
    readonly IReadOnlyList<Track> songs;
    readonly IReadOnlyList<Playlist> playlists;

    public VoiceMatcher(IReadOnlyList<Track> songs, IReadOnlyList<Playlist> playlists)
    {
        this.songs = songs; this.playlists = playlists;
        // tracks: one per (title, artist) once version tags are dropped; the shortest title is kept ("Heroes" over "Heroes (Live)")
        var forms = new Dictionary<string, Form>();
        Form F(string x) { if (!forms.TryGetValue(x, out var f)) forms[x] = f = Phonetic.FormOf(x); return f; }
        var best = new Dictionary<(string, string), Track>();
        foreach (var t in songs)
        {
            var key = (F(t.Title).Fold, F(StripFeat(t.Artist)).Fold);
            if (key.Item1.Length == 0) continue;
            if (!best.TryGetValue(key, out var old) || t.Title.Length < old.Title.Length) best[key] = t;
        }
        foreach (var t in best.Values)
        {
            var title = Phonetic.CleanTitle(t.Title); var af = F(StripFeat(t.Artist));
            var names = new[] { F(t.Title), t.Title.Contains('(') || t.Title.Contains('[') ? F(Phonetic.StripBrackets(t.Title)) : F(t.Title) }.Where(f => f.Fold.Length > 0).Distinct().ToArray();
            Add(new Ent { Kind = MatchKind.Track, Item = t, Display = $"{title} by {t.Artist}", Names = names, Artist = af, TitleFold = names[0].Fold, ArtistFold = af.Fold, Composites = Composite(names, af) }, extra: [af]);
        }
        foreach (var g in songs.Where(t => t.Album.Length > 0).GroupBy(t => (F(t.Album).Fold, F(StripFeat(t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist)).Fold)))
        {
            if (g.Key.Item1.Length == 0) continue;
            var first = g.First(); var list = g.ToList();
            var af = F(StripFeat(first.AlbumArtist.Length > 0 ? first.AlbumArtist : first.Artist));
            var names = new[] { F(first.Album) };
            Add(new Ent { Kind = MatchKind.Album, Item = new FLACie.Core.Group(first.Album, list, first.ArtKey), Display = $"the album {first.Album} by {(first.AlbumArtist.Length > 0 ? first.AlbumArtist : first.Artist)}", Names = names, Artist = af, TitleFold = names[0].Fold, ArtistFold = af.Fold, Composites = Composite(names, af) }, extra: [af]);
        }
        foreach (var g in songs.Where(t => t.Artist.Length > 0).GroupBy(t => F(StripFeat(t.Artist)).Fold))
        {
            if (g.Key.Length == 0) continue;
            var name = g.GroupBy(t => StripFeat(t.Artist)).OrderByDescending(x => x.Count()).First().Key;
            Add(new Ent { Kind = MatchKind.Artist, Item = new FLACie.Core.Group(name, g.ToList(), null), Display = name, Names = [Phonetic.FormOf(name)] });
        }
        foreach (var p in playlists)
        {
            var names = new List<Form> { Phonetic.FormOf(p.Name) };
            var noYear = Regex.Replace(p.Name, @"\s*\b(?:19|20)\d{2}\b\s*$", "").Trim();
            if (noYear.Length > 0 && noYear != p.Name) names.Add(Phonetic.FormOf(noYear));
            Add(new Ent { Kind = MatchKind.Playlist, Item = p, Display = $"your {p.Name} playlist", Names = names.Where(n => n.Fold.Length > 0).ToArray() });
        }
    }

    static string StripFeat(string s) => Phonetic.StripTags(s);

    static Form[] Composite(Form[] names, Form artist) =>
        artist.Fold.Length == 0 ? [] : names.SelectMany(n => new[] { new Form(n.Fold + artist.Fold, Phonetic.Skeleton(n.Fold + artist.Fold)), new Form(artist.Fold + n.Fold, Phonetic.Skeleton(artist.Fold + n.Fold)) }).ToArray();

    void Add(Ent e, Form[]? extra = null)
    {
        if (e.Names.Length == 0) return;
        var id = ents.Count; ents.Add(e);
        var seen = new HashSet<string>();
        foreach (var f in e.Names.Concat(extra ?? []))
        {
            foreach (var g in Grams(f)) if (seen.Add(g)) { if (!grams.TryGetValue(g, out var l)) grams[g] = l = []; l.Add(id); }
        }
    }

    static IEnumerable<string> Grams(Form f)
    {
        var s = f.Fold;
        if (s.Length < 3) { if (s.Length > 0) yield return s; }
        else for (var i = 0; i + 3 <= s.Length; i++) yield return s.Substring(i, 3);
        var k = f.Key;
        for (var i = 0; i + 2 <= k.Length; i++) yield return "~" + k.Substring(i, 2);
    }

    int[] Candidates(IEnumerable<Form> queries, int take = 90)
    {
        var counts = new Dictionary<int, int>();
        foreach (var q in queries)
            foreach (var g in Grams(q))
                if (grams.TryGetValue(g, out var l))
                {
                    if (l.Count > 6000) continue;   // a gram in most of a huge library says nothing
                    foreach (var id in l) counts[id] = counts.GetValueOrDefault(id) + 1;
                }
        return counts.OrderByDescending(kv => kv.Value).Take(take).Select(kv => kv.Key).ToArray();
    }

    // ---- matching ----

    public MatchResult Match(VoiceQuery q)
    {
        var scores = new Dictionary<int, double>();
        void Put(int id, double c) { if (c > scores.GetValueOrDefault(id, -1)) scores[id] = c; }

        // genre / decade: a made-up playlist of what fits
        if (q.Genre is not null || q.Decade is not null)
        {
            var fit = songs.Where(t => (q.Decade is null || InDecade(t.Year, q.Decade)) && (q.Genre is null || GenreFits(t.Genre, q.Genre))).ToList();
            if (fit.Count > 0)
            {
                var what = q.Genre is not null && q.Decade is not null ? $"{q.Decade} {q.Genre}" : q.Genre ?? q.Decade!;
                var pl = new Playlist("voice-" + what.Replace(' ', '-'), what, 0, null, fit.Select(t => new PlaylistEntry(t.Path, t.Title, t.Artist)).ToList());
                return new([new(MatchKind.Playlist, pl, 1.0, q.Genre is not null ? $"some {what}" : $"songs from the {what}")], Decision.Confident);
            }
            if (q.Decade is not null || q.Raw is null && q.Title is null && q.Artist is null && q.Album is null && q.Playlist is null && q.SimilarTo is null && q.Genre is null) return new([], Decision.NotFound);
            q = q with { Raw = q.Genre, Genre = null };   // a genre the library does not know: maybe it is a name
        }

        var forms = new List<Form>();
        Form? raw = q.Raw is { Length: > 0 } r ? Phonetic.FormOf(r) : null;
        Form? title = q.Title is { Length: > 0 } t ? Phonetic.FormOf(t) : null;
        Form? artist = q.Artist is { Length: > 0 } a ? Phonetic.FormOf(a) : null;
        Form? album = q.Album is { Length: > 0 } al ? Phonetic.FormOf(al) : null;
        Form? playlist = q.Playlist is { Length: > 0 } pl2 ? Phonetic.FormOf(pl2) : null;
        Form? like = q.SimilarTo is { Length: > 0 } sl ? Phonetic.FormOf(sl) : null;
        foreach (var f in new[] { raw, title, artist, album, playlist, like }) if (f is { Fold.Length: > 0 } ff) forms.Add(ff);
        if (forms.Count == 0) return new([], Decision.NotFound);

        var explicitKind = title is not null && raw is null || album is not null || playlist is not null || like is not null || (artist is not null && raw is null && title is null);
        foreach (var id in Candidates(forms))
        {
            var e = ents[id];
            switch (e.Kind)
            {
                case MatchKind.Track:
                    if (raw is { } rt && !explicitKind) Put(id, Best(rt, e.Names, e.Composites));
                    if (title is { } tt && album is null && playlist is null) Put(id, WithArtist(Best(tt, e.Names), artist, e));
                    break;
                case MatchKind.Album:
                    if (raw is { } ra && !explicitKind) Put(id, Math.Max(Best(ra, e.Names, e.Composites), Prefix(ra, e)));
                    if (album is { } aa) Put(id, Math.Max(WithArtist(Best(aa, e.Names), artist, e), WithArtist(Prefix(aa, e), artist, e)));
                    break;
                case MatchKind.Artist:
                    if (raw is { } rr && !explicitKind) Put(id, Best(rr, e.Names));
                    if (artist is { } ar && title is null && album is null) Put(id, Best(ar, e.Names));
                    if (like is { } lk) Put(id, Best(lk, e.Names));
                    break;
                case MatchKind.Playlist:
                    if (raw is { } rp && !explicitKind) Put(id, Math.Max(Best(rp, e.Names), Prefix(rp, e)));
                    if (playlist is { } pp) Put(id, Math.Max(Best(pp, e.Names), Prefix(pp, e)));
                    break;
            }
        }

        var ranked = scores.Where(kv => kv.Value >= 0.3)
            .Select(kv => (e: ents[kv.Key], c: Math.Round(kv.Value, 4)))
            .OrderByDescending(x => x.c).ThenBy(x => x.e.Kind switch { MatchKind.Artist => 0, MatchKind.Playlist => 1, MatchKind.Album => 2, _ => 3 })
            .Take(12).ToList();
        // an album of the same name and artist as a song is the same thing to ask about
        ranked = ranked.Where(x => x.e.Kind != MatchKind.Album || !ranked.Any(y => y.e.Kind == MatchKind.Track && y.e.TitleFold == x.e.TitleFold && y.e.ArtistFold == x.e.ArtistFold)).Take(8).ToList();
        var list = ranked.Select(x => new VoiceMatch(x.e.Kind, x.e.Item, x.c, Phonetic.SpeechSafe(x.e.Display))).ToList();
        return new(list, Decide(list));
    }

    static Decision Decide(List<VoiceMatch> r)
    {
        if (r.Count == 0 || r[0].Confidence < AskFloor) return Decision.NotFound;
        var top = r[0];
        if (top.Confidence < ConfidentThreshold) return Decision.Ambiguous;
        if (top.Kind == MatchKind.Artist) return Decision.Confident;
        var rival = r.Skip(1).Any(x => x.Confidence >= ConfidentThreshold && x.Confidence >= top.Confidence - Margin && x.Kind != MatchKind.Artist);
        return rival ? Decision.Ambiguous : Decision.Confident;
    }

    static double Best(Form q, Form[] names, Form[]? more = null)
    {
        var best = 0.0;
        foreach (var n in names) best = Math.Max(best, Phonetic.Similarity(q, n));
        if (more is not null) foreach (var n in more) best = Math.Max(best, Phonetic.Similarity(q, n));
        return best;
    }

    /// <summary>"road trip" is the start of "Road Trip 2024": a name that begins with all that was said (at least four letters) counts as a close match.</summary>
    static double Prefix(Form q, Ent e) => q.Fold.Length >= 4 && e.Names.Any(n => n.Fold.Length > q.Fold.Length && n.Fold.StartsWith(q.Fold, StringComparison.Ordinal)) ? 0.9 : 0;

    static double WithArtist(double title, Form? artist, Ent e)
    {
        if (artist is not { } a) return title;
        var sa = Phonetic.Similarity(a, e.Artist);
        return sa >= 0.8 ? title * (0.85 + 0.15 * sa) : title * 0.55 + sa * 0.2;
    }

    static bool InDecade(int year, string decade) => int.TryParse(decade.TrimEnd('s'), out var d) && year >= d && year < d + 10;

    static bool GenreFits(string genre, string want)
    {
        if (genre.Length == 0) return false;
        var g = Phonetic.Fold(genre); var w = Phonetic.Fold(want);
        return w.Length > 0 && (g == w || g.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>ONE short spoken question for an unsure result; "" when the result is confident; a short apology when nothing fits.</summary>
    public string DisambiguationQuestion(MatchResult r)
    {
        if (r.Decision == Decision.Confident) return "";
        if (r.Decision == Decision.NotFound || r.Ranked.Count == 0) return "Sorry, I couldn't find that.";
        var ask = r.Ranked.Where(x => x.Confidence >= AskFloor).Take(2).ToList();
        return ask.Count >= 2 && ask[1].Confidence >= ask[0].Confidence - 0.15 ? $"Did you mean {ask[0].Spoken}, or {ask[1].Spoken}?" : $"Did you mean {ask[0].Spoken}?";
    }
}
