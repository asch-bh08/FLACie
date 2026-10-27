using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>"Talk to the DJ": the quick rules, the text model behind them, and the answers carried out on a fake player.</summary>
public class DjAskTests
{
    sealed class AskUser : IDjAskUser
    {
        public string Account { get; set; } = "ash";
        public bool Ready { get; set; } = true;
        public List<Track> Library = [];
        public List<Playlist> Lists = [];
        public IReadOnlyList<Track> Songs => Library;
        public IReadOnlyList<Playlist> Playlists => Lists;
        public List<Track> Favs = [];
        public List<Track> Top = [];
        public HashSet<string> HeardPaths = [];
        public List<Track> Picks = [];
        public Dictionary<string, Vibe> Vibes = [];
        public List<(string, int)> Ratings = [];
        public List<string> Requests = [];
        public AlexaRequested? NextRequest = new("Akon", "Smack That");
        public Track? Find(string path) => Library.FirstOrDefault(t => t.Path == path);
        public Track? FindSong(string artist, string title) => Library.FirstOrDefault(s => Matching.MatchKey(s) == Matching.MatchKey(title, artist));
        public IReadOnlyList<Track> Favourites => Favs;
        public IReadOnlyList<Track> MostPlayed(int take) => Top.Take(take).ToList();
        public bool Heard(Track t) => HeardPaths.Contains(t.Path);
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => Picks.Take(n).ToList();
        public Vibe? VibeOf(Track t) => Vibes.GetValueOrDefault(t.Path);
        public Task RateAsync(Track t, int stars) { Ratings.Add((t.Title, stars)); return Task.CompletedTask; }
        public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) { Requests.Add(words); return Task.FromResult(NextRequest); }
    }

    sealed class FakeModel(Func<string, CancellationToken, Task<string?>> answer) : IDjAskModel
    {
        public int Calls;
        public Task<string?> AskAsync(string system, string user, CancellationToken ct) { Calls++; return answer(user, ct); }
    }

    sealed class FakePlayer : IDjAskPlayer
    {
        public List<string> Log = [];
        public IReadOnlyList<string> Queue = [];
        public double Volume = 0.5; public bool? Dj;
        public Task PlayAsync(IReadOnlyList<string> paths, string title, bool shuffle) { Queue = paths; Log.Add($"play {title}"); return Task.CompletedTask; }
        public Task UpNextAsync(IReadOnlyList<string> paths, bool replace) { Log.Add($"upnext {paths.Count} {replace}"); return Task.CompletedTask; }
        public Task NextAsync() { Log.Add("next"); return Task.CompletedTask; }
        public Task PauseAsync() { Log.Add("pause"); return Task.CompletedTask; }
        public Task ResumeAsync() { Log.Add("resume"); return Task.CompletedTask; }
        public Task NudgeVolumeAsync(double delta) { Volume = Math.Clamp(Volume + delta, 0, 1); return Task.CompletedTask; }
        public Task SetDjAsync(bool on) { Dj = on; return Task.CompletedTask; }
    }

    static Track T(string title, string artist, string genre = "", string album = "A") =>
        new($"/p/{artist}/{title}", title, artist, album, artist, 1, 1, 200000, 2006, null, 1, TrackSource.Jellyfin, JellyfinId: Guid.NewGuid().ToString("N"), Genre: genre);

    static AskUser Standard()
    {
        var u = new AskUser();
        u.Library.AddRange([T("Smack That", "Akon", "Hip-Hop", "Konvicted"), T("Lonely", "Akon", "Hip-Hop", "Trouble"), T("Creep", "Radiohead", "Alternative", "Pablo Honey"),
            T("Karma Police", "Radiohead", "Alternative", "OK Computer"), T("Weightless", "Marconi Union", "Ambient"), T("Teardrop", "Massive Attack", "Trip Hop"),
            T("One More Time", "Daft Punk", "House"), T("Levels", "Avicii", "EDM"), T("So What", "Miles Davis", "Jazz")]);
        u.Lists.Add(new("pl-pop", "Pop", 1, null, [new(u.Library[0].Path, "Smack That", "Akon"), new(u.Library[6].Path, "One More Time", "Daft Punk")]));
        u.Lists.Add(new("pl-run", "Running", 1, null, [new(u.Library[7].Path, "Levels", "Avicii")]));
        return u;
    }

    // ---- the quick rules ----

    [Theory]
    [InlineData("play Smack That", AskIntent.Play, "smack that")]
    [InlineData("Play Smack That by Akon", AskIntent.Play, "smack that by akon")]
    [InlineData("hey DJ, play Radiohead please", AskIntent.Play, "radiohead")]
    [InlineData("put on OK Computer", AskIntent.Play, "ok computer")]
    [InlineData("play my pop playlist", AskIntent.Playlist, "pop")]
    [InlineData("play the running playlist", AskIntent.Playlist, "running")]
    [InlineData("play playlist called road trip", AskIntent.Playlist, "road trip")]
    [InlineData("play my favourites", AskIntent.Favourites, null)]
    [InlineData("Play my favorites", AskIntent.Favourites, null)]
    [InlineData("play my liked songs", AskIntent.Favourites, null)]
    [InlineData("play my most played", AskIntent.MostPlayed, null)]
    [InlineData("play my frequently played songs", AskIntent.MostPlayed, null)]
    [InlineData("frequently played", AskIntent.MostPlayed, null)]
    [InlineData("play something I haven't heard", AskIntent.Unheard, null)]
    [InlineData("play something I've never heard before", AskIntent.Unheard, null)]
    [InlineData("shuffle my library", AskIntent.ShuffleLibrary, null)]
    [InlineData("shuffle everything", AskIntent.ShuffleLibrary, null)]
    [InlineData("next", AskIntent.Next, null)]
    [InlineData("Skip.", AskIntent.Next, null)]
    [InlineData("skip this song", AskIntent.Next, null)]
    [InlineData("next song please", AskIntent.Next, null)]
    [InlineData("pause", AskIntent.Pause, null)]
    [InlineData("stop the music", AskIntent.Pause, null)]
    [InlineData("resume", AskIntent.Resume, null)]
    [InlineData("play", AskIntent.Resume, null)]
    [InlineData("carry on", AskIntent.Resume, null)]
    [InlineData("turn it up", AskIntent.VolumeUp, null)]
    [InlineData("louder", AskIntent.VolumeUp, null)]
    [InlineData("turn the volume up", AskIntent.VolumeUp, null)]
    [InlineData("turn it down", AskIntent.VolumeDown, null)]
    [InlineData("quieter please", AskIntent.VolumeDown, null)]
    [InlineData("what's this song?", AskIntent.WhatsThis, null)]
    [InlineData("What’s playing", AskIntent.WhatsThis, null)]
    [InlineData("what song is this", AskIntent.WhatsThis, null)]
    [InlineData("who sings this?", AskIntent.WhoSings, null)]
    [InlineData("who is this", AskIntent.WhoSings, null)]
    [InlineData("more like this", AskIntent.MoreLike, null)]
    [InlineData("play more like this", AskIntent.MoreLike, null)]
    [InlineData("less like this", AskIntent.LessLike, null)]
    [InlineData("play something different", AskIntent.LessLike, null)]
    [InlineData("thumbs down", AskIntent.ThumbsDown, null)]
    [InlineData("I don't like this", AskIntent.ThumbsDown, null)]
    [InlineData("DJ on", AskIntent.DjOn, null)]
    [InlineData("turn on the DJ", AskIntent.DjOn, null)]
    [InlineData("DJ off", AskIntent.DjOff, null)]
    [InlineData("turn the DJ off", AskIntent.DjOff, null)]
    [InlineData("stop talking", AskIntent.DjOff, null)]
    [InlineData("play something chill", AskIntent.Mood, "chill")]
    [InlineData("play some jazz", AskIntent.Mood, "jazz")]
    [InlineData("something upbeat for the gym", AskIntent.Mood, "upbeat for the gym")]
    [InlineData("play jazz", AskIntent.Mood, "jazz")]
    [InlineData("play upbeat music", AskIntent.Mood, "upbeat music")]
    public void TheRulesKnowEachPhrase(string said, AskIntent intent, string? what)
    {
        var p = DjAskRules.Parse(said);
        Assert.NotNull(p);
        Assert.Equal(intent, p!.Intent);
        Assert.Equal(what, p.What);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("what's the weather like")]
    [InlineData("skipping rope")]
    [InlineData("next week")]
    [InlineData("pause for thought")]
    [InlineData("who won the football")]
    [InlineData("play something")]
    [InlineData("play something good")]
    [InlineData("a playlist")]
    [InlineData("turn it upside down")]
    public void NearMissesAreLeftToTheModel(string said) => Assert.Null(DjAskRules.Parse(said));

    [Fact] public void ThumbsDownIsNotAPlay() => Assert.Equal(AskIntent.ThumbsDown, DjAskRules.Parse("dislike this")!.Intent);
    [Fact] public void ASongCalledPlaylistIsStillAPlaylistAsk() => Assert.Equal(AskIntent.Play, DjAskRules.Parse("play next to me")!.Intent);

    // ---- the text model behind the rules ----

    [Fact] public async Task AValidModelAnswerIsUsed()
    {
        var u = Standard();
        var model = new FakeModel((_, _) => Task.FromResult<string?>("Sure! {\"action\": \"playlist\", \"query\": \"pop\"}"));
        var a = await new DjAskEngine(model).AnswerAsync(u, "I fancy that pop list of mine", null, []);
        Assert.Equal(1, model.Calls);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.Equal("Here's your Pop playlist.", a.Say);
        Assert.Equal(2, a.Paths!.Count);
    }

    [Fact] public async Task TheModelIsNotAskedWhenARuleFits()
    {
        var model = new FakeModel((_, _) => Task.FromResult<string?>("{\"action\":\"pause\"}"));
        var a = await new DjAskEngine(model).AnswerAsync(Standard(), "next", null, []);
        Assert.Equal(0, model.Calls); Assert.Equal(DjAskDo.Next, a.Do);
    }

    [Fact] public async Task AMoodFromTheModelGoesToTheEnginesPicks()
    {
        var u = Standard();
        u.Picks = [u.Library[7], u.Library[0], u.Library[4]];   // Levels (EDM), Smack That (hip-hop), Weightless (ambient)
        u.Vibes[u.Library[7].Path] = new(0.8, 0.8, 128, 0.9); u.Vibes[u.Library[4].Path] = new(0.1, 0.2, 0, 0.1);
        var model = new FakeModel((_, _) => Task.FromResult<string?>("{\"action\":\"mood\",\"query\":\"upbeat gym\"}"));
        var a = await new DjAskEngine(model).AnswerAsync(u, "get me pumped for my run", null, []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.Equal(u.Library[7].Path, a.Paths![0]);   // the engine's pick that fits comes first
        Assert.DoesNotContain(u.Library[4].Path, a.Paths);   // a quiet ambient song never
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"action\": \"launch_rocket\"}")]
    [InlineData("{\"action\": \"play\"}")]
    [InlineData("{\"action\": \"play\", \"query\": \"\"}")]
    [InlineData("{\"action\": 5}")]
    [InlineData("{\"action\": \"unknown\"}")]
    [InlineData("")]
    public async Task AnInvalidModelAnswerIsSorry(string answer)
    {
        var model = new FakeModel((_, _) => Task.FromResult<string?>(answer));
        var a = await new DjAskEngine(model).AnswerAsync(Standard(), "flibble the wobble", null, []);
        Assert.Equal(DjAskDo.None, a.Do);
        Assert.Equal(answer.Contains("unknown") ? DjAskDo.None : DjAskDo.None, a.Do);
        if (!answer.Contains("unknown")) Assert.Equal(DjAskEngine.DidntCatch, a.Say);
    }

    [Fact] public async Task ASlowModelIsSorryInTime()
    {
        var model = new FakeModel(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return "{\"action\":\"next\"}"; });
        var e = new DjAskEngine(model) { AiTimeout = TimeSpan.FromMilliseconds(100) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var a = await e.AnswerAsync(Standard(), "do the thing with the music", null, []);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(DjAskEngine.DidntCatch, a.Say);
    }

    [Fact] public async Task AModelThatIgnoresCancellationIsStillLeftBehind()
    {
        var never = new TaskCompletionSource<string?>();
        var e = new DjAskEngine(new FakeModel((_, _) => never.Task)) { AiTimeout = TimeSpan.FromMilliseconds(100) };
        Assert.Equal(DjAskEngine.DidntCatch, (await e.AnswerAsync(Standard(), "hmm hmm", null, [])).Say);
    }

    [Fact] public async Task NoModelIsSorry() => Assert.Equal(DjAskEngine.DidntCatch, (await new DjAskEngine().AnswerAsync(Standard(), "the weather", null, [])).Say);

    [Fact] public void TheModelsDefaultTimeoutIsTwoAndAHalfSeconds() => Assert.Equal(TimeSpan.FromSeconds(2.5), DjAskAi.Timeout);

    // ---- answers, not just commands ----

    static AskUser WithProdigy() { var u = Standard(); u.Library.Add(T("Breathe", "The Prodigy", "Electronic", "The Fat of the Land")); return u; }

    [Fact] public async Task AQuestionIsAnsweredFromWhatFlacieHolds()
    {
        var u = WithProdigy(); string? prompt = null;
        var model = new FakeModel((p, _) => { prompt = p; return Task.FromResult<string?>("{\"answer\": \"**The Prodigy** are an electronic act. You've got one of theirs, Breathe.\"}"); });
        var a = await new DjAskEngine(model).AnswerAsync(u, "who are the prodigy?", u.Library[2].Path, []);
        Assert.Equal(DjAskDo.None, a.Do); Assert.Equal("Answer", a.Intent);
        Assert.Equal("The Prodigy are an electronic act. You've got one of theirs, Breathe.", a.Say);
        Assert.Contains("In their library by The Prodigy: 1 songs, albums: The Fat of the Land", prompt);
        Assert.Contains("Playing now: Creep by Radiohead, from the album Pablo Honey, 2006", prompt);
    }

    [Theory]
    [InlineData("{\"answer\": \"See https://example.com for more.\"}")]
    [InlineData("{\"answer\": \"\"}")]
    [InlineData("{\"answer\": \"one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty twenty-one twenty-two twenty-three twenty-four twenty-five twenty-six twenty-seven twenty-eight twenty-nine thirty thirty-one thirty-two thirty-three thirty-four thirty-five thirty-six thirty-seven thirty-eight thirty-nine forty forty-one\"}")]
    public async Task AnUnfitAnswerIsSorry(string reply)
    {
        var a = await new DjAskEngine(new FakeModel((_, _) => Task.FromResult<string?>(reply))).AnswerAsync(Standard(), "what year is this?", null, []);
        Assert.Equal(DjAskEngine.DidntCatch, a.Say);
    }

    [Fact] public void ALongAnswerKeepsWholeSentencesUnderTheCap()
    {
        var s = DjAskAi.CleanAnswer("Short one. " + string.Join(' ', Enumerable.Repeat("word", 39)) + ".");
        Assert.Equal("Short one.", s);
    }

    [Fact] public async Task AQuestionGetsLongerThanACommand()
    {
        var model = new FakeModel(async (_, ct) => { await Task.Delay(400, ct); return "{\"answer\": \"Not sure, to be honest.\"}"; });
        var e = new DjAskEngine(model) { AiTimeout = TimeSpan.FromMilliseconds(100), QuestionTimeout = TimeSpan.FromSeconds(3) };
        Assert.Equal("Not sure, to be honest.", (await e.AnswerAsync(Standard(), "what album is this from?", null, [])).Say);
        Assert.Equal(DjAskEngine.DidntCatch, (await e.AnswerAsync(Standard(), "sort the music thing out", null, [])).Say);
        Assert.Equal(TimeSpan.FromSeconds(4), DjAskAi.QuestionTimeout);
    }

    [Fact] public async Task AFollowUpPlaysMoreFromThem()
    {
        var u = WithProdigy();
        var model = new FakeModel((_, _) => Task.FromResult<string?>("{\"answer\": \"An electronic act from Essex, I believe.\"}"));
        var e = new DjAskEngine(model);
        await e.AnswerAsync(u, "who are the prodigy?", null, []);
        var a = await e.AnswerAsync(u, "play more from them", null, []);
        Assert.Equal(1, model.Calls);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.All(a.Paths!, p => Assert.Contains("The Prodigy", p));
        // and the next question to the model carries the conversation
        string? prompt = null;
        var e2 = new DjAskEngine(new FakeModel((p, _) => { prompt = p; return Task.FromResult<string?>("{\"answer\": \"Not sure.\"}"); }), e.Contexts);
        await e2.AnswerAsync(u, "what else have they done?", null, []);
        Assert.Contains("They said: who are the prodigy?", prompt);
        Assert.Contains("Talking about: The Prodigy", prompt);
    }

    [Fact] public async Task AFollowUpIsKeptPerAccount()
    {
        var e = new DjAskEngine(new FakeModel((_, _) => Task.FromResult<string?>("{\"answer\": \"Not sure.\"}")));
        await e.AnswerAsync(WithProdigy(), "who are the prodigy?", null, []);
        var sam = WithProdigy(); sam.Account = "sam"; sam.NextRequest = null;
        var a = await e.AnswerAsync(sam, "play more from them", null, []);
        Assert.NotEqual(DjAskDo.Play, a.Do);
    }

    [Fact] public async Task LikeThisButSlowerKeepsCalmerPicks()
    {
        var u = Standard(); var cur = u.Library[6];   // One More Time
        u.Picks = [u.Library[7], u.Library[4]];
        u.Vibes[cur.Path] = new(0.7, 0.7, 120, 0.8); u.Vibes[u.Library[7].Path] = new(0.9, 0.9, 128, 0.9); u.Vibes[u.Library[4].Path] = new(0.2, 0.2, 60, 0.1);
        var a = await new DjAskEngine(new FakeModel((_, _) => Task.FromResult<string?>("{\"action\":\"more_like\",\"query\":\"slower\"}"))).AnswerAsync(u, "something like this but slower", cur.Path, []);
        Assert.Equal(DjAskDo.UpNext, a.Do); Assert.Equal([u.Library[4].Path], a.Paths);
    }

    [Fact] public async Task WhyDidYouPickThisUsesTheEnginesReason()
    {
        var u = Standard(); string? prompt = null;
        var e = new DjAskEngine(new FakeModel((p, _) => { prompt = p; return Task.FromResult<string?>("{\"answer\": \"Because you play Akon.\"}"); }));
        e.Contexts.For("ash").Picked(u.Library[1].Path, "Because you play Akon");
        await e.AnswerAsync(u, "why did you pick this?", u.Library[1].Path, []);
        Assert.Contains("Why you picked it: Because you play Akon", prompt);
    }

    // ---- answers ----

    [Fact] public async Task ASongIsPlayedAndNamedInCharacter()
    {
        var a = await new DjAskEngine().AnswerAsync(Standard(), "play Smack That", null, []);
        Assert.Equal(DjAskDo.Play, a.Do); Assert.Equal("Smack That, by Akon.", a.Say);
        Assert.Equal("/p/Akon/Smack That", a.Paths![0]);
    }

    [Fact] public async Task WhatsThisAndWhoSingsNameTheSongPlaying()
    {
        var u = Standard(); var e = new DjAskEngine();
        Assert.Equal("This is Creep, by Radiohead.", (await e.AnswerAsync(u, "what's this song?", u.Library[2].Path, [])).Say);
        Assert.Equal("That's Radiohead.", (await e.AnswerAsync(u, "who sings this?", u.Library[2].Path, [])).Say);
        Assert.Equal("Nothing's playing right now.", (await e.AnswerAsync(u, "who sings this?", null, [])).Say);
    }

    [Fact] public async Task ThumbsDownRatesAndSkips()
    {
        var u = Standard();
        var a = await new DjAskEngine().AnswerAsync(u, "thumbs down", u.Library[2].Path, []);
        Assert.Equal(DjAskDo.Next, a.Do); Assert.Equal([("Creep", 1)], u.Ratings);
    }

    [Fact] public async Task MoreLikeThisQueuesTheEnginesPicks()
    {
        var u = Standard(); u.Picks = [u.Library[3], u.Library[2]];
        var a = await new DjAskEngine().AnswerAsync(u, "more like this", u.Library[2].Path, [u.Library[2].Path]);
        Assert.Equal(DjAskDo.UpNext, a.Do); Assert.True(a.Replace);
        Assert.Equal([u.Library[3].Path], a.Paths);   // never the song playing
    }

    [Fact] public async Task LessLikeThisMovesAwayFromTheArtist()
    {
        var u = Standard();
        var a = await new DjAskEngine().AnswerAsync(u, "less like this", u.Library[2].Path, []);
        Assert.Equal(DjAskDo.UpNext, a.Do);
        Assert.DoesNotContain(a.Paths!, p => p.Contains("Radiohead"));
    }

    [Fact] public async Task AMissingSongIsDownloadedAndWaitedFor()
    {
        var u = Standard(); u.NextRequest = new("Outkast", "Hey Ya!");
        var a = await new DjAskEngine().AnswerAsync(u, "play Hey Ya by Outkast", null, []);
        Assert.Equal(DjAskDo.None, a.Do);
        Assert.Equal("Can't find that one, getting it now.", a.Say);
        Assert.Equal(new DjAskWait("Outkast", "Hey Ya!"), a.Wait);
        Assert.Equal(["hey ya outkast"], u.Requests);
        Assert.Null(DjAskEngine.Landed(u, "Outkast", "Hey Ya!"));
        var landed = T("Hey Ya!", "Outkast"); u.Library.Add(landed);
        Assert.Equal(landed.Path, DjAskEngine.Landed(u, "Outkast", "Hey Ya!"));
    }

    [Fact] public async Task ASongNowhereToBeFoundIsSaidSo()
    {
        var u = Standard(); u.NextRequest = null;
        var a = await new DjAskEngine().AnswerAsync(u, "play zzzqqq blorp", null, []);
        Assert.Equal("Can't find that one anywhere, sorry.", a.Say); Assert.Null(a.Wait);
    }

    [Fact] public async Task FavouritesMostPlayedAndUnheard()
    {
        var u = Standard(); u.Favs = [u.Library[0], u.Library[2]]; u.Top = [u.Library[5]];
        foreach (var t in u.Library.Skip(1)) u.HeardPaths.Add(t.Path);
        var e = new DjAskEngine();
        var fav = await e.AnswerAsync(u, "play my favourites", null, []);
        Assert.Equal(DjAskDo.Play, fav.Do); Assert.Equal(2, fav.Paths!.Count); Assert.True(fav.Shuffle);
        var top = await e.AnswerAsync(u, "play my most played", null, []);
        Assert.Equal([u.Library[5].Path], top.Paths);
        var fresh = await e.AnswerAsync(u, "play something I haven't heard", null, []);
        Assert.Equal([u.Library[0].Path], fresh.Paths);
        var empty = await e.AnswerAsync(new AskUser(), "play my favourites", null, []);
        Assert.Equal(DjAskDo.None, empty.Do);
    }

    [Fact] public async Task AnUnknownPlaylistIsSaidSo()
    {
        var a = await new DjAskEngine().AnswerAsync(Standard(), "play my wedding playlist", null, []);
        Assert.Equal(DjAskDo.None, a.Do); Assert.Contains("wedding", a.Say);
    }

    [Fact] public async Task EachAccountOnlyReachesItsOwnLibrary()
    {
        var e = new DjAskEngine();
        var ash = Standard();
        var sam = new AskUser { Account = "sam" }; sam.Library.Add(T("Yellow", "Coldplay")); sam.Lists.Add(new("pl-sam", "Sleep", 1, null, [new(sam.Library[0].Path, "Yellow", "Coldplay")]));
        var mine = await e.AnswerAsync(ash, "play my pop playlist", null, []);
        var theirs = await e.AnswerAsync(sam, "play my pop playlist", null, []);
        Assert.Equal(DjAskDo.Play, mine.Do);
        Assert.Equal(DjAskDo.None, theirs.Do);
        var sleep = await e.AnswerAsync(ash, "play my sleep playlist", null, []);
        Assert.Equal(DjAskDo.None, sleep.Do);
        var samSleep = await e.AnswerAsync(sam, "play my sleep playlist", null, []);
        Assert.All(samSleep.Paths!, p => Assert.Contains("Coldplay", p));
    }

    // ---- carried out on a player ----

    [Fact] public async Task AnswersRunOnThePlayer()
    {
        var u = Standard(); u.Favs = [u.Library[0]]; u.Top = [u.Library[1]];
        var e = new DjAskEngine(); var p = new FakePlayer();
        async Task Say(string words) => await DjAskRunner.RunAsync(p, await e.AnswerAsync(u, words, u.Library[2].Path, []));
        await Say("play my pop playlist"); Assert.Equal("play Pop", p.Log[^1]); Assert.Equal(2, p.Queue.Count);
        await Say("play my favourites"); Assert.Equal("play Favourites", p.Log[^1]);
        await Say("play my most played"); Assert.Equal("play Most played", p.Log[^1]); Assert.Equal([u.Library[1].Path], p.Queue);
        await Say("next"); Assert.Equal("next", p.Log[^1]);
        await Say("pause"); Assert.Equal("pause", p.Log[^1]);
        await Say("resume"); Assert.Equal("resume", p.Log[^1]);
        await Say("turn it up"); Assert.Equal(0.5 + DjAskRunner.VolumeStep, p.Volume, 3);
        await Say("turn it down"); await Say("turn it down"); Assert.Equal(0.5 - DjAskRunner.VolumeStep, p.Volume, 3);
        await Say("DJ off"); Assert.False(p.Dj);
        await Say("DJ on"); Assert.True(p.Dj);
        var count = p.Log.Count;
        u.NextRequest = new("Outkast", "Hey Ya!");
        await Say("play Hey Ya by Outkast");   // a download: nothing changes until it lands
        Assert.Equal(count, p.Log.Count);
    }

    [Fact] public void TheAnswerAsJsonCarriesWhatThePhoneNeeds()
    {
        var j = DjAskService.Json(new DjAskAnswer(DjAskDo.Play, "Here's your Pop playlist.", ["/a", "/b"], "Pop", Shuffle: true, Wait: null, Intent: "Playlist") { Clip = "/api/dj/clip/x.mp3" });
        Assert.Equal("play", (string?)j["do"]); Assert.Equal("/api/dj/clip/x.mp3", (string?)j["clip"]); Assert.Equal(2, j["paths"]!.AsArray().Count);
        Assert.True((bool?)j["shuffle"]);
        var w = DjAskService.Json(new DjAskAnswer(DjAskDo.None, "Can't find that one, getting it now.", Wait: new("Akon", "Smack That")));
        Assert.Equal("Akon", (string?)w["wait"]!["artist"]);
    }
}
