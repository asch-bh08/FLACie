namespace FLACie.Core;

/// <summary>The kinds of line the DJ says (Appendix A.1). Each is used only when its facts are known.</summary>
public enum DjSlot { Open, Greet, Bridge, Intro, Why, Year, Album, Genre, SameArtist, Return, Reset, Long, ArtistOnly, TitleOnly, Segue, Time, Feel }

/// <summary>Extra conditions a template has beyond its placeholders (a placeholder with no value already rules a template out).</summary>
[Flags]
public enum DjNeed { None = 0, TenYearsOld = 1, SameAlbum = 2, Soundtrack = 4, SameGenre = 8 }

/// <summary>
/// One line pattern. Placeholders, filled with the spoken or the written form: {T}/{A} the next song's title and artist, {pT}/{pA} the previous song's, {eT} a song by the
/// same artist heard earlier, {Al} album, {Y} year, {D} decade, {AD} "A seventies" / "An eighties", {G} genre, {M} minutes, {W} the "why", {w} the same starting in lower case,
/// {W1}/{w1} its first part only, {Day} Saturday or Sunday, {ToD}/{tod} a notable time ("Monday morning", "late night"), {F}/{f} the song's feel ("a big one").
/// [Style]: null for Classic (and for every style when that style has no template of the slot); Chill or Hype for their own wording.
/// </summary>
public sealed record DjTemplate(string Id, DjSlot Slot, string Text, DjNeed Need = DjNeed.None, DjStyle? Style = null)
{
    /// <summary>The first word before any placeholder, lower case ("here's", "from"); "" when the line opens with a placeholder. Used to vary how lines start.</summary>
    public string Opener { get; } = Text.IndexOf('{') is var i && i == 0 ? "" : (i < 0 ? Text : Text[..i]).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim(',', '.', ':').ToLowerInvariant() ?? "";
}

/// <summary>The 60 templates of Appendix A.2, calm and short. Never "!", never "I".</summary>
public static class DjTemplates
{
    public static readonly IReadOnlyList<DjTemplate> All =
    [
        new("open1", DjSlot.Open, "Starting with {T} by {A}."),
        new("open2", DjSlot.Open, "First up, {T} by {A}."),
        new("open3", DjSlot.Open, "Here's {A} to start, with {T}."),
        new("open4", DjSlot.Open, "Let's begin with {T}, by {A}."),
        new("open5", DjSlot.Open, "Opening with {A}: {T}."),

        new("greet1", DjSlot.Greet, "Good morning."),
        new("greet2", DjSlot.Greet, "Good afternoon."),
        new("greet3", DjSlot.Greet, "Good evening."),
        new("greet4", DjSlot.Greet, "{Day} morning."),

        new("bridge1", DjSlot.Bridge, "That was {pT} by {pA}. Here's {T} by {A}."),
        new("bridge2", DjSlot.Bridge, "{pT}, from {pA}. Now it's {A}, with {T}."),
        new("bridge3", DjSlot.Bridge, "After {pA}, here's {A} with {T}."),
        new("bridge4", DjSlot.Bridge, "That was {pT}. Next up, {T} by {A}."),
        new("bridge5", DjSlot.Bridge, "You heard {pA} there. This is {T} by {A}."),
        new("bridge6", DjSlot.Bridge, "From {pA} to {A}: this is {T}."),
        new("bridge7", DjSlot.Bridge, "That was {pT}. Here's {A} next, with {T}."),

        new("intro1", DjSlot.Intro, "Here's {T} by {A}."),
        new("intro2", DjSlot.Intro, "Next, {T} by {A}."),
        new("intro3", DjSlot.Intro, "This is {A}, with {T}."),
        new("intro4", DjSlot.Intro, "Up next, {T} from {A}."),
        new("intro5", DjSlot.Intro, "Now, {A}: {T}."),

        new("why1", DjSlot.Why, "{W}. Here's {T} by {A}."),
        new("why2", DjSlot.Why, "{W}, so here's {T} by {A}."),
        new("why3", DjSlot.Why, "Next, {T} by {A}. {W}."),
        new("why4", DjSlot.Why, "This is {T} by {A}: {w1}."),
        new("why5", DjSlot.Why, "Picked because {w}: {T} by {A}."),
        new("why6", DjSlot.Why, "That was {pT}. {W}, here's {T}."),
        new("why7", DjSlot.Why, "Chosen for you: {w1}. This is {A} with {T}."),

        new("year1", DjSlot.Year, "From {Y}, this is {T} by {A}."),
        new("year2", DjSlot.Year, "Back to {Y} now: {T} by {A}.", DjNeed.TenYearsOld),
        new("year3", DjSlot.Year, "{T} by {A}, from {Y}."),
        new("year4", DjSlot.Year, "Here's {A} in {Y}, with {T}."),
        new("year5", DjSlot.Year, "{AD} one now: {T} by {A}."),
        new("year6", DjSlot.Year, "Something from the {D}: {A}, {T}."),

        new("album1", DjSlot.Album, "From the album {Al}, here's {T} by {A}."),
        new("album2", DjSlot.Album, "{T}, by {A}, from {Al}."),
        new("album3", DjSlot.Album, "Here's {A} with {T}, from {Al}."),
        new("album4", DjSlot.Album, "Another from {Al}: {T}.", DjNeed.SameAlbum),
        new("album5", DjSlot.Album, "From the {Al} soundtrack, this is {T} by {A}.", DjNeed.Soundtrack),

        new("genre1", DjSlot.Genre, "Some {G} now: {T} by {A}."),
        new("genre2", DjSlot.Genre, "Staying with {G}, here's {T} by {A}.", DjNeed.SameGenre),
        new("genre3", DjSlot.Genre, "A bit of {G} next: {A}, with {T}."),

        new("same1", DjSlot.SameArtist, "More from {A}: this is {T}."),
        new("same2", DjSlot.SameArtist, "Staying with {A}, here's {T}."),
        new("same3", DjSlot.SameArtist, "{A} again, with {T}."),
        new("same4", DjSlot.SameArtist, "Another from {A} now: {T}."),

        new("return1", DjSlot.Return, "Back to {A} now, with {T}."),
        new("return2", DjSlot.Return, "Returning to {A}: {T}."),
        new("return3", DjSlot.Return, "We heard {A} earlier with {eT}. Now it's {T}."),

        new("reset1", DjSlot.Reset, "Let's try something different: {T} by {A}."),
        new("reset2", DjSlot.Reset, "A change of direction now. Here's {T} by {A}."),
        new("reset3", DjSlot.Reset, "Something else, then. This is {A}, with {T}."),

        new("long1", DjSlot.Long, "A long one now, about {M} minutes: {T} by {A}."),
        new("long2", DjSlot.Long, "Settle in, this one's about {M} minutes: {T} by {A}."),

        new("artist1", DjSlot.ArtistOnly, "Here's {A}."),
        new("artist2", DjSlot.ArtistOnly, "Next, something from {A}."),
        new("artist3", DjSlot.ArtistOnly, "This one's from {A}."),

        new("title1", DjSlot.TitleOnly, "Here's {T}."),
        new("title2", DjSlot.TitleOnly, "Next, {T}."),
        new("title3", DjSlot.TitleOnly, "This one's called {T}."),

        // ---- segues, time of day, the song's feel (every style) ----
        new("seg1", DjSlot.Segue, "That was {pT}... and this is {T}, by {A}."),
        new("seg2", DjSlot.Segue, "{pT}, from {pA}... and now, {T}."),
        new("seg3", DjSlot.Segue, "That was {pA}... and here's {A}, with {T}."),
        new("time1", DjSlot.Time, "{ToD}, and here's {T} by {A}."),
        new("time2", DjSlot.Time, "For a {tod}, {T} by {A}."),
        new("feel1", DjSlot.Feel, "{F} now: {T} by {A}."),
        new("feel2", DjSlot.Feel, "Here's {T} by {A}, {f}."),

        // ---- Chill: softer, fewer words ----
        new("open-c1", DjSlot.Open, "Easing in with {T} by {A}.", Style: DjStyle.Chill),
        new("open-c2", DjSlot.Open, "Let's start slowly: {A}, {T}.", Style: DjStyle.Chill),
        new("intro-c1", DjSlot.Intro, "Now then, {T} by {A}.", Style: DjStyle.Chill),
        new("intro-c2", DjSlot.Intro, "This is {A}. {T}.", Style: DjStyle.Chill),
        new("intro-c3", DjSlot.Intro, "Next, nice and easy, {T} by {A}.", Style: DjStyle.Chill),
        new("bridge-c1", DjSlot.Bridge, "That was {pA}. Here's {A} now, with {T}.", Style: DjStyle.Chill),
        new("seg-c1", DjSlot.Segue, "That was {pT}... and drifting on, {T} by {A}.", Style: DjStyle.Chill),
        new("seg-c2", DjSlot.Segue, "{pT} there... and now, gently, {T}.", Style: DjStyle.Chill),
        new("time-c1", DjSlot.Time, "{ToD}, so let's keep it easy: {T} by {A}.", Style: DjStyle.Chill),
        new("reset-c1", DjSlot.Reset, "Let's change the mood: {T} by {A}.", Style: DjStyle.Chill),
        new("same-c1", DjSlot.SameArtist, "Staying a while with {A}: {T}.", Style: DjStyle.Chill),

        // ---- Hype: livelier, still never "!" ----
        new("open-h1", DjSlot.Open, "Let's get going with {T} by {A}.", Style: DjStyle.Hype),
        new("open-h2", DjSlot.Open, "Kicking off with {A}: {T}.", Style: DjStyle.Hype),
        new("intro-h1", DjSlot.Intro, "Turn it up for {T} by {A}.", Style: DjStyle.Hype),
        new("intro-h2", DjSlot.Intro, "Right, here we go: {T} by {A}.", Style: DjStyle.Hype),
        new("intro-h3", DjSlot.Intro, "No messing about, it's {A} with {T}.", Style: DjStyle.Hype),
        new("bridge-h1", DjSlot.Bridge, "That was {pA}, and we keep it rolling with {A}: {T}.", Style: DjStyle.Hype),
        new("seg-h1", DjSlot.Segue, "That was {pT}... and straight into {T}, by {A}.", Style: DjStyle.Hype),
        new("seg-h2", DjSlot.Segue, "{pA} there... and no let-up: {A}, with {T}.", Style: DjStyle.Hype),
        new("time-h1", DjSlot.Time, "{ToD}, so let's lift it: {T} by {A}.", Style: DjStyle.Hype),
        new("reset-h1", DjSlot.Reset, "New direction, full speed: {T} by {A}.", Style: DjStyle.Hype),
        new("same-h1", DjSlot.SameArtist, "More {A}, and why not: {T}.", Style: DjStyle.Hype),
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
