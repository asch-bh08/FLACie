namespace FLACie.Core;

/// <summary>The kinds of line the DJ says (Appendix A.1). Each is used only when its facts are known.</summary>
public enum DjSlot { Open, Greet, Bridge, Intro, Why, Year, Album, Genre, SameArtist, Return, Reset, Long, ArtistOnly, TitleOnly, Segue, Time, Feel, Queue }

/// <summary>Extra conditions a template has beyond its placeholders (a placeholder with no value already rules a template out). [Picked]: the engine picked the
/// song, so the line may steer ("let's lift it", a change of direction); in the listener's own playlist or queue it may not. [NoArtist]: said only after a song
/// by the same artist, and names no artist.</summary>
[Flags]
public enum DjNeed { None = 0, TenYearsOld = 1, SameAlbum = 2, Soundtrack = 4, SameGenre = 8, Picked = 16, NoArtist = 32, Shuffled = 64, InOrder = 128 }

/// <summary>
/// One line pattern. Placeholders, filled with the spoken or the written form: {T}/{A} the next song's title and artist, {pT}/{pA} the previous song's, {eT} a song by the
/// same artist heard earlier, {Al} album, {Q} the playlist or album a queue has just started from, {Y} year, {D} decade, {AD} "A seventies" / "An eighties", {G} genre, {M} minutes, {W} the "why", {w} the same starting in lower case,
/// {W1}/{w1} its first part only, {Day} Saturday or Sunday, {ToD}/{tod} a notable time ("Monday morning", "late night"), {F}/{f} the song's feel ("a big one").
/// [Style]: null for Classic (and for every style when that style has no template of the slot); Chill or Hype for their own wording.
/// </summary>
public sealed record DjTemplate(string Id, DjSlot Slot, string Text, DjNeed Need = DjNeed.None, DjStyle? Style = null)
{
    /// <summary>The first word before any placeholder, lower case ("here's", "from"); "" when the line opens with a placeholder. Used to vary how lines start.</summary>
    public string Opener { get; } = Text.IndexOf('{') is var i && i == 0 ? "" : (i < 0 ? Text : Text[..i]).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim(',', '.', ':').ToLowerInvariant() ?? "";
}

/// <summary>The 60 templates of Appendix A.2, in a friend's voice: relaxed, casual and short, with "alright", "okay" and "so" now and then and "..." for a breath. Never "!", never an announcer's "up next we have".</summary>
public static class DjTemplates
{
    public static readonly IReadOnlyList<DjTemplate> All =
    [
        new("open1", DjSlot.Open, "Alright, we're starting with {T} by {A}."),
        new("open2", DjSlot.Open, "So... first up, {T} by {A}."),
        new("open3", DjSlot.Open, "Okay, starting with {A}... here's {T}."),
        new("open4", DjSlot.Open, "Alright, let's begin with {T}, by {A}."),
        new("open5", DjSlot.Open, "So, opening with {A}... {T}."),

        new("greet1", DjSlot.Greet, "Good morning."),
        new("greet2", DjSlot.Greet, "Good afternoon."),
        new("greet3", DjSlot.Greet, "Good evening."),
        new("greet4", DjSlot.Greet, "{Day} morning."),

        new("bridge1", DjSlot.Bridge, "Alright, that was {pT} by {pA}. Here's {T} by {A}."),
        new("bridge2", DjSlot.Bridge, "{pT}, from {pA}. Okay, now it's {A}... with {T}."),
        new("bridge3", DjSlot.Bridge, "So, after {pA}, here's {A} with {T}."),
        new("bridge4", DjSlot.Bridge, "That was {pT}. Okay, here's {T} by {A}."),
        new("bridge5", DjSlot.Bridge, "You just heard {pA}. Now, {T} by {A}."),
        new("bridge6", DjSlot.Bridge, "From {pA} to {A}... this is {T}."),
        new("bridge7", DjSlot.Bridge, "That was {pT}. Alright, {A} next, with {T}."),

        new("intro1", DjSlot.Intro, "Alright, here's {T} by {A}."),
        new("intro2", DjSlot.Intro, "Okay... {T}, by {A}."),
        new("intro3", DjSlot.Intro, "So, this is {A}, with {T}."),
        new("intro4", DjSlot.Intro, "Here's one for you: {T} from {A}."),
        new("intro5", DjSlot.Intro, "Okay, {A}... {T}."),

        new("why1", DjSlot.Why, "{W}. So, here's {T} by {A}."),
        new("why2", DjSlot.Why, "{W}... so here's {T} by {A}."),
        new("why3", DjSlot.Why, "Alright, {T} by {A}. {W}."),
        new("why4", DjSlot.Why, "Okay, this is {T} by {A}... {w1}."),
        new("why5", DjSlot.Why, "Picked this because {w}... {T} by {A}."),
        new("why6", DjSlot.Why, "That was {pT}. {W}... here's {T}."),
        new("why7", DjSlot.Why, "Okay, this one's for you: {w1}. It's {A} with {T}."),

        new("year1", DjSlot.Year, "This one's from {Y}... {T} by {A}."),
        new("year2", DjSlot.Year, "Taking it back to {Y} now. {T} by {A}.", DjNeed.TenYearsOld),
        new("year3", DjSlot.Year, "{T} by {A}... from {Y}."),
        new("year4", DjSlot.Year, "Alright, here's {A} in {Y}, with {T}."),
        new("year5", DjSlot.Year, "{AD} one now... {T} by {A}."),
        new("year6", DjSlot.Year, "Something from the {D}. {A}, with {T}."),

        new("album1", DjSlot.Album, "From the album {Al}... here's {T} by {A}."),
        new("album2", DjSlot.Album, "{T}, by {A}. That's from {Al}."),
        new("album3", DjSlot.Album, "Alright, {A} with {T}, from {Al}."),
        new("album4", DjSlot.Album, "Another one from {Al}... {T}.", DjNeed.SameAlbum),
        new("album5", DjSlot.Album, "Okay, from the {Al} soundtrack... {T} by {A}.", DjNeed.Soundtrack),

        new("genre1", DjSlot.Genre, "Some {G} now... {T} by {A}."),
        new("genre2", DjSlot.Genre, "Staying with {G}. Here's {T} by {A}.", DjNeed.SameGenre),
        new("genre3", DjSlot.Genre, "Okay, a bit of {G}... {A}, with {T}."),

        new("same1", DjSlot.SameArtist, "More {A}... this is {T}."),
        new("same2", DjSlot.SameArtist, "Alright, staying with {A}. Here's {T}."),
        new("same3", DjSlot.SameArtist, "{A} again... this time, {T}."),
        new("same4", DjSlot.SameArtist, "Okay, another one from {A}... {T}."),

        new("return1", DjSlot.Return, "Alright, back to {A} now... here's {T}."),
        new("return2", DjSlot.Return, "Okay, going back to {A}. {T}."),
        new("return3", DjSlot.Return, "We had {A} earlier, with {eT}... now it's {T}."),

        new("reset1", DjSlot.Reset, "Okay, let's try something different... {T} by {A}.", DjNeed.Picked),
        new("reset2", DjSlot.Reset, "Alright, changing it up. Here's {T} by {A}.", DjNeed.Picked),
        new("reset3", DjSlot.Reset, "So, something else. This is {A}, with {T}.", DjNeed.Picked),

        new("long1", DjSlot.Long, "Okay, this one's a long one, about {M} minutes... {T} by {A}."),
        new("long2", DjSlot.Long, "Settle in... this one's about {M} minutes. {T} by {A}."),

        new("artist1", DjSlot.ArtistOnly, "Alright, here's {A}."),
        new("artist2", DjSlot.ArtistOnly, "Okay, something from {A}."),
        new("artist3", DjSlot.ArtistOnly, "This one's from {A}."),

        new("title1", DjSlot.TitleOnly, "Alright, here's {T}."),
        new("title2", DjSlot.TitleOnly, "Okay... {T}."),
        new("title3", DjSlot.TitleOnly, "This one's called {T}."),

        // ---- segues, time of day, the song's feel (every style) ----
        new("seg1", DjSlot.Segue, "So, that was {pT}... and this is {T}, by {A}."),
        new("seg2", DjSlot.Segue, "{pT}, from {pA}... okay, now, {T}."),
        new("seg3", DjSlot.Segue, "That was {pA}... and now, {A}, with {T}."),
        new("time1", DjSlot.Time, "{ToD}... so here's {T} by {A}."),
        new("time2", DjSlot.Time, "Okay, for a {tod}... {T} by {A}."),
        new("feel1", DjSlot.Feel, "{F} now... {T} by {A}."),
        new("feel2", DjSlot.Feel, "Alright, here's {T} by {A}... {f}."),

        // ---- after a song by the same artist (a run, an artist's playlist): the title alone, since "Now Eminem, Without Me by Eminem" says it twice (owner, 30 Oct) ----
        new("intro-n1", DjSlot.Intro, "Alright... {T}.", DjNeed.NoArtist),
        new("intro-n2", DjSlot.Intro, "Okay, now, {T}.", DjNeed.NoArtist),
        new("intro-n3", DjSlot.Intro, "Here's {T}, next.", DjNeed.NoArtist),
        new("why-n1", DjSlot.Why, "{W}. So, here's {T}.", DjNeed.NoArtist),
        new("why-n2", DjSlot.Why, "{W}... so, {T}.", DjNeed.NoArtist),
        new("year-n1", DjSlot.Year, "This one's from {Y}... {T}.", DjNeed.NoArtist),
        new("year-n2", DjSlot.Year, "{T}... from {Y}.", DjNeed.NoArtist),
        new("album-n1", DjSlot.Album, "From the album {Al}... here's {T}.", DjNeed.NoArtist),
        new("album-n2", DjSlot.Album, "{T}. That's from {Al}.", DjNeed.NoArtist),
        new("genre-n1", DjSlot.Genre, "Some {G} now... {T}.", DjNeed.NoArtist),
        new("feel-n1", DjSlot.Feel, "{F} now... {T}.", DjNeed.NoArtist),
        new("time-n1", DjSlot.Time, "{ToD}... so here's {T}.", DjNeed.NoArtist),
        new("long-n1", DjSlot.Long, "Settle in... this one's about {M} minutes. {T}.", DjNeed.NoArtist),

        // ---- a queue that has just started from a playlist or album (owner, 30 Oct: nothing was said when shuffle was first pressed) ----
        new("queue1", DjSlot.Queue, "Shuffling {Q}... starting with {T}, by {A}.", DjNeed.Shuffled),
        new("queue2", DjSlot.Queue, "Alright, {Q} on shuffle. First up, {T} by {A}.", DjNeed.Shuffled),
        new("queue3", DjSlot.Queue, "So, shuffling {Q}... and we're off with {A}, {T}.", DjNeed.Shuffled),
        new("queue4", DjSlot.Queue, "Playing {Q}... starting with {T}, by {A}.", DjNeed.InOrder),
        new("queue5", DjSlot.Queue, "Alright, {Q}. First up, {T} by {A}.", DjNeed.InOrder),
        // (a list named after its artist, or a run of their songs: the artist is said once, in the name)
        new("queue-n1", DjSlot.Queue, "Shuffling {Q}... starting with {T}.", DjNeed.Shuffled | DjNeed.NoArtist),
        new("queue-n2", DjSlot.Queue, "Alright, {Q} on shuffle. First up, {T}.", DjNeed.Shuffled | DjNeed.NoArtist),
        new("queue-n3", DjSlot.Queue, "Playing {Q}... starting with {T}.", DjNeed.InOrder | DjNeed.NoArtist),

        // ---- Chill: softer, slower, fewer words ----
        new("open-c1", DjSlot.Open, "So... easing in with {T} by {A}.", Style: DjStyle.Chill),
        new("queue-c1", DjSlot.Queue, "Shuffling {Q}... easing in with {T} by {A}.", DjNeed.Shuffled, DjStyle.Chill),
        new("open-c2", DjSlot.Open, "Okay, nice and slow to start... {A}, {T}.", DjNeed.Picked, DjStyle.Chill),
        new("intro-c1", DjSlot.Intro, "Alright... here's {T} by {A}.", Style: DjStyle.Chill),
        new("intro-c2", DjSlot.Intro, "This is {A}... {T}.", Style: DjStyle.Chill),
        new("intro-c3", DjSlot.Intro, "Okay, nice and easy now... {T} by {A}.", DjNeed.Picked, DjStyle.Chill),
        new("bridge-c1", DjSlot.Bridge, "That was {pA}... and now, {A}, with {T}.", Style: DjStyle.Chill),
        new("seg-c1", DjSlot.Segue, "That was {pT}... so, drifting on, {T} by {A}.", DjNeed.Picked, DjStyle.Chill),
        new("seg-c2", DjSlot.Segue, "{pT} there... okay, now, gently... {T}.", DjNeed.Picked, DjStyle.Chill),
        new("time-c1", DjSlot.Time, "{ToD}, so let's keep it easy... {T} by {A}.", DjNeed.Picked, DjStyle.Chill),
        new("reset-c1", DjSlot.Reset, "Okay, let's change the mood a little... {T} by {A}.", DjNeed.Picked, DjStyle.Chill),
        new("same-c1", DjSlot.SameArtist, "Staying a while with {A}... {T}.", Style: DjStyle.Chill),

        // ---- Hype: a bit more energy, still casual and never "!" ----
        new("open-h1", DjSlot.Open, "Let's get going with {T} by {A}.", Style: DjStyle.Hype),
        new("queue-h1", DjSlot.Queue, "Shuffling {Q}... let's go with {T} by {A}.", DjNeed.Shuffled, DjStyle.Hype),
        new("open-h2", DjSlot.Open, "Kicking off with {A}... {T}.", Style: DjStyle.Hype),
        new("intro-h1", DjSlot.Intro, "Okay, let's pick it up a bit... {T} by {A}.", DjNeed.Picked, DjStyle.Hype),
        new("intro-h2", DjSlot.Intro, "Alright, here we go... {T} by {A}.", Style: DjStyle.Hype),
        new("intro-h3", DjSlot.Intro, "Okay, no messing about... it's {A} with {T}.", Style: DjStyle.Hype),
        new("bridge-h1", DjSlot.Bridge, "That was {pA}... now let's keep it going with {A}, {T}.", Style: DjStyle.Hype),
        new("seg-h1", DjSlot.Segue, "Alright, that was {pT}... and straight into {T}, by {A}.", Style: DjStyle.Hype),
        new("seg-h2", DjSlot.Segue, "{pA} there... and we're not slowing down. {A}, with {T}.", DjNeed.Picked, DjStyle.Hype),
        new("time-h1", DjSlot.Time, "{ToD}, so let's lift it... {T} by {A}.", DjNeed.Picked, DjStyle.Hype),
        new("reset-h1", DjSlot.Reset, "Okay, new direction... {T} by {A}.", DjNeed.Picked, DjStyle.Hype),
        new("same-h1", DjSlot.SameArtist, "More {A}, and why not... {T}.", Style: DjStyle.Hype),
    ];

    /// <summary>The templates of [slot] for [style]: the style's own when it has some for that slot, else the Classic ones.</summary>
    public static IEnumerable<DjTemplate> Of(DjSlot slot, DjStyle style)
    {
        List<DjTemplate> own = style == DjStyle.Classic ? [] : All.Where(t => t.Slot == slot && t.Style == style).ToList();
        return own.Count > 0 ? own : All.Where(t => t.Slot == slot && t.Style is null);
    }

    /// <summary>How likely each slot is to be picked when several fit (Appendix A.4).</summary>
    public static int Weight(DjSlot s) => s switch
    {
        DjSlot.SameArtist or DjSlot.Return or DjSlot.Reset => 4, DjSlot.Why => 3, DjSlot.Bridge or DjSlot.Intro or DjSlot.Segue or DjSlot.Feel => 2, _ => 1,
    };

    /// <summary>The template with its placeholders filled; null when one of them has no value.</summary>
    public static string? Render(string text, IReadOnlyDictionary<string, string> values)
    {
        var sb = new System.Text.StringBuilder(text.Length + 64);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') { sb.Append(text[i]); continue; }
            var end = text.IndexOf('}', i);
            if (end < 0) return null;
            var key = text[(i + 1)..end];
            if (!values.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return null;
            sb.Append(v);
            i = end;
        }
        return Finish(sb.ToString());
    }

    /// <summary>One clean line: single spaces, no doubled full stops (a title ending in "." or "?" before the template's own), no space before punctuation.</summary>
    public static string Finish(string s)
    {
        s = DjText.Tidy(s);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+([.,:;])", "$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<!\.)([.?!])\.(?!\.)", "$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"([.?!]),", "$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"([?!]):", "$1");
        return s;
    }
}
