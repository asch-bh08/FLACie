using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>A line the DJ will say: [Text] for the voice (years and minutes in words, names respelt), [Caption] for screens (the real spellings).
/// [Parts]/[PooledCaption]: the same moment as a pooled line (kept phrases and a name clip, see <see cref="DjPool"/>), said instead when the day's allowance
/// needs it; null when the song has no title and artist to name.</summary>
public sealed record DjLine(DjSlot Slot, string TemplateId, string Text, string Caption, IReadOnlyList<DjPart>? Parts = null, string? PooledCaption = null)
{
    public bool Equals(DjLine? o) => o is not null && Slot == o.Slot && TemplateId == o.TemplateId && Text == o.Text && Caption == o.Caption
        && PooledCaption == o.PooledCaption && (Parts is null ? o.Parts is null : o.Parts is not null && Parts.SequenceEqual(o.Parts));
    public override int GetHashCode() => HashCode.Combine(Slot, TemplateId, Text, Caption);
}

/// <summary>
/// Decides whether the DJ speaks before a song and writes the line (Appendix A.4 of docs/research/ai-dj-free.md). Pure: the clock, the randomness and the memory are
/// passed in, so the same inputs give the same answer. Call <see cref="Plan"/> once per song change: it moves the memory on.
/// </summary>
public static class DjPlanner
{
    public const int MaxWords = 18;
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ReturnAfter = TimeSpan.FromMinutes(15);

    /// <summary>Song changes that pass in silence after a line: 1 when Often, 4 when Rare, none on every song.</summary>
    public static int ForcedSilence(DjChatty c) => c switch { DjChatty.Rare => 4, DjChatty.Every => 0, _ => 1 };
    /// <summary>The chance of speaking once the forced silence is over: 0.5 Often (about 1 song in 3), 0.30 Rare (about 1 in 7.3), always on every song.</summary>
    public static double Chance(DjChatty c) => c switch { DjChatty.Rare => 0.30, DjChatty.Every => 1.0, _ => 0.5 };
    /// <summary>The same, by style: Chill a fifth less often, Hype a fifth more.</summary>
    public static double Chance(DjChatty c, DjStyle s) => Chance(c) * (s switch { DjStyle.Chill => 0.8, DjStyle.Hype => 1.2, _ => 1.0 });
    /// <summary>The chance of the opening line of a session: always when Often, half the time when Rare.</summary>
    public static double OpenChance(DjChatty c) => c == DjChatty.Rare ? 0.5 : 1.0;

    /// <summary>The line before [req.Next], or null to stay quiet. The switches (<see cref="DjRules.EffectiveOn"/>) come first; when they say off, the memory is not touched.
    /// [firstName]: for the hello at the start of a session; [extra]: what the server knows about the song (its feel).</summary>
    public static DjLine? Plan(DjSettings settings, DjRequest req, DjMemory mem, DateTime nowUtc, Random rng, DjPronounce? say = null, string? firstName = null, DjExtra? extra = null)
    {
        if (!DjRules.EffectiveOn(settings, req.Surface, req.PlaylistId, req.QueueDj)) return null;
        say ??= DjPronounce.Default;
        // a session starts with the first song, or after 30 minutes without a question
        // (a queue that has just started has no song before it, but it is not a new session by that: the hello is for the first thing heard in half an hour)
        var open = (req.Prev is null && !req.QueueStart) || mem.LastRequest is not { } last || nowUtc - last >= SessionGap;
        mem.LastRequest = nowUtc;
        if (open) mem.NewSession();

        var f = Facts.Of(req, nowUtc, say);
        var skip = req.SkippedRecently || !req.PrevPlayedOut;
        // "Let's try something different": the first full play after 3 or more skips in the last 5
        var reset = !skip && mem.Skips.Count > 0 && mem.Skips[^1] && mem.Skips.Count(x => x) >= 3;
        if (!req.QueueStart) mem.Skipped(skip);
        var song = ++mem.Songs;
        var heard = f.ArtistKey.Length > 0 && mem.Artists.TryGetValue(f.ArtistKey, out var h) ? h : null;
        if (f.PrevArtistKey.Length > 0 && !mem.Artists.ContainsKey(f.PrevArtistKey)) mem.Artists[f.PrevArtistKey] = new(nowUtc, song - 1, f.PrevTitleWritten);
        if (f.ArtistKey.Length > 0) mem.Artists[f.ArtistKey] = new(nowUtc, song, f.HasTitle ? f.TitleWritten : "");

        f.Feel = DjFeel.Of(extra?.Vibe, extra?.PrevVibe, f.Picked);
        f.Colour = Colour(req.LocalHour, req.Weekday);
        // the person's own choice for this song beats the chance, the forced silence and the quiet rules (but not the switches above)
        var choice = DjRules.SongChoice(settings, req.Next.Title, req.Next.Artist);
        // "Every song": a line before each song, without the draw or the forced silence; only the songs it must never talk over stay quiet
        // a queue that has just started from a playlist or album: "Shuffling Pop... starting with Firework, Katy Perry", once per start (owner, 30 Oct: it never said
        // anything when shuffle was first pressed). It follows the person's choice for the song, stays out of a clip of a song and of spoken word, and keeps to the
        // hello's chance (half the time on Rare); it counts as a line, so the song after it passes in silence
        var line = req.QueueStart && f.Queue.Length > 0 && f.HasTitle && f.HasArtist
            ? (choice == false || (choice is null && QuietQueue(req, f)) ? null
                : choice == true || rng.NextDouble() < OpenChance(settings.Chatty) ? Write(req, mem, f, open, reset, heard, song, nowUtc, rng, say, settings.Style, firstName) : null)
            : choice == false ? null
            : choice == true ? (f.HasTitle || f.HasArtist ? Write(req, mem, f, open, reset, heard, song, nowUtc, rng, say, settings.Style, firstName) : null)
            : settings.Chatty == DjChatty.Every ? (QuietEvery(settings, req, f) ? null : Write(req, mem, f, open, reset, heard, song, nowUtc, rng, say, settings.Style, firstName))
            : Quiet(settings, req, f) ? null
            : (open ? rng.NextDouble() < OpenChance(settings.Chatty) : mem.SilentRun >= ForcedSilence(settings.Chatty) && rng.NextDouble() < Chance(settings.Chatty, settings.Style))
                ? Write(req, mem, f, open, reset, heard, song, nowUtc, rng, say, settings.Style, firstName) : null;
        if (line is null) { mem.SilentRun = Math.Min(mem.SilentRun, DjMemory.Long) + 1; return null; }
        mem.SilentRun = 0;
        mem.Add(new(line.TemplateId, line.Slot, DjTemplates.All.First(t => t.Id == line.TemplateId).Opener, line.Text, nowUtc, line.Caption));
        return line;
    }

    /// <summary>Silent without a draw: a song under a minute, skipping going on (only with "Talk after skips" off: with it on, the listener still hears the DJ
    /// on the songs they let play), the next track of an album playing straight through, spoken word, or nothing to name.</summary>
    static bool Quiet(DjSettings settings, DjRequest req, Facts f) =>
        req.Next.DurationMs is > 0 and < 60_000 || (req.SkippedRecently && !settings.TalkAfterSkips) || NextInAlbum(req.Prev, req.Next) || DjText.IsSpokenWord(req.Next.Genre) || (!f.HasTitle && !f.HasArtist);

    /// <summary>Silent at a queue's start: a song under a minute, spoken word.</summary>
    static bool QuietQueue(DjRequest req, Facts f) => req.Next.DurationMs is > 0 and < 60_000 || DjText.IsSpokenWord(req.Next.Genre);

    /// <summary>Silent on "Every song": a song under a minute, skipping going on with "Talk after skips" off, spoken word, or nothing to name. (The next track
    /// of an album gets its line too.)</summary>
    static bool QuietEvery(DjSettings settings, DjRequest req, Facts f) =>
        req.Next.DurationMs is > 0 and < 60_000 || (req.SkippedRecently && !settings.TalkAfterSkips) || DjText.IsSpokenWord(req.Next.Genre) || (!f.HasTitle && !f.HasArtist);

    /// <summary>The next song is the next track of the same album (same album and album artist, the following track on the same disc, or track 1 of the next disc).</summary>
    public static bool NextInAlbum(DjSong? prev, DjSong next)
    {
        if (prev is null || next.TrackNo <= 0 || prev.TrackNo <= 0) return false;
        if (DjText.Tidy(next.Album) is not { Length: > 0 } al || !al.Equals(DjText.Tidy(prev.Album), StringComparison.OrdinalIgnoreCase)) return false;
        static string AlbumArtist(DjSong s) => DjText.Tidy(s.AlbumArtist.Length > 0 ? s.AlbumArtist : s.Artist);
        if (!AlbumArtist(next).Equals(AlbumArtist(prev), StringComparison.OrdinalIgnoreCase)) return false;
        int pd = Math.Max(1, prev.DiscNo), nd = Math.Max(1, next.DiscNo);
        return (nd == pd && next.TrackNo == prev.TrackNo + 1) || (nd == pd + 1 && next.TrackNo == 1);
    }

    static DjLine? Write(DjRequest req, DjMemory mem, Facts f, bool open, bool reset, DjMemory.Heard? heard, int song, DateTime now, Random rng, DjPronounce say,
        DjStyle style, string? firstName)
    {
        var recentIds = mem.Lines.TakeLast(12).Select(l => l.TemplateId).ToHashSet();
        var recentOpeners = mem.Lines.TakeLast(3).Select(l => l.Opener).Where(o => o.Length > 0).ToHashSet();
        DjSlot? lastSlot = mem.Lines.Count > 0 ? mem.Lines[^1].Slot : null;
        bool Lately(DjSlot s, int n) => mem.Lines.TakeLast(n).Any(l => l.Slot == s);

        List<DjSlot> order;
        if (req.QueueStart && f.Queue.Length > 0 && f.HasArtist && f.HasTitle) order = [DjSlot.Queue, open ? DjSlot.Open : DjSlot.Intro];
        else if (!f.HasArtist) order = [DjSlot.TitleOnly];
        else if (!f.HasTitle) order = [DjSlot.ArtistOnly];
        else if (open) order = [DjSlot.Open, DjSlot.Intro];
        else
        {
            var c = new List<DjSlot> { DjSlot.Intro };
            if (req.PrevPlayedOut && f.PA is not null && !f.SameArtist) c.Add(DjSlot.Bridge);
            // "That was X... and this is Y": now and then, never two in three lines
            if (req.PrevPlayedOut && f.PA is not null && !f.SameArtist && !Lately(DjSlot.Segue, 3)) c.Add(DjSlot.Segue);
            if (f.Colour is not null && !Lately(DjSlot.Time, 8)) c.Add(DjSlot.Time);
            if (f.Feel.Length > 0 && !Lately(DjSlot.Feel, 3)) c.Add(DjSlot.Feel);
            if (f.Why.Count > 0 && mem.Lines.TakeLast(5).Count(l => l.Slot == DjSlot.Why) < 3) c.Add(DjSlot.Why);   // WHY at most 3 of every 6 lines
            if (f.Year > 0) c.Add(DjSlot.Year);
            if (f.Album is not null) c.Add(DjSlot.Album);
            if (f.Genre is not null) c.Add(DjSlot.Genre);
            if (f.SameArtist) c.Add(DjSlot.SameArtist);
            if (heard is not null && !f.SameArtist && song - heard.Song >= 5 && now - heard.At >= ReturnAfter) c.Add(DjSlot.Return);
            // "let's try something different" only about a song the engine picked: in their own playlist or queue the next song is theirs
            if (reset && req.Why is { Length: > 0 }) c.Add(DjSlot.Reset);
            if (f.Minutes > 0) c.Add(DjSlot.Long);
            if (c.Count > 1 && lastSlot is { } ls) c.Remove(ls);   // no slot twice running
            // weighted order, drawn without putting back
            order = [];
            while (c.Count > 0)
            {
                var r = rng.Next(c.Sum(DjTemplates.Weight));
                foreach (var s in c) { r -= DjTemplates.Weight(s); if (r < 0) { order.Add(s); c.Remove(s); break; } }
            }
        }

        var greet = open ? Greeting(req.LocalHour, req.Weekday, style, firstName) : null;
        var (sp, wr) = Values(f, heard, say);
        foreach (var slot in order)
        {
            // the style's own wording first; once those were all said lately, the Classic ones of the slot
            var ts = DjTemplates.Of(slot, style).Where(t => !recentIds.Contains(t.Id) && Met(t.Need, f)).ToList();
            if (ts.Count == 0 && style != DjStyle.Classic) ts = DjTemplates.Of(slot, DjStyle.Classic).Where(t => !recentIds.Contains(t.Id) && Met(t.Need, f)).ToList();
            // right after a song by the same artist the line leaves the name out ("Now Eminem, Without Me by Eminem" said it twice; owner, 30 Oct); only when every
            // such wording was said lately does the artist get a mention again, once
            if (f.NoArtist && slot != DjSlot.SameArtist)
            {
                var quiet = DjTemplates.All.Where(t => t.Slot == slot && t.Need.HasFlag(DjNeed.NoArtist) && !recentIds.Contains(t.Id) && Met(t.Need, f)).ToList();
                if (quiet.Count > 0) ts = quiet;
            }
            for (var i = ts.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (ts[i], ts[j]) = (ts[j], ts[i]); }
            // openings unlike the last 3 lines first
            var tries = ts.Where(t => !recentOpeners.Contains(t.Opener)).Concat(ts.Where(t => recentOpeners.Contains(t.Opener))).ToList();
            // a long "why": both parts first, then the first part only (over 18 words the line is not said)
            var variants = slot == DjSlot.Why && f.Why.Count > 1 ? 2 : 1;
            for (var v = 0; v < variants; v++)
            {
                if (slot == DjSlot.Why) SetWhy(sp, wr, f, v == 0 ? f.Why.Count : 1);
                foreach (var t in tries)
                {
                    var text = DjTemplates.Render(t.Text, sp); var caption = DjTemplates.Render(t.Text, wr);
                    if (text is null || caption is null) continue;
                    if (greet is { } g) { text = g.Say + " " + text; caption = g.Write + " " + caption; }
                    if (DjText.WordCount(text) > MaxWords) continue;
                    var (parts, pooled) = Pooled(slot, f, greet, firstName is not null, style, song);
                    return new DjLine(slot, t.Id, text, caption, parts, pooled);
                }
            }
        }
        return null;
    }

    /// <summary>The same moment as a pooled line: the hello (kept for this person when it has their name) and an opener at the start of a session, "That was..."
    /// with the previous song's name for a segue or a bridge, else an intro phrase; then the song's name. [n] varies the phrase from line to line without
    /// drawing on the planner's randomness. Null when the song has no title and artist.</summary>
    static (IReadOnlyList<DjPart>? Parts, string? Caption) Pooled(DjSlot slot, Facts f, (string Say, string Write)? greet, bool named, DjStyle style, int n)
    {
        if (!f.HasTitle || !f.HasArtist) return (null, null);
        var name = DjPool.Name(f.TitleSay, f.TitleWritten, f.ArtistSay, f.ArtistWritten);
        var parts = new List<DjPart>();
        var caption = "";
        if (greet is { } g) { parts.Add(new DjPart(g.Say, g.Write, named)); caption = g.Write + " "; }
        if (slot is DjSlot.Segue or DjSlot.Bridge && f.PT is not null && f.PA is not null)
        {
            var prev = DjPool.Name(f.PTSay!, f.PT, f.PASay!, f.PA);
            parts.AddRange([new DjPart(DjPool.ThatWas, DjPool.ThatWas), prev, new DjPart(DjPool.AndThisIs, DjPool.AndThisIs), name]);
            return (parts, DjTemplates.Finish(caption + "That was " + prev.Caption.TrimEnd('.') + "... and this is " + name.Caption));
        }
        var pool = slot == DjSlot.Open ? DjPool.Openers(style) : DjPool.Intros(style);
        var phrase = pool[n % pool.Count];
        parts.Add(new DjPart(phrase, phrase)); parts.Add(name);
        return (parts, DjTemplates.Finish(caption + phrase + " " + name.Caption));
    }

    /// <summary>A time of day worth a word, where the listener is: "Late night" (23:00 to 02:59), "Monday morning", "Friday night", "Saturday night",
    /// "Sunday morning"; null at any other time or when it is not known.</summary>
    public static string? Colour(int? hour, int? weekday)
    {
        if (hour is not { } h) return null;
        var d = weekday is { } w and >= 0 and <= 7 ? w % 7 : -1;
        if (h is >= 23 or <= 2) return "Late night";
        if (d == 1 && h is >= 6 and <= 11) return "Monday morning";
        if (d == 0 && h is >= 6 and <= 11) return "Sunday morning";
        if (d == 5 && h is >= 18 and <= 22) return "Friday night";
        if (d == 6 && h is >= 18 and <= 22) return "Saturday night";
        return null;
    }

    static bool Met(DjNeed n, Facts f) =>
        (!n.HasFlag(DjNeed.TenYearsOld) || f.YearAge >= 10) && (!n.HasFlag(DjNeed.SameAlbum) || f.SameAlbum)
        && (!n.HasFlag(DjNeed.Soundtrack) || f.Soundtrack) && (!n.HasFlag(DjNeed.SameGenre) || f.SameGenre)
        && (!n.HasFlag(DjNeed.Picked) || f.Picked) && (!n.HasFlag(DjNeed.NoArtist) || f.NoArtist)
        && (!n.HasFlag(DjNeed.Shuffled) || f.Shuffled) && (!n.HasFlag(DjNeed.InOrder) || !f.Shuffled);

    /// <summary>"Good morning." (06-11), "Good afternoon." (12-17), "Good evening." (18-02), "Saturday morning." at the weekend; null at night or when the device's time is unknown.
    /// [weekday]: .NET DayOfWeek numbering, 0 = Sunday .. 6 = Saturday, from every client (an ISO 7 for Sunday is also taken).</summary>
    public static (string Say, string Write)? Greeting(int? hour, int? weekday) => Greeting(hour, weekday, DjStyle.Classic, null);

    /// <summary>
    /// The hello at the start of a session, with the listener's first name when it is known: Classic "Good evening, Ashley." ("Monday morning, Ashley." on a
    /// Monday, Saturday or Sunday morning), Chill "Evening, Ashley.", Hype "Hey Ashley, good evening." Null at night (03:00 to 05:59) or when the time is unknown.
    /// </summary>
    public static (string Say, string Write)? Greeting(int? hour, int? weekday, DjStyle style, string? firstName)
    {
        if (hour is not { } hr) return null;
        var t = hr switch { >= 6 and <= 11 => "greet1", >= 12 and <= 17 => "greet2", >= 18 and <= 23 or >= 0 and <= 2 => "greet3", _ => null };
        if (t is null) return null;
        var day = weekday switch { 6 => "Saturday", 0 or 7 => "Sunday", 1 => "Monday", _ => null };
        var dayMorning = t == "greet1" && day is not null;
        var classic = dayMorning ? DjTemplates.All.First(x => x.Id == "greet4").Text.Replace("{Day}", day) : DjTemplates.All.First(x => x.Id == t).Text;
        var name = firstName is { Length: > 0 } n ? n : null;
        string text;
        switch (style)
        {
            case DjStyle.Chill:
                var part = dayMorning ? day + " morning" : t switch { "greet1" => "Morning", "greet2" => "Afternoon", _ => "Evening" };
                text = part + (name is null ? "." : ", " + name + ".");
                break;
            case DjStyle.Hype:
                text = (name is null ? "Hey, " : "Hey " + name + ", ") + (dayMorning ? "happy " + day : DjWhy.LowerFirst(classic.TrimEnd('.'))) + ".";
                break;
            default:
                text = name is null ? classic : classic.TrimEnd('.') + ", " + name + ".";
                break;
        }
        return (text, text);
    }

    static (Dictionary<string, string> Say, Dictionary<string, string> Write) Values(Facts f, DjMemory.Heard? heard, DjPronounce say)
    {
        var sp = new Dictionary<string, string>(); var wr = new Dictionary<string, string>();
        void Put(string k, string? s, string? w) { if (!string.IsNullOrWhiteSpace(s) && !string.IsNullOrWhiteSpace(w)) { sp[k] = s; wr[k] = w; } }
        if (f.HasTitle) Put("T", f.TitleSay, f.TitleWritten);
        if (f.HasArtist) Put("A", f.ArtistSay, f.ArtistWritten);
        Put("pT", f.PTSay, f.PT);
        Put("pA", f.PASay, f.PA);
        if (heard is { Title.Length: > 0 } e && !e.Title.Equals(f.TitleWritten, StringComparison.OrdinalIgnoreCase)) Put("eT", Facts.SayTitle(e.Title, say), e.Title);
        Put("Al", f.AlbumSay, f.Album);
        Put("Q", f.QueueSay, f.Queue);
        if (f.Year > 0)
        {
            Put("Y", DjNumbers.Year(f.Year), f.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (DjNumbers.Decade(f.Year) is { } d) { Put("D", d, d); var ad = ("aeiou".Contains(d[0]) ? "An " : "A ") + d; Put("AD", ad, ad); }
        }
        if (f.Genre is { } g) Put("G", g.Say, g.Write);
        if (f.Minutes > 0) Put("M", DjNumbers.Words(f.Minutes), f.Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (f.Colour is { } c) { Put("ToD", c, c); var lc = c.StartsWith("Late") ? c.ToLowerInvariant() : c; Put("tod", lc, lc); }
        if (f.Feel.Length > 0) { var up = char.ToUpperInvariant(f.Feel[0]) + f.Feel[1..]; Put("F", up, up); Put("f", f.Feel, f.Feel); }
        SetWhy(sp, wr, f, f.Why.Count);
        return (sp, wr);
    }

    static void SetWhy(Dictionary<string, string> sp, Dictionary<string, string> wr, Facts f, int parts)
    {
        foreach (var k in new[] { "W", "w", "W1", "w1" }) { sp.Remove(k); wr.Remove(k); }
        if (f.Why.Count == 0) return;
        var ws = f.Why.Take(parts).ToList(); var ss = f.WhySay.Take(parts).ToList();
        sp["W"] = string.Join(", and ", ss); wr["W"] = string.Join(", and ", ws);
        sp["w"] = DjWhy.LowerFirst(sp["W"]); wr["w"] = DjWhy.LowerFirst(wr["W"]);
        sp["W1"] = ss[0]; wr["W1"] = ws[0];
        sp["w1"] = DjWhy.LowerFirst(ss[0]); wr["w1"] = DjWhy.LowerFirst(ws[0]);
    }

    /// <summary>The facts of one song change, cleaned up for speaking and for writing.</summary>
    sealed class Facts
    {
        public bool HasTitle, HasArtist, SameArtist, SameAlbum, SameGenre, Soundtrack;
        public string TitleWritten = "", TitleSay = "", ArtistWritten = "", ArtistSay = "", ArtistKey = "", PrevArtistKey = "", PrevTitleWritten = "";
        public string? PT, PTSay, PA, PASay, Album, AlbumSay;
        public int Year, YearAge, Minutes;
        public (string Say, string Write)? Genre;
        public List<string> Why = [], WhySay = [];
        public string Feel = "";
        /// <summary>The engine picked the next song (the request carries its reason); false for the listener's own playlist, album or queue.</summary>
        public bool Picked;
        /// <summary>The name of the playlist or album a queue has just started from, written and spoken ("" when this is not a queue start or the name is too long to say).</summary>
        public string Queue = "", QueueSay = "";
        public bool Shuffled;
        /// <summary>The artist is not worth naming: the song before was theirs, or the list is named after them ("Shuffling Eminem... starting with Without Me, by Eminem").</summary>
        public bool NoArtist => SameArtist || (Queue.Length > 0 && ArtistKey.Length > 0 && Queue.Contains(ArtistKey, StringComparison.OrdinalIgnoreCase));
        public string? Colour;

        public static string SayTitle(string written, DjPronounce say) => say.Title(written) is { } r ? DjText.Speak(r) : DjText.Speak(say.Phrases(written));

        /// <summary>Written and spoken title; null when there is none to say (blank, or the file name). [tooLong]: over 8 words or 50 characters.</summary>
        static (string Written, string Say)? TitleOf(DjSong s, DjPronounce say, out bool tooLong)
        {
            tooLong = false;
            var raw = DjText.Tidy(s.Title);
            if (raw.Length == 0 || DjText.IsFileName(raw, s.Path)) return null;
            var w = DjText.Title(raw);
            if (w.Length == 0) return null;
            var spoken = say.Title(raw) is { } r ? DjText.Speak(r) : SayTitle(w, say);
            if (spoken.Length == 0) return null;
            tooLong = DjText.WordCount(w) > 8 || w.Length > 50;
            return (w, spoken);
        }

        /// <summary>Written and spoken credit ("Mark Ronson with Amy Winehouse") and its lookup key; null for "Unknown Artist", "Various" or blank.</summary>
        static (string Written, string Say, string Key)? ArtistOf(DjSong s, DjPronounce say)
        {
            var artist = !DjText.NoName(s.Artist) ? s.Artist : !DjText.NoName(s.AlbumArtist) ? s.AlbumArtist : "";
            if (artist.Length == 0) return null;
            var c = DjText.CreditOf(artist, s.Title);
            if (c.Lead.Length == 0 || DjText.NoName(c.Lead)) return null;
            // "Akon, Eminem": two acts in one tag are said "Akon with Eminem" (as the model's facts have them)
            if (c.Guest is null && c.Lead.Split(", ") is { Length: 2 } two && two.All(x => x.Trim().Length > 0)) c = c with { Lead = two[0].Trim(), Guest = two[1].Trim() };
            string Say(string n) => say.Artist(n) is { } r ? DjText.Speak(r) : DjText.Speak(say.Phrases(n));
            var lead = Say(c.Lead);
            if (lead.Length == 0) return null;
            var guest = c.Guest is null ? "" : Say(c.Guest);
            var written = c.Guest is null || guest.Length == 0 ? c.Lead : c.Lead + " with " + c.Guest;
            var spoken = guest.Length == 0 ? lead : lead + " with " + guest;
            return (written, spoken, Matching.PrimaryArtist(c.Lead));
        }

        public static Facts Of(DjRequest req, DateTime now, DjPronounce say)
        {
            var f = new Facts();
            var n = req.Next; var p = req.Prev;
            if (TitleOf(n, say, out var tooLong) is { } t && !tooLong) { f.HasTitle = true; f.TitleWritten = t.Written; f.TitleSay = t.Say; }
            if (ArtistOf(n, say) is { } a) { f.HasArtist = true; f.ArtistWritten = a.Written; f.ArtistSay = a.Say; f.ArtistKey = a.Key; }
            if (p is not null)
            {
                if (TitleOf(p, say, out var pLong) is { } pt) { f.PrevTitleWritten = pt.Written; if (!pLong) { f.PT = pt.Written; f.PTSay = pt.Say; } }
                if (ArtistOf(p, say) is { } pa) { f.PA = pa.Written; f.PASay = pa.Say; f.PrevArtistKey = pa.Key; }
                f.SameArtist = f.ArtistKey.Length > 0 && f.ArtistKey == f.PrevArtistKey;
            }
            // album: not blank, "Unknown Album", Various Artists or the title itself
            var al = DjText.Album(n.Album, out var soundtrack);
            if (al.Length > 0 && !al.Equals(f.TitleWritten, StringComparison.OrdinalIgnoreCase) && !al.Equals(DjText.Title(n.Title), StringComparison.OrdinalIgnoreCase) && DjText.WordCount(al) <= 8 && al.Length <= 50)
            {
                f.Album = al; f.AlbumSay = DjText.Speak(say.Phrases(al)); f.Soundtrack = soundtrack;
                if (p is not null && DjText.Album(p.Album, out _) is { Length: > 0 } pal && pal.Equals(al, StringComparison.OrdinalIgnoreCase)
                    && Matching.PrimaryArtist(p.AlbumArtist.Length > 0 ? p.AlbumArtist : p.Artist) == Matching.PrimaryArtist(n.AlbumArtist.Length > 0 ? n.AlbumArtist : n.Artist))
                    f.SameAlbum = true;
            }
            // year: 1900 to now; not on compilations or remasters
            var remaster = n.Title.Contains("remaster", StringComparison.OrdinalIgnoreCase) || n.Album.Contains("remaster", StringComparison.OrdinalIgnoreCase);
            if (n.Year >= 1900 && n.Year <= now.Year && !remaster && !DjText.IsCompilation(n.Album, n.AlbumArtist)) { f.Year = n.Year; f.YearAge = now.Year - n.Year; }
            f.Genre = DjText.Genre(n.Genre);
            f.SameGenre = f.Genre is { } g && p is not null && DjText.Genre(p.Genre) is { } pg && pg.Write == g.Write;
            if (n.DurationMs >= 7 * 60_000) f.Minutes = (int)Math.Round(n.DurationMs / 60_000.0, MidpointRounding.AwayFromZero);
            f.Picked = req.Why is { Length: > 0 };
            if (req.QueueStart && DjText.Tidy(req.QueueName) is { Length: > 0 } qn && DjText.WordCount(qn) <= 8 && qn.Length <= 50)
            { f.Queue = qn; f.QueueSay = DjText.Speak(say.Phrases(qn)); f.Shuffled = req.Shuffled; }
            f.Why = DjWhy.Parts(req.Why);
            f.WhySay = f.Why.Select(w => DjText.Speak(say.Phrases(w))).ToList();
            if (f.WhySay.Any(w => w.Length == 0)) { f.Why = []; f.WhySay = []; }
            return f;
        }
    }
}

/// <summary>The recommender's reason, made speakable (Appendix A.2): split on "·", parts with digits, banned words or 9+ words dropped, at most 2 kept.</summary>
public static partial class DjWhy
{
    static readonly HashSet<string> Banned = new(StringComparer.OrdinalIgnoreCase)
        { "vibe", "vibes", "banger", "bangers", "tune", "tunes", "epic", "amazing", "awesome", "iconic", "love", "favourite", "favourites", "favorite", "favorites", "i", "we", "my" };

    [GeneratedRegex(@"^new to you\b", RegexOptions.IgnoreCase)] private static partial Regex NewToYou();

    public static List<string> Parts(string? why)
    {
        var res = new List<string>();
        if (string.IsNullOrWhiteSpace(why)) return res;
        foreach (var raw in why.Split('·'))
        {
            var p = DjText.Tidy(raw).Trim('.', '!', '?', ',', ';', ':', ' ');
            if (p.Length == 0 || p.Any(char.IsDigit)) continue;
            var words = p.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 9) continue;
            // "I'm", "we've": the part before the apostrophe counts
            if (words.Any(w => Banned.Contains(new string(w.Split('\'', '’')[0].Where(char.IsLetter).ToArray())))) continue;
            p = NewToYou().Replace(p, "Something new for you");
            res.Add(char.ToUpperInvariant(p[0]) + p[1..]);
            if (res.Count == 2) break;
        }
        return res;
    }

    /// <summary>The first letter in lower case, unless the first word is in capitals ("ABBA fans ...").</summary>
    public static string LowerFirst(string s) => s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;
}
