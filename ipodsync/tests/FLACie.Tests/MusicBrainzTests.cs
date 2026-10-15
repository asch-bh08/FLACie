using System.Net;
using System.Net.Http.Headers;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class MusicBrainzTests
{
    static Track T(string title = "Out of Space", string artist = "The Prodigy", string album = "Experience", long dur = 152_000) =>
        new("/Audio/a1b2c3d4e5f60718293a4b5c6d7e8f90/stream", title, artist, album, artist, 1, 1, dur, 1992, null, 0, TrackSource.Jellyfin, JellyfinId: "a1b2c3d4e5f60718293a4b5c6d7e8f90");

    const string Json = """
    {"recordings":[
      {"id":"r1","score":100,"title":"Out of Space","length":152000,"artist-credit":[{"name":"The Prodigy","joinphrase":"","artist":{"id":"a1","name":"The Prodigy"}}],
       "releases":[{"id":"rel1","title":"Experience","date":"1992-09-28","country":"GB","release-group":{"id":"rg1"}}]},
      {"id":"r2","score":90,"title":"Out of Space (Live)","length":300000,"artist-credit":[{"name":"The Prodigy"}],"releases":[{"id":"rel2","title":"Live at X","date":"1995"}]},
      {"id":"r3","score":60,"title":"Out of Space","length":150000,"artist-credit":[{"name":"Someone","joinphrase":" & "},{"name":"Else"}],"releases":[]}
    ]}
    """;

    sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Seen.Add(r); return Task.FromResult(answer(r)); }
    }

    static HttpResponseMessage Ok(string body = Json) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    // a gate that never really waits: time only moves when it is told to
    sealed class Clock
    {
        public DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public List<TimeSpan> Waits = [];
        public Task Delay(TimeSpan d, CancellationToken ct) { Waits.Add(d); Now += d; return Task.CompletedTask; }
    }

    static MusicBrainzClient Client(Fake f, Clock c, string? cache = null) =>
        new(new HttpClient(f), MusicBrainzClient.UserAgentFor("1.0-test"), cache, new MusicBrainzGate(TimeSpan.FromMilliseconds(1100), () => c.Now, c.Delay), () => c.Now);

    [Fact] public void UserAgentNamesTheAppAndAContact()
    {
        Assert.Equal("FLACie/1.0-beta60 ( https://github.com/asch-bh08/FLACie )", MusicBrainzClient.UserAgentFor("1.0-beta60"));
        Assert.Equal("FLACie/2 ( me@example.org )", MusicBrainzClient.UserAgentFor("2", " me@example.org "));
    }

    [Fact] public void QueryHasTitleArtistAndAlbumQuoted() =>
        Assert.Equal("recording:\"Out of Space\" AND artist:\"The Prodigy\" AND release:\"Experience\"", MusicBrainzClient.QueryFor(T()));

    [Fact] public void QueryEscapesQuotes() =>
        Assert.Contains("recording:\"Say \\\"Hi\\\"\"", MusicBrainzClient.QueryFor(T(title: "Say \"Hi\"")));

    [Fact] public void ParseReadsArtistsReleasesAndYear()
    {
        var l = MusicBrainzClient.Parse(Json);
        Assert.Equal(3, l.Count);
        Assert.Equal(("r1", "Out of Space", "The Prodigy", "Experience", "rel1", "rg1", 1992, 152000, "GB", 100), (l[0].Mbid, l[0].Title, l[0].Artist, l[0].Album, l[0].ReleaseMbid, l[0].ReleaseGroupMbid, l[0].Year, l[0].LengthMs, l[0].Country, l[0].ApiScore));
        Assert.Equal(1995, l[1].Year); Assert.Null(l[1].ReleaseGroupMbid); Assert.Equal("", l[2].Album); Assert.Equal("Someone & Else", l[2].Artist);
        Assert.Equal("https://musicbrainz.org/recording/r1", l[0].Url);
        Assert.Empty(MusicBrainzClient.Parse("{}")); Assert.Empty(MusicBrainzClient.Parse("""{"recordings":[{"title":"no id"}]}"""));
    }

    [Fact] public void TheRightRecordingScoresHighestAndTheLiveOneLower()
    {
        var ranked = MbMatching.Rank(T(), MusicBrainzClient.Parse(Json));
        Assert.Equal("r1", ranked[0].Candidate.Mbid); Assert.True(ranked[0].Score >= MbMatching.Likely, ranked[0].Score.ToString());
        Assert.True(ranked[1].Score < ranked[0].Score); Assert.True(ranked[2].Score < MbMatching.Likely);
    }

    [Fact] public void ScoreUsesDurationAndIgnoresCaseAndEditionsInTheAlbum()
    {
        var c = new MbCandidate("x", "OUT OF SPACE", "the prodigy", "Experience (Expanded Edition)", null, null, 1992, 151_000, null, 50);
        Assert.Equal(100, MbMatching.Score(T(), c));
        Assert.Equal(85, MbMatching.Score(T(), c with { LengthMs = 400_000 }));
        Assert.True(MbMatching.Score(T(artist: "Someone Else"), c) < MbMatching.Likely);
    }

    [Fact] public async Task TheRequestCarriesTheUserAgentAndGoesToMusicBrainz()
    {
        var f = new Fake(_ => Ok()); var c = new Clock();
        var hits = await Client(f, c).SearchRecordingAsync(T());
        Assert.Equal(3, hits.Count); Assert.Single(f.Seen);
        Assert.Equal("musicbrainz.org", f.Seen[0].RequestUri!.Host); Assert.Contains("fmt=json", f.Seen[0].RequestUri!.Query);
        Assert.Equal("FLACie/1.0-test ( https://github.com/asch-bh08/FLACie )", string.Join(" ", f.Seen[0].Headers.GetValues("User-Agent")));
    }

    [Fact] public async Task RequestsAreSpacedAtLeastTheGapApart()
    {
        var f = new Fake(_ => Ok()); var c = new Clock(); var client = Client(f, c);
        await client.SearchRecordingAsync(T("A")); await client.SearchRecordingAsync(T("B")); await client.SearchRecordingAsync(T("C"));
        Assert.Equal(3, f.Seen.Count);
        Assert.Equal(2, c.Waits.Count);   // the first goes at once; the next two wait out the gap
        Assert.All(c.Waits, w => Assert.InRange(w.TotalMilliseconds, 1099, 1101));
    }

    [Fact] public async Task ConcurrentRequestsNeverRunTogether()
    {
        var running = 0; var worst = 0;
        var gate = new MusicBrainzGate(TimeSpan.Zero);
        var tasks = Enumerable.Range(0, 12).Select(i => gate.RunAsync(async () => { var n = Interlocked.Increment(ref running); worst = Math.Max(worst, n); await Task.Delay(5); Interlocked.Decrement(ref running); return i; }, interactive: i % 2 == 0));
        await Task.WhenAll(tasks);
        Assert.Equal(1, worst); Assert.Equal(12, gate.Requests);
    }

    [Fact] public async Task APersonWaitingGoesBeforeBackgroundWork()
    {
        var gate = new MusicBrainzGate(TimeSpan.Zero);
        var order = new List<string>(); var hold = new TaskCompletionSource();
        var first = gate.RunAsync(async () => { await hold.Task; order.Add("first"); return 0; }, false);
        await Task.Delay(30);
        var bg = gate.RunAsync(() => { order.Add("background"); return Task.FromResult(0); }, false);
        var user = gate.RunAsync(() => { order.Add("user"); return Task.FromResult(0); }, true);
        await Task.Delay(30); hold.SetResult();
        await Task.WhenAll(first, bg, user);
        Assert.Equal(["first", "user", "background"], order);
    }

    [Fact] public async Task ARetryLaterAnswerIsWaitedOutAndTriedAgain()
    {
        var n = 0;
        var f = new Fake(_ =>
        {
            n++;
            if (n == 1) { var r = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)); return r; }
            return Ok();
        });
        var c = new Clock();
        var hits = await Client(f, c).SearchRecordingAsync(T());
        Assert.Equal(3, hits.Count); Assert.Equal(2, f.Seen.Count);
        Assert.Contains(c.Waits, w => w.TotalSeconds >= 6.9 && w.TotalSeconds <= 7.2);
    }

    [Fact] public async Task GivesUpAfterThreeBusyAnswersAndOtherErrorsAtOnce()
    {
        var busy = new Fake(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(busy, new Clock()).SearchRecordingAsync(T()));
        Assert.Equal(3, busy.Seen.Count);
        var bad = new Fake(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(bad, new Clock()).SearchRecordingAsync(T()));
        Assert.Single(bad.Seen);
    }

    [Fact] public async Task TheSecondAskComesFromTheDiskCacheAndExpires()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var f = new Fake(_ => Ok()); var c = new Clock(); var client = Client(f, c, dir);
            await client.SearchRecordingAsync(T()); await client.SearchRecordingAsync(T());
            Assert.Single(f.Seen);
            // another process / a restart: the cache is on disk
            var f2 = new Fake(_ => Ok()); await Client(f2, c, dir).SearchRecordingAsync(T()); Assert.Empty(f2.Seen);
            c.Now = c.Now.AddDays(31);
            await Client(f2, c, dir).SearchRecordingAsync(T()); Assert.Single(f2.Seen);
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact] public async Task NothingFoundIsRememberedLessLong()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var f = new Fake(_ => Ok("""{"recordings":[]}""")); var c = new Clock(); var client = Client(f, c, dir);
            Assert.Empty(await client.SearchRecordingAsync(T())); Assert.Empty(await client.SearchRecordingAsync(T())); Assert.Single(f.Seen);
            c.Now = c.Now.AddDays(8); await Client(f, c, dir).SearchRecordingAsync(T()); Assert.Equal(2, f.Seen.Count);
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact] public async Task ASongWithNoTitleAsksNothing()
    {
        var f = new Fake(_ => Ok());
        Assert.Empty(await Client(f, new Clock()).SearchRecordingAsync(T(title: "  ")));
        Assert.Empty(f.Seen);
    }

    [Fact] public void TheChoiceIsKeptBesideTheLibraryAndCanBeCleared()
    {
        var file = Path.Combine(Path.GetTempPath(), "mbo-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var o = new MbOverlay(file); var c = MusicBrainzClient.Parse(Json)[0];
            Assert.Null(o.Get(T())); o.Set(T(), c);
            Assert.Equal("r1", new MbOverlay(file).Get(T())!.Mbid);   // survives a restart
            Assert.Equal("rg1", o.ByRelease("REL1")!.ReleaseGroupMbid);   // the iPod desk serves a disc picture only for a release someone matched
            Assert.Null(o.ByRelease("rel2"));
            o.Clear(T()); Assert.Null(new MbOverlay(file).Get(T()));
            Assert.Null(o.ByRelease("rel1"));
        }
        finally { try { File.Delete(file); } catch (Exception) { } }
    }
}
