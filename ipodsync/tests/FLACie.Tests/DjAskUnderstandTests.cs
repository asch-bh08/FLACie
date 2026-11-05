using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>F10: requests understood by meaning, answered as a list of steps.</summary>
public class DjAskUnderstandTests
{
    internal sealed class User : IDjAskUser
    {
        public string Account => "ash";
        public bool Ready => true;
        public List<Track> Library = [];
        public List<Playlist> Lists = [];
        public IReadOnlyList<Track> Songs => Library;
        public IReadOnlyList<Playlist> Playlists => Lists;
        public Track? Find(string path) => Library.FirstOrDefault(t => t.Path == path);
        public Track? FindSong(string artist, string title) => Library.FirstOrDefault(s => Matching.MatchKey(s) == Matching.MatchKey(title, artist));
        public List<Track> Hearts = [];
        public IReadOnlyList<Track> Favourites => Hearts;
        public List<ListenEvent> Log = [];
        public IReadOnlyList<ListenEvent> Listens => Log;
        public List<Track> Top = [];
        public Dictionary<string, int> Pop = [];
        public IReadOnlyList<Track> MostPlayed(int take) => Top.Take(take).ToList();
        public int Popularity(Track t) => Pop.GetValueOrDefault(t.Path);
        public bool Heard(Track t) => false;
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => [];
        public Vibe? VibeOf(Track t) => null;
        public Task RateAsync(Track t, int stars) => Task.CompletedTask;
        public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) => Task.FromResult<AlexaRequested?>(null);
    }

    sealed class Model(string reply) : IDjAskModel
    {
        public Task<string?> AskAsync(string system, string user, CancellationToken ct) => Task.FromResult<string?>(reply);
    }

    internal static Track T(string title, string artist) =>
        new($"/p/{artist}/{title}", title, artist, "A", artist, 1, 1, 200000, 2006, null, 1, TrackSource.Jellyfin, JellyfinId: Guid.NewGuid().ToString("N"));

    static User Library()
    {
        var u = new User();
        u.Library.AddRange([T("Just Dance", "Lady Gaga"), T("LoveGame", "Lady Gaga"), T("Firework", "Katy Perry"), T("Rhythm of the Night", "Corona"), T("Toxic", "Britney Spears")]);
        u.Lists.Add(new("pl-pop", "Pop", 1, null, [.. new[] { 0, 1, 2, 4 }.Select(i => new PlaylistEntry(u.Library[i].Path, u.Library[i].Title, u.Library[i].Artist))]));
        return u;
    }

    [Fact]
    public async Task Play_a_song_from_a_playlist_plays_it_then_the_rest()
    {
        var u = Library();
        var a = await new DjAskEngine().AnswerAsync(u, "play lovegame from my pop playlist", null, []);
        var step = Assert.Single(a.AllSteps);
        Assert.Equal(DjAskDo.Play, step.Do);
        Assert.Equal("/p/Lady Gaga/LoveGame", step.Paths![0]);
        Assert.Equal(4, step.Paths.Count);
        Assert.Equal(new[] { "/p/Lady Gaga/Just Dance", "/p/Katy Perry/Firework", "/p/Britney Spears/Toxic" }.Order(), step.Paths.Skip(1).Order());
        Assert.Equal("Pop", step.Title);
    }

    [Fact]
    public async Task Play_and_add_to_a_playlist_adds_it_then_plays_it_next()
    {
        var u = Library();
        var a = await new DjAskEngine().AnswerAsync(u, "play rhythm of the night, add it to my pop playlist", null, []);
        Assert.Equal([DjAskDo.AddToPlaylist, DjAskDo.UpNext], a.AllSteps.Select(s => s.Do));
        Assert.Equal("Pop", a.AllSteps[0].Playlist);
        Assert.Equal(["/p/Corona/Rhythm of the Night"], a.AllSteps[0].Paths);
        Assert.Equal(["/p/Corona/Rhythm of the Night"], a.AllSteps[1].Paths);
        Assert.Equal(DjAskDo.UpNext, a.Do); // an old player still plays it next
    }

    [Fact]
    public async Task Add_this_to_a_playlist_means_the_song_playing()
    {
        var u = Library();
        var a = await new DjAskEngine().AnswerAsync(u, "add this to my pop playlist", "/p/Corona/Rhythm of the Night", []);
        var step = Assert.Single(a.AllSteps);
        Assert.Equal(DjAskDo.AddToPlaylist, step.Do);
        Assert.Equal(["/p/Corona/Rhythm of the Night"], step.Paths);
    }

    [Fact]
    public async Task The_model_can_answer_with_the_same_steps()
    {
        var u = Library();
        var model = new Model("{\"action\": \"add_to_playlist\", \"query\": \"Rhythm of the Night\", \"playlist\": \"Pop\", \"play\": true}");
        var a = await new DjAskEngine(model).AnswerAsync(u, "stick the corona one on my pop list and play it after this", null, []);
        Assert.Equal([DjAskDo.AddToPlaylist, DjAskDo.UpNext], a.AllSteps.Select(s => s.Do));
        var from = await new DjAskEngine(new Model("{\"action\": \"play_from\", \"query\": \"LoveGame\", \"playlist\": \"Pop\"}")).AnswerAsync(u, "lovegame off pop and keep going", null, []);
        Assert.Equal("/p/Lady Gaga/LoveGame", from.AllSteps[0].Paths![0]);
        Assert.Equal(4, from.AllSteps[0].Paths!.Count);
    }

    [Theory]
    [InlineData("play lovegame from my pop playlist", AskIntent.PlayFrom, "lovegame", "pop")]
    [InlineData("play lovegame from the pop playlist then the rest", AskIntent.PlayFrom, "lovegame", "pop")]
    [InlineData("add rhythm of the night to pop and play it next", AskIntent.AddToPlaylist, "rhythm of the night", "pop")]
    [InlineData("play rhythm of the night add it to my pop playlist", AskIntent.AddToPlaylist, "rhythm of the night", "pop")]
    public void Rules_read_the_two_part_requests(string said, AskIntent intent, string what, string playlist)
    {
        var p = DjAskRules.Parse(said)!;
        Assert.Equal(intent, p.Intent);
        Assert.Equal(what, p.What);
        Assert.Equal(playlist, p.Playlist);
    }
}
