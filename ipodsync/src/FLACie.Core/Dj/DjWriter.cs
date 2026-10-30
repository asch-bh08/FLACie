using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>The facts a model-written line may use: what the planner knows about this song change, already tidied. Blank fields are unknown and never mentioned.</summary>
/// <remarks>[Genre], [ReleaseType] ("single", "live album"), [FirstYear] (when it first came out, only if before [Year]), [Label], [Feel] ("a big one"),
/// [Colour] ("Monday morning"), [FirstName] (only at the start of a session) and [Style]: from MusicBrainz, the vibe index and the account.
/// [Request]: what the listener asked the DJ for lately ("car songs"), so a line can keep it going; [Context]: the shared <see cref="DjContext"/> summary.
/// [IntroMs] (0 = not known) and [TargetWords] (0 = no target): the song's intro before the singing, and how many words fit it (<see cref="DjTiming.TargetWords"/>).
/// [SameArtistRun]: the previous song was by the same lead artist (an artist's playlist, an album, a run), so the name need not be said again (owner, 30 Oct: "Now
/// Eminem, Without Me by Eminem... like who else is it gonna be by").</remarks>
public sealed record DjFacts(string Title, string Artist, string ArtistLead, string? Guest, string Album, int Year, string PrevTitle, string PrevArtist,
    bool Open, string TimeOfDay, string Day, string Mood, string Why, string Find, string TemplateLine,
    string Genre = "", string ReleaseType = "", int FirstYear = 0, string Label = "", string Feel = "", string Colour = "", string FirstName = "", DjStyle Style = DjStyle.Classic, int IntroMs = 0, int TargetWords = 0,
    string Request = "", string Context = "", string Source = "", bool SameArtistRun = false);

/// <summary>
/// Writes the DJ's line with a small text model (Workers AI) instead of a template: the prompt (a DJ character plus the facts), and the checks on what comes back.
/// The planner still decides when to speak; this only changes how the line is worded. Anything doubtful is refused, and the caller says the template line instead.
/// Pure: the model call itself is the server's (<c>CloudflareVoice.WriteAsync</c>).
/// </summary>
public static partial class DjWriter
{
    /// <summary>A model line is at most this many words (the template limit is 18).</summary>
    public const int MaxWords = 25;
    /// <summary>How long the model may take before the template line is used.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2.5);
    /// <summary>How many recent lines go into the prompt (to be avoided).</summary>
    public const int Recent = 6;

    public const string Character =
        "You are the voice of a personal music station, talking like a friend who knows this listener's taste: relaxed, warm, casual and personal, never like a radio announcer. "
        + "Write in British English, the way people really talk: contractions, and light fillers such as \"alright\", \"okay\" or \"so\" now and then. "
        + "Use \"...\" for a natural pause, at most once in the line. "
        + "Never cringe, never over the top, never salesy. No radio clichés, no \"up next we have\", no \"ladies and gentlemen\". No emojis, no hashtags, no exclamation marks, no quotation marks, no stage directions.\n"
        + "Write ONE line to say just before the next song: short and personal, one or two sentences (a pause with \"...\" counts as a sentence), at most 25 words in all. "
        + "Make it personal only from the facts: why the song was picked, whether they love it or it is new, what they asked for, the time of day, the song before. "
        + "Name the song and the artist naturally, the way a friend says them (for example: Smack That, Akon with Eminem). "
        + "Use only the facts given. Never invent anything: no chart positions, awards, samples, stories or other songs, and never say they played something a certain number of times or lately unless the facts say so. Mention the year or the album only if given. "
        + "Never say or suggest you chose, changed or swapped a song, or that you are reacting to a skip, unless the facts say you picked it. "
        // owner, 30 Oct: "Now Eminem, Without Me by Eminem", "because you skipped this song" in a playlist, "slowing down" before a faster song
        + "Never mention a skipped song or a change of direction, and no steering of the mood (slowing down, lifting it), unless the facts say you picked this song: in their own playlist they chose the order, and a feel only describes the song. "
        + "Never say the artist's name twice in one line; after a song by the same artist, leave the name out or say it once. "
        // the voice ran song names into the words around them (owner, 30 Oct): commas give it a breath either side
        + "Set a song title off with commas when words follow it, so the voice pauses around it (\"Here's Chandelier, from Sia, to lift things\"). "
        + "Do not start the way any of the recent lines start, and do not repeat their wording. Reply with the line only.";

    /// <summary>What each style adds to the character: the tone, and for Chill a shorter line.</summary>
    public static string Tone(DjStyle s) => s switch
    {
        DjStyle.Chill => "Style: chill. Softer and slower, a friend talking low late at night; fewer words (about 15), pauses with \"...\", nothing loud.",
        DjStyle.Hype => "Style: hype. A little more energy, a friend who is pleased to play this for you; still casual and short, never a shouty radio voice, never cheesy.",
        _ => "Style: classic. A relaxed, warm friend who knows your taste, easy and chatty, a few words at a time.",
    };

    static string Clean(string? s) => DjText.Tidy(s);

    /// <summary>The facts of this song change, in their written forms. [line] is the planner's own (template) line, given as a fallback idea for the model.
    /// [extra]: what the server knows for certain about the song (MusicBrainz, its feel); [firstName]: said only in a hello.</summary>
    public static DjFacts FactsOf(DjRequest req, DjLine line, DateTime nowUtc, DjExtra? extra = null, DjStyle style = DjStyle.Classic, string? firstName = null)
    {
        var n = req.Next; var p = req.Prev;
        string TitleOf(DjSong? s) => s is null || Clean(s.Title) is not { Length: > 0 } t || DjText.IsFileName(t, s.Path) ? "" : DjText.Title(t);
        (string Written, string Lead, string? Guest) ArtistOf(DjSong? s)
        {
            if (s is null) return ("", "", null);
            var a = !DjText.NoName(s.Artist) ? s.Artist : !DjText.NoName(s.AlbumArtist) ? s.AlbumArtist : "";
            if (a.Length == 0) return ("", "", null);
            var c = DjText.CreditOf(a, s.Title);
            string lead = c.Lead; var guest = c.Guest;
            // "Akon, Eminem": two acts in one tag are said "Akon with Eminem"
            if (guest is null && lead.Split(", ") is { Length: 2 } two && two.All(x => x.Trim().Length > 0)) { lead = two[0].Trim(); guest = two[1].Trim(); }
            return (guest is null ? lead : lead + " with " + guest, lead, guest);
        }
        var (artist, lead, g) = ArtistOf(n);
        var sameRun = lead.Length > 0 && ArtistOf(p).Lead is { Length: > 0 } pl && Matching.PrimaryArtist(lead).Equals(Matching.PrimaryArtist(pl), StringComparison.OrdinalIgnoreCase);
        var album = DjText.Album(n.Album, out _);
        if (album.Equals(TitleOf(n), StringComparison.OrdinalIgnoreCase)) album = "";
        var remaster = n.Title.Contains("remaster", StringComparison.OrdinalIgnoreCase) || n.Album.Contains("remaster", StringComparison.OrdinalIgnoreCase);
        var year = n.Year >= 1900 && n.Year <= nowUtc.Year && !remaster && !DjText.IsCompilation(n.Album, n.AlbumArtist) ? n.Year : 0;
        var tod = req.LocalHour switch { >= 5 and <= 11 => "morning", >= 12 and <= 17 => "afternoon", >= 18 and <= 22 => "evening", null => "", _ => "late at night" };
        var day = req.Weekday is { } wd and >= 0 and <= 7 ? ((DayOfWeek)(wd % 7)).ToString() : "";
        // where the song comes from: only a song the engine picked (it carries a reason) is the DJ's own choice. In their playlist or queue the next song
        // is simply theirs, and a skip is not mentioned (owner, 28 Oct: "skipped that last one, playing this one" about the playlist's own next song)
        var picked = req.Why is { Length: > 0 };
        var source = picked ? "you picked it for them" : req.PlaylistId is { Length: > 0 } ? "their own playlist (they chose it and its order; you did not pick this song)"
            : "their own queue (they chose it; you did not pick this song)";
        // (the reset is only ever planned for an engine pick; checked here too, so no other route can put skips into a playlist's prompt)
        var mood = picked && line.Slot == DjSlot.Reset ? "they skipped several songs in a row and have now let one play: a change of direction"
            : picked && (req.SkippedRecently || !req.PrevPlayedOut) ? "they have just skipped a song" : "settled in, letting songs play";
        var why = string.Join("; ", DjWhy.Parts(req.Why));
        // loved or new: only what the account's listening says (DjRequest.Bond), never guessed from the recommender's words
        var find = req.Bond switch { DjBond.NewFind => "a new find for them, never played before", DjBond.Loved => "a song they love and play a lot", _ => "" };
        var open = line.Slot == DjSlot.Open || req.Prev is null;
        var fy = extra?.OriginalYear ?? 0;
        var first = fy >= 1900 && fy <= nowUtc.Year && (year == 0 || fy < year) && !remaster ? fy : 0;
        var intro = DjIntro.Usable(extra?.Vibe?.Intro);
        return new(TitleOf(n), artist, lead, g, album, year, TitleOf(p), ArtistOf(p).Written, open,
            tod, day, mood, why, find, line.Caption,
            DjText.Genre(n.Genre)?.Write ?? "", Clean(extra?.ReleaseType), first, Clean(extra?.Label), DjFeel.Of(extra?.Vibe, extra?.PrevVibe, picked),
            DjPlanner.Colour(req.LocalHour, req.Weekday) ?? "", open && firstName is { Length: > 0 } fn ? fn : "", style,
            intro ?? 0, DjTiming.TargetWords(intro), Source: source, SameArtistRun: sameRun);
    }

    /// <summary>The most words a model line may have: <see cref="MaxWords"/>, or a few over the intro's target when there is one (a little over only leads the song in).</summary>
    public static int WordLimit(DjFacts f) => f.TargetWords > 0 ? Math.Min(MaxWords, f.TargetWords + 3) : MaxWords;

    /// <summary>The system and user messages: the DJ's character, then the facts and the recent lines to avoid.</summary>
    public static (string System, string User) Prompt(DjFacts f, IReadOnlyList<string> recent)
    {
        var sb = new StringBuilder();
        void Put(string k, string v) { if (v.Length > 0) sb.Append("- ").Append(k).Append(": ").Append(v).Append('\n'); }
        sb.Append("Facts:\n");
        Put("Next song", f.Title);
        Put("Where it comes from", f.Source);
        Put("Artist", f.Artist);
        Put("Album", f.Album);
        if (f.Year > 0) Put("Year", f.Year.ToString(CultureInfo.InvariantCulture));
        if (f.FirstYear > 0) Put("First released", f.FirstYear.ToString(CultureInfo.InvariantCulture));
        Put("Released as", f.ReleaseType.Length > 0 ? ("aeiou".Contains(char.ToLowerInvariant(f.ReleaseType[0])) && !f.ReleaseType.StartsWith("EP") ? "an " : "a ") + f.ReleaseType : "");
        Put("Label", f.Label);
        Put("Genre", f.Genre);
        Put("How it sounds", f.Feel);
        Put("Previous song", f.PrevTitle.Length > 0 && f.PrevArtist.Length > 0 && !f.SameArtistRun ? f.PrevTitle + " by " + f.PrevArtist : f.PrevTitle);
        if (f.SameArtistRun) Put("Same artist", "the previous song was by the same artist, so do not say the artist's name again: the title alone is enough, or say the name just once");
        Put("Moment", f.Open ? "the start of a listening session: a short hello first" + (f.FirstName.Length > 0 ? ", by first name" : "") : "between songs, mid-session");
        Put("Listener's first name", f.FirstName);
        Put("Time of day", f.TimeOfDay);
        Put("Day", f.Day);
        Put("Worth a word about the time", f.Colour);
        Put("Listener", f.Mood);
        Put("Why this song was picked", f.Why);
        Put("This song is", f.Find);
        Put("They asked you for (you may keep it going, in your own words)", f.Request);
        Put("A plain version of the line (do better than this)", f.TemplateLine);
        if (f.TargetWords > 0)
            Put("Length", $"the song's intro before the singing is about {Math.Round(f.IntroMs / 1000.0).ToString(CultureInfo.InvariantCulture)} seconds, so keep the line to about {f.TargetWords} words");
        if (f.Context.Length > 0) sb.Append("What you know about this listener right now (use only if it fits):\n").Append(f.Context);
        var rs = recent.Where(r => r.Length > 0).TakeLast(Recent).ToList();
        if (rs.Count > 0)
        {
            sb.Append("Recent lines (do not repeat them or start the same way):\n");
            foreach (var r in rs) sb.Append("- ").Append(r).Append('\n');
        }
        sb.Append("Write the line now.");
        return (Character + "\n" + Tone(f.Style), sb.ToString());
    }

    [GeneratedRegex(@"[*_`#~]|\[|\]")] private static partial Regex Markdown();
    [GeneratedRegex(@"^\s*(dj|presenter|line|host|you)\s*:\s*", RegexOptions.IgnoreCase)] private static partial Regex Speaker();
    [GeneratedRegex(@"https?:|www\.|@\w", RegexOptions.IgnoreCase)] private static partial Regex Link();
    [GeneratedRegex(@"[.!?]+(\s|$)")] private static partial Regex SentenceEnd();
    [GeneratedRegex(@"\p{N}+")] private static partial Regex Number();

    /// <summary>Words a line may start with a capital mid-sentence without being a fact (days, greetings, "I").</summary>
    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "I", "I'm", "I've", "I'd", "I'll", "FLACie", "DJ", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "Good", "Morning", "Afternoon", "Evening", "OK", "Hello", "Hey",
    };

    static readonly string[] Cheesy = ["vibe", "vibes", "banger", "bangers", "epic", "lit", "fire", "slaps", "certified", "legendary", "iconic", "buckle up", "get ready"];

    /// <summary>The engine picked this song (it has a reason): the only case where the DJ may speak of a skip, a change of direction or steering the mood.</summary>
    static bool Picked(DjFacts f) => f.Why.Length > 0 || f.Source.StartsWith("you picked", StringComparison.Ordinal);

    /// <summary>What the DJ never says about a song the listener chose themselves (owner, 30 Oct).</summary>
    static readonly string[] OwnNever =
    [
        "skip", "skips", "skipped", "skipping", "direction", "something different", "something else", "changing it up", "changing things up", "switching it up",
        "mixing it up", "slowing", "slowing it down", "slow it down", "slow things down", "picking up the pace", "pick it up", "pick up the pace", "lift it", "lifting it",
        "i picked", "i've picked", "i chose", "i've chosen", "i swapped", "i've swapped", "i changed", "i've changed", "swapped", "swapping", "up next",
    ];

    static string Bare(string w) => new string(w.Trim().Where(c => char.IsLetterOrDigit(c) || c is '\'' or '’' or '-' or '$' or '.').ToArray()).Trim('.', '\'', '’', '-');

    /// <summary>
    /// The model's answer made safe, or null to use the template line: first line only, quotes, markdown and "DJ:" stripped; refused when empty, over
    /// <see cref="MaxWords"/> words or two sentences, with links, emoji or hashtags, without the song or the artist, with a name or number that is not in the facts,
    /// with cheesy words, or starting like (or repeating) a recent line.
    /// </summary>
    public static string? Check(string? raw, DjFacts f, IReadOnlyList<string> recent)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
        if (Link().IsMatch(s) || s.Contains('#')) return null;
        s = Markdown().Replace(s, "");
        s = Speaker().Replace(s, "");
        s = DjText.Tidy(s.Replace('\u201C', '"').Replace('\u201D', '"').Replace('\u2018', '\'').Replace('\u2019', '\''));
        // quotes round the whole line, or round a title, go
        s = DjText.Tidy(s.Replace("\"", ""));
        if (s.StartsWith('\'') && s.EndsWith('\'')) s = s[1..^1].Trim();
        if (s.Length < 4) return null;
        foreach (var r in s.EnumerateRunes())
            if (Rune.GetUnicodeCategory(r) is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse) return null;
        if (DjText.WordCount(s) > WordLimit(f) || SentenceEnd().Matches(s).Count > 2) return null;
        var low = s.ToLowerInvariant();
        // it names the song or the artist
        bool Has(string x) => x.Length > 0 && low.Contains(x.ToLowerInvariant());
        if (!Has(f.Title) && !Has(f.ArtistLead)) return null;
        // the lead artist is said once at most (a title that holds their name is not a second time): "Now Eminem, Without Me by Eminem" is refused
        if (f.ArtistLead.Length > 0)
        {
            var rest = f.Title.Length > 0 ? low.Replace(f.Title.ToLowerInvariant(), " ") : low;
            if (Regex.Matches(rest, @"(?<![\p{L}\p{N}])" + Regex.Escape(f.ArtistLead.ToLowerInvariant()) + @"(?![\p{L}\p{N}])").Count > 1) return null;
        }
        // in the listener's own playlist, album or queue the DJ chose nothing: no skips, no change of direction, no steering, no "I picked" (checked against the
        // song's own names, so a title such as "Skip to My Lou" is its own business)
        if (!Picked(f))
        {
            var names = string.Join(" \n ", new[] { f.Title, f.Artist, f.Album, f.PrevTitle, f.PrevArtist }).ToLowerInvariant();
            if (OwnNever.Any(c => Word(low, c) && !Word(names, c))) return null;
        }
        // every capitalised word mid-sentence and every number comes from the facts
        var known = string.Join(" \n ", new[] { f.Title, f.Artist, f.Album, f.PrevTitle, f.PrevArtist, f.Why, f.Day, f.TimeOfDay, f.TemplateLine,
            f.Year > 0 ? f.Year.ToString(CultureInfo.InvariantCulture) : "", f.FirstYear > 0 ? f.FirstYear.ToString(CultureInfo.InvariantCulture) : "",
            f.Genre, f.ReleaseType, f.Label, f.Feel, f.Colour, f.FirstName, f.Request, f.Context }).ToLowerInvariant();
        static bool Word(string text, string w) => Regex.IsMatch(text, @"(?<![\p{L}])" + Regex.Escape(w) + @"(?![\p{L}])");
        if (Cheesy.Any(c => Word(low, c) && !Word(known, c))) return null;
        foreach (Match m in Number().Matches(s))
        {
            var d = m.Value;
            bool Part(int y) => y > 0 && y.ToString(CultureInfo.InvariantCulture).EndsWith(d, StringComparison.Ordinal) && d.Length >= 2;
            var yearPart = Part(f.Year) || Part(f.FirstYear);
            if (!yearPart && !known.Contains(d)) return null;
        }
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = true;
        foreach (var raw2 in words)
        {
            var w = Bare(raw2);
            var possessive = w.EndsWith("'s", StringComparison.OrdinalIgnoreCase) ? w[..^2] : w;
            if (!start && w.Length > 0 && char.IsUpper(w[0]) && !Allowed.Contains(w) && !Allowed.Contains(possessive)
                && !Regex.IsMatch(known, @"(?<![\p{L}\p{N}])" + Regex.Escape(possessive.ToLowerInvariant()) + @"(?![\p{L}\p{N}])"))
                return null;
            start = raw2.EndsWith('.') || raw2.EndsWith('!') || raw2.EndsWith('?') || raw2.EndsWith(':');
        }
        // not a recent line again, and not the same opening as the last one
        static string Opening(string x) => string.Join(' ', x.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(Bare));
        foreach (var r in recent)
            if (r.Equals(s, StringComparison.OrdinalIgnoreCase)) return null;
        if (recent.Count > 0 && recent[^1].Length > 0 && Opening(recent[^1]) == Opening(s)) return null;
        return s;
    }

    /// <summary>
    /// The spoken form of a checked line: the artist and the title respelt the way the planner says them (exact entries first), every other pronunciation entry
    /// wherever it appears, years in words, and the clean-up of <see cref="DjText.Speak"/> on the names.
    /// </summary>
    public static string Speak(string caption, DjFacts f, DjPronounce say)
    {
        var slots = new List<string>();
        var s = caption;
        string Hold(string spoken) { slots.Add(spoken); return "\u0001" + (slots.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0002"; }
        void Swap(string written, string spoken)
        {
            if (written.Length == 0 || spoken.Length == 0) return;
            s = Regex.Replace(s, @"(?<![\p{L}\p{N}])" + Regex.Escape(written) + @"(?![\p{L}\p{N}])", _ => Hold(spoken), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        string Name(string n, Func<string, string?> exact) => exact(n) is { } r ? DjText.Speak(r) : DjText.Speak(say.Phrases(n));
        // longest first, so a title that contains the artist's name is taken whole
        var names = new List<(string W, string S)>();
        if (f.Title.Length > 0) names.Add((f.Title, Name(f.Title, say.Title)));
        if (f.ArtistLead.Length > 0) names.Add((f.ArtistLead, Name(f.ArtistLead, say.Artist)));
        if (f.Guest is { Length: > 0 } g) names.Add((g, Name(g, say.Artist)));
        if (f.PrevArtist.Length > 0) names.Add((f.PrevArtist, Name(f.PrevArtist, say.Artist)));
        foreach (var (w, sp) in names.OrderByDescending(x => x.W.Length)) Swap(w, sp);
        s = say.Phrases(s);
        foreach (var y in new[] { f.Year, f.FirstYear }.Where(y => y > 0))
            s = Regex.Replace(s, @"(?<!\p{N})" + y.ToString(CultureInfo.InvariantCulture) + @"(?!\p{N})", DjNumbers.Year(y));
        s = Regex.Replace(s, "\u0001(\\d+)\u0002", m => slots[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
        return DjText.Tidy(s);
    }
}
