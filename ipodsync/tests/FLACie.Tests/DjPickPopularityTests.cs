using System.Net;
using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>F11: real popularity, Deezer's track rank from the catalog search (fixtures here, never the real Deezer), kept on disk.</summary>
public sealed class DjPickPopularityTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "pop-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }
    string File => Path.Combine(dir, "popularity.json");

    sealed class Deezer : HttpMessageHandler
    {
        public int Asked;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Asked++;
            var q = Uri.UnescapeDataString(r.RequestUri!.Query);
            var body = q.Contains("Corona") ? """{"data": [{"title": "The Rhythm of the Night", "rank": 812345, "artist": {"name": "Corona"}}, {"title": "The Rhythm of the Night (Remix)", "rank": 900000, "artist": {"name": "Corona"}}]}"""
                : q.Contains("DeBarge") ? """{"data": [{"title": "Rhythm of the Night", "rank": 412000, "artist": {"name": "DeBarge"}}, {"title": "Rhythm of the Night", "rank": 999999, "artist": {"name": "Some Tribute Band"}}]}"""
                : """{"data": []}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    sealed class User(DjPickPopularity pop) : IDjAskUser
    {
        public List<Track> Library = [];
        public string Account => "ash";
        public bool Ready => true;
        public IReadOnlyList<Track> Songs => Library;
        public IReadOnlyList<Playlist> Playlists => [];
        public Track? Find(string path) => Library.FirstOrDefault(t => t.Path == path);
        public Track? FindSong(string artist, string title) => null;
        public IReadOnlyList<Track> Favourites => [];
        public IReadOnlyList<Track> MostPlayed(int take) => [];
        public bool Heard(Track t) => false;
        public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) => [];
        public Vibe? VibeOf(Track t) => null;
        public Task RateAsync(Track t, int stars) => Task.CompletedTask;
        public Task<FLACie.Server.Alexa.AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) => Task.FromResult<FLACie.Server.Alexa.AlexaRequested?>(null);
        public int Popularity(Track t) => (int)pop.Of(t);
        public Task LookUpPopularityAsync(IReadOnlyList<Track> songs, CancellationToken ct) => pop.LookUpAsync(songs, ct);
    }

    [Fact]
    public async Task Rhythm_of_the_night_picks_Corona_over_DeBarge_when_neither_was_played()
    {
        var deezer = new Deezer();
        var pop = new DjPickPopularity(new WebCatalog(new HttpClient(deezer)), File);
        var u = new User(pop);
        u.Library.AddRange([DjAskUnderstandTests.T("Rhythm of the Night", "DeBarge"), DjAskUnderstandTests.T("The Rhythm of the Night", "Corona")]);
        var a = await new DjAskEngine().AnswerAsync(u, "play rhythm of the night", null, []);
        Assert.Equal("/p/Corona/The Rhythm of the Night", a.Paths![0]);
        Assert.Equal(812345, pop.Of(u.Library[1]));
        Assert.Equal(412000, pop.Of(u.Library[0]));   // the tribute band's rank is not DeBarge's
    }

    [Fact]
    public async Task Ranks_are_kept_on_disk_and_not_asked_again()
    {
        var deezer = new Deezer();
        var t = DjAskUnderstandTests.T("The Rhythm of the Night", "Corona");
        await new DjPickPopularity(new WebCatalog(new HttpClient(deezer)), File).LookUpAsync([t], default);
        var asked = deezer.Asked;
        var again = new DjPickPopularity(new WebCatalog(new HttpClient(deezer)), File);
        Assert.Equal(812345, again.Of(t));
        await again.LookUpAsync([t], default);
        Assert.Equal(asked, deezer.Asked);
    }

    [Fact]
    public async Task A_lookup_Deezer_could_not_answer_is_not_kept()
    {
        var t = DjAskUnderstandTests.T("Toxic", "Britney Spears");
        var pop = new DjPickPopularity((_, _, _) => Task.FromResult<long?>(null), File);
        await pop.LookUpAsync([t], default);
        Assert.False(System.IO.File.Exists(File));
        Assert.Equal(0, pop.Of(t));
    }
}
