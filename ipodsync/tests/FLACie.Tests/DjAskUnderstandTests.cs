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
        public Dictionary<string, int> JfPlays = [];
        public int JellyfinPlays(Track t) => JfPlays.GetValueOrDefault(t.Path);
        public bool Heard(Track t) => false;
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => [];
        public Vibe? VibeOf(Track t) => null;
        public Task RateAsync(Track t, int stars) => Task.CompletedTask;
        /// <summary>What the DJ asked to download, and the answer (null: found nowhere).</summary>
        public List<string> Fetched = [];
        public Func<string, AlexaRequested?> Fetch = _ => null;
        public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) { Fetched.Add(words); return Task.FromResult(Fetch(words)); }
        public Dictionary<string, string> Picked = [];
        public IReadOnlyDictionary<string, string> Chosen => Picked;
        public void NoteChosen(string title, string song) => Picked[title] = song;
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
    public async Task Play_and_add_to_a_playlist_adds_it_then_plays_it_now()
    {
        // owner, 6 Nov: "play ... and add it to ..." queued it instead of playing it
        var u = Library();
        var a = await new DjAskEngine().AnswerAsync(u, "play rhythm of the night, add it to my pop playlist", null, []);
        Assert.Equal([DjAskDo.AddToPlaylist, DjAskDo.Play], a.AllSteps.Select(s => s.Do));
        Assert.Equal("Pop", a.AllSteps[0].Playlist);
        Assert.Equal(["/p/Corona/Rhythm of the Night"], a.AllSteps[0].Paths);
        Assert.Equal(["/p/Corona/Rhythm of the Night"], a.AllSteps[1].Paths);
        Assert.Equal(DjAskDo.Play, a.Do); // an old player plays it too
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

    static User WithDeBarge()
    {
        var u = new User();
        u.Library.AddRange([T("Just Dance", "Lady Gaga"), T("Rhythm of the Night", "DeBarge"), T("Toxic", "Britney Spears")]);
        u.Lists.Add(new("pl-pop", "Pop", 1, null, [new PlaylistEntry(u.Library[0].Path, u.Library[0].Title, u.Library[0].Artist), new PlaylistEntry(u.Library[2].Path, u.Library[2].Title, u.Library[2].Artist)]));
        return u;
    }

    [Theory]
    [InlineData("play rhythm of the night and add it to my pop playlist and shuffle pop", "pop", "shuffle")]
    [InlineData("play rhythm of the night add it to pop playlist then shuffle it", "pop", "shuffle")]
    [InlineData("add rhythm of the night to my pop playlist and shuffle the playlist", "pop", "shuffle")]
    [InlineData("add rhythm of the night to pop and play it next", "pop", null)]
    public void The_playlist_name_stops_where_the_next_ask_starts(string said, string playlist, string? then)
    {
        // owner, 6 Nov: "Pop playlist and shuffle pop" was taken as the playlist's name, and a new playlist made with it
        var p = DjAskRules.Parse(said)!;
        Assert.Equal(AskIntent.AddToPlaylist, p.Intent);
        Assert.Equal(playlist, p.Playlist);
        Assert.Equal(then, p.Then);
    }

    [Fact]
    public async Task Add_and_shuffle_adds_it_then_shuffles_the_playlist_starting_with_it()
    {
        var u = Library();
        var a = await new DjAskEngine().AnswerAsync(u, "play rhythm of the night and add it to my pop playlist and shuffle pop", null, []);
        Assert.Equal([DjAskDo.AddToPlaylist, DjAskDo.Play], a.AllSteps.Select(s => s.Do));
        Assert.Equal("Pop", a.AllSteps[0].Playlist);
        var play = a.AllSteps[1];
        Assert.Equal("/p/Corona/Rhythm of the Night", play.Paths![0]);
        Assert.Equal(5, play.Paths.Count);   // the song, then the 4 of Pop
        Assert.Equal("Pop", play.Title);
    }

    [Fact]
    public async Task By_an_artist_the_library_lacks_fetches_theirs_and_never_plays_another_artists_song()
    {
        // owner, 6 Nov: "rhythm of the night by corona" added DeBarge's to a playlist
        var u = WithDeBarge();
        u.Fetch = w => new AlexaRequested("Corona", "The Rhythm of the Night");
        var a = await new DjAskEngine().AnswerAsync(u, "play rhythm of the night by corona and add it to my pop playlist and shuffle pop", null, []);
        Assert.DoesNotContain(a.AllSteps, s => s.Paths?.Contains("/p/DeBarge/Rhythm of the Night") == true);
        Assert.Contains(u.Fetched, w => w.Contains("corona", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new DjAskWait("Corona", "The Rhythm of the Night", "Pop", true), a.Wait);
        Assert.DoesNotContain(a.AllSteps, s => s.Do == DjAskDo.AddToPlaylist);   // added when it lands, by the player
    }

    [Fact]
    public async Task The_corona_one_gets_that_song_by_corona()
    {
        var u = WithDeBarge();
        u.Fetch = w => new AlexaRequested("Corona", "The Rhythm of the Night");
        var a = await new DjAskEngine().AnswerAsync(u, "the corona one", "/p/DeBarge/Rhythm of the Night", []);
        Assert.Contains(u.Fetched, w => w.Contains("rhythm of the night", StringComparison.OrdinalIgnoreCase) && w.Contains("corona", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Corona", a.Wait?.Artist);
    }

    [Fact]
    public async Task Not_that_one_with_no_other_version_says_how_to_name_the_right_one()
    {
        var u = WithDeBarge();
        var a = await new DjAskEngine().AnswerAsync(u, "not that one", "/p/DeBarge/Rhythm of the Night", []);
        Assert.Contains("who it’s by", a.Say);
    }

    [Theory]
    [InlineData("add this to the playlist")]
    [InlineData("add it to a playlist")]
    [InlineData("add this to my list")]
    public async Task A_playlist_not_named_is_asked_about_never_made_as_Playlist(string said)
    {
        // owner, 6 Nov: an empty playlist called "Playlist" appeared
        var u = Library();
        var a = await new DjAskEngine().AnswerAsync(u, said, "/p/Corona/Rhythm of the Night", []);
        Assert.DoesNotContain(a.AllSteps, s => s.Do is DjAskDo.AddToPlaylist or DjAskDo.CreatePlaylist);
        Assert.Contains("Which playlist", a.Say);
    }

    [Fact]
    public async Task The_song_whose_title_is_what_was_said_beats_one_that_only_contains_it()
    {
        // owner, 6 Nov: "play Macarena" played Tyga's Ayy Macarena, not Los Del Rio's Macarena (the Bayside Boys Remix is the one in the library)
        var u = new User();
        u.Library.AddRange([T("Ayy Macarena", "Tyga"), T("Macarena - Bayside Boys Remix", "Los Del Rio"), T("Toxic", "Britney Spears")]);
        var a = await new DjAskEngine().AnswerAsync(u, "play macarena", null, []);
        Assert.Equal("/p/Los Del Rio/Macarena - Bayside Boys Remix", a.AllSteps.First(s => s.Do == DjAskDo.Play).Paths![0]);
        var b = await new DjAskEngine().AnswerAsync(u, "play ayy macarena", null, []);
        Assert.Equal("/p/Tyga/Ayy Macarena", b.AllSteps.First(s => s.Do == DjAskDo.Play).Paths![0]);
    }

    [Fact]
    public async Task The_version_picked_with_the_other_one_is_played_from_then_on()
    {
        // owner, 6 Nov: "it should know to play that one because I've asked so many times"
        var u = new User();
        u.Library.AddRange([T("Rhythm of the Night", "DeBarge"), T("The Rhythm of the Night", "Corona"), T("Toxic", "Britney Spears")]);
        u.Pop["/p/DeBarge/Rhythm of the Night"] = 10;   // the catalog thinks DeBarge's is the better known
        var e = new DjAskEngine();
        var first = await e.AnswerAsync(u, "play rhythm of the night", null, []);
        Assert.Equal("/p/DeBarge/Rhythm of the Night", first.AllSteps.First(s => s.Do == DjAskDo.Play).Paths![0]);
        var other = await e.AnswerAsync(u, "the other one", "/p/DeBarge/Rhythm of the Night", []);
        Assert.Equal("/p/Corona/The Rhythm of the Night", other.AllSteps.First(s => s.Do == DjAskDo.Play).Paths![0]);
        var again = await new DjAskEngine().AnswerAsync(u, "play rhythm of the night", null, []);
        Assert.Equal("/p/Corona/The Rhythm of the Night", again.AllSteps.First(s => s.Do == DjAskDo.Play).Paths![0]);
    }
}
