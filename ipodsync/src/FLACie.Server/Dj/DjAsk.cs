using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FLACie.Core;
using FLACie.Server.Alexa;

namespace FLACie.Server.Dj;

/// <summary>What a request to the DJ is about. The names (snake case) are also what the text model may answer with.</summary>
public enum AskIntent { Play, Playlist, Favourites, MostPlayed, Unheard, ShuffleLibrary, Mood, Next, Pause, Resume, VolumeUp, VolumeDown, WhatsThis, WhoSings, MoreLike, LessLike, ThumbsDown, DjOn, DjOff, Situation }

/// <summary>A request understood: the intent, and what it is about (the song, artist or album for Play, the playlist's name, the mood's words, the
/// situation: car, night, morning, workout, chill or none). [Key]: a learned request's phrase; [Vibe]: "high" or "low" when the words say.</summary>
public sealed record AskParse(AskIntent Intent, string? What = null, string? Key = null, string? Vibe = null);

/// <summary>
/// The quick rules for "talk to the DJ": the phrases people say most, matched without any model. Null when none fits (then the text model is asked).
/// Song, artist and album names are left to <see cref="VoiceMatcher"/>, as Alexa does.
/// </summary>
public static partial class DjAskRules
{
    [GeneratedRegex(@"^(?:(?:hey|ok|okay|yo) (?:dj|deejay|flacie)\b ?|please|can you |could you |would you |will you |just )+")] private static partial Regex Lead();
    [GeneratedRegex(@"(?: please| for me| now| thanks| thank you)+$")] private static partial Regex Tail();

    /// <summary>Lower case, no punctuation (apostrophes go too, so "what's" and "whats" are one), single spaces, without "hey DJ", "please" and the like.</summary>
    public static string Normalise(string text)
    {
        var s = (text ?? "").ToLowerInvariant().Replace("’", "'").Replace("'", "");
        s = Regex.Replace(s, @"[^\p{L}\p{N}&\- ]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        for (var again = true; again;) { var t = Tail().Replace(Lead().Replace(s, "").Trim(), "").Trim(); again = t != s; s = t; }
        return s;
    }

    static readonly (Regex Rx, AskIntent Intent)[] Fixed =
    [
        (new(@"^(?:(?:turn|switch) (?:the )?dj on|(?:turn|switch) on (?:the )?dj|dj on|start (?:the )?dj|start talking|talk to me)$"), AskIntent.DjOn),
        (new(@"^(?:(?:turn|switch) (?:the )?dj off|(?:turn|switch) off (?:the )?dj|dj off|stop (?:the )?dj|stop talking|no more talking|be quiet dj)$"), AskIntent.DjOff),
        (new(@"^(?:next|skip|skip (?:it|this|this one|this song|this track|song|track)|next (?:song|track|one)|play the next (?:song|track|one)|go to the next (?:song|track|one))$"), AskIntent.Next),
        (new(@"^(?:pause|stop|hold on|pause (?:it|this|the music|music|the song)|stop (?:it|the music|music|playing))$"), AskIntent.Pause),
        (new(@"^(?:resume|play|continue|unpause|carry on|keep playing|resume (?:it|playing|the music|music)|play (?:it|the music) again|start (?:the )?music again)$"), AskIntent.Resume),
        (new(@"^(?:turn it up|turn this up|louder|a bit louder|volume up|crank it up|pump it up|turn (?:the )?(?:volume|music) up|turn up (?:the )?(?:volume|music|this))$"), AskIntent.VolumeUp),
        (new(@"^(?:turn it down|turn this down|quieter|a bit quieter|softer|volume down|turn (?:the )?(?:volume|music) down|turn down (?:the )?(?:volume|music|this))$"), AskIntent.VolumeDown),
        (new(@"^(?:whats|what is) (?:this|that|playing|this song|that song|this track|that track|the song|the name of this song)$|^what (?:song|track) is (?:this|that|playing)$|^what am i listening to$|^name this (?:song|track)$"), AskIntent.WhatsThis),
        (new(@"^who (?:sings|sang|is singing|s singing|is this by|performs|made|wrote) (?:this|that|it|this song|that song)$|^who is (?:this|that|the artist|the singer)$|^whos (?:this|that|singing)$"), AskIntent.WhoSings),
        (new(@"^(?:play )?(?:more|more songs|more music|some more) like (?:this|that|this one|this song)$|^more of (?:this|that)$|^play (?:something|stuff|songs) like (?:this|that|this one|this song)$"), AskIntent.MoreLike),
        (new(@"^(?:play )?less like (?:this|that|this one|this song)$|^(?:play )?something (?:different|else)$|^not (?:really )?my (?:thing|vibe)$"), AskIntent.LessLike),
        (new(@"^(?:thumbs down|dislike(?: this| it| this song)?|i (?:dont|do not) like (?:this|it|this song)|i hate (?:this|it|this song))$"), AskIntent.ThumbsDown),
        (new(@"^(?:play |shuffle )?(?:my |the )?(?:favourites|favorites|favourite songs|favorite songs|liked songs|loved songs|hearted songs|songs i (?:like|love))$"), AskIntent.Favourites),
        (new(@"^(?:play )?(?:my |the )?(?:most played|most listened(?: to)?|frequently played|top|most popular)(?: songs| tracks| music)?$|^play what i play (?:the )?most$|^play my top songs$"), AskIntent.MostPlayed),
        (new(@"^(?:play )?(?:me )?something (?:i (?:havent|have not|never) (?:heard|listened to|played)|ive never heard|new to me)(?: before| yet)?$|^(?:play )?(?:songs|music) i (?:havent|have not|never) heard(?: before| yet)?$"), AskIntent.Unheard),
        (new(@"^(?:shuffle|shuffle (?:my |the )?(?:library|music|songs|everything|all|all my (?:music|songs))|play (?:my |the )?(?:whole )?library|shuffle all|play everything|play all my (?:music|songs))$"), AskIntent.ShuffleLibrary),
    ];

    /// <summary>Moods people ask for, and the Home mood shelf (<see cref="HomeBuilder.Moods"/>) and energy each one means.</summary>
    public static readonly (string Word, string Shelf, MoodEnergy Energy)[] MoodWords =
    [
        ("chill", "Relax", MoodEnergy.Low), ("chilled", "Relax", MoodEnergy.Low), ("relaxing", "Relax", MoodEnergy.Low), ("relax", "Relax", MoodEnergy.Low), ("calm", "Relax", MoodEnergy.Low),
        ("mellow", "Relax", MoodEnergy.Low), ("laid back", "Relax", MoodEnergy.Low), ("sleepy", "Relax", MoodEnergy.Low), ("quiet", "Relax", MoodEnergy.Low), ("soft", "Relax", MoodEnergy.Low),
        ("sad", "Late night", MoodEnergy.Low), ("moody", "Late night", MoodEnergy.Low), ("late night", "Late night", MoodEnergy.Any), ("dark", "Late night", MoodEnergy.Any),
        ("upbeat", "Workout", MoodEnergy.High), ("gym", "Workout", MoodEnergy.High), ("workout", "Workout", MoodEnergy.High), ("running", "Workout", MoodEnergy.High), ("energetic", "Workout", MoodEnergy.High),
        ("hype", "Workout", MoodEnergy.High), ("high energy", "Workout", MoodEnergy.High), ("pump", "Workout", MoodEnergy.High), ("loud", "Workout", MoodEnergy.High),
        ("party", "Party", MoodEnergy.High), ("dance", "Party", MoodEnergy.High), ("dancing", "Party", MoodEnergy.High), ("danceable", "Party", MoodEnergy.High),
        ("happy", "Feel good", MoodEnergy.Any), ("feel good", "Feel good", MoodEnergy.Any), ("cheerful", "Feel good", MoodEnergy.Any), ("summery", "Feel good", MoodEnergy.Any), ("sunny", "Feel good", MoodEnergy.Any),
        ("focus", "Focus", MoodEnergy.Low), ("study", "Focus", MoodEnergy.Low), ("studying", "Focus", MoodEnergy.Low), ("concentrate", "Focus", MoodEnergy.Low), ("work", "Focus", MoodEnergy.Any),
    ];

    public static readonly string[] GenreWords = ["hip hop", "hip-hop", "r&b", "rnb", "rock", "pop", "jazz", "rap", "metal", "punk", "indie", "alternative", "soul", "blues", "country", "classical",
        "electronic", "dance", "folk", "reggae", "grunge", "techno", "house", "funk", "disco", "ambient", "soundtrack", "edm", "lo-fi", "lofi", "acoustic", "garage", "drum and bass", "trance"];

    static bool HasWord(string s, string w) => Regex.IsMatch(s, $@"(?:^|\s){Regex.Escape(w)}(?:\s|$)");
    public static bool IsMoody(string s) => MoodWords.Any(m => HasWord(s, m.Word)) || GenreWords.Any(g => HasWord(s, g));

    [GeneratedRegex(@"^(?:play |put on |give me |i want |i need |id like |i would like )?(?:me |us )?(?:something|some|anything|a bit of|music|songs|tunes|stuff|a mix of|a playlist of|a playlist for)\b ?(.*)$")] private static partial Regex Vague();
    [GeneratedRegex(@"^(?:play|put on|listen to|i want to hear|start playing)\s+(.+)$")] private static partial Regex PlayRx();
    [GeneratedRegex(@"^(?:play |put on |shuffle )?(?:my |the )?(.+?)\s+playlist$|^(?:play |put on )?(?:my |the )?playlist (?:called |named )?(.+)$")] private static partial Regex PlaylistRx();

    [GeneratedRegex(@"(?:^|\s)(?:car|cars|drive|drives|driving|road|roads|b-road|b-roads|back-road|back-roads|backroad|backroads|motorway|motorways|commute|commuting|road trip)(?:\s|$)")] private static partial Regex CarWords();
    [GeneratedRegex(@"(?:^|\s)(?:music|songs|tunes|stuff|mix)$|^(?:play |put on |give me )?(?:me )?(?:something|some|anything)\b")] private static partial Regex Asking();

    public static AskParse? Parse(string text)
    {
        var s = Normalise(text);
        if (s.Length == 0 || s.Length > 200) return null;
        foreach (var (rx, intent) in Fixed) if (rx.IsMatch(s)) return new(intent);
        if (PlaylistRx().Match(s) is { Success: true } pm)
        {
            var name = (pm.Groups[1].Success ? pm.Groups[1].Value : pm.Groups[2].Value).Trim();
            if (name.Length > 0 && name is not ("a" or "new" or "some" or "any")) return new(AskIntent.Playlist, name);
        }
        // "play car songs", "something for the B-roads", "driving tunes": what this person plays in the car (never a song called "Road", nor a playlist named so)
        if (CarWords().IsMatch(s) && Asking().IsMatch(s)) return new(AskIntent.Situation, "car", PhraseKeys.Of(s));
        // "something chill", "some jazz", "music for the gym": a mood or a genre. Something vague without one is the model's to work out (never a song to download)
        if (Vague().Match(s) is { Success: true } vm)
        {
            var rest = vm.Groups[1].Value.Trim();
            return IsMoody(s) ? new(AskIntent.Mood, rest.Length > 0 ? rest : s) : null;
        }
        if (PlayRx().Match(s) is { Success: true } play)
        {
            var what = play.Groups[1].Value.Trim();
            // "play jazz", "play upbeat music": a genre or a mood, not a song called that
            if (GenreWords.Contains(what) || Regex.IsMatch(what, @"^(?:\S+ )?(?:music|songs|tunes|stuff)$") && IsMoody(what)) return new(AskIntent.Mood, what);
            return new(AskIntent.Play, what);
        }
        return null;
    }
}

public enum MoodEnergy { Any, Low, High }

/// <summary>The free Workers AI text model, as the DJ's request reader sees it (a fake in the tests).</summary>
public interface IDjAskModel
{
    Task<string?> AskAsync(string system, string user, CancellationToken ct);
}

/// <summary>The DJ's own text model (the owner's free Cloudflare account, the same calls and daily cap as the DJ's lines).</summary>
public sealed class CloudflareAskModel(CloudflareVoice voice) : IDjAskModel
{
    /// <summary>Room for a 35-word answer wrapped in JSON (a line between songs needs fewer).</summary>
    public const int AnswerTokens = 110;
    public Task<string?> AskAsync(string system, string user, CancellationToken ct) => voice.Ready ? voice.WriteAsync(system, user, 0, ct, AnswerTokens) : Task.FromResult<string?>(null);
}

/// <summary>What the text model made of something said: an action (with what it is about), or a short answer in the DJ's voice. Both null: not understood.</summary>
public sealed record AskAiResult(AskParse? Parse, string? Answer);

/// <summary>
/// For what the rules don't know: the text model is asked either to map the words onto one of the same intents, or to answer a question briefly in the DJ's
/// character from the facts FLACie holds (given in the prompt). JSON only. Its answer is checked (a known action, a short query where one is needed; an answer
/// with no markdown or links and at most <see cref="MaxAnswerWords"/> words); anything else, or nothing in time, is not understood.
/// </summary>
public static partial class DjAskAi
{
    /// <summary>How long the model may take for an action (a question gets <see cref="QuestionTimeout"/>).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan QuestionTimeout = TimeSpan.FromSeconds(4);
    public const int MaxAnswerWords = 40, MaxAnswerChars = 260;

    public const string System =
        "You are the DJ on a personal music station, warm and knowledgeable, in British English. A listener has just said something to you. " +
        "Reply with JSON only, no other words, in one of two forms. " +
        "1) To do something: {\"action\": \"...\", \"query\": \"...\"}. Actions: play (query: the song, artist or album named), playlist (query: the playlist's name), " +
        "favourites, most_played, unheard, shuffle_library, mood (query: the mood or genre in a few words, like \"chill\" or \"upbeat gym\" or \"jazz\"), " +
        "next, pause, resume, volume_up, volume_down, whats_this, who_sings, more_like (query, optional: \"slower\" or \"faster\"), less_like, thumbs_down, dj_on, dj_off, " +
        "situation (music for a place, time or activity of their own, like driving, the school run or sunday cleaning: query is car, night, morning, workout, chill or none; " +
        "add \"key\": the request in one to three words, reusing a phrase they have used before when it means the same; add \"vibe\": \"high\" or \"low\" when the words suggest an energy). " +
        "Use mood for any feeling, activity or genre. When they say them, him, her or this artist, use the artist the conversation is about. " +
        "2) To answer a question: {\"answer\": \"...\"}: one or two short spoken sentences, at most 35 words, no lists, no markdown. " +
        "Use ONLY the facts given. If the facts do not say, answer that you're not sure; never invent years, albums, chart positions or stories. " +
        "If it is neither, reply {\"action\": \"unknown\"}.";

    static string NameOf(AskIntent i) => Regex.Replace(i.ToString(), "(?<=[a-z])([A-Z])", "_$1").ToLowerInvariant();
    static readonly Dictionary<string, AskIntent> Names = Enum.GetValues<AskIntent>().ToDictionary(NameOf);
    static readonly HashSet<AskIntent> NeedsQuery = [AskIntent.Play, AskIntent.Playlist, AskIntent.Mood];

    /// <summary>A question rather than a command: it is given the longer time.</summary>
    public static bool LooksLikeQuestion(string text)
    {
        var s = DjAskRules.Normalise(text);
        return text.TrimEnd().EndsWith('?') || Regex.IsMatch(s, @"^(?:who|whos|what|whats|when|where|why|how|which|is|are|was|were|do|does|did|tell me)\b");
    }

    /// <summary>The model's action checked; null when it is not one of the actions, as JSON, with what it needs.</summary>
    public static AskParse? Read(string? answer) => ReadAll(answer).Parse;

    /// <summary>The model's reply checked: an action, or an answer fit to be spoken, or neither.</summary>
    public static AskAiResult ReadAll(string? answer)
    {
        AskAiResult none = new(null, null);
        if (string.IsNullOrWhiteSpace(answer)) return none;
        var a = answer.IndexOf('{'); var b = answer.LastIndexOf('}');
        if (a < 0 || b <= a) return none;
        JsonObject? o;
        try { o = JsonNode.Parse(answer[a..(b + 1)]) as JsonObject; } catch (System.Text.Json.JsonException) { return none; }
        if (o is null) return none;
        if (o["answer"] is JsonValue sv && sv.TryGetValue<string>(out var said)) return new(null, CleanAnswer(said));
        if (o["action"] is not JsonValue av || !av.TryGetValue<string>(out var action)) return none;
        if (!Names.TryGetValue(action.Trim().ToLowerInvariant(), out var intent)) return none;
        var query = o["query"] is JsonValue qv && qv.TryGetValue<string>(out var q) ? q.Trim() : null;
        if (query is { Length: > 120 } || query?.Any(char.IsControl) == true) query = null;
        if (intent == AskIntent.Situation)
        {
            var sit = query?.ToLowerInvariant() is { } q2 && SituationTaste.Known.Contains(q2) ? q2 : "none";
            var key = o["key"] is JsonValue kv && kv.TryGetValue<string>(out var k) && k.Length <= 60 ? PhraseKeys.Of(k) : "";
            var vibe = o["vibe"] is JsonValue vv && vv.TryGetValue<string>(out var vb) && vb.ToLowerInvariant() is "high" or "low" ? vb.ToLowerInvariant() : null;
            return new(new(intent, sit, key.Length > 0 ? key : null, vibe), null);
        }
        if (NeedsQuery.Contains(intent)) return query is { Length: > 0 } ? new(new(intent, query), null) : none;
        return new(new(intent, query is { Length: > 0 } ? query : null), null);
    }

    [GeneratedRegex(@"https?:|www\.|[<>{}\[\]|]")] private static partial Regex Unsafe();

    /// <summary>An answer fit to be spoken: no markdown, quotes or "DJ:", one paragraph, whole sentences up to the word cap; null when nothing fit is left.</summary>
    public static string? CleanAnswer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
        if (Unsafe().IsMatch(s)) return null;
        s = Regex.Replace(s, "[*_`#~\"“”]", "");
        s = Regex.Replace(s, @"^\s*(?:dj|answer|presenter)\s*:\s*", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"^\s*[-•]\s*", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        foreach (var r in s.EnumerateRunes())
            if (Rune.GetUnicodeCategory(r) is UnicodeCategory.OtherSymbol or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate) return null;
        if (s.Length < 2) return null;
        // whole sentences while they fit the cap
        var kept = new List<string>(); var words = 0;
        foreach (var x in Regex.Split(s, @"(?<=[.!?])\s+"))
        {
            var n = x.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (words + n > MaxAnswerWords || string.Join(' ', kept.Append(x)).Length > MaxAnswerChars) break;
            kept.Add(x); words += n;
        }
        return kept.Count == 0 ? null : string.Join(' ', kept);
    }

    public static async Task<AskParse?> MapAsync(IDjAskModel? model, string text, CancellationToken ct, TimeSpan? timeout = null) =>
        (await AskAsync(model, text, "", ct, timeout)).Parse;

    /// <summary>The model asked, with [facts] (what FLACie holds: the song playing, the library, the shared context). Not understood on any failure or timeout.</summary>
    public static async Task<AskAiResult> AskAsync(IDjAskModel? model, string text, string facts, CancellationToken ct, TimeSpan? timeout = null)
    {
        AskAiResult none = new(null, null);
        if (model is null) return none;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? Timeout);
        try
        {
            var user = (facts.Length > 0 ? "Facts:\n" + facts + "\n" : "Facts: none.\n") + "The listener said: " + text.Trim();
            var ask = model.AskAsync(System, user, limit.Token);
            return ReadAll(await ask.WaitAsync(limit.Token));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return none; }
        catch (TimeoutException) { return none; }
        catch (Exception e) when (e is not OperationCanceledException) { return none; }
    }
}

/// <summary>Everything the DJ's request reader needs from one person's FLACie. A fake in tests; <see cref="SessionAskUser"/> in the server.</summary>
public interface IDjAskUser
{
    /// <summary>The account (the shared <see cref="DjContext"/> and the conversation are kept per account).</summary>
    string Account { get; }
    bool Ready { get; }
    IReadOnlyList<Track> Songs { get; }
    IReadOnlyList<Playlist> Playlists { get; }
    Track? Find(string path);
    Track? FindSong(string artist, string title);
    IReadOnlyList<Track> Favourites { get; }
    /// <summary>The songs played most, most first (only those played at all).</summary>
    IReadOnlyList<Track> MostPlayed(int take);
    /// <summary>Played (or skipped) at least once on this account.</summary>
    bool Heard(Track t);
    IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n);
    Vibe? VibeOf(Track t);
    Task RateAsync(Track t, int stars);
    /// <summary>Looks the words up in the public catalog and starts downloading the best match (as Alexa does); null when nothing matches.</summary>
    Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct);
    /// <summary>The account's listening log (for what they play in the car, at night, after a phrase of their own).</summary>
    IReadOnlyList<ListenEvent> Listens => [];
    /// <summary>The account's learned requests, oldest first.</summary>
    IReadOnlyList<PhraseAsk> Phrases => [];
    void NotePhrase(PhraseAsk p) { }
    /// <summary>More that FLACie holds for certain about a song (how often it was played, what the MusicBrainz cache says), as "key: value" lines; "" when nothing.</summary>
    string About(Track t) => "";
}

/// <summary>
/// "Talk to the DJ": turns what was said into a <see cref="DjAskAnswer"/> for one person's player. The quick rules first, then the text model (an action, or a
/// short answer from the facts FLACie holds); songs, artists, albums and playlists are found with Alexa's <see cref="VoiceMatcher"/>, and a song the library
/// lacks is downloaded the way Alexa does it. Each account has its own <see cref="DjContext"/> (shared with the lines between songs), holding the last few
/// exchanges for ten minutes so "play more from them" follows on.
/// </summary>
public sealed partial class DjAskEngine(IDjAskModel? model = null, DjContexts? contexts = null, Func<DateTime>? clock = null)
{
    /// <summary>The replies that are always the same words: made ahead in FLACie's own voice (free), so these answers start at once (DjService.WarmRepliesAsync).
    /// Keep in step with the replies below.</summary>
    public static readonly string[] FixedReplies =
    [
        "Sorry, I didn't catch that.", "I'm here. I'll say a word between songs.", "Okay, I'll keep quiet.", "Nothing's playing right now.",
        "Sorry, I couldn't save that.", "Fair enough. I won't play that as much.", "Play something first, and I'll find more like it.",
        "Something like this, a bit slower.", "Something like this, with more pace.", "More like this, coming up.", "I couldn't find anything like this yet.",
        "Okay, moving on.", "Got it. Something different.", "You haven't got any favourites yet. Tap the heart on a song you love.", "Here are your favourites.",
        "I don't know what you play most yet.", "Your most played, coming up.", "You've heard everything in your library. Impressive.",
        "Here's something you haven't heard yet.", "There's nothing in your library yet.", "Shuffling your library.",
        "I'm still loading your library. Try again in a moment.", "Can't find that one anywhere, sorry.", "Can't find that one, getting it now.",
    ];

    public const string DidntCatch = "Sorry, I didn't catch that.";
    public TimeSpan AiTimeout { get; set; } = DjAskAi.Timeout;
    public TimeSpan QuestionTimeout { get; set; } = DjAskAi.QuestionTimeout;
    public DjContexts Contexts { get; } = contexts ?? new DjContexts();
    readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);

    sealed class Index(IReadOnlyList<Track> songs, IReadOnlyList<Playlist> lists) { public readonly IReadOnlyList<Track> Songs = songs; public readonly IReadOnlyList<Playlist> Lists = lists; public readonly VoiceMatcher M = new(songs, lists); }
    readonly ConditionalWeakTable<IDjAskUser, Index> indexes = new();

    VoiceMatcher MatcherFor(IDjAskUser u)
    {
        var songs = u.Songs; var lists = u.Playlists;
        lock (indexes)
        {
            if (indexes.TryGetValue(u, out var have) && ReferenceEquals(have.Songs, songs) && ReferenceEquals(have.Lists, lists)) return have.M;
            var fresh = new Index(songs, lists); indexes.AddOrUpdate(u, fresh); return fresh.M;
        }
    }

    static string T(string s) => Phonetic.CleanTitle(s).Trim();
    static bool Playable(Track t) => t.JellyfinId is { Length: > 0 } || t.FilePath is { Length: > 0 };

    [GeneratedRegex(@"^(?:play |put on |give me |some )?(?:some )?more (?:from|by|of) (?:them|him|her|that artist|this artist|that band|this band|those guys|that lot)$|^play (?:them|him|her) (?:again|more)$")]
    private static partial Regex MoreFromThem();

    /// <summary>The answer to [text]. [current]: the song playing (null when none); [recent]: the last few played, the last one playing now; [localHour]: the listener's hour.</summary>
    public async Task<DjAskAnswer> AnswerAsync(IDjAskUser u, string text, string? current, IReadOnlyList<string> recent, CancellationToken ct = default, int? localHour = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return DjAskAnswer.Said(DidntCatch);
        if (text.Length > 300) text = text[..300];
        var ctx = Contexts.For(u.Account);
        var cur = current is { Length: > 0 } ? u.Find(current) : null;
        var at = now();
        // "play more from them": the artist the conversation is about, else the one playing
        AskParse? parse = null; string? answered = null;
        if (MoreFromThem().IsMatch(DjAskRules.Normalise(text)) && (ctx.Subject(at) ?? cur?.Artist) is { Length: > 0 } them) parse = new(AskIntent.Play, them);
        parse ??= DjAskRules.Parse(text);
        if (parse is null)
        {
            var question = DjAskAi.LooksLikeQuestion(text);
            var ai = await DjAskAi.AskAsync(model, text, Facts(u, ctx, cur, text, localHour), ct, question ? QuestionTimeout : AiTimeout);
            parse = ai.Parse; answered = ai.Answer;
        }
        DjAskAnswer answer;
        if (parse is not null) answer = (await RunAsync(u, parse, cur, recent, ct, text)) with { Intent = parse.Intent.ToString() };
        else if (answered is not null) answer = DjAskAnswer.Said(answered, "Answer");
        else answer = DjAskAnswer.Said(DidntCatch);
        Remember(u, ctx, text, parse, answer, cur, at);
        return answer;
    }

    /// <summary>The conversation and what the person asked for go into the shared context (the asked words are forgotten after <see cref="DjContext.TalkLife"/>).</summary>
    void Remember(IDjAskUser u, DjContext ctx, string text, AskParse? p, DjAskAnswer a, Track? cur, DateTime at)
    {
        if (a.Say != DidntCatch) ctx.Asked(text, a.Say.Length > 0 ? a.Say : "(done)", at);
        if (p is null)
        {
            // an answer about an artist in the library: later "them" means that artist
            if (a.Say != DidntCatch && ArtistsIn(u, text).FirstOrDefault() is { } named) ctx.About(named, at);
            return;
        }
        switch (p.Intent)
        {
            case AskIntent.Play when a.Do == DjAskDo.Play && a.Paths is { Count: > 0 } ps && u.Find(ps[0]) is { } first:
                ctx.About(first.Artist, at);
                ctx.Requested(p.What ?? "", at);
                break;
            case AskIntent.Play when a.Wait is { } w: ctx.About(w.Artist, at); ctx.Requested(p.What ?? "", at); break;
            case AskIntent.Playlist or AskIntent.Mood or AskIntent.Situation when a.Do == DjAskDo.Play: ctx.Requested(a.Title ?? p.What ?? "", at); break;
            case AskIntent.Favourites or AskIntent.MostPlayed or AskIntent.Unheard or AskIntent.ShuffleLibrary when a.Do == DjAskDo.Play: ctx.Requested(a.Title ?? "", at); break;
            case AskIntent.MoreLike or AskIntent.LessLike when a.Do is DjAskDo.UpNext or DjAskDo.Play: ctx.Requested(p.Intent == AskIntent.MoreLike ? "more like " + (cur is null ? "this" : T(cur.Title)) : "something different", at); break;
            case AskIntent.WhatsThis or AskIntent.WhoSings when cur is not null: ctx.About(cur.Artist, at); break;
        }
    }

    /// <summary>Library artists named in [text] (whole words, at least three letters), longest name first.</summary>
    static IEnumerable<string> ArtistsIn(IDjAskUser u, string text)
    {
        var s = " " + DjAskRules.Normalise(text) + " ";
        return u.Songs.Select(t => t.Artist).Where(a => a.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(a => DjAskRules.Normalise(a) is { Length: >= 3 } n && (s.Contains(" " + n + " ") || n.StartsWith("the ") && s.Contains(" " + n[4..] + " ")))
            .OrderByDescending(a => a.Length);
    }

    /// <summary>
    /// What FLACie holds, for the model to answer from: the song playing (its tags, plays, MusicBrainz cache and the engine's reason for picking it), what the
    /// library has of any artist named (or the one being talked about), and the shared context. Compact; nothing guessed.
    /// </summary>
    public string Facts(IDjAskUser u, DjContext ctx, Track? cur, string text, int? localHour = null)
    {
        var sb = new StringBuilder();
        var at = now();
        if (cur is not null)
        {
            sb.Append("Playing now: ").Append(T(cur.Title)).Append(" by ").Append(cur.Artist);
            if (cur.Album.Length > 0) sb.Append(", from the album ").Append(cur.Album);
            if (cur.Year is >= 1900 and <= 2100) sb.Append(", ").Append(cur.Year.ToString(CultureInfo.InvariantCulture));
            if (cur.Genre.Length > 0) sb.Append(", genre ").Append(cur.Genre);
            sb.Append('\n');
            if (u.VibeOf(cur) is { } v)
                sb.Append("Its feel: ").Append(v.Energy < 0.35 ? "calm" : v.Energy > 0.65 ? "high energy" : "mid energy").Append(v.Bpm > 0 ? $", about {Math.Round(v.Bpm).ToString(CultureInfo.InvariantCulture)} bpm" : "").Append('\n');
            try { if (u.About(cur) is { Length: > 0 } more) sb.Append(more.Trim()).Append('\n'); } catch (Exception) { }
            if (ctx.WhyOf(cur.Path) is { Length: > 0 } why) sb.Append("Why you picked it: ").Append(string.Join("; ", DjWhy.Parts(why))).Append('\n');
            else sb.Append("Why it is playing: they chose it (not your pick)\n");
        }
        else sb.Append("Nothing is playing.\n");
        var artists = ArtistsIn(u, text).Take(2).ToList();
        if (artists.Count == 0 && ctx.Subject(at) is { } subj) artists.Add(subj);
        foreach (var a in artists)
        {
            var key = Matching.PrimaryArtist(a);
            var theirs = u.Songs.Where(t => Matching.PrimaryArtist(t.Artist) == key).ToList();
            if (theirs.Count == 0) { sb.Append("In their library by ").Append(a).Append(": nothing\n"); continue; }
            var albums = theirs.Select(t => t.Album).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
            var years = theirs.Select(t => t.Year).Where(y => y is >= 1900 and <= 2100).ToList();
            sb.Append("In their library by ").Append(a).Append(": ").Append(theirs.Count.ToString(CultureInfo.InvariantCulture)).Append(" songs");
            if (albums.Count > 0) sb.Append(", albums: ").Append(string.Join(", ", albums));
            if (years.Count > 0) sb.Append(", years ").Append(years.Min().ToString(CultureInfo.InvariantCulture)).Append('-').Append(years.Max().ToString(CultureInfo.InvariantCulture));
            var genres = theirs.Select(t => t.Genre).Where(g => g.Length > 0).GroupBy(g => g, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(2).ToList();
            if (genres.Count > 0) sb.Append(", genre ").Append(string.Join(", ", genres));
            sb.Append('\n');
        }
        try { if (u.Phrases.Select(x => x.Key).Distinct().TakeLast(12).ToList() is { Count: > 0 } ks) sb.Append("Phrases they have used before: ").Append(string.Join(", ", ks)).Append('\n'); } catch (Exception) { }
        sb.Append(ctx.Summary(at, localHour));
        return sb.ToString();
    }

    async Task<DjAskAnswer> RunAsync(IDjAskUser u, AskParse p, Track? cur, IReadOnlyList<string> recent, CancellationToken ct, string text = "")
    {
        switch (p.Intent)
        {
            case AskIntent.Next: return new(DjAskDo.Next, "");
            case AskIntent.Pause: return new(DjAskDo.Pause, "");
            case AskIntent.Resume: return new(DjAskDo.Resume, "");
            case AskIntent.VolumeUp: return new(DjAskDo.VolumeUp, "");
            case AskIntent.VolumeDown: return new(DjAskDo.VolumeDown, "");
            case AskIntent.DjOn: return new(DjAskDo.DjOn, "I'm here. I'll say a word between songs.");
            case AskIntent.DjOff: return new(DjAskDo.DjOff, "Okay, I'll keep quiet.");
            case AskIntent.WhatsThis:
                return cur is null ? DjAskAnswer.Said("Nothing's playing right now.") : DjAskAnswer.Said($"This is {T(cur.Title)}, by {cur.Artist}.");
            case AskIntent.WhoSings:
                return cur is null ? DjAskAnswer.Said("Nothing's playing right now.") : DjAskAnswer.Said($"That's {cur.Artist}.");
            case AskIntent.ThumbsDown:
                if (cur is null) return DjAskAnswer.Said("Nothing's playing right now.");
                try { await u.RateAsync(cur, 1); } catch (Exception) { return DjAskAnswer.Said("Sorry, I couldn't save that."); }
                return new(DjAskDo.Next, "Fair enough. I won't play that as much.");
            case AskIntent.MoreLike:
            {
                if (cur is null) return DjAskAnswer.Said("Play something first, and I'll find more like it.");
                // "like this but slower" (or faster): the engine's picks, kept to those calmer (or livelier) than this one where the feel is known
                var slower = p.What is { } w1 && Regex.IsMatch(w1, @"\b(?:slow|slower|calm|calmer|chill|softer|quieter|mellow)\b", RegexOptions.IgnoreCase);
                var faster = p.What is { } w2 && Regex.IsMatch(w2, @"\b(?:fast|faster|upbeat|livelier|harder|louder|energetic|more energy)\b", RegexOptions.IgnoreCase);
                var e = u.VibeOf(cur)?.Energy;
                bool Pace(Track t) => e is null || !(slower || faster) || u.VibeOf(t)?.Energy is not { } te || (slower ? te < e.Value - 0.05 : te > e.Value + 0.05);
                var picks = Picks(u, cur, recent, 40).Where(t => t.Path != cur.Path && Pace(t)).Take(15).Select(t => t.Path).ToList();
                var said = slower ? "Something like this, a bit slower." : faster ? "Something like this, with more pace." : "More like this, coming up.";
                return picks.Count == 0 ? DjAskAnswer.Said("I couldn't find anything like this yet.") : new(DjAskDo.UpNext, said, picks, Replace: true);
            }
            case AskIntent.LessLike:
            {
                if (cur is null) return DjAskAnswer.Said("Nothing's playing right now.");
                var artist = Matching.PrimaryArtist(cur.Artist);
                // away from this song: not this artist, not its genre, and a different energy where the vibes are known
                var e = u.VibeOf(cur)?.Energy;
                var away = u.Songs.Where(t => Playable(t) && Matching.PrimaryArtist(t.Artist) != artist && (cur.Genre.Length == 0 || !t.Genre.Equals(cur.Genre, StringComparison.OrdinalIgnoreCase)))
                    .Where(t => e is null || u.VibeOf(t)?.Energy is not { } te || Math.Abs(te - e.Value) > 0.15)
                    .OrderBy(_ => Random.Shared.Next()).Take(15).Select(t => t.Path).ToList();
                return away.Count == 0 ? new(DjAskDo.Next, "Okay, moving on.") : new(DjAskDo.UpNext, "Got it. Something different.", away, Replace: true);
            }
            case AskIntent.Favourites:
            {
                var fav = u.Favourites.Where(Playable).Select(t => t.Path).Distinct().Take(500).ToList();
                return fav.Count == 0 ? DjAskAnswer.Said("You haven't got any favourites yet. Tap the heart on a song you love.") : new(DjAskDo.Play, "Here are your favourites.", fav, "Favourites", Shuffle: true);
            }
            case AskIntent.MostPlayed:
            {
                var top = u.MostPlayed(100).Where(Playable).Select(t => t.Path).ToList();
                return top.Count == 0 ? DjAskAnswer.Said("I don't know what you play most yet.") : new(DjAskDo.Play, "Your most played, coming up.", top, "Most played", Shuffle: true);
            }
            case AskIntent.Unheard:
            {
                var fresh = u.Songs.Where(t => Playable(t) && !u.Heard(t)).OrderBy(_ => Random.Shared.Next()).Take(100).Select(t => t.Path).ToList();
                return fresh.Count == 0 ? DjAskAnswer.Said("You've heard everything in your library. Impressive.") : new(DjAskDo.Play, "Here's something you haven't heard yet.", fresh, "Something new to you");
            }
            case AskIntent.ShuffleLibrary:
            {
                var all = u.Songs.Where(Playable).OrderBy(_ => Random.Shared.Next()).Take(500).Select(t => t.Path).ToList();
                return all.Count == 0 ? DjAskAnswer.Said("There's nothing in your library yet.") : new(DjAskDo.Play, "Shuffling your library.", all, "Your library");
            }
            case AskIntent.Mood: return Mood(u, p.What ?? "", cur, recent);
            case AskIntent.Situation: return Situation(u, p, text, cur, recent);
            case AskIntent.Playlist: return Playlist(u, p.What ?? "");
            default: return await PlayAsync(u, p.What ?? "", cur, recent, ct);
        }
    }

    /// <summary>The engine's picks after the songs played lately (or this one), best first.</summary>
    static IReadOnlyList<Track> Picks(IDjAskUser u, Track? cur, IReadOnlyList<string> recent, int n)
    {
        var session = recent.Count > 0 ? recent.TakeLast(20).ToList() : cur is null ? [] : [cur.Path];
        try { return u.RecommendNext(session, n).Where(Playable).ToList(); } catch (Exception) { return []; }
    }

    DjAskAnswer Mood(IDjAskUser u, string what, Track? cur, IReadOnlyList<string> recent)
    {
        var words = DjAskRules.Normalise(what);
        var moods = DjAskRules.MoodWords.Where(m => Regex.IsMatch(words, $@"(?:^|\s){Regex.Escape(m.Word)}(?:\s|$)")).ToList();
        var genres = DjAskRules.GenreWords.Where(g => Regex.IsMatch(words, $@"(?:^|\s){Regex.Escape(g)}(?:\s|$)")).ToList();
        var shelves = moods.Select(m => HomeBuilder.Moods.First(x => x.Name == m.Shelf)).Distinct().ToList();
        var energy = moods.Select(m => m.Energy).FirstOrDefault(e => e != MoodEnergy.Any);
        bool GenreFits(Track t) => t.Genre.Length > 0 && (genres.Count > 0 ? genres : shelves.SelectMany(m => m.Genres)).Any(g => t.Genre.Contains(g, StringComparison.OrdinalIgnoreCase));
        bool EnergyFits(Track t) => energy == MoodEnergy.Any || u.VibeOf(t) is not { } v || (energy == MoodEnergy.Low ? v.Energy < 0.45 : v.Energy > 0.55);
        bool Fits(Track t) => genres.Count > 0 ? GenreFits(t) && EnergyFits(t)
            : (GenreFits(t) && EnergyFits(t)) || (energy != MoodEnergy.Any && u.VibeOf(t) is { } v && (energy == MoodEnergy.Low ? v.Energy < 0.35 : v.Energy > 0.65));
        // the engine's picks first (they know the person's taste), then the rest of the library that fits, shuffled
        var list = Picks(u, cur, recent, 50).Where(Fits).ToList();
        var taken = list.Select(t => t.Path).ToHashSet();
        list.AddRange(u.Songs.Where(t => Playable(t) && !taken.Contains(t.Path) && Fits(t)).OrderBy(_ => Random.Shared.Next()).Take(60));
        var paths = list.Select(t => t.Path).Distinct().Take(60).ToList();
        var name = genres.FirstOrDefault() ?? moods.FirstOrDefault().Word ?? words;
        if (paths.Count == 0) return DjAskAnswer.Said($"I couldn't find anything {name} in your library.");
        var say = genres.Count > 0 ? $"Here's some {name}." : $"Something {name}, coming up.";
        return new(DjAskDo.Play, say, paths, Capital(name));
    }

    /// <summary>
    /// A learned request: the songs this person plays in that situation (the car, night, morning) and after the same phrase before, best first, kept to the
    /// vibe; then the engine's picks, the artists they favour there first. With nothing learned yet, the vibe alone (and the DJ says so lightly). The phrase
    /// is noted, so the plays after it teach it.
    /// </summary>
    DjAskAnswer Situation(IDjAskUser u, AskParse p, string text, Track? cur, IReadOnlyList<string> recent)
    {
        var sit = p.What is { } w && SituationTaste.Known.Contains(w) ? w : "none";
        var nowMs = new DateTimeOffset(DateTime.SpecifyKind(now(), DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        IReadOnlyList<ListenEvent> events; IReadOnlyList<PhraseAsk> asks;
        try { events = u.Listens; asks = u.Phrases; } catch (Exception) { events = []; asks = []; }
        var key = PhraseKeys.Closest(p.Key ?? PhraseKeys.Of(text), asks.Select(a => a.Key));
        if (key.Length == 0) key = sit;
        var score = SituationTaste.Score(events, e => SituationTaste.In(e, sit), nowMs);
        foreach (var (path, v) in SituationTaste.Phrase(events, asks, key, nowMs)) score[path] = score.GetValueOrDefault(path) + 2 * v;
        try { u.NotePhrase(new(key, nowMs)); } catch (Exception) { }

        var energy = p.Vibe switch { "high" => MoodEnergy.High, "low" => MoodEnergy.Low, _ => sit switch { "car" or "workout" => MoodEnergy.High, "night" or "chill" => MoodEnergy.Low, _ => MoodEnergy.Any } };
        bool Fits(Track t) => energy == MoodEnergy.Any || u.VibeOf(t) is not { } v || (energy == MoodEnergy.Low ? v.Energy < 0.45 : v.Energy > 0.55);
        var byPath = new Dictionary<string, ListenEvent>(StringComparer.Ordinal);
        foreach (var e in events) if (e.Kind == ListenKind.Play) byPath[e.Path] = e;
        Track? Of(string path) => u.Find(path) ?? (byPath.TryGetValue(path, out var e) ? u.FindSong(e.Artist, e.Title) : null);
        var learned = score.Where(x => x.Value > 0).OrderByDescending(x => x.Value).Select(x => Of(x.Key)).OfType<Track>().Where(Playable).DistinctBy(t => t.Path).ToList();
        var against = score.Where(x => x.Value < 0).Select(x => Of(x.Key)?.Path).OfType<string>().ToHashSet();
        var label = sit is "none" ? key : sit;
        if (learned.Count == 0)
        {
            if (sit is "workout" or "chill") return Mood(u, sit, cur, recent);
            var fresh = Picks(u, cur, recent, 60).Where(t => Fits(t) && !against.Contains(t.Path)).ToList();
            var taken = fresh.Select(t => t.Path).ToHashSet();
            fresh.AddRange(u.Songs.Where(t => Playable(t) && !taken.Contains(t.Path) && !against.Contains(t.Path) && Fits(t) && u.VibeOf(t) is not null).OrderBy(_ => Random.Shared.Next()).Take(40));
            var paths = fresh.Select(t => t.Path).Distinct().Take(40).ToList();
            if (paths.Count == 0) return DjAskAnswer.Said($"I haven't got any {label} songs from you yet. Play a few and I'll learn them.");
            var feel = sit switch { "car" => "with a bit of drive", "night" => "a bit mellower", "morning" => "to ease you in", _ => energy == MoodEnergy.High ? "with some energy" : energy == MoodEnergy.Low ? "a bit calmer" : "I think you'll like" };
            return new(DjAskDo.Play, $"Haven't got many {label} songs from you yet, so here's something {feel}.", paths, Capital(label) + " songs");
        }
        var artists = learned.Select(t => Matching.PrimaryArtist(t.Artist)).ToHashSet();
        var list = learned.Where(Fits).Concat(learned.Where(t => !Fits(t))).ToList();
        var have = list.Select(t => t.Path).ToHashSet();
        var more = Picks(u, cur, recent, 80).Where(t => !have.Contains(t.Path) && !against.Contains(t.Path) && Fits(t))
            .OrderByDescending(t => artists.Contains(Matching.PrimaryArtist(t.Artist))).ToList();
        var all = list.Concat(more).Select(t => t.Path).Distinct().Take(40).ToList();
        return new(DjAskDo.Play, $"Your {label} songs, coming up.", all, Capital(label) + " songs");
    }

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    DjAskAnswer Playlist(IDjAskUser u, string name)
    {
        var m = MatcherFor(u).Match(new VoiceQuery(null, null, null, name, null, null, null));
        if (m.Decision == Decision.NotFound || m.Ranked[0].Item is not Playlist pl) return DjAskAnswer.Said($"I can't find a playlist called {name}.");
        var paths = PathsOf(u, pl);
        return paths.Count == 0 ? DjAskAnswer.Said($"Your {pl.Name} playlist has nothing I can play yet.") : new(DjAskDo.Play, $"Here's your {pl.Name} playlist.", paths, pl.Name);
    }

    static List<string> PathsOf(IDjAskUser u, Playlist p) =>
        p.Entries.Select(e => u.Find(e.Path) ?? u.Songs.FirstOrDefault(x => Matching.MatchKey(x.Title, x.Artist) == Matching.MatchKey(e.Title, e.Artist)))
            .OfType<Track>().Where(Playable).Select(t => t.Path).Distinct().Take(500).ToList();

    async Task<DjAskAnswer> PlayAsync(IDjAskUser u, string what, Track? cur, IReadOnlyList<string> recent, CancellationToken ct)
    {
        if (!u.Ready) return DjAskAnswer.Said("I'm still loading your library. Try again in a moment.");
        var query = VoiceParser.FromRaw("play " + what);
        if (query.Genre is not null || query.Decade is not null) return Mood(u, what, cur, recent);
        if (query.SimilarTo is not null) query = query with { Artist = query.SimilarTo };
        var m = MatcherFor(u).Match(query);
        if (m.Decision != Decision.NotFound)
        {
            var best = m.Ranked[0];
            switch (best.Item)
            {
                case Track t when Playable(t):
                    var others = u.Songs.Where(x => x.Path != t.Path && Playable(x) && Matching.PrimaryArtist(x.Artist) == Matching.PrimaryArtist(t.Artist)).OrderBy(_ => Random.Shared.Next()).Take(25).Select(x => x.Path);
                    return new(DjAskDo.Play, $"{T(t.Title)}, by {t.Artist}.", [t.Path, .. others], T(t.Title));
                case Track t:
                    return DjAskAnswer.Said($"{T(t.Title)} is on its way into your library. Give it a minute.");
                case FLACie.Core.Group g when best.Kind == MatchKind.Artist:
                    var songs = g.Tracks.Where(Playable).Select(x => x.Path).Take(300).ToList();
                    if (songs.Count > 0) return new(DjAskDo.Play, $"Here's some {g.Name}.", songs, g.Name, Shuffle: true);
                    break;
                case FLACie.Core.Group a:
                    var tracks = a.Tracks.Where(Playable).OrderBy(x => x.DiscNo).ThenBy(x => x.TrackNo).Select(x => x.Path).ToList();
                    if (tracks.Count > 0) return new(DjAskDo.Play, $"Here's {a.Name}.", tracks, a.Name);
                    break;
                case Playlist p:
                    var paths = PathsOf(u, p);
                    if (paths.Count > 0) return new(DjAskDo.Play, $"Here's your {p.Name} playlist.", paths, p.Name);
                    break;
            }
        }
        // not in the library: fetched as Alexa fetches it, and played when it lands
        var words = query.Title is { Length: > 0 } ti ? (query.Artist is { Length: > 0 } ar ? $"{ti} {ar}" : ti) : query.Album ?? query.Raw ?? query.Artist ?? what;
        if (words.Trim().Length < 2) return DjAskAnswer.Said(DidntCatch);
        AlexaRequested? req;
        try { req = await u.RequestDownloadAsync(words, ct); } catch (OperationCanceledException) { throw; } catch (Exception) { req = null; }
        if (req is null) return DjAskAnswer.Said("Can't find that one anywhere, sorry.");
        if (req.InLibrary && u.FindSong(req.Artist, req.Title) is { } have && Playable(have))
            return new(DjAskDo.Play, $"{T(have.Title)}, by {have.Artist}.", [have.Path], T(have.Title));
        return new(DjAskDo.None, "Can't find that one, getting it now.", Wait: new(req.Artist, req.Title));
    }

    /// <summary>Whether a song asked for (and downloading) has landed: its path once it can be played, else null.</summary>
    public static string? Landed(IDjAskUser u, string artist, string title) => u.FindSong(artist, title) is { } t && Playable(t) ? t.Path : null;
}
