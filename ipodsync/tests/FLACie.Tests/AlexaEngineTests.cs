using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using Xunit;

namespace FLACie.Tests;

sealed class FakeUser : IAlexaUser
{
    public string Name => "ash";
    public List<Track> Library = [];
    public List<Playlist> Lists = [];
    public IReadOnlyList<Track> Songs { get; set; } = [];
    public IReadOnlyList<Playlist> Playlists => Lists;
    public List<AlexaDownload> DownloadList = [];
    public AlexaRequested? NextRequest = new("The Weeknd", "Blinding Lights");
    public List<string> Requests = [];
    public List<(string, int)> Ratings = [];
    public Track? Find(string path) => Songs.FirstOrDefault(s => s.Path == path);
    public Track? FindSong(string artist, string title) => Songs.FirstOrDefault(s => Matching.MatchKey(s) == Matching.MatchKey(title, artist));
    public Task RateAsync(Track t, int stars) { Ratings.Add((t.Title, stars)); return Task.CompletedTask; }
    public UserSession.PlaylistAdd AddToPlaylist(string name, Track t, out string used)
    {
        var p = Lists.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase)); used = p?.Name ?? name;
        if (p is null) return UserSession.PlaylistAdd.NoPlaylist;
        if (p.Entries.Any(e => e.Path == t.Path)) return UserSession.PlaylistAdd.Already;
        p.Entries.Add(new(t.Path, t.Title, t.Artist)); return UserSession.PlaylistAdd.Added;
    }
    public IReadOnlyList<Track> SimilarTo(FLACie.Core.Group artist, int take) => Songs.Where(s => s.Artist != artist.Name).Take(take).ToList();
    public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) { Requests.Add(words); return Task.FromResult(NextRequest); }
    public IReadOnlyList<AlexaDownload> Downloads() => DownloadList;

    public static Track T(string title, string artist, string album = "A", int no = 1, string? jf = "auto") =>
        new($"/p/{artist}/{title}", title, artist, album, artist, no, 1, 200000, 2000, null, no, TrackSource.Jellyfin, JellyfinId: jf == "auto" ? Guid.NewGuid().ToString("N") : jf);
}

public class AlexaEngineTests
{
    sealed class Rig : IDisposable
    {
        public string Dir = Directory.CreateTempSubdirectory().FullName;
        public AlexaConfig Cfg = new() { Enabled = true, Downloads = true };
        public AlexaQueues Queues; public AlexaEngine Engine; public FakeUser U = new();
        public Rig(params Track[] tracks)
        {
            U.Songs = tracks.ToList(); Queues = new(new DataPaths(Dir)); Engine = new(Queues, () => Cfg);
        }
        public void Dispose() { try { Directory.Delete(Dir, true); } catch (Exception) { } }
        public AlexaQueue Q => Queues.For("L");
    }

    static Rig Standard() => new(
        FakeUser.T("Blinding Lights", "The Weeknd", "After Hours", 1), FakeUser.T("Save Your Tears", "The Weeknd", "After Hours", 2),
        FakeUser.T("Heroes", "David Bowie", "Heroes", 1), FakeUser.T("Heroes", "Peter Gabriel", "Scratch", 1), FakeUser.T("Creep", "Radiohead", "Pablo Honey", 1),
        FakeUser.T("Karma Police", "Radiohead", "OK Computer", 2), FakeUser.T("Paranoid Android", "Radiohead", "OK Computer", 1), FakeUser.T("No Id Yet", "Radiohead", "New", 1, jf: null));

    [Fact] public async Task ConfidentSongPlaysAtOnceWithAShortReply()
    {
        using var r = Standard();
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play blinding lights"));
        Assert.Equal("Playing Blinding Lights by The Weeknd.", reply.Speech);
        Assert.Equal("Blinding Lights", reply.Play!.Track.Title); Assert.Equal(0, reply.Play.OffsetMs);
        Assert.True(reply.EndSession);
        Assert.Equal("Blinding Lights", r.U.Find(r.Q.Current!)!.Title);
    }

    [Fact] public async Task AnArtistShufflesTheirSongs()
    {
        using var r = Standard();
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play radiohead"));
        Assert.Equal("Playing Radiohead.", reply.Speech);
        Assert.True(r.Q.Shuffle); Assert.Equal(3, r.Q.Order.Count);   // the song without a Jellyfin id is left out
    }

    [Fact] public async Task AnAlbumPlaysInTrackOrder()
    {
        using var r = Standard();
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play the album ok computer"));
        Assert.Equal("Playing the album OK Computer.", reply.Speech);
        Assert.Equal(["Paranoid Android", "Karma Police"], r.Q.Order.Select(p => r.U.Find(p)!.Title).ToList());
    }

    [Fact] public async Task AmbiguousAsksOneQuestionAndTheAnswerPlaysThatChoice()
    {
        using var r = Standard();
        var ask = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play heroes"));
        Assert.False(ask.EndSession); Assert.StartsWith("Did you mean", ask.Speech); Assert.Null(ask.Play);
        var play = await r.Engine.ChooseAsync(r.U, "L", 1);
        Assert.Equal("Heroes", play.Play!.Track.Title);
        Assert.Equal(play.Play.Track.Path, r.Q.Current);
    }

    [Fact] public async Task ANoToTheQuestionAsksWhatToHear()
    {
        using var r = Standard();
        await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play heroes"));
        var reply = await r.Engine.ChooseAsync(r.U, "L", -1);
        Assert.Null(reply.Play); Assert.False(reply.EndSession);
    }

    [Fact] public async Task AnAnswerWithNoQuestionIsHandled()
    {
        using var r = Standard();
        var reply = await r.Engine.ChooseAsync(r.U, "L", 0);
        Assert.Contains("didn't ask", reply.Speech);
    }

    [Fact] public async Task AStaleQuestionIsForgotten()
    {
        using var r = Standard(); var t = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var e = new AlexaEngine(r.Queues, () => r.Cfg, () => t);
        await e.PlayAsync(r.U, "L", VoiceParser.FromRaw("play heroes"));
        t += TimeSpan.FromMinutes(3);
        Assert.Contains("didn't ask", (await e.ChooseAsync(r.U, "L", 0)).Speech);
    }

    [Fact] public async Task NotFoundSaysItIsSearchingRequestsTheDownloadAndRemembersIt()
    {
        using var r = Standard(); var said = new List<string>();
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play flurbo zanzibar song"), s => { said.Add(s); return Task.CompletedTask; });
        Assert.Single(said); Assert.Contains("Searching and downloading", said[0]);
        Assert.Single(r.U.Requests);
        Assert.Contains("downloading Blinding Lights by The Weeknd", reply.Speech); Assert.True(reply.AskNotifyPermission);
        Assert.Equal(Matching.MatchKey("Blinding Lights", "The Weeknd"), r.Q.LastDownloadKey);
    }

    [Fact] public async Task NotFoundWithDownloadsOffDoesNotDownload()
    {
        using var r = Standard(); r.Cfg.Downloads = false; var said = new List<string>();
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play flurbo zanzibar"), s => { said.Add(s); return Task.CompletedTask; });
        Assert.Empty(said); Assert.Empty(r.U.Requests); Assert.Contains("turned off", reply.Speech);
    }

    [Fact] public async Task NotFoundAtTheCatalogToo()
    {
        using var r = Standard(); r.U.NextRequest = null;
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play flurbo zanzibar"));
        Assert.Contains("couldn't find it", reply.Speech);
    }

    [Fact] public async Task AMissingPlaylistIsNeverDownloaded()
    {
        using var r = Standard();
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play my gardening playlist"));
        Assert.Empty(r.U.Requests); Assert.Contains("playlist called gardening", reply.Speech);
    }

    [Fact] public async Task PlayLastDownloadWorksOnceTheSongIsInTheLibrary()
    {
        using var r = Standard(); r.U.NextRequest = new("Queen", "Bicycle Race");
        await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play flurbo zanzibar"));
        r.U.DownloadList.Add(new("Queen", "Bicycle Race", "running", "Soulseek", DateTime.UtcNow));
        Assert.Contains("still downloading", r.Engine.IsItReady(r.U, "L").Speech);
        Assert.Contains("not ready", r.Engine.PlayLastDownload(r.U, "L").Speech);
        r.U.DownloadList[0] = r.U.DownloadList[0] with { State = "done" };   // downloaded, not scanned yet
        Assert.Contains("still adding", r.Engine.PlayLastDownload(r.U, "L").Speech);
        r.U.Songs = r.U.Songs.Concat([FakeUser.T("Bicycle Race", "Queen")]).ToList();
        Assert.Contains("is ready", r.Engine.IsItReady(r.U, "L").Speech);
        Assert.Equal("Bicycle Race", r.Engine.PlayLastDownload(r.U, "L").Play!.Track.Title);
    }

    [Fact] public async Task AFailedDownloadIsSaidPlainly()
    {
        using var r = Standard(); r.U.NextRequest = new("Queen", "Bicycle Race");
        await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play flurbo zanzibar"));
        r.U.DownloadList.Add(new("Queen", "Bicycle Race", "failed", "No source had it", DateTime.UtcNow));
        Assert.Contains("couldn't get", r.Engine.IsItReady(r.U, "L").Speech);
        Assert.Contains("couldn't download", r.Engine.PlayLastDownload(r.U, "L").Speech);
    }

    [Fact] public void NothingRequestedYet()
    {
        using var r = Standard();
        Assert.Contains("haven't asked", r.Engine.IsItReady(r.U, "L").Speech);
        Assert.Contains("haven't asked", r.Engine.PlayLastDownload(r.U, "L").Speech);
    }

    [Fact] public void WhatsDownloadingListsRunningJobs()
    {
        using var r = Standard();
        Assert.Equal("Nothing is downloading.", r.Engine.WhatsDownloading(r.U).Speech);
        r.U.DownloadList.Add(new("Queen", "Bicycle Race", "running", "", DateTime.UtcNow));
        Assert.Equal("I'm downloading Bicycle Race by Queen.", r.Engine.WhatsDownloading(r.U).Speech);
    }

    [Fact] public async Task NextPreviousAndTheEndOfTheList()
    {
        using var r = Standard();
        await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play the album ok computer"));
        Assert.Equal("Karma Police", r.Engine.Next(r.U, "L").Play!.Track.Title);
        var end = r.Engine.Next(r.U, "L"); Assert.True(end.Stop); Assert.Contains("last song", end.Speech);
        Assert.Equal("Paranoid Android", r.Engine.Previous(r.U, "L").Play!.Track.Title);
        Assert.Equal("Paranoid Android", r.Engine.Previous(r.U, "L").Play!.Track.Title);   // top of the list: restarts it
    }

    [Fact] public async Task PauseSavesWhereYouWereAndResumeStartsThere()
    {
        using var r = Standard();
        var first = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play blinding lights"));
        r.Engine.Started("L", first.Play!.Token);
        Assert.True(r.Engine.Pause("L").Stop);
        r.Engine.Stopped("L", first.Play.Token, 83_000);
        var back = r.Engine.Resume(r.U, "L");
        Assert.Equal(83_000, back.Play!.OffsetMs); Assert.NotEqual(first.Play.Token, back.Play.Token);
    }

    [Fact] public async Task NothingToResume()
    {
        using var r = Standard();
        Assert.Contains("nothing to resume", r.Engine.Resume(r.U, "L").Speech, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task NearlyFinishedEnqueuesTheNextSongAndStartedAdvances()
    {
        using var r = Standard();
        var first = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play the album ok computer"));
        r.Engine.Started("L", first.Play!.Token);
        var next = r.Engine.NearlyFinished(r.U, "L", first.Play.Token)!;
        Assert.Equal("Karma Police", next.Track.Title); Assert.Equal(first.Play.Token, next.ExpectedPrevious);
        r.Engine.Started("L", next.Token);
        Assert.Equal("Karma Police", r.U.Find(r.Q.Current!)!.Title);
        Assert.Null(r.Engine.NearlyFinished(r.U, "L", next.Token));   // end of the list
    }

    [Fact] public async Task RepeatOneAndRepeatAllGiveNewTokens()
    {
        using var r = Standard();
        var first = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play the album ok computer"));
        r.Engine.SetRepeat("L", AlexaRepeat.One);
        var again = r.Engine.NearlyFinished(r.U, "L", first.Play!.Token)!;
        Assert.Equal("Paranoid Android", again.Track.Title); Assert.NotEqual(first.Play.Token, again.Token);
        r.Engine.SetRepeat("L", AlexaRepeat.All); r.Engine.Started("L", again.Token);
        r.Engine.Next(r.U, "L"); var last = r.Q;
        var wrap = r.Engine.NearlyFinished(r.U, "L", EngineToken(r)); Assert.Equal("Paranoid Android", wrap!.Track.Title);
    }
    static string EngineToken(Rig r) => AlexaEngine.Token(r.Q, r.Q.Index, r.Q.Pass);

    [Fact] public async Task StaleEventsAreIgnored()
    {
        using var r = Standard();
        var first = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play the album ok computer"));
        r.Engine.Next(r.U, "L");   // a new serial
        var idx = r.Q.Index;
        r.Engine.Started("L", first.Play!.Token);
        r.Engine.Stopped("L", first.Play.Token, 5000);
        Assert.Equal(idx, r.Q.Index); Assert.Equal(0, r.Q.OffsetMs);
        Assert.Null(r.Engine.NearlyFinished(r.U, "L", first.Play.Token));
        Assert.Null(r.Engine.NearlyFinished(r.U, "L", "garbage"));
    }

    [Fact] public async Task FailedSkipsOnAndStopsAfterThreeInARow()
    {
        using var r = new Rig(Enumerable.Range(1, 8).Select(i => FakeUser.T("Song " + i, "Band", "Alb", i)).ToArray());
        var first = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play the album alb"));
        var token = first.Play!.Token;
        for (var i = 0; i < 3; i++) { var p = r.Engine.Failed(r.U, "L", token); Assert.NotNull(p); token = p!.Token; }
        Assert.Null(r.Engine.Failed(r.U, "L", token));
        r.Engine.Started("L", token); Assert.Equal(0, r.Q.FailStreak);
    }

    [Fact] public async Task LikeAndDislikeStoreStarsAndDislikeSkips()
    {
        using var r = Standard();
        await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play the album ok computer"));
        Assert.Contains("like", (await r.Engine.RateAsync(r.U, "L", true)).Speech); Assert.Equal(("Paranoid Android", 5), r.U.Ratings[0]);
        var dis = await r.Engine.RateAsync(r.U, "L", false);
        Assert.Equal(("Paranoid Android", 1), r.U.Ratings[1]); Assert.Equal("Karma Police", dis.Play!.Track.Title);
    }

    [Fact] public async Task RatingWithNothingPlaying()
    {
        using var r = Standard();
        Assert.Equal("Nothing is playing.", (await r.Engine.RateAsync(r.U, "L", true)).Speech);
    }

    [Fact] public async Task AddToPlaylistPaths()
    {
        using var r = Standard(); r.U.Lists.Add(new("p1", "Road Trip", 0, null, []));
        Assert.Equal("Nothing is playing.", r.Engine.AddToPlaylist(r.U, "L", "road trip").Speech);
        await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play blinding lights"));
        Assert.Equal("Which playlist?", r.Engine.AddToPlaylist(r.U, "L", null).Speech);
        Assert.Equal("Added Blinding Lights to Road Trip.", r.Engine.AddToPlaylist(r.U, "L", "road trip").Speech);
        Assert.Equal("It's already in Road Trip.", r.Engine.AddToPlaylist(r.U, "L", "road trip").Speech);
        Assert.Contains("couldn't find a playlist", r.Engine.AddToPlaylist(r.U, "L", "gardening").Speech);
    }

    [Fact] public async Task WhatsPlaying()
    {
        using var r = Standard();
        Assert.Equal("Nothing is playing.", r.Engine.WhatsPlaying(r.U, "L").Speech);
        await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play blinding lights"));
        Assert.Equal("This is Blinding Lights by The Weeknd, from After Hours.", r.Engine.WhatsPlaying(r.U, "L").Speech);
    }

    [Fact] public async Task ShuffleKeepsTheCurrentSong()
    {
        using var r = Standard();
        await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play the album ok computer"));
        var cur = r.Q.Current;
        r.Engine.SetShuffle(r.U, "L", true); Assert.Equal(cur, r.Q.Current);
        r.Engine.SetShuffle(r.U, "L", false); Assert.Equal(cur, r.Q.Current);
    }

    [Fact] public void RecentAndLibraryShortcuts()
    {
        using var r = Standard();
        Assert.StartsWith("Playing your recently added", r.Engine.PlayRecent(r.U, "L").Speech);
        Assert.Equal("Playing your library.", r.Engine.PlayLibrary(r.U, "L").Speech);
        Assert.Equal(7, r.Q.Order.Count);
        using var empty = new Rig(); Assert.Contains("nothing to play", empty.Engine.PlayLibrary(empty.U, "L").Speech, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task SomethingLikeStartsWithTheArtistThenOthers()
    {
        using var r = Standard();
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play something like radio head"));
        Assert.Equal("Playing music like Radiohead.", reply.Speech); Assert.True(r.Q.Order.Count > 3);
    }

    [Fact] public void NoticesAreTakenOnce()
    {
        using var r = Standard();
        r.Engine.DownloadEnded("L", "The Weeknd", "Blinding Lights", true, "");
        Assert.Contains("is ready", r.Engine.TakeNotice("L")!); Assert.Null(r.Engine.TakeNotice("L"));
        r.Engine.DownloadEnded("L", "Queen", "Bicycle Race", false, "no source");
        Assert.Contains("couldn't download", r.Engine.TakeNotice("L")!);
    }

    [Fact] public async Task TracksWithoutAJellyfinIdAreNeverQueued()
    {
        using var r = Standard();
        var reply = await r.Engine.PlayAsync(r.U, "L", VoiceParser.FromRaw("play no id yet"));
        Assert.Null(reply.Play); Assert.Contains("hasn't added it", reply.Speech);
    }
}
