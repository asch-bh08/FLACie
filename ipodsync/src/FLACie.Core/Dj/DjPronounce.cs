using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// How odd names are said (Appendix A.5): a built-in starter list plus the server's own <c>dj-pronounce.tsv</c>, one name per line: written, kind (artist, title or word),
/// say (a respelling) and an optional ipa, separated by tabs. Only the spoken line uses them; captions keep the real spelling. A file entry replaces a built-in one.
/// </summary>
public sealed class DjPronounce
{
    public enum Kind { Artist, Title, Word }
    public sealed record Entry(string Written, Kind Kind, string Say, string? Ipa = null);

    /// <summary>Starting points, to be tuned by ear. The last column is the name's sounds in the symbols FLACie's own voice (Kokoro, through espeak) reads, with
    /// the stress mark before the stressed vowel; given those, the voice says exactly that (<see cref="Sounds"/>). Each was picked because the voice got the
    /// name wrong as written (checked on frank, 30 Oct: "Sia" came out "zhuh", "Eminem" "Em-eye-num", "Beyonce" "Bay-onss", "Ariana Grande" "grand").</summary>
    public static readonly IReadOnlyList<Entry> StarterList =
    [
        new("Sigur Rós", Kind.Artist, "Siggur Rose", "sˈɪɡɚ ɹˈoʊs"),
        new("Beyoncé", Kind.Artist, "Bee-yon-say", "biːjˈɑːnseɪ"),
        new("Mötley Crüe", Kind.Artist, "Motley Crew", "mˈɑːtli kɹˈuː"),
        new("CHVRCHES", Kind.Artist, "Churches", "tʃˈɜːtʃᵻz"),
        new("MØ", Kind.Artist, "Mur", "mˈɜː"),
        new("deadmau5", Kind.Artist, "dead mouse", "dˈɛd mˈaʊs"),
        new("Sia", Kind.Artist, "See-a", "sˈiːə"),
        new("Dvořák", Kind.Word, "Dvor-zhak", "dvˈɔːɹʒɑːk"),
        // respelt for the Workers AI voices (Aura-1 and MeloTTS read hyphenated syllables one by one); tune by ear on the dashboard's Test
        new("Sigur Ros", Kind.Artist, "Siggur Rose", "sˈɪɡɚ ɹˈoʊs"),
        new("Beyonce", Kind.Artist, "Bee-yon-say", "biːjˈɑːnseɪ"),
        new("Eminem", Kind.Artist, "Em-in-em", "ˈɛmɪnˌɛm"),
        new("Sade", Kind.Artist, "Shar-day", "ʃɑːdˈeɪ"),
        new("Björk", Kind.Artist, "Byerk", "bjˈɜːk"),
        new("Bjork", Kind.Artist, "Byerk", "bjˈɜːk"),
        new("Ne-Yo", Kind.Artist, "Nee-yo", "nˈiː jˈoʊ"),
        new("will.i.am", Kind.Artist, "Will I Am", "wˈɪl ˈaɪ ˈæm"),
        new("Rihanna", Kind.Artist, "Ree-anna", "ɹiːˈænə"),
        new("Ke$ha", Kind.Artist, "Kesha", "kˈɛʃə"),
        new("A$AP Rocky", Kind.Artist, "A-sap Rocky", "ˈeɪsˌæp ɹˈɑːki"),
        new("Jay-Z", Kind.Artist, "Jay Zee", "dʒˈeɪ zˈiː"),
        new("Jay Z", Kind.Artist, "Jay Zee", "dʒˈeɪ zˈiː"),
        new("The Prodigy", Kind.Artist, "The Prod-ij-ee", "ðə pɹˈɑːdɪdʒi"),
        new("Moby", Kind.Artist, "Moe-bee", "mˈoʊbi"),
        new("Avicii", Kind.Artist, "Uh-vee-chee", "ɐvˈiːtʃi"),
        new("Tiësto", Kind.Artist, "Tee-esto", "tiːˈɛstoʊ"),
        new("Tiesto", Kind.Artist, "Tee-esto", "tiːˈɛstoʊ"),
        new("Måneskin", Kind.Artist, "Mor-ness-kin", "mˈɔːnɛskɪn"),
        new("Maneskin", Kind.Artist, "Mor-ness-kin", "mˈɔːnɛskɪn"),
        new("Ariana Grande", Kind.Artist, "Ariana Grandy", "ˌɑːɹiˈɑːnə ɡɹˈɑːndeɪ"),
        new("Kygo", Kind.Artist, "Kee-go", "kˈiːɡoʊ"),
        // owner, 9 Nov: "the DJ has a stroke trying to say Katy Perry, and names like Bobo and Gaga"
        new("Katy Perry", Kind.Artist, "Katie Perry", "kˈeɪti pˈɛɹi"),
        new("Katy", Kind.Word, "Katie", "kˈeɪti"),
        new("Lady Gaga", Kind.Artist, "Lady Gah-gah", "lˈeɪdi ɡˈɑːɡɑː"),
        new("Gaga", Kind.Word, "Gah-gah", "ɡˈɑːɡɑː"),
        new("Bobo", Kind.Word, "Boh-boh", "bˈoʊboʊ"),
        // every other name the by-ear check (data/dj-pronounce.json, kokoro:am_liam) heard wrong and had no sounds for, 9 Nov
        new("DJ BoBo", Kind.Artist, "DJ Boh-boh", "dˌiː dʒˈeɪ bˈoʊboʊ"),
        new("Adele", Kind.Artist, "Uh-dell", "ədˈɛl"),
        new("Akon", Kind.Artist, "Akon", "ˈeɪkɑːn"),
        new("Aphex Twin", Kind.Artist, "Ay-fex Twin", "ˈeɪfɛks twˈɪn"),
        new("Bellini", Kind.Artist, "Bell-ee-nee", "bɛlˈiːni"),
        new("D12", Kind.Artist, "D Twelve", "dˈiː twˈɛlv"),
        new("Daddy DJ", Kind.Artist, "Daddy D J", "dˈædi dˌiː dʒˈeɪ"),
        new("David Guetta", Kind.Artist, "David Getta", "dˈeɪvɪd ɡˈɛtə"),
        new("Guetta", Kind.Word, "Getta", "ɡˈɛtə"),
        new("DeBarge", Kind.Artist, "De-barge", "dəbˈɑːɹdʒ"),
        new("E-Rotic", Kind.Artist, "Ee-rotic", "iːɹˈɑːtɪk"),
        new("Ellie Goulding", Kind.Artist, "Ellie Gold-ing", "ˈɛli ɡˈoʊldɪŋ"),
        new("Florence + the Machine", Kind.Artist, "Florence and the Machine", "flˈɔːɹəns ænd ðə məʃˈiːn"),
        new("Gigi D'Agostino", Kind.Artist, "Gee-gee Dag-oss-tee-no", "dʒˈiːdʒi dˌæɡəstˈiːnoʊ"),
        new("Gliese", Kind.Artist, "Glee-zuh", "ɡlˈiːzə"),
        new("JAŸ-Z", Kind.Artist, "Jay Zee", "dʒˈeɪ zˈiː"),
        new("JME", Kind.Artist, "J M E", "dʒˈeɪ ˈɛm ˈiː"),
        new("Kesha", Kind.Artist, "Kesha", "kˈɛʃə"),
        new("Kip Moore", Kind.Artist, "Kip Moore", "kˈɪp mˈʊɹ"),
        new("La Bouche", Kind.Artist, "La Boosh", "lɑː bˈuːʃ"),
        new("Le Click", Kind.Artist, "Luh Click", "lə klˈɪk"),
        new("Livin' Joy", Kind.Artist, "Livin Joy", "lˈɪvɪn dʒˈɔɪ"),
        new("Maxx", Kind.Artist, "Max", "mˈæks"),
        new("P!nk", Kind.Artist, "Pink", "pˈɪŋk"),
        new("Pitbull", Kind.Artist, "Pit-bull", "pˈɪtbʊl"),
        new("Queen", Kind.Artist, "Queen", "kwˈiːn"),
        new("Rockin Moroccin", Kind.Artist, "Rockin Mo-rock-in", "ɹˈɑːkɪn məɹˈɑːkɪn"),
        new("Roxette", Kind.Artist, "Rocks-ett", "ɹɑːksˈɛt"),
        new("SOS", Kind.Word, "S O S", "ˈɛs ˈoʊ ˈɛs"),
        new("Taio Cruz", Kind.Artist, "Tie-oh Cruz", "tˈaɪoʊ kɹˈuːz"),
        new("SZA", Kind.Artist, "Sizza", "sˈɪzə"),
        new("Kanye", Kind.Word, "Kahn-yay", "kˈɑːnjeɪ"),
        new("Shania", Kind.Word, "Shuh-nye-uh", "ʃənˈaɪə"),
        new("Hozier", Kind.Artist, "Hoe-zhure", "hˈoʊʒɚ"),
        new("Khalid", Kind.Artist, "Kuh-leed", "kəlˈiːd"),
        new("Noah Kahan", Kind.Artist, "Noah Kay-un", "nˈoʊə kˈeɪən"),
        new("Röyksopp", Kind.Artist, "Royk-sopp", "ɹˈɔɪksɑːp"),
        new("Q Lazzarus", Kind.Artist, "Q Lazz-arus", "kjˈuː lˈæzəɹəs"),
        new("Rosalía", Kind.Artist, "Rosa-lee-a", "ɹoʊzəlˈiːə"),
        new("Rosalia", Kind.Artist, "Rosa-lee-a", "ɹoʊzəlˈiːə"),
        new("Nirvana", Kind.Artist, "Nir-vana", "nɪɹvˈɑːnə"),
        new("SOS", Kind.Title, "S O S", "ˈɛs ˈoʊ ˈɛs"),
        new("FLACie", Kind.Word, "Flacky", "flˈæki"),   // as the Alexa skill says it
        new("HMS", Kind.Word, "H M S", "ˌeɪtʃ ˌɛm ˈɛs"),   // the owner's account name: said letter by letter, not "herms" (owner, 2 Nov)
    ];

    /// <summary>Changes whenever the sounds above do, so FLACie's voice makes its kept clips again (DjClips.Salt).</summary>
    /// "sounds-2": the voice reads lines with misaki, Kokoro's own reader, so every clip made the espeak way is made again (owner, 30 Oct: "jyz").
    /// "sounds-3": Katy Perry, Gaga, Bobo and every other name the by-ear check heard wrong get their sounds (owner, 9 Nov: "do all"), so clips that
    /// said them wrong are made again.
    public const string SoundsVersion = "sounds-3";

    public static DjPronounce Default { get; } = new([]);

    readonly Dictionary<string, Entry> artists = new(StringComparer.OrdinalIgnoreCase), titles = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(Regex Find, string Say)> phrases = [];
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>[heard]: the server's checked table for the voice in use (data/dj-pronounce.json, <see cref="DjHearing"/>), which goes first: a name with a
    /// respelling there is said that way; a name with null is said as written (the respelling made it worse, or it was never needed).</summary>
    public DjPronounce(IEnumerable<Entry> extra, IReadOnlyDictionary<string, string?>? heard = null)
    {
        var all = new Dictionary<(string, Kind), Entry>();
        foreach (var e in StarterList.Concat(extra)) all[(e.Written.ToLowerInvariant(), e.Kind)] = e;
        foreach (var (written, say) in heard ?? new Dictionary<string, string?>())
        {
            var key = written.ToLowerInvariant();
            var kinds = all.Keys.Where(k => k.Item1 == key).ToList();
            var kind = kinds.Count > 0 ? kinds[0].Item2 : Kind.Artist;
            foreach (var k in kinds) all.Remove(k);
            if (say is { Length: > 0 } && !string.Equals(say, written, StringComparison.OrdinalIgnoreCase)) all[(key, kind)] = new(written, kind, say);
        }
        Entries = all.Values.ToList();
        sounds = new(BuildSounds);
        foreach (var e in Entries)
        {
            if (e.Kind == Kind.Artist) artists[e.Written] = e;
            else if (e.Kind == Kind.Title) titles[e.Written] = e;
        }
        // inside a longer name or line, any entry is matched as a whole word or phrase; longest first, so "Sigur Rós" goes before a shorter entry inside it
        foreach (var e in Entries.OrderByDescending(e => e.Written.Length))
            phrases.Add((new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(e.Written) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), e.Say));
    }

    /// <summary>The respelling for an artist name exactly as tagged, or null.</summary>
    public string? Artist(string written) => artists.TryGetValue(DjText.Tidy(written), out var e) ? e.Say : null;

    /// <summary>The respelling for a title exactly as tagged, or null.</summary>
    public string? Title(string written) => titles.TryGetValue(DjText.Tidy(written), out var e) ? e.Say : null;

    readonly Lazy<(Regex? Find, Dictionary<string, string> Ipa)> sounds;

    /// <summary>
    /// For FLACie's own voice (Kokoro): every name with its sounds written down, as written or as already respelt for the older voices, is marked
    /// "[name](/sounds/)", which the voice says exactly (ipodsync/tools/kokoro/server.py). Done just before the voice, so it covers everything the DJ says:
    /// its lines, its answers and its stock replies.
    /// </summary>
    public string Sounds(string s)
    {
        if (s.Length == 0) return s;
        var (find, ipa) = sounds.Value;
        return find is null ? s : find.Replace(s, m => ipa.TryGetValue(m.Value.ToLowerInvariant(), out var i) ? "[" + m.Value + "](/" + i + "/)" : m.Value);
    }

    (Regex?, Dictionary<string, string>) BuildSounds()
    {
        var ipa = new Dictionary<string, string>();
        // the starter sounds too: the by-ear check (DjHearing) may have dropped a respelling for this voice, and the written name must still get its sounds
        foreach (var e in Entries.Concat(StarterList))
        {
            if (e.Ipa is not { Length: > 0 } i) continue;
            ipa.TryAdd(e.Written.ToLowerInvariant(), i);
            ipa.TryAdd(e.Say.ToLowerInvariant(), i);
        }
        if (ipa.Count == 0) return (null, ipa);
        var alts = string.Join("|", ipa.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        return (new Regex(@"(?<![\p{L}\p{N}\[])(?:" + alts + @")(?![\p{L}\p{N}\]])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), ipa);
    }

    /// <summary>Every entry respelt wherever it appears as a whole word or phrase.</summary>
    public string Phrases(string s)
    {
        if (s.Length == 0) return s;
        foreach (var (find, say) in phrases) s = find.Replace(s, say.Replace("$", "$$"));
        return s;
    }

    /// <summary>Reads a tab-separated file (two or more spaces also separate columns; "#" starts a comment). Lines it cannot read are skipped.</summary>
    public static List<Entry> Parse(string text)
    {
        var res = new List<Entry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith('#') || line.Trim().Length == 0) continue;
            var cols = line.Contains('\t') ? line.Split('\t') : Regex.Split(line.Trim(), @"\s{2,}");
            cols = cols.Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
            if (cols.Length < 3) continue;
            Kind? kind = cols[1].ToLowerInvariant() switch { "artist" => Kind.Artist, "title" => Kind.Title, "word" => Kind.Word, _ => null };
            if (kind is null || cols[0].Length > 200 || cols[2].Length > 200) continue;
            res.Add(new(cols[0], kind.Value, cols[2], cols.Length > 3 ? cols[3] : null));
        }
        return res;
    }

    /// <summary>The starter list plus the file at [path] when there is one.</summary>
    public static DjPronounce Load(string? path, IReadOnlyDictionary<string, string?>? heard = null)
    {
        try { if (path is not null && File.Exists(path)) return new DjPronounce(Parse(File.ReadAllText(path)), heard); } catch (Exception) { }
        return heard is { Count: > 0 } ? new DjPronounce([], heard) : Default;
    }
}
