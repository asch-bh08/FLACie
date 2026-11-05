using System.Net;
using System.Text.Json.Nodes;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F18.1: NetEase's synced lyrics after LRCLIB, with length and artist checks. No test reaches the real services.</summary>
public sealed class LyricsNeteaseTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "lyr-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    static readonly Track Song = new("/m/1", "Price Tag", "Jessie J", "Who You Are", "Jessie J", 3, 1, 223_000, 2011, null, 1, TrackSource.Cloud);

    static string Search(string artist, long ms) =>
        new JsonObject { ["result"] = new JsonObject { ["songs"] = new JsonArray(new JsonObject { ["id"] = 77, ["name"] = "Price Tag", ["duration"] = ms, ["artists"] = new JsonArray(new JsonObject { ["name"] = artist }) }) } }.ToJsonString();

    const string Lrc = "{\"lrc\": {\"lyric\": \"[00:12.00]Seems like everybody's got a price\\n[00:15.50]I wonder how they sleep at night\"}}";

    sealed class Fake(Func<Uri, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<string> Asked = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Asked.Add(r.RequestUri!.Host);
            var (code, body) = answer(r.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    LyricsService Service(Fake f) => new(new HttpClient(f), new JellyfinClient(new HttpClient(f), "test"), dir) { Retry = new RetryPolicy { Attempts = 1 } };

    static (HttpStatusCode, string) LrclibPlain(Uri u) =>
        u.AbsolutePath.EndsWith("/get") ? (HttpStatusCode.OK, "{\"plainLyrics\": \"Seems like everybody's got a price\\nI wonder how they sleep at night\", \"duration\": 223}") : (HttpStatusCode.NotFound, "");

    [Fact]
    public async Task Plain_lyrics_from_LRCLIB_give_way_to_NetEase_synced_ones()
    {
        var f = new Fake(u => u.Host == "lrclib.net" ? LrclibPlain(u) : u.AbsolutePath.Contains("search") ? (HttpStatusCode.OK, Search("Jessie J", 224_500)) : (HttpStatusCode.OK, Lrc));
        var l = await Service(f).GetAsync(Song, null);
        Assert.NotNull(l);
        Assert.True(l!.Synced);
        Assert.Equal("NetEase", l.Source);
        Assert.Equal(12_000, l.Lines[0].TimeMs);
    }

    [Theory]
    [InlineData("Jessie J", 240_000)]   // 17 s longer: another recording
    [InlineData("Some Cover Band", 223_000)]
    public async Task A_NetEase_result_with_another_length_or_artist_is_ignored(string artist, long ms)
    {
        var f = new Fake(u => u.Host == "lrclib.net" ? LrclibPlain(u) : u.AbsolutePath.Contains("search") ? (HttpStatusCode.OK, Search(artist, ms)) : (HttpStatusCode.OK, Lrc));
        var l = await Service(f).GetAsync(Song, null);
        Assert.False(l!.Synced);
        Assert.Equal("LRCLIB", l.Source);
    }

    [Fact]
    public void The_closest_match_within_8_seconds_is_picked()
    {
        var o = JsonNode.Parse(Search("Jessie J, B.o.B", 230_000))!;
        Assert.Equal(77, LyricsNetease.Pick(o, Song));
        Assert.Null(LyricsNetease.Pick(JsonNode.Parse(Search("Jessie J", 232_000))!, Song));
    }
}
