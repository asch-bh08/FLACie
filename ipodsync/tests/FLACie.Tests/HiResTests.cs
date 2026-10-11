using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

public class HiResTests
{
    public static IEnumerable<object[]> Cases() => new[]
    {
        new object[] { "flac", 16, 44100, 1700, false }, new object[] { "flac", 16, 44100, 2000, false },
        new object[] { "flac", 24, 44100, 1000, true }, new object[] { "flac", 24, 48000, 1000, true }, new object[] { "flac", 16, 48000, 900, true },
        new object[] { "flac", 16, 96000, 1500, true }, new object[] { "flac", 24, 96000, 2800, true }, new object[] { "flac", 24, 192000, 5000, true },
        new object[] { "mp3", 0, 44100, 320, false }, new object[] { "mp3", 0, 48000, 320, false }, new object[] { "aac", 0, 48000, 256, false },
        new object[] { "alac", 24, 48000, 1500, true }, new object[] { "wav", 16, 44100, 1411, false }, new object[] { "flac", 0, 0, 1900, false },
    };

    [Theory, MemberData(nameof(Cases))]
    public void The_rule_depends_only_on_codec_depth_and_rate(string codec, int depth, int rate, int kbps, bool expected)
    {
        var mi = new MediaInfo(codec, codec, kbps, rate, depth, 2, 0, 0, "", "Jellyfin");
        Assert.Equal(expected, mi.HiRes);
        Assert.Equal(expected, new AudioFacts(codec, rate, depth, kbps, 2).HiRes);
        Assert.Equal(expected, MediaInfo.IsHiRes(mi.Lossless, depth, rate));
    }

    static Track Song(string path, string file) => new(path, "T", "A", "Al", "A", 1, 1, 1000, 2000, null, 0, TrackSource.Jellyfin, file, "0123456789abcdef0123456789abcdef");

    static FormatIndex Index() => new(new DataPaths(Directory.CreateTempSubdirectory().FullName));

    [Fact]
    public void A_Hi_Res_file_name_with_real_CD_facts_is_not_Hi_Res()
    {
        var idx = Index(); var t = Song("p1", "/m/A - T [Hi-Res].flac");
        idx.Set(t, new MediaInfo("flac", "flac", 1700, 44100, 16, 2, 0, 0, "", "Jellyfin"));
        Assert.Equal("FLAC", InfoService.QuickFormat(t));
    }

    [Fact]
    public void A_Hi_Res_file_name_with_unknown_facts_shows_no_Hi_Res()
    {
        var idx = Index(); var t = Song("p2", "/m/A - T [Hi-Res].flac");
        Assert.Equal("FLAC", InfoService.QuickFormat(t));
        Assert.Null(idx.Get(t));
    }

    [Fact]
    public void Real_hi_res_facts_badge_hi_res()
    {
        var idx = Index(); var t = Song("p3", "/m/plain.flac");
        idx.Set(t, new MediaInfo("flac", "flac", 2800, 96000, 24, 2, 0, 0, "", "Jellyfin"));
        Assert.Equal("HI-RES", InfoService.QuickFormat(t));
    }

    [Fact]
    public void A_partial_read_never_replaces_a_complete_fact()
    {
        var idx = Index(); var t = Song("p4", "/m/x.flac");
        idx.Set(t, new MediaInfo("flac", "flac", 2800, 96000, 24, 2, 0, 0, "", "Jellyfin"));
        idx.Set(t, new MediaInfo("flac", "flac", 900, 0, 0, 2, 0, 0, "", "Jellyfin"));
        Assert.Equal(96000, idx.Get(t)!.Rate);
    }

    [Fact]
    public async Task InfoService_does_not_cache_a_failed_or_partial_read_and_retries()
    {
        var idx = Index();
        var svc = new InfoService(new JellyfinClient(new HttpClient(), "dev"), null!, new DownloadLog(new DataPaths(Directory.CreateTempSubdirectory().FullName))) { RetryDelayMs = 1 };
        var s = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u" };
        var t = Song("p5", "/m/x.flac");
        int calls = 0, failFor = 3;
        svc.Reader = (_, _) =>
        {
            calls++;
            return Task.FromResult(calls <= failFor ? new MediaInfo("flac", "flac", 0, 0, 0, 0, 0, 0, "", "Jellyfin") : new MediaInfo("flac", "flac", 2800, 96000, 24, 2, 0, 0, "", "Jellyfin"));
        };
        var first = await svc.GetAsync(s, t);       // three attempts, all partial: not cached, not indexed
        Assert.False(first.HiRes); Assert.Null(idx.Get(t)); Assert.Equal(3, calls);
        var second = await svc.GetAsync(s, t);      // asks the source again and now gets the real thing
        Assert.True(second.HiRes); Assert.Equal(4, calls);
        Assert.True(idx.Get(t)!.HiRes);
        await svc.GetAsync(s, t); Assert.Equal(4, calls);   // now cached
    }
}