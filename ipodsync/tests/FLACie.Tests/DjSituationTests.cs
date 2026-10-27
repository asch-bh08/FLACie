using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>Learned requests: "play car songs" plays what this person plays in the car, and their own phrases learn from what they finish and skip.</summary>
public class DjSituationTests
{
    sealed class User : IDjAskUser
    {
        public string Account { get; set; } = "ash";
        public bool Ready => true;
        public List<Track> Library = [];
        public IReadOnlyList<Track> Songs => Library;
        public IReadOnlyList<Playlist> Playlists => [];
        public Track? Find(string path) => Library.FirstOrDefault(t => t.Path == path);
        public Track? FindSong(string artist, string title) => Library.FirstOrDefault(s => Matching.MatchKey(s) == Matching.MatchKey(title, artist));
        public IReadOnlyList<Track> Favourites => [];
        public IReadOnlyList<Track> MostPlayed(int take) => [];
        public bool Heard(Track t) => false;
        public List<Track> Picks = [];
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => Picks.Take(n).ToList();
        public Dictionary<string, Vibe> Vibes = [];
        public Vibe? VibeOf(Track t) => Vibes.GetValueOrDefault(t.Path);
        public Task RateAsync(Track t, int stars) => Task.CompletedTask;
        public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) => Task.FromResult<AlexaRequested?>(null);
        public List<ListenEvent> Events = [];
        public IReadOnlyList<ListenEvent> Listens => Events;
        public List<PhraseAsk> Asked = [];
        public IReadOnlyList<PhraseAsk> Phrases => Asked;
        public void NotePhrase(PhraseAsk p) => Asked.Add(p);
    }

    static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    static long Ms(DateTime d) => new DateTimeOffset(d).ToUnixTimeMilliseconds();

    static Track T(string title, string artist) =>
        new($"/p/{artist}/{title}", title, artist, "A", artist, 1, 1, 200000, 1993, null, 1, TrackSource.Jellyfin, JellyfinId: Guid.NewGuid().ToString("N"));

    static ListenEvent Play(Track t, bool finished, string? surface, DateTime at, int? hour = null) =>
        new(Guid.NewGuid().ToString("N"), ListenKind.Play, Ms(at), t.Path, t.Title, t.Artist, finished ? 200 : 10, 200, null, surface, hour);

    static User Standard()
    {
        var u = new User();
        u.Library.AddRange([T("U Got 2 Know", "Cappella"), T("Creep", "Radiohead"), T("Weightless", "Marconi Union"), T("Levels", "Avicii"), T("So What", "Miles Davis")]);
        u.Picks.AddRange([u.Library[1], u.Library[2], u.Library[3], u.Library[4]]);
        return u;
    }

    [Fact]
    public void Old_events_without_the_new_fields_still_load_and_new_ones_round_trip()
    {
        var old = JsonSerializer.Deserialize<ListenEvent>("{\"id\":\"a\",\"kind\":0,\"at\":1,\"path\":\"/p\",\"title\":\"t\",\"artist\":\"a\",\"heard\":10,\"length\":200}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(old.Surface); Assert.Null(old.Hour);
        var dir = Directory.CreateTempSubdirectory().FullName;
        var store = new ListenLogStore(new DataPaths(dir));
        var me = new UserSession("local", null, null) { Id = "me" };
        store.Append(me, [old, old with { Id = "b", Surface = "car", Hour = 7 }]);
        var back = new ListenLogStore(new DataPaths(dir)).Read(me);
        Assert.Equal("car", back.Single(e => e.Id == "b").Surface);
        Assert.Equal(7, back.Single(e => e.Id == "b").Hour);
    }

    [Fact]
    public void Events_made_now_carry_the_local_hour()
    {
        var e = ListenEvent.Of(ListenKind.Play, T("Creep", "Radiohead"), 10, 200, surface: "web");
        Assert.Equal("web", e.Surface);
        Assert.Equal(DateTime.Now.Hour, e.Hour);
    }

    [Fact]
    public void The_car_profile_prefers_songs_finished_in_the_car()
    {
        var u = Standard();
        var cappella = u.Library[0]; var creep = u.Library[1];
        var events = new List<ListenEvent> { Play(cappella, true, "car", Now.AddDays(-2)), Play(cappella, true, "car", Now.AddDays(-9)), Play(creep, true, "phone", Now.AddDays(-1)), Play(creep, false, "car", Now.AddDays(-1)) };
        var car = SituationTaste.Score(events, e => SituationTaste.In(e, "car"), Ms(Now));
        Assert.True(car[cappella.Path] > 1);
        Assert.True(car[creep.Path] < 0);
        Assert.True(SituationTaste.In(Play(creep, true, null, Now, hour: 23), "night"));
        Assert.True(SituationTaste.In(Play(creep, true, null, Now, hour: 7), "morning"));
    }

    [Theory]
    [InlineData("play some B-road music")]
    [InlineData("play car songs")]
    [InlineData("something for the B-roads")]
    [InlineData("back-roads music")]
    [InlineData("driving tunes")]
    public void Road_words_mean_the_car(string said) => Assert.Equal(new AskParse(AskIntent.Situation, "car"), DjAskRules.Parse(said) is { } p ? p with { Key = null, Vibe = null } : null);

    [Fact]
    public void A_song_called_road_is_still_a_song() => Assert.Equal(AskIntent.Play, DjAskRules.Parse("play Road to Nowhere")!.Intent);

    [Fact]
    public async Task B_road_music_plays_from_the_car_profile()
    {
        var u = Standard();
        u.Events.Add(Play(u.Library[0], true, "car", Now.AddDays(-3)));
        var a = await new DjAskEngine(clock: () => Now).AnswerAsync(u, "play some B-road music", null, []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.Equal(u.Library[0].Path, a.Paths![0]);
        Assert.DoesNotContain("Haven't", a.Say);
        Assert.Single(u.Asked);
    }

    [Fact]
    public async Task No_car_history_falls_back_to_the_vibe_and_says_so_lightly()
    {
        var u = Standard();
        u.Vibes[u.Library[2].Path] = new(0.1, 0.1, 70, 0.1);
        u.Vibes[u.Library[3].Path] = new(0.9, 0.9, 128, 0.9);
        var a = await new DjAskEngine(clock: () => Now).AnswerAsync(u, "driving tunes", null, []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.StartsWith("Haven't got many car songs from you yet", a.Say);
        Assert.Contains(u.Library[3].Path, a.Paths!);
        Assert.DoesNotContain(u.Library[2].Path, a.Paths!);
    }

    [Fact]
    public async Task A_phrase_learns_from_what_is_finished_and_skipped_after_it()
    {
        var u = Standard();
        var model = new Func<string, string?>(_ => "{\"action\":\"situation\",\"query\":\"none\",\"key\":\"sunday cleaning\"}");
        var engine = new DjAskEngine(new Model(model), clock: () => Now.AddDays(-1));
        await engine.AnswerAsync(u, "music for sunday cleaning", null, []);
        var asked = Assert.Single(u.Asked);
        Assert.Equal("sunday cleaning", asked.Key);
        var at = DateTimeOffset.FromUnixTimeMilliseconds(asked.At).UtcDateTime;
        u.Events.Add(Play(u.Library[4], true, "web", at.AddMinutes(5)));    // finished after the request: counts for it
        u.Events.Add(Play(u.Library[1], false, "web", at.AddMinutes(10)));  // skipped: counts against
        u.Events.Add(Play(u.Library[3], true, "web", at.AddHours(3)));      // too late: not part of it
        var again = await new DjAskEngine(new Model(_ => "{\"action\":\"situation\",\"query\":\"none\",\"key\":\"sunday cleaning\"}"), clock: () => Now).AnswerAsync(u, "sunday cleaning songs please", null, []);
        Assert.Equal(u.Library[4].Path, again.Paths![0]);
        Assert.DoesNotContain(u.Library[1].Path, again.Paths!);
        var phrase = SituationTaste.Phrase(u.Events, u.Asked.Take(1).ToList(), "sunday cleaning", Ms(Now));
        Assert.False(phrase.ContainsKey(u.Library[3].Path));
    }

    [Fact]
    public void Close_phrases_find_the_key_already_learned()
    {
        Assert.Equal("b road", PhraseKeys.Closest(PhraseKeys.Of("play some B-road music"), ["b road", "gym"]));
        Assert.Equal("sunday cleaning", PhraseKeys.Closest("cleaning sunday", ["sunday cleaning"]));
        Assert.Equal("gym", PhraseKeys.Closest("gym", ["sunday cleaning"]));
    }

    [Fact]
    public void Phrases_are_kept_per_account()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var store = new ListenLogStore(new DataPaths(dir));
        var me = new UserSession("local", null, null) { Id = "me" }; var you = new UserSession("local", null, null) { Id = "you" };
        store.AddPhrase(me, new("b road", 5));
        Assert.Single(new ListenLogStore(new DataPaths(dir)).Phrases(me));
        Assert.Empty(store.Phrases(you));
    }

    sealed class Model(Func<string, string?> answer) : IDjAskModel
    {
        public Task<string?> AskAsync(string system, string user, CancellationToken ct) => Task.FromResult(answer(user));
    }
}
