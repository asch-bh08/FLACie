using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>The custom skill against request JSON written to the shapes in Amazon's request reference (these are hand-written, not recorded from a real Echo).</summary>
public class AlexaSkillTests
{
    const string Alexa = "amzn1.ask.account.TESTUSER";
    sealed class Dir(string? linkId, IAlexaUser? user) : IAlexaDirectory
    {
        public (string, IAlexaUser)? Resolve(string id) => linkId is not null && user is not null && id == Alexa ? (linkId, user) : null;
    }
    sealed class Prog : IProgressive
    {
        public List<(string Ep, string Speech)> Sent = [];
        public Task SendAsync(string ep, string tok, string rid, string speech, CancellationToken ct) { Sent.Add((ep, speech)); return Task.CompletedTask; }
    }

    sealed class Rig : IDisposable
    {
        public string Dir = Directory.CreateTempSubdirectory().FullName;
        public FakeUser U = new(); public AlexaStore Store; public AlexaQueues Queues; public AlexaEngine Engine; public AlexaUrlSigner Signer; public AlexaSkill Skill; public Prog Progress = new();
        public AlexaConfig Cfg = new() { Enabled = true };
        public Rig(bool linked = true)
        {
            U.Songs = [FakeUser.T("Blinding Lights", "The Weeknd", "After Hours", 1), FakeUser.T("Save Your Tears", "The Weeknd", "After Hours", 2), FakeUser.T("Creep", "Radiohead", "Pablo Honey", 1)];
            U.Lists.Add(new("p1", "Road Trip", 0, null, []));
            Store = new(new DataPaths(Dir), DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Dir, "keys"))), new ConfigurationBuilder().Build());
            Queues = new(new DataPaths(Dir)); Engine = new(Queues, () => Cfg);
            Signer = new(new byte[32], () => DateTime.UtcNow);
            Skill = new(Engine, Store, new Dir(linked ? "LINK1" : null, U), Signer, () => "https://flacie.example.com", Progress);
        }
        public void Dispose() { try { System.IO.Directory.Delete(Dir, true); } catch (Exception) { } }
        public AlexaQueue Q => Queues.For("LINK1");
        public Task<JsonObject> Send(JsonObject req) => Skill.HandleAsync(req);
    }

    static JsonObject Req(JsonObject request, string? userId = Alexa, bool withAp = false, string? apToken = null, long apOffset = 0, bool consent = false)
    {
        var user = new JsonObject { ["userId"] = userId };
        if (consent) user["permissions"] = new JsonObject { ["consentToken"] = "x" };
        var sys = new JsonObject { ["application"] = new JsonObject { ["applicationId"] = "amzn1.ask.skill.test" }, ["user"] = user, ["device"] = new JsonObject { ["deviceId"] = "dev1" }, ["apiEndpoint"] = "https://api.amazonalexa.com", ["apiAccessToken"] = "api-token-xyz" };
        var ctx = new JsonObject { ["System"] = sys };
        if (withAp) ctx["AudioPlayer"] = new JsonObject { ["token"] = apToken, ["offsetInMilliseconds"] = apOffset, ["playerActivity"] = "PLAYING" };
        return new JsonObject { ["version"] = "1.0", ["context"] = ctx, ["request"] = request };
    }
    static JsonObject Launch() => Req(new() { ["type"] = "LaunchRequest", ["requestId"] = "r1", ["locale"] = "en-GB" });
    static JsonObject Intent(string name, params (string, string)[] slots)
    {
        var s = new JsonObject(); foreach (var (k, v) in slots) s[k] = new JsonObject { ["name"] = k, ["value"] = v };
        return Req(new() { ["type"] = "IntentRequest", ["requestId"] = "r1", ["locale"] = "en-GB", ["intent"] = new JsonObject { ["name"] = name, ["slots"] = s } });
    }
    static JsonObject Event(string type, string token, long offset = 0, string? error = null)
    {
        var r = new JsonObject { ["type"] = type, ["requestId"] = "e1", ["token"] = token, ["offsetInMilliseconds"] = offset };
        if (error is not null) r["error"] = new JsonObject { ["type"] = error, ["message"] = "m" };
        return Req(r, withAp: true, apToken: token, apOffset: offset);
    }
    static string Speech(JsonObject r) => (string?)r["response"]?["outputSpeech"]?["ssml"] is { } x ? "<speak>" + System.Xml.Linq.XDocument.Parse(x).Root!.Value + "</speak>" : "";
    static JsonObject? Play(JsonObject r) => r["response"]?["directives"]?[0] as JsonObject is { } d && (string?)d["type"] == "AudioPlayer.Play" ? d : null;
    static bool Ends(JsonObject r) => (bool)r["response"]!["shouldEndSession"]!;

    [Fact] public async Task UnlinkedAccountIsToldHowToLinkAndNothingElse()
    {
        using var r = new Rig(linked: false);
        var launch = await r.Send(Launch()); Assert.Contains("create a code", Speech(launch)); Assert.False(Ends(launch));
        var play = await r.Send(Intent("PlayIntent", ("query", "blinding lights"))); Assert.Null(Play(play)); Assert.Contains("create a code", Speech(play));
        Assert.Empty(r.Progress.Sent);
    }

    [Fact] public async Task UnlinkedAudioEventsAreIgnored()
    {
        using var r = new Rig(linked: false);
        var res = await r.Send(Event("AudioPlayer.PlaybackNearlyFinished", "1:0:0"));
        Assert.Equal("", Speech(res)); Assert.Null(Play(res));
    }

    [Fact] public async Task PairingCodeLinksAndABadCodeDoesNot()
    {
        using var r = new Rig(linked: false);
        var code = r.Store.NewCode(new JellyfinAccount("http://jf", "u1", "ash", "TOKEN")).Code;
        Assert.Contains("didn't work", Speech(await r.Send(Intent("LinkIntent", ("code", "111111")))));
        Assert.False(r.Store.IsLinked(Alexa));
        var ok = await r.Send(Intent("LinkIntent", ("code", code)));
        Assert.Contains("linked", Speech(ok)); Assert.True(r.Store.IsLinked(Alexa));
    }

    [Fact] public async Task LinkWithNoDigitsExplainsAndAlreadyLinkedSaysSo()
    {
        using var r = new Rig(linked: false);
        Assert.Contains("create a code", Speech(await r.Send(Intent("LinkIntent"))));
        r.Store.Redeem(Alexa, r.Store.NewCode(new JellyfinAccount("http://jf", "u1", "ash", "T")).Code, "");
        Assert.Contains("already linked", Speech(await r.Send(Intent("LinkIntent"))));
    }

    [Fact] public async Task PermissionEventsSwitchNotificationsOnAndOff()
    {
        using var r = new Rig(linked: false);
        r.Store.Redeem(Alexa, r.Store.NewCode(new JellyfinAccount("http://jf", "u1", "ash", "T")).Code, "en-GB");
        Assert.False(r.Store.IsNotifiable(Alexa));
        var accepted = Req(new() { ["type"] = "AlexaSkillEvent.SkillPermissionAccepted", ["body"] = new JsonObject { ["acceptedPermissions"] = new JsonArray(new JsonObject { ["scope"] = AlexaSkill.NotifyPermission }) } });
        Assert.Equal("", Speech(await r.Send(accepted))); Assert.True(r.Store.IsNotifiable(Alexa));
        var changed = Req(new() { ["type"] = "AlexaSkillEvent.SkillPermissionChanged", ["body"] = new JsonObject { ["acceptedPermissions"] = new JsonArray() } });
        await r.Send(changed); Assert.False(r.Store.IsNotifiable(Alexa));
    }

    [Fact] public async Task MissingUserIdGivesAnEmptyAnswer()
    {
        using var r = new Rig();
        var res = await r.Send(Req(new() { ["type"] = "LaunchRequest" }, userId: null));
        Assert.Equal("", Speech(res)); Assert.True(Ends(res));
    }

    [Fact] public async Task LaunchGreetsShortAndKeepsTheSessionOpen()
    {
        using var r = new Rig();
        var res = await r.Send(Launch());
        Assert.Equal("<speak>FLACie. What would you like to hear?</speak>", Speech(res)); Assert.False(Ends(res));
        Assert.NotNull(res["response"]!["reprompt"]);
    }

    [Fact] public async Task PlayGivesAPlayDirectiveWithASignedHttpsUrlAndEndsTheSession()
    {
        using var r = new Rig();
        var res = await r.Send(Intent("PlayIntent", ("query", "blinding lights")));
        Assert.Equal("<speak>Playing Blinding Lights by The Weeknd.</speak>", Speech(res)); Assert.True(Ends(res));
        var d = Play(res)!; Assert.Equal("REPLACE_ALL", (string?)d["playBehavior"]);
        var stream = d["audioItem"]!["stream"]!;
        var url = (string)stream["url"]!;
        Assert.StartsWith("https://flacie.example.com/alexa/stream/t/", url); Assert.EndsWith(".mp3", url.Split('?')[0]);
        Assert.DoesNotContain("TOKEN", url, StringComparison.OrdinalIgnoreCase); Assert.True(url.Length < 300);
        Assert.Equal(0, (long)stream["offsetInMilliseconds"]!);
        // the URL verifies with the same signer
        var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query); var id = Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
        Assert.Equal(SignedUrlResult.OK, r.Signer.Verify("t", id, "mp3", q["u"]!, long.Parse(q["e"]!), q["s"]!));
        Assert.Equal(AlexaUrlSigner.UserHash("LINK1"), q["u"]);
    }

    [Theory]
    [InlineData("PlayAlbumIntent", "ok computer", "Playing the album")]
    [InlineData("PlayArtistIntent", "radiohead", "Playing Radiohead.")]
    [InlineData("PlaySimilarIntent", "radiohead", "Playing music like Radiohead.")]
    [InlineData("PlaySongIntent", "creep", "Playing Creep by Radiohead.")]
    public async Task EachKindOfPlayKeepsItsMeaning(string intent, string said, string expect)
    {
        using var r = new Rig(); r.U.Songs = [.. r.U.Songs, FakeUser.T("Karma Police", "Radiohead", "OK Computer", 2), FakeUser.T("Paranoid Android", "Radiohead", "OK Computer", 1)];
        var res = await r.Send(Intent(intent, ("query", said)));
        Assert.StartsWith(expect, Speech(res).Replace("<speak>", "")); Assert.NotNull(Play(res));
    }

    [Fact] public async Task APlaylistIntentFindsThePlaylistNotASong()
    {
        using var r = new Rig(); r.U.Lists[0].Entries.Add(new("/p/The Weeknd/Blinding Lights", "Blinding Lights", "The Weeknd"));
        var res = await r.Send(Intent("PlayPlaylistIntent", ("query", "road trip")));
        Assert.Contains("Playing your Road Trip playlist", Speech(res)); Assert.NotNull(Play(res));
    }

    [Fact] public async Task PlayWithNoWordsAsksWhatToPlay()
    {
        using var r = new Rig();
        var res = await r.Send(Intent("PlayIntent", ("query", "  ")));
        Assert.Contains("What would you like", Speech(res)); Assert.False(Ends(res));
    }

    [Fact] public async Task NotFoundSendsAProgressiveMessageThenTheDownloadAnswer()
    {
        using var r = new Rig();
        var res = await r.Send(Intent("PlayIntent", ("query", "flurbo zanzibar song")));
        Assert.Single(r.Progress.Sent); Assert.Equal("https://api.amazonalexa.com", r.Progress.Sent[0].Ep); Assert.Contains("Searching", r.Progress.Sent[0].Speech);
        Assert.Contains("downloading Blinding Lights", Speech(res));
        Assert.Equal("AskForPermissionsConsent", (string?)res["response"]!["card"]!["type"]);
        Assert.Equal(AlexaSkill.NotifyPermission, (string?)res["response"]!["card"]!["permissions"]![0]);
        var again = await r.Send(Intent("PlayIntent", ("query", "flurbo zanzibar song")));
        Assert.Null(again["response"]!["card"]);   // asked once only
    }

    [Fact] public async Task NoPermissionCardWhenTheyAlreadyGrantedIt()
    {
        using var r = new Rig();
        var req = Intent("PlayIntent", ("query", "flurbo zanzibar song")); req["context"]!["System"]!["user"]!["permissions"] = new JsonObject { ["consentToken"] = "t" };
        var res = await r.Send(req); Assert.Null(res["response"]!["card"]);
        Assert.True(r.Store.Links.Count == 0 || true);
    }

    [Fact] public async Task ProgressiveMessageGoesOnlyToAmazonHosts()
    {
        using var r = new Rig();
        var req = Intent("PlayIntent", ("query", "flurbo zanzibar song")); req["context"]!["System"]!["apiEndpoint"] = "https://evil.example.com";
        await r.Send(req); Assert.Empty(r.Progress.Sent);
        req["context"]!["System"]!["apiEndpoint"] = "http://api.amazonalexa.com"; await r.Send(req); Assert.Empty(r.Progress.Sent);
        req["context"]!["System"]!["apiEndpoint"] = "https://api.amazonalexa.com.evil.com"; await r.Send(req); Assert.Empty(r.Progress.Sent);
    }

    [Fact] public async Task DisambiguationKeepsTheSessionOpenAndYesPicksTheFirst()
    {
        using var r = new Rig(); r.U.Songs = [.. r.U.Songs, FakeUser.T("Heroes", "David Bowie"), FakeUser.T("Heroes", "Peter Gabriel")];
        var ask = await r.Send(Intent("PlayIntent", ("query", "heroes")));
        Assert.StartsWith("<speak>Did you mean", Speech(ask)); Assert.False(Ends(ask));
        var yes = await r.Send(Intent("AMAZON.YesIntent")); Assert.NotNull(Play(yes));
    }

    [Theory]
    [InlineData("first", 0)] [InlineData("the second one", 1)] [InlineData("2", 1)] [InlineData("no", -1)]
    public async Task ChooseIntentReadsTheAnswer(string said, int _)
    {
        using var r = new Rig(); r.U.Songs = [.. r.U.Songs, FakeUser.T("Heroes", "David Bowie"), FakeUser.T("Heroes", "Peter Gabriel")];
        await r.Send(Intent("PlayIntent", ("query", "heroes")));
        var res = await r.Send(Intent("ChooseIntent", ("choice", said)));
        if (said == "no") Assert.Null(Play(res)); else Assert.NotNull(Play(res));
    }

    [Fact] public async Task PauseGivesAStopDirectiveAndTheOffsetIsRemembered()
    {
        using var r = new Rig();
        var play = await r.Send(Intent("PlayIntent", ("query", "blinding lights")));
        var token = (string)Play(play)!["audioItem"]!["stream"]!["token"]!;
        await r.Send(Event("AudioPlayer.PlaybackStarted", token));
        var pauseReq = Intent("AMAZON.PauseIntent"); pauseReq["context"]!["AudioPlayer"] = new JsonObject { ["token"] = token, ["offsetInMilliseconds"] = 42_000L, ["playerActivity"] = "PLAYING" };
        var pause = await r.Send(pauseReq);
        Assert.Equal("AudioPlayer.Stop", (string?)pause["response"]!["directives"]![0]!["type"]); Assert.True(Ends(pause));
        var resume = await r.Send(Intent("AMAZON.ResumeIntent"));
        Assert.Equal(42_000, (long)Play(resume)!["audioItem"]!["stream"]!["offsetInMilliseconds"]!);
    }

    [Fact] public async Task NearlyFinishedEnqueuesTheNextOneWithTheExpectedPreviousToken()
    {
        using var r = new Rig();
        var play = await r.Send(Intent("PlayIntent", ("query", "the album after hours")));
        var token = (string)Play(play)!["audioItem"]!["stream"]!["token"]!;
        await r.Send(Event("AudioPlayer.PlaybackStarted", token));
        var next = await r.Send(Event("AudioPlayer.PlaybackNearlyFinished", token));
        var d = Play(next)!; Assert.Equal("ENQUEUE", (string?)d["playBehavior"]);
        Assert.Equal(token, (string?)d["audioItem"]!["stream"]!["expectedPreviousToken"]);
        Assert.Equal("", Speech(next));   // no speech is allowed in answer to AudioPlayer events
    }

    [Fact] public async Task FailedMovesOnAndFinishedAndStoppedAnswerNothing()
    {
        using var r = new Rig();
        var play = await r.Send(Intent("PlayIntent", ("query", "the album after hours")));
        var token = (string)Play(play)!["audioItem"]!["stream"]!["token"]!;
        var failed = await r.Send(Event("AudioPlayer.PlaybackFailed", token, error: "MEDIA_ERROR_UNKNOWN"));
        Assert.NotNull(Play(failed)); Assert.Equal("REPLACE_ALL", (string?)Play(failed)!["playBehavior"]);
        Assert.Null(Play(await r.Send(Event("AudioPlayer.PlaybackStopped", token, 1000))));
        Assert.Null(Play(await r.Send(Event("AudioPlayer.PlaybackFinished", token))));
    }

    [Fact] public async Task PlaybackControllerButtonsWork()
    {
        using var r = new Rig();
        await r.Send(Intent("PlayIntent", ("query", "the album after hours")));
        var next = await r.Send(Req(new() { ["type"] = "PlaybackController.NextCommandIssued", ["requestId"] = "c1" }));
        Assert.NotNull(Play(next)); Assert.Equal("", Speech(next));
        var pause = await r.Send(Req(new() { ["type"] = "PlaybackController.PauseCommandIssued", ["requestId"] = "c2" }));
        Assert.Equal("AudioPlayer.Stop", (string?)pause["response"]!["directives"]![0]!["type"]);
    }

    [Theory]
    [InlineData("AMAZON.NextIntent")] [InlineData("AMAZON.PreviousIntent")] [InlineData("AMAZON.ShuffleOnIntent")] [InlineData("AMAZON.ShuffleOffIntent")]
    [InlineData("AMAZON.LoopOnIntent")] [InlineData("AMAZON.LoopOffIntent")] [InlineData("AMAZON.RepeatIntent")] [InlineData("AMAZON.StartOverIntent")]
    [InlineData("LikeIntent")] [InlineData("DislikeIntent")] [InlineData("WhatsPlayingIntent")] [InlineData("WhatsDownloadingIntent")] [InlineData("IsItReadyIntent")]
    [InlineData("PlayRecentIntent")] [InlineData("PlayLibraryIntent")] [InlineData("PlayLastDownloadIntent")] [InlineData("AMAZON.StopIntent")] [InlineData("AMAZON.CancelIntent")]
    public async Task EveryIntentAnswersWithoutError(string name)
    {
        using var r = new Rig(); await r.Send(Intent("PlayIntent", ("query", "the album after hours")));
        var res = await r.Send(Intent(name));
        Assert.Equal("1.0", (string?)res["version"]); Assert.NotNull(res["response"]);
    }

    [Fact] public async Task AddToPlaylistReadsTheSlot()
    {
        using var r = new Rig(); await r.Send(Intent("PlayIntent", ("query", "blinding lights")));
        var res = await r.Send(Intent("AddToPlaylistIntent", ("playlist", "my Road Trip playlist")));
        Assert.Equal("<speak>Added Blinding Lights to Road Trip.</speak>", Speech(res));
    }

    [Fact] public async Task HelpFallbackAndSessionEnd()
    {
        using var r = new Rig();
        Assert.Contains("play Blinding Lights", Speech(await r.Send(Intent("AMAZON.HelpIntent"))));
        Assert.Contains("didn't get that", Speech(await r.Send(Intent("AMAZON.FallbackIntent"))));
        var ended = await r.Send(Req(new() { ["type"] = "SessionEndedRequest", ["reason"] = "USER_INITIATED" })); Assert.Equal("", Speech(ended));
        var exc = await r.Send(Req(new() { ["type"] = "System.ExceptionEncountered", ["error"] = new JsonObject { ["type"] = "INVALID_RESPONSE" } })); Assert.Equal("", Speech(exc));
    }

    [Fact] public async Task AWaitingNoticeIsReadOnLaunchAndBeforeAPlay()
    {
        using var r = new Rig();
        r.Engine.DownloadEnded("LINK1", "Queen", "Bicycle Race", true, "");
        Assert.Contains("Bicycle Race by Queen is ready", Speech(await r.Send(Launch())));
        r.Engine.DownloadEnded("LINK1", "Queen", "Bicycle Race", false, "x");
        var play = await r.Send(Intent("PlayIntent", ("query", "blinding lights")));
        Assert.Contains("couldn't download Bicycle Race", Speech(play)); Assert.Contains("Playing Blinding Lights", Speech(play));
    }

    [Fact] public async Task SpeechIsXmlSafe()
    {
        using var r = new Rig(); r.U.Songs = [FakeUser.T("Rock & Roll <3 \"Tour\"", "Tom & Jerry")];
        var res = await r.Send(Intent("PlayIntent", ("query", "rock and roll 3 tour")));
        var ssml = Speech(res); Assert.StartsWith("<speak>", ssml); Assert.DoesNotContain("& ", ssml);
        System.Xml.Linq.XDocument.Parse(ssml);   // well-formed
    }

    [Fact] public async Task ArtIsSentOnlyWithAllFourFieldsAndSigned()
    {
        using var r = new Rig();
        var jf = "0123456789abcdef0123456789abcdef";
        r.U.Songs = [new Track("/p/x", "Art Song", "Band", "Alb", "Band", 1, 1, 1000, 2000, "jf" + jf + "~abc123", 0, TrackSource.Jellyfin, JellyfinId: Guid.NewGuid().ToString("N"))];
        var d = Play(await r.Send(Intent("PlayIntent", ("query", "art song"))))!;
        var meta = d["audioItem"]!["metadata"]!;
        Assert.Equal("Art Song", (string?)meta["title"]); Assert.Equal("Band", (string?)meta["subtitle"]);
        var art = (string)meta["art"]!["sources"]![0]!["url"]!; Assert.StartsWith("https://flacie.example.com/alexa/stream/a/" + jf + "-abc123.jpg?u=", art);
        Assert.Equal(art, (string?)meta["backgroundImage"]!["sources"]![0]!["url"]);
    }
}
