using System.Security;
using System.Text.Json.Nodes;
using FLACie.Core;

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
public sealed class AlexaSkill(AlexaEngine engine, AlexaStore store, IAlexaDirectory directory, AlexaUrlSigner signer, Func<string> baseUrl, IProgressive? progressive = null, ILogger? log = null, Func<DateTime>? clock = null)
{
    public static readonly TimeSpan StreamLife = TimeSpan.FromHours(6);
    public const string NotifyPermission = "alexa::devices:all:notifications:write";
    readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);

    public const string HelpText = "You can say: play Blinding Lights, play Radiohead, play my Road Trip playlist, play something like Radiohead, or what's playing. If I don't have a song, I can download it.";
    const string LinkText = "To use FLACie, open Settings in FLACie, choose Alexa, and create a code. Then say: my code is, followed by the six digits.";

    public async Task<JsonObject> HandleAsync(JsonNode request, CancellationToken ct = default)
    {
        var type = (string?)request["request"]?["type"] ?? "";
        var userId = (string?)request["context"]?["System"]?["user"]?["userId"] ?? (string?)request["session"]?["user"]?["userId"] ?? "";
        var locale = (string?)request["request"]?["locale"] ?? "";
        log?.LogInformation("Alexa request {Type} {Intent}", type, (string?)request["request"]?["intent"]?["name"] ?? "");

        if (userId.Length == 0) return Empty();
        var granted = (string?)request["context"]?["System"]?["user"]?["permissions"]?["consentToken"] is { Length: > 0 };

        // the pairing code is the one thing an unlinked account may do
        if (type == "IntentRequest" && (string?)request["request"]?["intent"]?["name"] == "LinkIntent")
            return Link(userId, Slot(request, "code") ?? "", locale);

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
            reply = Reply.Say((notice is null ? "FLACie. " : notice + " ") + "What would you like to hear?", end: false);
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
            case "PlayIntent":
                var words = Slot(request, "query") ?? "";
                if (words.Trim().Length == 0) return Reply.Say("What would you like to hear?", end: false);
                return await engine.PlayAsync(user, link, VoiceParser.FromRaw("play " + words), Progress, ct);
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
            case "AMAZON.NextIntent": return engine.Next(user, link);
            case "AMAZON.PreviousIntent": return engine.Previous(user, link);
            case "AMAZON.StartOverIntent" or "AMAZON.RepeatIntent": return engine.StartOver(user, link);
            case "AMAZON.ShuffleOnIntent": return engine.SetShuffle(user, link, true);
            case "AMAZON.ShuffleOffIntent": return engine.SetShuffle(user, link, false);
            case "AMAZON.LoopOnIntent": return engine.SetRepeat(link, AlexaRepeat.All);
            case "AMAZON.LoopOffIntent": return engine.SetRepeat(link, AlexaRepeat.Off);
            case "AMAZON.HelpIntent": return Reply.Say(HelpText, end: false);
            case "AMAZON.NavigateHomeIntent": return new("", EndSession: true);
            default: return Reply.Say("Sorry, I didn't get that. Say help for examples.", end: false);
        }
    }

    JsonObject AudioEvent(JsonNode request, string type, string link, IAlexaUser user)
    {
        var token = (string?)request["request"]?["token"];
        var offset = (long?)request["request"]?["offsetInMilliseconds"] ?? 0;
        switch (type)
        {
            case "AudioPlayer.PlaybackStarted": engine.Started(link, token); return Empty();
            case "AudioPlayer.PlaybackStopped": engine.Stopped(link, token, offset); return Empty();
            case "AudioPlayer.PlaybackFinished": engine.Finished(link, token); return Empty();
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
    static JsonObject Ssml(string text) => new() { ["type"] = "SSML", ["ssml"] = "<speak>" + SecurityElement.Escape(text) + "</speak>" };

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
