using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using FLACie.Server.Dj;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>F12 end to end on the web player: what is said goes through the DJ's request reader and the web runner, and the person's playlists change only
/// as the conversation says (are you sure / yes / no, make and rename, "not that one" across turns).</summary>
public class DjConversationWebTests
{
    sealed class Js : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
    }

    /// <summary>The DJ's view of a real UserSession (its songs and playlists), as SessionAskUser gives it on the server.</summary>
    sealed class SessionUser(UserSession s) : IDjAskUser
    {
        public string Account => "u1";
        public bool Ready => true;
        public IReadOnlyList<Track> Songs => s.Extra.Values.OrderBy(t => t.Path).ToList();
        public IReadOnlyList<Playlist> Playlists => s.Playlists;
        public Track? Find(string path) => s.FindByPath(path);
        public Track? FindSong(string artist, string title) => Songs.FirstOrDefault(t => Matching.MatchKey(t) == Matching.MatchKey(title, artist));
        public IReadOnlyList<Track> Favourites => [];
        public IReadOnlyList<Track> MostPlayed(int take) => [];
        public bool Heard(Track t) => false;
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => [];
        public Vibe? VibeOf(Track t) => null;
        public Task RateAsync(Track t, int stars) => Task.CompletedTask;
        public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) => Task.FromResult<AlexaRequested?>(null);
        public int Popularity(Track t) => t.Artist == "Corona" ? 900 : 100;
    }

    static (PlayerState P, UserSession S) Rig()
    {
        var me = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };
        foreach (var t in new[] { DjAskUnderstandTests.T("Toxic", "Britney Spears"), DjAskUnderstandTests.T("Firework", "Katy Perry"), DjAskUnderstandTests.T("Roads", "Portishead"),
                     DjAskUnderstandTests.T("The Rhythm of the Night", "Corona"), DjAskUnderstandTests.T("Rhythm of the Night", "DeBarge") })
            me.Extra[t.Path] = t;
        var pop = me.CreatePlaylist("Pop");
        me.AddToPlaylist(pop.Id, me.Extra["/p/Britney Spears/Toxic"]); me.AddToPlaylist(pop.Id, me.Extra["/p/Katy Perry/Firework"]);
        var engine = new DjAskEngine();
        var user = new SessionUser(me);
        RetroHost.DjAsk = (s, text, current, recent, ct) => engine.AnswerAsync(user, text, current, recent, ct);
        var p = new PlayerState(new Js());
        p.SetLibrary(() => new Library([.. me.Extra.Values]));
        p.SetPlanner(me, new PlayerDjLiveTests.NoPlanner());
        return (p, me);
    }

    static List<string> Pop(UserSession s) => s.Playlists.First(x => x.Name == "Pop").Entries.Select(e => e.Title).ToList();

    [Fact]
    public async Task Remove_asks_first_yes_removes_and_no_keeps()
    {
        var (p, s) = Rig();
        await p.Play([s.Extra["/p/Britney Spears/Toxic"]], 0, "Pop");
        var q = await p.AskDjAsync("remove this from the playlist");
        Assert.Contains("sure", q!.Say);
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(q, 1));
        Assert.True(DjConversation.AskedBack(q));
        Assert.Equal(["Toxic", "Firework"], Pop(s));
        await p.AskDjAsync("no");
        Assert.Equal(["Toxic", "Firework"], Pop(s));
        await p.AskDjAsync("remove this from my pop playlist");
        await p.AskDjAsync("yes");
        Assert.Equal(["Firework"], Pop(s));
    }

    [Fact]
    public async Task Make_a_playlist_names_it_then_call_it_renames_it()
    {
        var (p, s) = Rig();
        var a = await p.AskDjAsync("make a backroads playlist");
        Assert.True(DjConversation.AskedBack(a!));
        var made = Assert.Single(s.Playlists, x => x.Name == "Backroads");
        Assert.NotEmpty(made.Entries);
        Assert.NotNull(p.Current);   // and it plays
        await p.AskDjAsync("call it Country Roads");
        Assert.DoesNotContain(s.Playlists, x => x.Name == "Backroads");
        Assert.Equal(made.Entries.Count, Assert.Single(s.Playlists, x => x.Name == "Country Roads").Entries.Count);
    }

    [Fact]
    public async Task Not_that_one_across_turns_plays_the_other_version()
    {
        var (p, _) = Rig();
        await p.AskDjAsync("play rhythm of the night");
        Assert.Equal("Corona", p.Current!.Artist);
        await p.AskDjAsync("not that one");
        Assert.Equal("DeBarge", p.Current!.Artist);
    }

    [Fact]
    public async Task The_web_runner_runs_every_step_kind()
    {
        var (p, s) = Rig();
        var toxic = "/p/Britney Spears/Toxic"; var roads = "/p/Portishead/Roads";
        var answer = new DjAskAnswer(DjAskDo.None, "")
        {
            Steps = [new(DjAskDo.Play, [toxic, roads], "Mix"), new(DjAskDo.UpNext, ["/p/Katy Perry/Firework"]), DjAskStep.AddTo("Pop", [roads]),
                DjAskStep.RemoveFrom("Pop", [toxic]), DjAskStep.Create("Trip", [roads, toxic]), new(DjAskDo.RenamePlaylist, Title: "Road Trip", Playlist: "Trip"),
                new(DjAskDo.VolumeDown), new(DjAskDo.Next), new(DjAskDo.Pause), new(DjAskDo.Resume), new(DjAskDo.Shuffle, Shuffle: true), DjAskStep.Question("Anything else?")],
        };
        RetroHost.DjAsk = (_, _, _, _, _) => Task.FromResult(answer);
        await p.AskDjAsync("do it all");
        Assert.Equal(["Firework", "Roads"], Pop(s));
        Assert.Equal(["Roads", "Toxic"], Assert.Single(s.Playlists, x => x.Name == "Road Trip").Entries.Select(e => e.Title));
        Assert.True(p.Shuffle);
        Assert.True(p.Volume < 1);
        Assert.Equal("Firework", p.Current!.Title);   // Up next, then Next (shuffle after it leaves the current song alone)
        Assert.True(DjConversation.AskedBack(answer));
    }
}
