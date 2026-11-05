using System.Net;
using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F25.1 on the server: YouTube and SoundCloud through flacie-ytdl (via the file mover), Apple Music from its page. All answers are fixtures.</summary>
public class PlaylistLinksReaderTests
{
    sealed class Net : HttpMessageHandler
    {
        public List<(string Url, string Body)> Asked = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var u = r.RequestUri!.ToString(); var body = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            Asked.Add((u, body));
            var text = u == "http://mover.test/playlist" ? """{"name": "Gym Bangers", "tracks": [{"title": "The Prodigy - Breathe (Official Video)", "artist": "The Prodigy", "durationMs": 335000}]}"""
                : u.StartsWith("https://music.apple.com/") ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAudio", "apple", "todays-hits-real.html"))
                : "";
            return new HttpResponseMessage(text.Length > 0 ? HttpStatusCode.OK : HttpStatusCode.NotFound) { Content = new StringContent(text) };
        }
    }

    static DownloadServices Mover => DownloadServices.From(System.Text.Json.Nodes.JsonNode.Parse("""{"services": {"filemover": {"url": "http://mover.test", "key": "k"}}}""")!.AsObject());

    [Fact]
    public async Task A_youtube_music_playlist_is_read_through_the_yt_dlp_service_and_cleaned()
    {
        var net = new Net();
        var p = await new PlaylistLinkReader(new DjVoiceTests.Factory(net)).ReadAsync("https://music.youtube.com/playlist?list=RDCLAK5uy_kmPRjHDECIcuVwnKsx2Ng7fyNgFKWNJFs", default, Mover);
        Assert.Equal("Gym Bangers", p.Name);
        Assert.Equal(new ImportedTrack("The Prodigy", "Breathe"), Assert.Single(p.Tracks));
        Assert.Contains("https://www.youtube.com/playlist?list=RDCLAK5uy_kmPRjHDECIcuVwnKsx2Ng7fyNgFKWNJFs", net.Asked.Single().Body);
    }

    [Fact]
    public async Task An_apple_music_link_reads_its_public_page()
    {
        var p = await new PlaylistLinkReader(new DjVoiceTests.Factory(new Net())).ReadAsync("https://music.apple.com/us/playlist/todays-hits/pl.f4d106fed2bd41149aaacabb233eb5eb");
        Assert.Equal(50, p.Tracks.Count);
    }

    [Fact]
    public async Task Without_download_services_or_for_another_host_it_says_so()
    {
        var reader = new PlaylistLinkReader(new DjVoiceTests.Factory(new Net()));
        var e = await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync("https://soundcloud.com/the-prodigy/sets/x"));
        Assert.Contains("file mover", e.Message);
        var other = await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync("https://tidal.com/browse/playlist/1"));
        Assert.Equal(PlaylistLinks.Refusal, other.Message);
    }
}
