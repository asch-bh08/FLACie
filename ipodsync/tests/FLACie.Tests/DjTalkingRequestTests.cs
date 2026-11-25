using FLACie.Core;
using FLACie.Server.Alexa;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// Talking requests answered end to end by the DJ's request reader, with no text model (owner, 25 Nov: "if I ask the DJ to play a song and it plays the
/// wrong one or some random remix, that's bad"; "make the DJ smart with talking requests"): misheard titles, two songs in a row, which version, and the
/// follow-ups to a question the DJ asked ("yes", "no", "that one", "the second one").
/// </summary>
public class DjTalkingRequestTests
{
    sealed class User : IDjAskUser
    {
        public string Account => "ash";
        public bool Ready => true;
        public List<Track> Library = [];
        public List<Playlist> Lists = [];
        public IReadOnlyList<Track> Songs => Library;
        public IReadOnlyList<Playlist> Playlists => Lists;
        public Track? Find(string path) => Library.FirstOrDefault(t => t.Path == path);
        public Track? FindSong(string artist, string title) => Library.FirstOrDefault(s => Matching.MatchKey(s) == Matching.MatchKey(title, artist));
        public IReadOnlyList<Track> Favourites => [];
        public IReadOnlyList<Track> MostPlayed(int take) => [];
        public bool Heard(Track t) => false;
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => Library.Where(t => !session.Contains(t.Path)).Take(n).ToList();
        public Vibe? VibeOf(Track t) => null;
        public Task RateAsync(Track t, int stars) => Task.CompletedTask;
        public List<string> Requests = [];
        public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) { Requests.Add(words); return Task.FromResult<AlexaRequested?>(null); }
    }

    static Track T(string title, string artist, string genre = "", string album = "A", int year = 2006) =>
        new($"/p/{artist}/{album}/{title}", title, artist, album, artist, 1, 1, 200000, year, null, 1, TrackSource.Jellyfin, JellyfinId: Guid.NewGuid().ToString("N"), Genre: genre);

    static User Lib()
    {
        var u = new User();
        u.Library.AddRange([
            T("Heads Will Roll", "Yeah Yeah Yeahs", "Indie Rock", "It's Blitz!", 2009), T("Heads Will Roll (A-Trak Remix)", "Yeah Yeah Yeahs", "Electro", "Heads Will Roll EP", 2009),
            T("Maps", "Yeah Yeah Yeahs", "Indie Rock", "Fever to Tell", 2003), T("Maps (Live)", "Yeah Yeah Yeahs", "Indie Rock", "Live at the Roundhouse", 2004),
            T("Zero", "Yeah Yeah Yeahs", "Indie Rock", "It's Blitz!", 2009),
            T("Out of Space", "The Prodigy", "Big Beat", "Experience", 1992), T("Out of Space (Audio Bullys Remix)", "The Prodigy", "Big Beat", "Out of Space Remixes", 2005),
            T("Out of Space (Live)", "The Prodigy", "Big Beat", "Live at Brixton", 1997), T("Firestarter", "The Prodigy", "Big Beat", "The Fat of the Land", 1996),
            T("Breathe", "The Prodigy", "Big Beat", "The Fat of the Land", 1996),
            T("Creep", "Radiohead", "Alternative", "Pablo Honey", 1993), T("Creep (Acoustic)", "Radiohead", "Alternative", "My Iron Lung", 1994),
            T("The Rhythm of the Night", "Corona", "Eurodance", "The Rhythm of the Night", 1993), T("What Is Love", "Haddaway", "Eurodance", "The Album", 1993),
            T("Weightless", "Marconi Union", "Ambient", "Weightless", 2011), T("Teardrop", "Massive Attack", "Trip Hop", "Mezzanine", 1998),
            T("So What", "Miles Davis", "Jazz", "Kind of Blue", 1959), T("Levels", "Avicii", "EDM", "Levels", 2011),
        ]);
        u.Lists.Add(new("pl-pop", "Pop", 1, null, [new(u.Library[12].Path, "The Rhythm of the Night", "Corona")]));
        u.Lists.Add(new("pl-run", "Running", 1, null, [new(u.Library[17].Path, "Levels", "Avicii")]));
        return u;
    }

    static string Title(User u, string? path) => path is null ? "" : u.Find(path)?.Title ?? path;

    [Theory]
    [InlineData("play heads will role", "Heads Will Roll")]
    [InlineData("play heads will roll", "Heads Will Roll")]
    [InlineData("um can you play heads will roll", "Heads Will Roll")]
    [InlineData("hey dj play out of spaced", "Out of Space")]
    [InlineData("play out of space", "Out of Space")]
    [InlineData("play out of space by the prodigy", "Out of Space")]
    [InlineData("put maps on", "Maps")]
    [InlineData("play the song called zero", "Zero")]
    [InlineData("i wanna hear creep", "Creep")]
    [InlineData("play rhythm of the night", "The Rhythm of the Night")]
    [InlineData("play the rythm of the night", "The Rhythm of the Night")]
    [InlineData("play what is love", "What Is Love")]
    public async Task A_song_asked_for_plays_its_album_version(string said, string title)
    {
        var u = Lib();
        var a = await new DjAskEngine().AnswerAsync(u, said, null, []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.Equal(title, Title(u, a.Paths![0]));
        Assert.Single(a.Paths!);
    }

    [Theory]
    [InlineData("play late night music")]
    [InlineData("music for the gym")]
    [InlineData("play something chill")]
    [InlineData("play some 90s music")]
    [InlineData("play some jazz")]
    public async Task A_mood_genre_or_decade_plays_a_list_not_a_song_of_that_name(string said)
    {
        var u = Lib();
        var a = await new DjAskEngine().AnswerAsync(u, said, null, []);
        Assert.Equal("Mood", a.Intent);
        Assert.Empty(u.Requests);
    }

    [Fact]
    public async Task A_decade_plays_that_decade()
    {
        var u = Lib();
        var a = await new DjAskEngine().AnswerAsync(u, "play some 90s music", null, []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.All(a.Paths!, p => Assert.InRange(u.Find(p)!.Year, 1990, 1999));
    }

    [Fact]
    public async Task Something_by_an_artist_plays_that_artist()
    {
        var u = Lib();
        var a = await new DjAskEngine().AnswerAsync(u, "play something by the prodigy", null, []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.All(a.Paths!, p => Assert.Equal("The Prodigy", u.Find(p)!.Artist));
    }

    [Theory]
    [InlineData("play maps then heads will roll", "Maps", "Heads Will Roll")]
    [InlineData("play zero and then out of space", "Zero", "Out of Space")]
    [InlineData("play creep followed by teardrop", "Creep", "Teardrop")]
    public async Task Play_this_then_that_plays_the_first_and_queues_the_second(string said, string first, string then)
    {
        var u = Lib();
        var a = await new DjAskEngine().AnswerAsync(u, said, null, []);
        var steps = a.AllSteps;
        Assert.Equal(DjAskDo.Play, steps[0].Do);
        Assert.Equal(first, Title(u, steps[0].Paths![0]));
        Assert.Equal(DjAskDo.UpNext, steps[1].Do);
        Assert.Equal(then, Title(u, steps[1].Paths![0]));
        Assert.Contains(then, a.Say);
    }

    /// <summary>The phone's emulator library: "Song 1".."Song 400" by "Artist 001".. (phone DJ test, 25 Nov: "play Song 7" answered "Sorry, I didn't catch that").</summary>
    static User Numbered()
    {
        var u = new User();
        for (var i = 1; i <= 400; i++)
            u.Library.Add(new($"/m/{i}", $"Song {i}", $"Artist {i % 40 + 1:000}", $"Album {i % 30}", $"Artist {i % 40 + 1:000}", 1, 1, 200000, 2000, null, 1, TrackSource.Jellyfin,
                FilePath: $"/music/Artist {i % 40 + 1:000}/Song {i}.flac", JellyfinId: $"id{i}"));
        return u;
    }

    [Theory]
    [InlineData("play Song 7", "Song 7")]
    [InlineData("play song 7", "Song 7")]
    [InlineData("play Song 70", "Song 70")]
    [InlineData("play song seven", "Song 7")]
    [InlineData("play Song 7 by Artist 008", "Song 7")]
    public async Task A_numbered_title_plays_that_song_quickly_with_its_file(string said, string title)
    {
        var u = Numbered();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var a = await new DjAskEngine().AnswerAsync(u, said, null, []);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"{sw.ElapsedMilliseconds} ms");
        Assert.True(a.Do == DjAskDo.Play, a.Say);
        Assert.Equal(title, Title(u, a.Paths![0]));
        // the phone finds the song by its file: the answer's songs carry it
        var json = DjAskService.Json(a, u.Find);
        Assert.Equal($"/music/Artist 008/{title}.flac".Replace("Artist 008", u.Find(a.Paths[0])!.Artist), json["songs"]![0]!["file"]!.GetValue<string>());
    }

    sealed class OneActionModel : IDjAskModel
    {
        public Task<string?> AskAsync(string system, string user, CancellationToken ct) => Task.FromResult<string?>("{\"action\": \"play\", \"query\": \"maps\"}");
    }

    [Fact]
    public async Task Play_this_then_that_keeps_both_songs_when_the_model_is_on()
    {
        var u = Lib();
        var a = await new DjAskEngine(new OneActionModel()).AnswerAsync(u, "play maps then heads will roll", null, []);
        Assert.Equal(2, a.AllSteps.Count);
        Assert.Equal("Heads Will Roll", Title(u, a.AllSteps[1].Paths![0]));
    }

    [Theory]
    [InlineData("Heads Will Roll (A-Trak Remix)", "the original", "Heads Will Roll")]
    [InlineData("Heads Will Roll (A-Trak Remix)", "not the remix", "Heads Will Roll")]
    [InlineData("Out of Space (Live)", "play the album version", "Out of Space")]
    [InlineData("Out of Space", "the live one", "Out of Space (Live)")]
    [InlineData("Out of Space", "play the remix", "Out of Space (Audio Bullys Remix)")]
    [InlineData("Creep", "the acoustic version", "Creep (Acoustic)")]
    [InlineData("Maps", "the live version", "Maps (Live)")]
    public async Task Which_version_of_the_song_playing(string playing, string said, string title)
    {
        var u = Lib();
        var cur = u.Library.First(t => t.Title == playing);
        var a = await new DjAskEngine().AnswerAsync(u, said, cur.Path, [cur.Path]);
        Assert.True(a.Do == DjAskDo.Play, a.Say);
        Assert.Equal(title, Title(u, a.Paths![0]));
    }

    [Fact]
    public async Task Only_one_version_is_said_so()
    {
        var u = Lib();
        var cur = u.Library.First(t => t.Title == "Zero");
        var a = await new DjAskEngine().AnswerAsync(u, "the live one", cur.Path, [cur.Path]);
        Assert.Equal(DjAskDo.None, a.Do);
        Assert.Contains("Zero", a.Say);
    }

    [Fact]
    public async Task Add_this_to_a_playlist_asks_which_and_the_second_one_answers()
    {
        var u = Lib(); var e = new DjAskEngine();
        var cur = u.Library.First(t => t.Title == "Zero");
        var q = await e.AnswerAsync(u, "add this to a playlist", cur.Path, [cur.Path]);
        Assert.True(DjConversation.AskedBack(q));
        Assert.Contains("Pop", q.Say); Assert.Contains("Running", q.Say);
        var a = await e.AnswerAsync(u, "the second one", cur.Path, [cur.Path]);
        var add = Assert.Single(a.AllSteps, s => s.Do == DjAskDo.AddToPlaylist);
        Assert.Equal("Running", add.Playlist);
    }

    [Theory]
    [InlineData("yes", "Pop")]
    [InlineData("that one", "Pop")]
    [InlineData("the first one", "Pop")]
    [InlineData("pop", "Pop")]
    [InlineData("my running one", "Running")]
    [InlineData("the running playlist", "Running")]
    public async Task The_reply_to_which_playlist_picks_one(string reply, string playlist)
    {
        var u = Lib(); var e = new DjAskEngine();
        var cur = u.Library.First(t => t.Title == "Zero");
        await e.AnswerAsync(u, "add this to my playlist", cur.Path, [cur.Path]);
        var a = await e.AnswerAsync(u, reply, cur.Path, [cur.Path]);
        Assert.Equal(playlist, Assert.Single(a.AllSteps, s => s.Do == DjAskDo.AddToPlaylist).Playlist);
    }

    [Fact]
    public async Task No_to_which_playlist_leaves_it()
    {
        var u = Lib(); var e = new DjAskEngine();
        var cur = u.Library.First(t => t.Title == "Zero");
        await e.AnswerAsync(u, "add this to a playlist", cur.Path, [cur.Path]);
        var a = await e.AnswerAsync(u, "no", cur.Path, [cur.Path]);
        Assert.Empty(a.AllSteps);
        Assert.NotEqual(DjAskEngine.DidntCatch, a.Say);
    }

    [Theory]
    [InlineData("what's this", "Zero")]
    [InlineData("what song is this", "Zero")]
    [InlineData("who sings this", "Yeah Yeah Yeahs")]
    [InlineData("who's this by", "Yeah Yeah Yeahs")]
    public async Task Questions_about_the_song_playing(string said, string word)
    {
        var u = Lib();
        var cur = u.Library.First(t => t.Title == "Zero");
        var a = await new DjAskEngine().AnswerAsync(u, said, cur.Path, [cur.Path]);
        Assert.Contains(word, a.Say);
    }

    [Theory]
    [InlineData("skip", DjAskDo.Next)]
    [InlineData("move on", DjAskDo.Next)]
    [InlineData("more like this", DjAskDo.UpNext)]
    [InlineData("keep this vibe going", DjAskDo.UpNext)]
    [InlineData("change it up", DjAskDo.UpNext)]
    public async Task Controls(string said, DjAskDo did)
    {
        var u = Lib();
        var cur = u.Library.First(t => t.Title == "Zero");
        var a = await new DjAskEngine().AnswerAsync(u, said, cur.Path, [cur.Path]);
        Assert.Equal(did, a.Do);
    }
}
