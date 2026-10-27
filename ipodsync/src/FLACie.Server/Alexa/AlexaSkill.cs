using System.Security;
using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server.Dj;

namespace FLACie.Server.Alexa;

/// <summary>Finds the FLACie library behind a linked Alexa account.</summary>
public interface IAlexaDirectory
{
    /// <summary>The link id and the user for this Alexa account, or null when it is not linked (or the sign-in behind the link is no longer valid).</summary>
    (string LinkId, IAlexaUser User)? Resolve(string alexaUserId);
}

/// <summary>Sends a "one moment" message while the answer is still being worked out (Alexa's progressive response).</summary>
public interface IProgressive { Task SendAsync(string apiEndpoint, string accessToken, string requestId, string speech, CancellationToken ct); }

/// <summary>
/// The custom skill: Alexa's JSON in, Alexa's JSON out. Everything that decides what to say is in <see cref="AlexaEngine"/>; this class only reads the request (intent, slots, the
/// AudioPlayer state), links an Alexa account to a person with the pairing code, and writes the answer (speech, AudioPlayer.Play with a signed stream URL, cards).
/// </summary>
public sealed class AlexaSkill(AlexaEngine engine, AlexaStore store, IAlexaDirectory directory, AlexaUrlSigner signer, Func<string> baseUrl, IProgressive? progressive = null, ILogger? log = null, Func<DateTime>? clock = null, IAlexaBrain? brain = null)
{
    /// <summary>The question intents: the word their samples start with is not in the slot, so it is put back.</summary>
    static readonly Dictionary<string, string> AskIntents = new() { ["AskWhoIntent"] = "who ", ["AskWhyIntent"] = "why ", ["AskWhatIntent"] = "what ", ["AskHowIntent"] = "how ", ["AskDjIntent"] = "" };
    /// <summary>What the DJ's reader answers better than the name matcher: "play car songs", "my favourites", "something I haven't heard", "more like this".</summary>
    static readonly HashSet<AskIntent> DjKinds = [AskIntent.Situation, AskIntent.Favourites, AskIntent.MostPlayed, AskIntent.Unheard, AskIntent.MoreLike, AskIntent.LessLike];
    /// <summary>Per link, the last song token put in the listening log (one event per play).</summary>
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> logged = new();

    public static readonly TimeSpan StreamLife = TimeSpan.FromHours(6);
    public const string NotifyPermission = "alexa::devices:all:notifications:write";
    readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);

    /// <summary>Every custom intent this class answers; the interaction model must list exactly these (plus the built-ins), checked by a test.</summary>
    public static readonly string[] CustomIntents = ["PlayIntent", "PlaySongIntent", "PlayAlbumIntent", "PlayPlaylistIntent", "PlayArtistIntent", "PlaySimilarIntent", "PlayRecentIntent", "PlayLibraryIntent", "PlayLastDownloadIntent", "IsItReadyIntent", "WhatsDownloadingIntent", "WhatsPlayingIntent", "LikeIntent", "DislikeIntent", "AddToPlaylistIntent", "ChooseIntent", "LinkIntent", "NameIntent", "AskWhoIntent", "AskWhyIntent", "AskWhatIntent", "AskHowIntent", "AskDjIntent"];

    public const string HelpText = "You can say: play Blinding Lights, play Radiohead, play my Road Trip playlist, play something like Radiohead, or what's playing. If I don't have a song, I can download it.";
    const string LinkText = "To use FLACie, open Settings in FLACie, choose Alexa, and create a code. Then say: my code is, followed by the six digits.";

    public async Task<JsonObject> HandleAsync(JsonNode request, CancellationToken ct = default)
    {
        var type = (string?)request["request"]?["type"] ?? "";
        var userId = (string?)request["context"]?["System"]?["user"]?["userId"] ?? (string?)request["session"]?["user"]?["userId"] ?? (string?)request["request"]?["body"]?["userId"] ?? "";
        var locale = (string?)request["request"]?["locale"] ?? "";
        log?.LogInformation("Alexa request {Type} {Intent}", type, (string?)request["request"]?["intent"]?["name"] ?? "");

        if (userId.Length == 0) return Empty();
        var granted = (string?)request["context"]?["System"]?["user"]?["permissions"]?["consentToken"] is { Length: > 0 };

        // the pairing code is the one thing an unlinked account may do
        if (type == "IntentRequest" && (string?)request["request"]?["intent"]?["name"] == "LinkIntent")
            return Slot(request, "code") is { Length: > 0 } spoken ? Link(userId, spoken, locale) : Speak(store.IsLinked(userId) ? "FLACie is already linked to your account." : LinkText, end: false);

        // the person allowed (or took back) notifications in the Alexa app: the skill-event subscription in the manifest sends this
        if (type == "AlexaSkillEvent.ProactiveSubscriptionChanged")
        {
            store.Touch(userId, locale, (request["request"]?["body"]?["subscriptions"] as JsonArray)?.Count > 0);
            return Empty();
        }
        if (type.StartsWith("AlexaSkillEvent.SkillPermission", StringComparison.Ordinal))
        {
            // Amazon names the scope "permissionScope"; "scope" is accepted too (the shape was written from the documentation and has not met a real event)
            var scopes = (request["request"]?["body"]?["acceptedPermissions"] as JsonArray)?.Select(p => (string?)p?["permissionScope"] ?? (string?)p?["scope"]).ToList() ?? [];
            store.Touch(userId, locale, type == "AlexaSkillEvent.SkillPermissionAccepted" ? scopes.Contains(NotifyPermission) : type == "AlexaSkillEvent.SkillPermissionChanged" ? scopes.Contains(NotifyPermission) : null);
            return Empty();
        }

        var found = directory.Resolve(userId);
        if (found is null)
            return type is "LaunchRequest" or "IntentRequest" ? Speak(store.IsLinked(userId) ? "Your FLACie sign-in has expired. Link FLACie again in Settings." : LinkText, end: false) : Empty();
        var (link, user) = found.Value;
        store.Touch(userId, locale, granted ? true : null);

        // where the player is, whatever the request is
        var ap = request["context"]?["AudioPlayer"];
        if (ap?["token"] is JsonValue tv && tv.TryGetValue<string>(out var curToken) && ap["offsetInMilliseconds"] is { } off)
            engine.Position(link, curToken, (long?)off ?? 0);

        if (type.StartsWith("AudioPlayer.", StringComparison.Ordinal)) return AudioEvent(request, type, link, user);
        if (type is "PlaybackController.NextCommandIssued" or "PlaybackController.PreviousCommandIssued") LogNow(link, user);
        if (type.StartsWith("PlaybackController.", StringComparison.Ordinal)) return Build(type switch
        {
            "PlaybackController.NextCommandIssued" => engine.Next(user, link),
            "PlaybackController.PreviousCommandIssued" => engine.Previous(user, link),
            "PlaybackController.PlayCommandIssued" => engine.Resume(user, link),
            "PlaybackController.PauseCommandIssued" => engine.Pause(link),
            _ => new Reply(""),
        }, link, speech: false);
        if (type == "SessionEndedRequest" || type == "System.ExceptionEncountered") return Empty();

        Reply reply;
        if (type == "LaunchRequest")
        {
            var notice = engine.TakeNotice(link);
            // "play" first: a bare song name after this question is not understood (Alexa needs the word to know it is a request), and the owner's first try was lost to it
            reply = Reply.Say((notice is null ? "FLACie. " : notice + " ") + "What would you like to hear? Say play, then a song, artist, album or playlist.", end: false);
        }
        else reply = await Intent(request, link, user, ct);

        if (type == "IntentRequest" && reply.Play is not null && engine.TakeNotice(link) is { } notice2) reply = reply with { Speech = notice2 + " " + reply.Speech };
        var card = reply.AskNotifyPermission && !granted && engine.ShouldAskPermission(link);
        return Build(reply, link, askPermission: card);
    }

    async Task<Reply> Intent(JsonNode request, string link, IAlexaUser user, CancellationToken ct)
    {
        var name = (string?)request["request"]?["intent"]?["name"] ?? "";
        async Task Progress(string s)
        {
            if (progressive is null) return;
            var sys = request["context"]?["System"];
            var ep = (string?)sys?["apiEndpoint"]; var tok = (string?)sys?["apiAccessToken"]; var rid = (string?)request["request"]?["requestId"];
            if (ep is null || tok is null || rid is null || !IsAlexaApi(ep)) return;
            await progressive.SendAsync(ep, tok, rid, s, ct);
        }
        switch (name)
        {
            // a bare name from the library ("ask flaky to play X" reaches the skill as just "X": Amazon takes "ask flaky to play" as the launch phrase)
            case "NameIntent":
                var bare = (Slot(request, "name") ?? "").Trim();
                if (bare.Length == 0) return Reply.Say("What would you like to hear? Start with play, like: play Out of Space.", end: false);
                if (ForDj(bare) is { } dj) return await AskDj(dj, link, user, ct);
                return await engine.PlayAsync(user, link, VoiceParser.FromRaw("play " + bare), Progress, ct);
            case "PlayIntent":
                var words = Slot(request, "query") ?? "";
                if (words.Trim().Length == 0) return Reply.Say("What would you like to hear? Start with play, like: play Out of Space.", end: false);
                if (ForDj(words.Trim()) is { } dj2) return await AskDj(dj2, link, user, ct);
                return await engine.PlayAsync(user, link, VoiceParser.FromRaw("play " + words), Progress, ct);
            // the words that named the kind ("the album ...") are not in the slot, so each kind is its own intent and puts them back for the parser
            case "PlaySongIntent" or "PlayAlbumIntent" or "PlayPlaylistIntent" or "PlayArtistIntent" or "PlaySimilarIntent":
                var what = (Slot(request, "query") ?? "").Trim();
                if (what.Length == 0) return Reply.Say("What would you like to hear? Start with play, like: play Out of Space.", end: false);
                var phrase = name switch { "PlaySongIntent" => "play the song ", "PlayAlbumIntent" => "play the album ", "PlayArtistIntent" => "play songs by ", "PlaySimilarIntent" => "play something like ", _ => "play my " } + what + (name == "PlayPlaylistIntent" ? " playlist" : "");
                return await engine.PlayAsync(user, link, VoiceParser.FromRaw(phrase), Progress, ct);
            case "PlayRecentIntent": return engine.PlayRecent(user, link);
            case "PlayLibraryIntent": return engine.PlayLibrary(user, link);
            case "PlayLastDownloadIntent": return engine.PlayLastDownload(user, link);
            case "IsItReadyIntent": return engine.IsItReady(user, link);
            case "WhatsDownloadingIntent": return engine.WhatsDownloading(user);
            case "WhatsPlayingIntent": return engine.WhatsPlaying(user, link);
            case "LikeIntent": return await engine.RateAsync(user, link, true);
            case "DislikeIntent": return await engine.RateAsync(user, link, false);
            case "AddToPlaylistIntent":
                var pl = System.Text.RegularExpressions.Regex.Replace((Slot(request, "playlist") ?? "").Trim(), @"^(?:my |the )|\s+playlist$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                return engine.AddToPlaylist(user, link, pl);
            case "ChooseIntent": return await engine.ChooseAsync(user, link, ChoiceIndex(request), Progress, ct);
            case "AMAZON.YesIntent": return await engine.ChooseAsync(user, link, 0, Progress, ct);
            case "AMAZON.NoIntent": return await engine.ChooseAsync(user, link, -1, Progress, ct);
            case "AMAZON.PauseIntent" or "AMAZON.StopIntent" or "AMAZON.CancelIntent": return engine.Pause(link);
            case "AMAZON.ResumeIntent": return engine.Resume(user, link);
            case "AMAZON.NextIntent": LogNow(link, user); return engine.Next(user, link);
            case "AMAZON.PreviousIntent": LogNow(link, user); return engine.Previous(user, link);
            case var ask when AskIntents.TryGetValue(ask, out var word):
                var said = (Slot(request, "text") ?? "").Trim();
                if (said.Length == 0) return Reply.Say("Sorry, I didn't get that. Start with play, like: play Out of Space. Or say help.", end: false);
                return await AskDj(word + said, link, user, ct);
            case "AMAZON.StartOverIntent" or "AMAZON.RepeatIntent": return engine.StartOver(user, link);
            case "AMAZON.ShuffleOnIntent": return engine.SetShuffle(user, link, true);
            case "AMAZON.ShuffleOffIntent": return engine.SetShuffle(user, link, false);
            case "AMAZON.LoopOnIntent": return engine.SetRepeat(link, AlexaRepeat.All);
            case "AMAZON.LoopOffIntent": return engine.SetRepeat(link, AlexaRepeat.Off);
            case "AMAZON.HelpIntent": return Reply.Say(HelpText, end: false);
            case "AMAZON.NavigateHomeIntent": return new("", EndSession: true);
            default: return Reply.Say("Sorry, I didn't get that. Start with play, like: play Out of Space. Or say help.", end: false);
        }
    }

    /// <summary>Words the DJ's reader should have rather than the name matcher (null for a song, artist, album or playlist name).</summary>
    string? ForDj(string words)
    {
        if (brain is null) return null;
        var text = words.StartsWith("play ", StringComparison.OrdinalIgnoreCase) ? words : "play " + words;
        return DjAskRules.Parse(text) is { } p && DjKinds.Contains(p.Intent) ? text : null;
    }

    /// <summary>The words through the DJ's request reader for this account; its answer carried out on Alexa's player and said in Alexa's own voice.</summary>
    async Task<Reply> AskDj(string text, string link, IAlexaUser user, CancellationToken ct)
    {
        if (brain is null) return Reply.Say("Sorry, I didn't get that. Start with play, like: play Out of Space. Or say help.", end: false);
        var (current, recent) = engine.Context(link);
        DjAskAnswer? a;
        try { a = await brain.AskAsync(user, text, current, recent, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { log?.LogWarning(e, "Alexa DJ ask failed"); a = null; }
        if (a is null) return Reply.Say("Sorry, I couldn't work that out.");
        var say = a.Say;
        switch (a.Do)
        {
            case DjAskDo.Play when a.Paths is { Count: > 0 } ps: return engine.PlayPaths(user, link, ps, a.Title ?? "", a.Shuffle, say);
            case DjAskDo.UpNext when a.Paths is { Count: > 0 } ps: return engine.QueueNext(user, link, ps, say);
            case DjAskDo.Next: LogNow(link, user); var n = engine.Next(user, link); return say.Length > 0 && n.Play is not null ? n with { Speech = say } : n;
            case DjAskDo.Pause: return engine.Pause(link);
            case DjAskDo.Resume: return engine.Resume(user, link);
            case DjAskDo.VolumeUp or DjAskDo.VolumeDown: return Reply.Say(say.Length > 0 ? say : "Say: Alexa, volume " + (a.Do == DjAskDo.VolumeUp ? "up." : "down."));
            default: return say.Length > 0 ? Reply.Say(say) : Reply.Say("Sorry, I didn't get that. Start with play, like: play Out of Space. Or say help.", end: false);
        }
    }

    /// <summary>A play into the account's listening log, once per token: a finish is the whole song, a stop is what was heard.</summary>
    void Log(string link, IAlexaUser user, string? token, long offsetMs, bool finished)
    {
        if (brain is null || token is null || engine.TrackOf(user, link, token) is not { } t) return;
        if (logged.TryGetValue(link, out var last) && last == token) return;
        logged[link] = token;
        var length = t.DurationMs / 1000.0;
        brain.Listened(user, t, finished ? length : Math.Min(length > 0 ? length : double.MaxValue, offsetMs / 1000.0), length);
    }

    /// <summary>The song playing now, as it is left for another ("next"): Alexa's last reported place in it.</summary>
    void LogNow(string link, IAlexaUser user) { if (engine.Now(link) is { } n) Log(link, user, n.Token, n.OffsetMs, finished: false); }

    JsonObject AudioEvent(JsonNode request, string type, string link, IAlexaUser user)
    {
        var token = (string?)request["request"]?["token"];
        var offset = (long?)request["request"]?["offsetInMilliseconds"] ?? 0;
        switch (type)
        {
            case "AudioPlayer.PlaybackStarted": engine.Started(link, token); return Empty();
            case "AudioPlayer.PlaybackStopped": Log(link, user, token, offset, finished: false); engine.Stopped(link, token, offset); return Empty();
            case "AudioPlayer.PlaybackFinished": Log(link, user, token, 0, finished: true); engine.Finished(link, token); return Empty();
            case "AudioPlayer.PlaybackNearlyFinished":
                return engine.NearlyFinished(user, link, token) is { } next ? Directives(PlayDirective(next, "ENQUEUE", link)) : Empty();
            case "AudioPlayer.PlaybackFailed":
                log?.LogWarning("Alexa playback failed: {Error}", (string?)request["request"]?["error"]?["type"] ?? "");
                return engine.Failed(user, link, token) is { } p ? Directives(PlayDirective(p, "REPLACE_ALL", link)) : Empty();
            default: return Empty();
        }
    }

    JsonObject Link(string alexaUserId, string spoken, string locale)
    {
        if (store.Redeem(alexaUserId, spoken, locale) is null) return Speak("That code didn't work. Create a new one in FLACie Settings and try again.", end: false);
        return Speak("Done. FLACie is linked. Try saying: play a song.", end: false);
    }

    // ---- reading ----

    static string? Slot(JsonNode request, string slot) => (string?)request["request"]?["intent"]?["slots"]?[slot]?["value"];

    static int ChoiceIndex(JsonNode request)
    {
        var id = (string?)request["request"]?["intent"]?["slots"]?["choice"]?["resolutions"]?["resolutionsPerAuthority"]?[0]?["values"]?[0]?["value"]?["id"];
        var text = (id ?? Slot(request, "choice") ?? "").Trim().ToLowerInvariant();
        return text switch { "1" or "one" or "first" or "the first one" or "first one" => 0, "2" or "two" or "second" or "the second one" or "second one" => 1, "no" or "neither" or "none" => -1, _ => text.Contains("second") ? 1 : 0 };
    }

    static bool IsAlexaApi(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && (u.Host == "api.amazonalexa.com" || u.Host.EndsWith(".amazonalexa.com", StringComparison.OrdinalIgnoreCase));

    // ---- writing ----

    static JsonObject Envelope(JsonObject response) => new() { ["version"] = "1.0", ["sessionAttributes"] = new JsonObject(), ["response"] = response };
    static JsonObject Empty() => Envelope(new() { ["shouldEndSession"] = true });
    static JsonObject Directives(JsonObject d) => Envelope(new() { ["directives"] = new JsonArray(d), ["shouldEndSession"] = true });
    // FLACie is said the way its people say it ("flacky"); Alexa read the written name as "flacy"
    static JsonObject Ssml(string text) => new() { ["type"] = "SSML", ["ssml"] = "<speak>" + SecurityElement.Escape(text).Replace("FLACie", "<sub alias=\"flacky\">FLACie</sub>") + "</speak>" };

    static JsonObject Speak(string text, bool end)
    {
        var r = new JsonObject { ["outputSpeech"] = Ssml(text), ["shouldEndSession"] = end };
        if (!end) r["reprompt"] = new JsonObject { ["outputSpeech"] = Ssml(text.Length > 90 ? HelpText : text) };
        return Envelope(r);
    }

    JsonObject Build(Reply reply, string link, bool speech = true, bool askPermission = false)
    {
        var r = new JsonObject();
        if (speech && reply.Speech.Length > 0) r["outputSpeech"] = Ssml(reply.Speech);
        var dirs = new JsonArray();
        if (reply.Play is { } play) dirs.Add(PlayDirective(play, "REPLACE_ALL", link));
        else if (reply.Stop || reply.ClearQueue) dirs.Add(new JsonObject { ["type"] = "AudioPlayer.Stop" });
        if (dirs.Count > 0) r["directives"] = dirs;
        // with audio the session must end, or Alexa pauses the stream to listen
        var end = reply.Play is not null || reply.Stop || reply.EndSession;
        r["shouldEndSession"] = end;
        if (!end) r["reprompt"] = new JsonObject { ["outputSpeech"] = Ssml(reply.Reprompt is { Length: > 0 } rp && rp.Length <= 90 ? rp : HelpText) };
        if (askPermission) r["card"] = new JsonObject { ["type"] = "AskForPermissionsConsent", ["permissions"] = new JsonArray(NotifyPermission) };
        return Envelope(r);
    }

    JsonObject PlayDirective(AlexaTrackPlay p, string behavior, string link)
    {
        var url = baseUrl().TrimEnd('/') + signer.Sign("t", link, p.Track.JellyfinId!, "mp3", StreamLife);
        var stream = new JsonObject { ["token"] = p.Token, ["url"] = url, ["offsetInMilliseconds"] = p.OffsetMs };
        if (behavior == "ENQUEUE") stream["expectedPreviousToken"] = p.ExpectedPrevious;
        var item = new JsonObject { ["stream"] = stream };
        // title, subtitle, art and background are sent together or not at all
        var art = ArtUrl(p.Track, link);
        if (art is not null)
            item["metadata"] = new JsonObject
            {
                ["title"] = Phonetic.CleanTitle(p.Track.Title), ["subtitle"] = p.Track.Artist,
                ["art"] = new JsonObject { ["sources"] = new JsonArray(new JsonObject { ["url"] = art }) },
                ["backgroundImage"] = new JsonObject { ["sources"] = new JsonArray(new JsonObject { ["url"] = art }) },
            };
        return new JsonObject { ["type"] = "AudioPlayer.Play", ["playBehavior"] = behavior, ["audioItem"] = item };
    }

    string? ArtUrl(Track t, string link)
    {
        if (t.ArtKey is not { } k || !k.StartsWith("jf", StringComparison.Ordinal) || k.Length < 34) return null;
        var id = k[2..].Replace('~', '-');
        if (!id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return null;
        return baseUrl().TrimEnd('/') + signer.Sign("a", link, id, "jpg", TimeSpan.FromHours(24));
    }
}
