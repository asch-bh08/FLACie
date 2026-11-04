using System.Net;
using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>The shared path of "Paste a Spotify or Deezer link" (Import page and POST /api/import/link), with Spotify and Deezer stubbed: nothing leaves the machine.</summary>
public class LinkImportTests
{
    sealed class Stub(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Asked.Add(r.RequestUri!.ToString()); return Task.FromResult(answer(r.RequestUri.ToString())); }
    }
    sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }

    const string SpotifyPage = """<html><script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"state":{"data":{"entity":{"type":"playlist","name":"Road","trackList":[{"title":"Loser","subtitle":"Tame Impala"}]}}}}}}</script></html>""";
    const string DeezerJson = """{"title":"Top","tracks":{"data":[{"title":"Blinding Lights","artist":{"name":"The Weeknd"},"album":{"title":"After Hours"}}],"next":null}}""";

    static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    static async Task<(ImportJob? Job, ImportedPlaylist? Started, Exception? Error, Stub Net)> Run(string? url, Func<string, HttpResponseMessage> answer)
    {
        var net = new Stub(answer);
        ImportedPlaylist? started = null;
        try
        {
            var job = await LinkImport.StartAsync(new PlaylistLinkReader(new Factory(net)), l => { started = l; return new ImportJob { File = l.Name }; }, url);
            return (job, started, null, net);
        }
        catch (Exception e) { return (null, started, e, net); }
    }

    [Fact]
    public async Task A_spotify_link_starts_a_job_named_after_the_playlist()
    {
        var r = await Run("  https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=x ", _ => Ok(SpotifyPage));
        Assert.Null(r.Error);
        Assert.Equal("Road", r.Job!.File);
        Assert.Equal([new ImportedTrack("Tame Impala", "Loser")], r.Started!.Tracks);
        Assert.Equal(["https://open.spotify.com/embed/playlist/37i9dQZF1DXcBWIGoYBM5M"], r.Net.Asked);
    }

    [Fact]
    public async Task A_deezer_link_starts_a_job()
    {
        var r = await Run("https://www.deezer.com/en/playlist/3155776842", _ => Ok(DeezerJson));
        Assert.Null(r.Error);
        Assert.Equal("Top", r.Job!.File);
        Assert.Single(r.Started!.Tracks);
    }

    [Theory]
    [InlineData("https://example.com/not-a-playlist")]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_junk_link_is_a_400_with_the_pages_words_and_asks_nobody(string? url)
    {
        var r = await Run(url, _ => Ok(""));
        Assert.Empty(r.Net.Asked);
        Assert.Null(r.Started);
        var (status, msg) = LinkImport.Describe(r.Error!);
        Assert.Equal(400, status);
        Assert.Equal("That isn't a Spotify or Deezer playlist or album link. Copy it with Share > Copy link.", msg);
    }

    [Fact]
    public async Task A_private_or_missing_playlist_is_a_400()
    {
        var r = await Run("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", _ => Ok("<html>sign in</html>"));
        Assert.Equal((400, "No songs found at that link. Is the playlist public?"), LinkImport.Describe(r.Error!));
        var d = await Run("https://www.deezer.com/playlist/12345", _ => Ok("""{"error":{"type":"DataException"}}"""));
        Assert.Equal(400, LinkImport.Describe(d.Error!).Status);
    }

    [Fact]
    public async Task An_unreachable_service_says_which_one()
    {
        var r = await Run("https://www.deezer.com/playlist/12345", _ => throw new HttpRequestException("down"));
        Assert.Equal((400, "Couldn't reach Deezer. Try again in a minute."), LinkImport.Describe(r.Error!));
    }

    [Fact]
    public void Anything_else_is_a_502_in_plain_words() =>
        Assert.Equal((502, "Something went wrong. Try again."), LinkImport.Describe(new InvalidOperationException("secret detail")));

    [Fact]
    public void A_person_who_may_not_import_is_a_403() =>
        Assert.Equal((403, new ImportNotAllowedException().Message), LinkImport.Describe(new ImportNotAllowedException()));
}
