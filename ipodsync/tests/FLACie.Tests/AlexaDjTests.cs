using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FLACie.Tests;

/// <summary>The skill with the DJ's brain: free-form words go through the same request reader as "talk to the DJ", and Alexa's plays reach the listening log.</summary>
public class AlexaDjTests
{
    const string Alexa = "amzn1.ask.account.TESTUSER";
    sealed class Dir(IAlexaUser user) : IAlexaDirectory { public (string, IAlexaUser)? Resolve(string id) => id == Alexa ? ("LINK1", user) : null; }

    /// <summary>The same person, as the DJ's reader sees them.</summary>
    sealed class AskUser(FakeUser u) : IDjAskUser
    {
        public string Account => "ash";
        public bool Ready => true;
        public IReadOnlyList<Track> Songs => u.Songs;
        public IReadOnlyList<Playlist> Playlists => [];
        public Track? Find(string path) => u.Find(path);
        public Track? FindSong(string artist, string title) => u.FindSong(artist, title);
        public IReadOnlyList<Track> Favourites => [];
        public IReadOnlyList<Track> MostPlayed(int take) => [];
        public bool Heard(Track t) => false;
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => [];
        public Vibe? VibeOf(Track t) => null;
        public Task RateAsync(Track t, int stars) => Task.CompletedTask;
        public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) => Task.FromResult<AlexaRequested?>(null);
        public List<ListenEvent> Events = [];
        public IReadOnlyList<ListenEvent> Listens => Events;
        public List<PhraseAsk> Asked = [];
        public IReadOnlyList<PhraseAsk> Phrases => Asked;
        public void NotePhrase(PhraseAsk p) => Asked.Add(p);
    }

    sealed class Model(string reply) : IDjAskModel
    {
        public List<string> Prompts = [];
        public Task<string?> AskAsync(string system, string user, CancellationToken ct) { Prompts.Add(user); return Task.FromResult<string?>(reply); }
    }

    sealed class Rig : IDisposable
    {
        public string Path = Directory.CreateTempSubdirectory().FullName;
        public FakeUser U = new(); public AskUser A; public AlexaQueues Queues; public AlexaEngine Engine; public AlexaSkill Skill; public Model M;
        public Rig(string modelReply = "{\"action\":\"unknown\"}")
        {
            U.Songs = [FakeUser.T("Blinding Lights", "The Weeknd", "After Hours", 1), FakeUser.T("Creep", "Radiohead", "Pablo Honey", 1), FakeUser.T("U Got 2 Know", "Cappella", "U Got 2 Know", 1)];
            A = new(U); M = new(modelReply);
            var store = new AlexaStore(new DataPaths(Path), DataProtectionProvider.Create(new DirectoryInfo(System.IO.Path.Combine(Path, "keys"))), new ConfigurationBuilder().Build());
            Queues = new(new DataPaths(Path)); Engine = new(Queues, () => new AlexaConfig { Enabled = true });
            var brain = new AlexaAskBrain(new DjAskEngine(M), u => ReferenceEquals(u, U) ? A : null, (_, e) => A.Events.Add(e));
            Skill = new(Engine, store, new Dir(U), new AlexaUrlSigner(new byte[32], () => DateTime.UtcNow), () => "https://flacie.example.com", brain: brain);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (Exception) { } }
        public Task<JsonObject> Send(JsonObject req) => Skill.HandleAsync(req);
    }

    static JsonObject Req(JsonObject request) => new()
    {
        ["version"] = "1.0", ["request"] = request,
        ["context"] = new JsonObject { ["System"] = new JsonObject { ["user"] = new JsonObject { ["userId"] = Alexa }, ["device"] = new JsonObject { ["deviceId"] = "dev1" } } },
    };
    static JsonObject Intent(string name, params (string, string)[] slots)
    {
        var s = new JsonObject(); foreach (var (k, v) in slots) s[k] = new JsonObject { ["name"] = k, ["value"] = v };
        return Req(new() { ["type"] = "IntentRequest", ["requestId"] = "r1", ["locale"] = "en-GB", ["intent"] = new JsonObject { ["name"] = name, ["slots"] = s } });
    }
    static JsonObject Event(string type, string token, long offset = 0) => Req(new() { ["type"] = type, ["requestId"] = "e1", ["token"] = token, ["offsetInMilliseconds"] = offset });
    static string Ssml(JsonObject r) => (string?)r["response"]?["outputSpeech"]?["ssml"] ?? "";
    /// <summary>The song the reply started: a Play directive was sent, and the queue's current song.</summary>
    static string? PlayedTitle(Rig r, JsonObject res) => (string?)res["response"]?["directives"]?[0]?["type"] == "AudioPlayer.Play" && r.Queues.For("LINK1").Current is { } p ? r.U.Find(p)?.Title : null;

    [Fact]
    public async Task A_question_gets_a_grounded_answer_in_Alexas_own_voice()
    {
        using var r = new Rig("{\"answer\":\"I picked Creep because FLACie had you on Radiohead.\"}");
        await r.Engine.PlayAsync(r.U, "LINK1", VoiceParser.FromRaw("play creep"));
        var res = await r.Send(Intent("AskWhyIntent", ("text", "did you pick this")));
        var ssml = Ssml(res);
        Assert.Contains("I picked Creep", ssml);
        Assert.Contains("<sub alias=\"flacky\">FLACie</sub>", ssml);   // Alexa's speech, with its pronunciation
        Assert.Contains("Creep", Assert.Single(r.M.Prompts));          // the facts named the song playing
        Assert.Contains("why did you pick this", r.M.Prompts[0]);
        Assert.Null(res["response"]?["directives"]);
    }

    // the DJ asks when it isn't sure (owner, 27 Oct): Alexa keeps listening for the reply, and repeats the question if nothing is said
    [Fact]
    public async Task A_question_back_keeps_Alexa_listening()
    {
        using var r = new Rig("{\"ask\":\"Do you mean your hearted songs, or the ones you play most?\"}");
        var res = await r.Send(Intent("AskWhatIntent", ("text", "play my stuff")));
        Assert.Contains("hearted songs", Ssml(res));
        Assert.False((bool?)res["response"]?["shouldEndSession"]);
        Assert.Contains("hearted songs", (string?)res["response"]?["reprompt"]?["outputSpeech"]?["ssml"] ?? "");
    }

    [Fact]
    public async Task Who_sings_this_is_answered_by_the_rules_from_the_song_playing()
    {
        using var r = new Rig();
        await r.Engine.PlayAsync(r.U, "LINK1", VoiceParser.FromRaw("play creep"));
        Assert.Contains("Radiohead", Ssml(await r.Send(Intent("AskWhoIntent", ("text", "sings this")))));
    }

    [Fact]
    public async Task Play_car_songs_through_Alexa_plays_the_car_profile()
    {
        using var r = new Rig();
        var cappella = r.U.Songs[2];
        r.A.Events.Add(new("c1", ListenKind.Play, DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds(), cappella.Path, cappella.Title, cappella.Artist, 200, 200, null, "car", 8));
        // "ask flaky to play car songs" reaches the skill as the bare words
        var res = await r.Send(Intent("NameIntent", ("name", "car songs")));
        Assert.Equal("U Got 2 Know", PlayedTitle(r, res));
        Assert.Single(r.A.Asked);   // learned as a phrase, as in the app
        var viaPlay = await r.Send(Intent("PlayIntent", ("query", "car songs")));
        Assert.Equal("U Got 2 Know", PlayedTitle(r, viaPlay));
    }

    [Fact]
    public async Task A_song_name_still_goes_to_the_name_matcher()
    {
        using var r = new Rig();
        Assert.Equal("Blinding Lights", PlayedTitle(r, await r.Send(Intent("NameIntent", ("name", "blinding lights")))));
        Assert.Empty(r.M.Prompts);
    }

    [Fact]
    public async Task Finishes_and_skips_land_in_the_account_log_marked_alexa()
    {
        using var r = new Rig();
        var first = await r.Engine.PlayAsync(r.U, "LINK1", VoiceParser.FromRaw("play songs by the weeknd"));
        var tok = first.Play!.Token;
        await r.Send(Event("AudioPlayer.PlaybackStarted", tok));
        await r.Send(Event("AudioPlayer.PlaybackFinished", tok));
        await r.Send(Event("AudioPlayer.PlaybackFinished", tok));   // a resend is not counted twice
        var fin = Assert.Single(r.A.Events);
        Assert.Equal("alexa", fin.Surface); Assert.True(ListenClass.IsFinish(fin)); Assert.Equal("Blinding Lights", fin.Title);

        var again = await r.Engine.PlayAsync(r.U, "LINK1", VoiceParser.FromRaw("play creep"));
        await r.Send(Event("AudioPlayer.PlaybackStarted", again.Play!.Token));
        await r.Send(Event("AudioPlayer.PlaybackStopped", again.Play.Token, 12_000));   // "Alexa, next" stops it first
        await r.Send(Intent("AMAZON.NextIntent"));
        Assert.Equal(2, r.A.Events.Count);
        var skip = r.A.Events[1];
        Assert.Equal("Creep", skip.Title); Assert.Equal("alexa", skip.Surface); Assert.True(ListenClass.IsSkip(skip));
    }

    [Fact]
    public async Task Next_while_playing_logs_the_song_left_as_a_skip()
    {
        using var r = new Rig();
        var first = await r.Engine.PlayAsync(r.U, "LINK1", VoiceParser.FromRaw("play creep"));
        await r.Send(Event("AudioPlayer.PlaybackStarted", first.Play!.Token));
        r.Engine.Position("LINK1", first.Play.Token, 5_000);
        await r.Send(Req(new() { ["type"] = "PlaybackController.NextCommandIssued", ["requestId"] = "p1" }));
        var skip = Assert.Single(r.A.Events);
        Assert.True(ListenClass.IsSkip(skip)); Assert.Equal(5, skip.Heard);
    }
}
