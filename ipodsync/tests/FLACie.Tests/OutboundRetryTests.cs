using System.Net;
using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Alexa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The outside services that used to fail on the first hiccup and give up: the music catalogs (Deezer, then iTunes) behind Search, Charts and the
/// online covers; LRCLIB's lyrics; the ntfy notifications; the Alexa signing certificate. Each now asks again (HttpRetry), and a lyrics lookup that
/// could not be asked is not remembered as "no lyrics" for a week.
/// </summary>
public class OutboundRetryTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("flacie-outbound").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    sealed class Script(Func<HttpRequestMessage, int, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls; public readonly List<string> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Urls.Add(r.RequestUri!.ToString()); return Task.FromResult(answer(r, ++Calls)); }
    }

    static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    static HttpResponseMessage Status(HttpStatusCode c) => new(c) { Content = new StringContent("") };
    static readonly RetryPolicy NoWait = new() { Delay = (_, _) => Task.CompletedTask };

    const string DeezerYellow = """{"data":[{"title":"Yellow","artist":{"name":"Coldplay"},"album":{"title":"Parachutes","cover_xl":"https://e.example/c.jpg"},"duration":266}]}""";

    [Fact]
    public async Task A_catalog_search_that_hits_a_busy_moment_still_finds_the_song()
    {
        var h = new Script((r, n) => n == 1 ? Status(HttpStatusCode.ServiceUnavailable) : Json(DeezerYellow));
        var songs = await new WebCatalog(new HttpClient(h)) { Retry = NoWait }.SongsAsync("coldplay yellow");
        Assert.Equal("Yellow", Assert.Single(songs).Title);
        Assert.All(h.Urls, u => Assert.StartsWith("https://api.deezer.com/", u));   // not iTunes: Deezer answered the second time
    }

    [Fact]
    public async Task Deezers_quota_answer_is_waited_out_once_instead_of_falling_to_iTunes()
    {
        // Deezer says "Quota limit exceeded" (50 requests in 5 seconds) with a 200 and an error object
        var h = new Script((r, n) => n == 1 ? Json("""{"error":{"type":"Exception","message":"Quota limit exceeded","code":4}}""") : Json(DeezerYellow));
        var songs = await new WebCatalog(new HttpClient(h)) { Retry = NoWait }.SongsAsync("coldplay yellow");
        Assert.Single(songs);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task The_online_album_lookup_asks_again_and_a_dead_catalog_gives_nothing_without_throwing()
    {
        var album = """{"data":[{"title":"Parachutes","artist":{"name":"Coldplay"},"cover_big":"https://e.example/big.jpg"}]}""";
        var h = new Script((r, n) => n == 1 ? Status(HttpStatusCode.BadGateway) : Json(album));
        var hit = await MediaEndpoints.OnlineAlbum(new HttpClient(h), "Coldplay " + Guid.NewGuid().ToString("N")[..6], "Parachutes", "Yellow", default, NoWait);
        Assert.Equal("https://e.example/big.jpg", hit?.Cover);
        // a catalog that could not be asked gives nothing, without throwing, and is not remembered as "no such album": the next view asks again
        var who = "Somebody " + Guid.NewGuid().ToString("N")[..6];
        var dead = new Script((_, _) => throw new HttpRequestException("down"));
        Assert.Null(await MediaEndpoints.OnlineAlbum(new HttpClient(dead), who, "Parachutes", "Yellow", default, NoWait));
        var back = new Script((_, _) => Json(album.Replace("Coldplay", who)));
        Assert.Equal("https://e.example/big.jpg", (await MediaEndpoints.OnlineAlbum(new HttpClient(back), who, "Parachutes", "Yellow", default, NoWait))?.Cover);
    }

    static readonly Track Song = new("/m/yellow.flac", "Yellow", "Coldplay", "Parachutes", "Coldplay", 5, 1, 266_000, 2000, null, 0, TrackSource.Nas);
    const string Lrc = """{"syncedLyrics":"[00:01.00]Look at the stars","plainLyrics":"Look at the stars"}""";

    [Fact]
    public async Task Lyrics_come_through_a_busy_moment_at_LRCLIB()
    {
        var h = new Script((r, n) => n == 1 ? Status(HttpStatusCode.ServiceUnavailable) : Json(Lrc));
        var l = await new LyricsService(new HttpClient(h), new JellyfinClient(new HttpClient(), "dev"), dir) { Retry = NoWait }.GetAsync(Song, null);
        Assert.Equal("Look at the stars", l!.Lines[0].Text);
    }

    [Fact]
    public async Task Lyrics_that_could_not_be_asked_for_are_not_remembered_as_none()
    {
        // before: LRCLIB down for a minute meant "no lyrics" for that song for a week
        var down = true;
        var h = new Script((r, n) => down ? Status(HttpStatusCode.ServiceUnavailable) : Json(Lrc));
        var svc = new LyricsService(new HttpClient(h), new JellyfinClient(new HttpClient(), "dev"), dir) { Retry = NoWait };
        Assert.Null(await svc.GetAsync(Song, null));
        down = false;
        Assert.NotNull(await svc.GetAsync(Song, null));
        // a real "not found" is still remembered
        var none = new Script((r, n) => r.RequestUri!.AbsolutePath.EndsWith("/search") ? Json("[]") : Status(HttpStatusCode.NotFound));
        var other = Song with { Path = "/m/other.flac", Title = "Nothing Here" };
        var svc2 = new LyricsService(new HttpClient(none), new JellyfinClient(new HttpClient(), "dev"), dir) { Retry = NoWait };
        Assert.Null(await svc2.GetAsync(other, null));
        var asked = none.Calls;
        Assert.Null(await svc2.GetAsync(other, null));
        Assert.Equal(asked, none.Calls);
    }

    // Lovegame on the owner's Jellyfin had a .lrc running 0.5-1.5 s late with one line 10 s early; LRCLIB's copy for that exact length was right (5 Nov)
    static readonly Track JfSong = Song with { Path = "http://192.0.2.10:8096/Audio/0123456789abcdef0123456789abcdef/universal", Source = TrackSource.Jellyfin, JellyfinId = "0123456789abcdef0123456789abcdef" };
    static readonly JellyfinAccount Jf = new("http://192.0.2.10:8096", "u1", "ash", "T");
    const string JfLrc = """{"Lyrics":[{"Start":50300000,"Text":"Look at the stars"}]}""";

    [Fact]
    public async Task LRCLIBs_exact_match_for_the_songs_length_wins_over_jellyfins_own_file()
    {
        var h = new Script((r, n) => r.RequestUri!.AbsolutePath.EndsWith("/Lyrics") ? Json(JfLrc) : r.RequestUri.AbsolutePath.EndsWith("/api/get") ? Json(Lrc) : Status(HttpStatusCode.NotFound));
        var l = await new LyricsService(new HttpClient(h), new JellyfinClient(new HttpClient(h), "dev"), dir) { Retry = NoWait }.GetAsync(JfSong, Jf);
        Assert.Equal(("LRCLIB", 1000L), (l!.Source, l.Lines[0].TimeMs));
    }

    [Fact]
    public async Task Jellyfins_file_is_used_when_LRCLIB_has_no_exact_match_and_old_jellyfin_answers_are_looked_at_again()
    {
        var lrclibUp = false;
        var h = new Script((r, n) => r.RequestUri!.AbsolutePath.EndsWith("/Lyrics") ? Json(JfLrc) : lrclibUp && r.RequestUri.AbsolutePath.EndsWith("/api/get") ? Json(Lrc) : r.RequestUri.AbsolutePath.EndsWith("/search") ? Json("[]") : Status(HttpStatusCode.NotFound));
        var l = await new LyricsService(new HttpClient(h), new JellyfinClient(new HttpClient(h), "dev"), dir) { Retry = NoWait }.GetAsync(JfSong, Jf);
        Assert.Equal(("Jellyfin", 5030L), (l!.Source, l.Lines[0].TimeMs));
        // a song saved under the old order (a bare "Jellyfin" first line) is asked again once; one saved under the new order is not
        var file = Directory.GetFiles(dir, "*.lrc").Single();
        File.WriteAllText(file, "Jellyfin\n[00:05.03]Look at the stars");
        lrclibUp = true;
        var again = await new LyricsService(new HttpClient(h), new JellyfinClient(new HttpClient(h), "dev"), dir) { Retry = NoWait }.GetAsync(JfSong, Jf);
        Assert.Equal("LRCLIB", again!.Source);
    }

    sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }
    sealed class NoLog : ILogger<Notifier> { public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null; public bool IsEnabled(LogLevel l) => false; public void Log<TState>(LogLevel l, EventId id, TState s, Exception? e, Func<TState, Exception?, string> f) { } }

    [Fact]
    public async Task A_notification_turned_away_unread_is_sent_again_but_never_twice_after_a_server_error()
    {
        var busy = new Script((r, n) => n == 1 ? Status(HttpStatusCode.ServiceUnavailable) : Status(HttpStatusCode.OK));
        var n1 = new Notifier(new DataPaths(dir), new Factory(busy), DataProtectionProvider.Create(new DirectoryInfo(dir)), new ConfigurationBuilder().Build(), new NoLog(), new ActivityLog());
        n1.Save(new NotifyConfig { Url = "https://ntfy.example", Topic = "t", Enabled = true }, null);
        Assert.True((await n1.SendAsync("x", "y")).Ok);
        Assert.Equal(2, busy.Calls);
        var broken = new Script((r, n) => Status(HttpStatusCode.InternalServerError));
        var n2 = new Notifier(new DataPaths(dir), new Factory(broken), DataProtectionProvider.Create(new DirectoryInfo(dir)), new ConfigurationBuilder().Build(), new NoLog(), new ActivityLog());
        n2.Save(new NotifyConfig { Url = "https://ntfy.example", Topic = "t", Enabled = true }, null);
        Assert.False((await n2.SendAsync("x", "y")).Ok);
        Assert.Equal(1, broken.Calls);   // a 500 may have been delivered: a duplicate alert is worse than a missing one
    }

    [Fact]
    public async Task The_Internet_Archive_finder_comes_through_a_busy_moment()
    {
        // archive.org answers 502 and 503 often; one hiccup no longer means "not found there" and a slower source
        var h = new Script((r, n) =>
            n == 1 ? Status(HttpStatusCode.ServiceUnavailable)
            : r.RequestUri!.AbsolutePath.StartsWith("/advancedsearch") ? Json("""{"response":{"docs":[{"identifier":"gd1977"}]}}""")
            : Json("""{"result":[{"name":"d1t01.flac","format":"Flac","title":"Scarlet Begonias","length":"610.5"}]}"""));
        var hit = await OpenSources.ArchiveOrg.FindAsync(new HttpClient(h), "Grateful Dead", "Scarlet Begonias", 0, default);
        Assert.Equal("https://archive.org/download/gd1977/d1t01.flac", hit?.Url);
    }

    [Fact]
    public async Task The_Alexa_signing_certificate_comes_through_a_dropped_connection()
    {
        // a failed certificate fetch turns every Alexa request away for 30 seconds: one more try inside the 5 second budget first
        var h = new Script((r, n) => n == 1 ? throw new HttpRequestException("reset") : Json("PEM"));
        var bytes = await new HttpAlexaCertSource(new HttpClient(h)) { Retry = NoWait }.FetchAsync(new Uri("https://s3.amazonaws.com/echo.api/echo-api-cert.pem"), default);
        Assert.Equal("PEM", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Equal(2, h.Calls);
    }
}
