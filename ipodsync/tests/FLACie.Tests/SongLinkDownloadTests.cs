using System.Net;
using System.Text.Json.Nodes;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Getting one song from a link (owner, 8 Nov): the yt-dlp service (through the file mover) is asked for exactly that address and files it; the
/// download ends with the song it named, playable at once. Never reaches a real server: the file mover here is a fake at example.ts.net.</summary>
public class SongLinkDownloadTests
{
    sealed class Fake(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<(string Url, string Body)> Asked = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            Asked.Add((r.RequestUri!.ToString(), body));
            return answer(r, body);
        }
    }

    static DownloadServices Services => new("", "", "", "https://mover.example.ts.net", "k", "", "", "Music/Downloads");

    static HttpResponseMessage Json(HttpStatusCode code, object o) => new(code) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(o)) };

    [Fact]
    public async Task The_service_is_asked_for_the_clean_link_and_the_song_it_names_is_ready()
    {
        var fake = new Fake((_, _) => Json(HttpStatusCode.OK, new { ok = true, ext = "mp3", artist = "Lady Gaga", title = "LoveGame (Someone Remix)", path = "Music/Downloads/Lady Gaga/Lady Gaga - LoveGame (Someone Remix) (SoundCloud).mp3", durationSec = 214 }));
        var http = new HttpClient(fake);
        var c = new DownloadCoordinator(http, new WebCatalog(http), Services);
        var said = new List<DownloadStatus>();
        await c.DownloadLinkAsync("https://m.soundcloud.com/someproducer/lovegame-remix?si=x", said.Add);

        var (url, body) = Assert.Single(fake.Asked);
        Assert.Equal("https://mover.example.ts.net/ytdl", url);
        var j = JsonNode.Parse(body)!;
        Assert.Equal("https://soundcloud.com/someproducer/lovegame-remix", j["url"]!.GetValue<string>());
        Assert.Equal("Music/Downloads", j["toDir"]!.GetValue<string>());
        var done = said.Last();
        Assert.Equal(DownloadStage.Done, done.Stage);
        var t = Assert.Single(done.NewTracks!);
        Assert.Equal("Lady Gaga", t.Artist); Assert.Equal("LoveGame (Someone Remix)", t.Title); Assert.Equal(214_000, t.DurationMs);
        Assert.Contains(Uri.EscapeDataString("Music/Downloads/Lady Gaga/Lady Gaga - LoveGame (Someone Remix) (SoundCloud).mp3"), t.Path);
    }

    [Fact]
    public async Task A_private_or_missing_song_fails_with_words()
    {
        var http = new HttpClient(new Fake((_, _) => Json(HttpStatusCode.NotFound, new { error = "not found" })));
        var said = new List<DownloadStatus>();
        await new DownloadCoordinator(http, new WebCatalog(http), Services).DownloadLinkAsync("https://soundcloud.com/someproducer/gone", said.Add);
        Assert.Equal(DownloadStage.Failed, said.Last().Stage);
        Assert.Contains("Is it public?", said.Last().Message);
    }

    [Fact]
    public async Task Not_a_song_link_asks_nothing()
    {
        var fake = new Fake((_, _) => Json(HttpStatusCode.OK, new { }));
        var http = new HttpClient(fake);
        var said = new List<DownloadStatus>();
        await new DownloadCoordinator(http, new WebCatalog(http), Services).DownloadLinkAsync("https://soundcloud.com/someproducer/sets/summer", said.Add);
        Assert.Empty(fake.Asked);
        Assert.Equal(DownloadStage.Failed, said.Last().Stage);
    }
}
