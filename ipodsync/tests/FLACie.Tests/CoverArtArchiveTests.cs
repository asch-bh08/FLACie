using System.Net;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class CoverArtArchiveTests
{
    const string Json = """
    {"images":[
      {"id":2,"image":"http://coverartarchive.org/release/r/2.jpg","front":false,"back":true,"types":["Back"],"thumbnails":{"250":"http://coverartarchive.org/release/r/2-250.jpg","1200":"http://coverartarchive.org/release/r/2-1200.jpg"}},
      {"id":1,"image":"http://coverartarchive.org/release/r/1.jpg","front":true,"back":false,"types":["Front"],"thumbnails":{"small":"http://coverartarchive.org/release/r/1-250.jpg","large":"http://coverartarchive.org/release/r/1-500.jpg"}},
      {"image":"http://x/no-id.jpg"}
    ]}
    """;

    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Seen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Seen.Add(r.RequestUri!.ToString()); return Task.FromResult(answer(r)); }
    }

    static CoverArtArchiveClient Client(Fake f) => new(new HttpClient(f), "FLACie/test ( x )", new MusicBrainzGate(TimeSpan.Zero));

    [Fact] public void ParseOrdersFrontFirstUpgradesToHttpsAndSkipsBrokenOnes()
    {
        var l = CoverArtArchiveClient.Parse(Json);
        Assert.Equal(2, l.Count);
        Assert.Equal("1", l[0].Id); Assert.True(l[0].Front); Assert.Equal("https://coverartarchive.org/release/r/1-250.jpg", l[0].Thumb); Assert.Equal("https://coverartarchive.org/release/r/1-500.jpg", l[0].Full);
        Assert.Equal("2", l[1].Id); Assert.True(l[1].Back); Assert.Equal("https://coverartarchive.org/release/r/2-1200.jpg", l[1].Full);
        Assert.Empty(CoverArtArchiveClient.Parse("{}"));
    }

    [Fact] public void PicturesAreRecognisedByTheirFirstBytes()
    {
        Assert.Equal("image/png", CoverArtArchiveClient.Sniff(Png)); Assert.Equal("image/jpeg", CoverArtArchiveClient.Sniff([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/webp", CoverArtArchiveClient.Sniff("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));
        Assert.Null(CoverArtArchiveClient.Sniff("<html>not a picture</html>"u8.ToArray())); Assert.Null(CoverArtArchiveClient.Sniff([]));
    }

    [Fact] public async Task AReleaseWithNoPicturesFallsBackToItsReleaseGroup()
    {
        var f = new Fake(r => r.RequestUri!.AbsolutePath.StartsWith("/release/") ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) });
        var l = await Client(f).ListAsync("rel", "rg");
        Assert.Equal(2, l.Count); Assert.Equal(["https://coverartarchive.org/release/rel", "https://coverartarchive.org/release-group/rg"], f.Seen);
    }

    [Fact] public async Task NoPicturesAnywhereIsAnEmptyListAndOtherErrorsAreErrors()
    {
        var none = new Fake(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Empty(await Client(none).ListAsync("rel", "rg")); Assert.Equal(2, none.Seen.Count);
        Assert.Empty(await Client(new Fake(_ => new HttpResponseMessage(HttpStatusCode.NotFound))).ListAsync(null, null));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(new Fake(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))).ListAsync("rel", null));
    }

    [Fact] public async Task OnlyRealPicturesAreFetched()
    {
        Assert.Equal(Png, await Client(new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) })).FetchAsync("https://coverartarchive.org/x.png"));
        Assert.Null(await Client(new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") })).FetchAsync("https://coverartarchive.org/x"));
        Assert.Null(await Client(new Fake(_ => new HttpResponseMessage(HttpStatusCode.NotFound))).FetchAsync("https://coverartarchive.org/x"));
        var f = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });
        Assert.Null(await Client(f).FetchAsync("file:///etc/passwd")); Assert.Null(await Client(f).FetchAsync("ftp://x/y")); Assert.Empty(f.Seen);
        Assert.NotNull(await Client(f).FetchAsync("http://coverartarchive.org/x.png"));   // plain http is upgraded to https
        Assert.StartsWith("https://", f.Seen[0]);
    }

    static string Temp() => Path.Combine(Path.GetTempPath(), "ov-" + Guid.NewGuid().ToString("N"));

    [Fact] public void AReplacementGetsANewKeyThatMapsAndCanBeUndone()
    {
        var dir = Temp();
        try
        {
            var o = new CoverOverrides(dir);
            Assert.Null(o.Replacement("jfabc~tag")); Assert.False(o.Has("jfabc~tag"));
            var k1 = o.Set("jfabc~tag", Png, "Cover Art Archive");
            Assert.StartsWith("ov", k1); Assert.Equal(k1, o.Replacement("jfabc~tag")); Assert.True(o.Has("jfabc~tag"));
            var found = o.Find(k1)!.Value; Assert.Equal("image/png", found.Mime); Assert.Equal(Png, File.ReadAllBytes(found.File));
            // choosing again is a new key (browsers keep the old picture for good), and the old file is gone
            var k2 = o.Set("jfabc~tag", Png, "again"); Assert.NotEqual(k1, k2); Assert.Null(o.Find(k1)); Assert.NotNull(o.Find(k2));
            // it survives a restart
            Assert.Equal(k2, new CoverOverrides(dir).Replacement("jfabc~tag"));
            o.Clear("jfabc~tag"); Assert.Null(o.Replacement("jfabc~tag")); Assert.Null(o.Find(k2));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact] public void NotAPictureIsRefusedAndOddKeysAreNeverFiles()
    {
        var dir = Temp();
        try
        {
            var o = new CoverOverrides(dir);
            Assert.Null(o.Set("k", "<html>"u8.ToArray(), "x")); Assert.False(o.Has("k"));
            Assert.Null(o.Find("ov../../etc/passwd")); Assert.Null(o.Find("ov" + new string('a', 60))); Assert.Null(o.Find("jfabc")); Assert.Null(o.Find("ovdeadbeef"));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact] public void MapShowsTheReplacementWhereThereIsOneAndTheOriginalElse()
    {
        var dir = Temp(); var before = CoverOverrides.Current;
        try
        {
            Assert.Null(CoverOverrides.Map(null));
            CoverOverrides.Current = null; Assert.Equal("jfx", CoverOverrides.Map("jfx"));
            CoverOverrides.Current = new CoverOverrides(dir);
            Assert.Equal("jfx", CoverOverrides.Map("jfx"));
            var k = CoverOverrides.Current.Set("jfx", Png, "t"); Assert.Equal(k, CoverOverrides.Map("jfx")); Assert.Equal("jfy", CoverOverrides.Map("jfy"));
        }
        finally { CoverOverrides.Current = before; try { Directory.Delete(dir, true); } catch (Exception) { } }
    }
}
