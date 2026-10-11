using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FLACie.Tests;

/// <summary>The pieces that keep this server from overloading the owner's Jellyfin: back-off, the bulk read, the token cache, single-flight covers, the pause while playing, the call counters.</summary>
public class LoadTests
{
    [Theory]
    [InlineData(1, 1)] [InlineData(2, 6)] [InlineData(3, 24)] [InlineData(4, 168)] [InlineData(5, 168)] [InlineData(50, 168)] [InlineData(0, 1)]
    public void Backoff_schedule_is_1h_6h_24h_7d_and_stays_at_7d(int failures, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours), ProbeBackoff.Delay(failures));

    static Track Song(string path, string id = "0123456789abcdef0123456789abcdef") => new(path, "T", "A", "Al", "A", 1, 1, 1000, 2000, null, 0, TrackSource.Jellyfin, "/m/x.flac", id);
    static FormatIndex Index(string? dir = null) => new(new DataPaths(dir ?? Directory.CreateTempSubdirectory().FullName));

    [Fact]
    public void A_missed_song_is_not_due_until_its_back_off_passes_and_each_miss_waits_longer()
    {
        var idx = Index(); var t = Song("p"); var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(idx.Due(t, t0));
        idx.MarkMiss(t, t0);
        Assert.False(idx.Due(t, t0.AddMinutes(5)));      // the old 5-minute pass must not retry it
        Assert.False(idx.Due(t, t0.AddMinutes(59)));
        Assert.True(idx.Due(t, t0.AddMinutes(61)));
        idx.MarkMiss(t, t0.AddHours(2));
        Assert.False(idx.Due(t, t0.AddHours(7)));
        Assert.True(idx.Due(t, t0.AddHours(8.1)));
        Assert.Equal(2, idx.MissOf(t)!.Fails);
    }

    [Fact]
    public void The_negative_cache_survives_a_restart_and_a_real_read_clears_it()
    {
        var dir = Directory.CreateTempSubdirectory().FullName; var t = Song("p"); var now = DateTime.UtcNow;
        var a = Index(dir); a.MarkMiss(t, now); a.Flush();
        var b = Index(dir);
        Assert.False(b.Due(t, now.AddMinutes(10)));
        b.Set(t, new MediaInfo("flac", "flac", 900, 44100, 16, 2, 0, 0, "", "Jellyfin"));
        Assert.True(b.Due(t, now.AddMinutes(10)));
        Assert.Null(b.MissOf(t));
    }

    // the shape Jellyfin returns for /Users/{id}/Items?Fields=MediaSources,MediaStreams (trimmed to the fields that matter)
    const string Page = """
    {"Items":[
      {"Id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1","Name":"One","RunTimeTicks":1800000000,"MediaSources":[{"Container":"flac","Size":31000000,"Path":"/music/One.flac","Bitrate":2800000,"RunTimeTicks":1800000000,
        "MediaStreams":[{"Type":"Audio","Codec":"flac","Channels":2,"BitRate":2800000,"SampleRate":96000,"BitDepth":24},{"Type":"Video","Codec":"mjpeg"}]}]},
      {"Id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa2","Name":"Two","MediaSources":[{"Container":"mp3","Size":8000000,"Path":"/music/Two.mp3","Bitrate":320000,
        "MediaStreams":[{"Type":"Audio","Codec":"mp3","Channels":2,"BitRate":320000,"SampleRate":44100}]}]},
      {"Id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa3","Name":"No sources"},
      {"Id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa4","Name":"Empty stream","MediaSources":[{"Container":"flac","Path":"/music/Four.flac","MediaStreams":[]}]}
    ],"TotalRecordCount":9000,"StartIndex":0}
    """;

    [Fact]
    public void The_bulk_page_parser_reads_codec_rate_depth_and_bit_rate_per_item()
    {
        var items = JellyfinClient.ParseMediaInfoPage(JsonNode.Parse(Page));
        Assert.Equal(4, items.Count);
        var one = items[0].Info!;
        Assert.Equal(("flac", 96000, 24, 2800, 2), (one.Codec, one.SampleRateHz, one.BitDepth, one.BitrateKbps, one.Channels));
        Assert.True(one.HiRes); Assert.Equal("/music/One.flac", one.Path);
        var two = items[1].Info!;
        Assert.Equal(("mp3", 44100, 0, 320), (two.Codec, two.SampleRateHz, two.BitDepth, two.BitrateKbps));
        Assert.False(two.HiRes);
        Assert.Null(items[2].Info);                 // no media source at all
        Assert.False(items[3].Info!.Known);         // a source with no usable streams is not a read: it goes to the back-off
    }

    [Theory]
    [InlineData("/Users/Me", "users/me")]
    [InlineData("/Users/u1/Items?IncludeItemTypes=Audio&Limit=300", "items")]
    [InlineData("/Users/u1/Items/abc123", "info")]
    [InlineData("/Audio/abc/stream?static=true", "stream")]
    [InlineData("/Items/abc/Images/Primary?maxWidth=500", "image")]
    [InlineData("/DisplayPreferences/ipodplayer?userId=u1", "other")]
    public void Calls_are_counted_by_kind(string path, string kind) => Assert.Equal(kind, JellyfinCallStats.Classify(path));

    [Fact]
    public void The_counters_cover_the_last_minute_only()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var s = new JellyfinCallStats { Clock = () => now };
        s.HitUrl("http://jf:8096/Users/Me"); s.HitUrl("http://jf:8096/Users/Me"); s.HitUrl("http://jf:8096/Users/u/Items?x=1");
        now = now.AddSeconds(30); s.HitUrl("http://jf:8096/Audio/x/stream");
        var m = s.LastMinute();
        Assert.Equal(2, m["users/me"]); Assert.Equal(1, m["items"]); Assert.Equal(1, m["stream"]); Assert.Equal(0, m["image"]);
        now = now.AddSeconds(45);   // the first three are 75 s old
        m = s.LastMinute();
        Assert.Equal(0, m["users/me"]); Assert.Equal(1, m["stream"]);
        Assert.Equal(2, s.Totals()["users/me"]);
    }

    [Fact]
    public void A_token_is_remembered_for_the_window_then_asked_again()
    {
        var now = DateTime.UtcNow; var c = new TokenCache { Clock = () => now };
        Assert.Null(c.Get("http://jf", "tok"));
        c.Put("http://jf", "tok", new TokenCache.Who("u1", "tester"));
        Assert.Equal("u1", c.Get("http://jf", "tok")!.UserId);
        Assert.Null(c.Get("http://other", "tok"));       // another server is another answer
        now = now.AddSeconds(30); Assert.NotNull(c.Get("http://jf", "tok"));
        now = now + TokenCache.Window; Assert.Null(c.Get("http://jf", "tok"));   // a revoked token stops working at the latest here
    }

    [Fact]
    public void Sign_out_or_a_401_drops_a_token_at_once()
    {
        var c = new TokenCache();
        c.Put("http://a", "tok", new("u1", "x")); c.Put("http://b", "tok", new("u1", "x")); c.Put("http://a", "keep", new("u2", "y"));
        c.Invalidate("tok");
        Assert.Null(c.Get("http://a", "tok")); Assert.Null(c.Get("http://b", "tok"));
        Assert.NotNull(c.Get("http://a", "keep"));
    }

    [Fact]
    public async Task Concurrent_lookups_of_one_cover_share_one_fetch()
    {
        var f = new SingleFlight<string>(); var gate = new TaskCompletionSource(); var runs = 0;
        Task<string> Ask(CancellationToken ct = default) => f.RunAsync("jfabc", async () => { Interlocked.Increment(ref runs); await gate.Task; return "bytes"; }, ct);
        var all = Enumerable.Range(0, 20).Select(_ => Ask()).ToList();
        using var cancelled = new CancellationTokenSource();
        var quitter = Ask(cancelled.Token);
        await Task.Delay(50);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => quitter);   // one caller leaving...
        gate.SetResult();
        Assert.All(await Task.WhenAll(all), r => Assert.Equal("bytes", r));      // ...does not cancel the lookup for the rest
        Assert.Equal(1, runs);
        Assert.Equal(1, f.Started);
        Assert.Equal("bytes", await f.RunAsync("jfabc", () => Task.FromResult("bytes")));   // finished lookups are forgotten: a later ask is a new one
        Assert.Equal(2, f.Started);
    }

    [Fact]
    public async Task Background_work_waits_while_a_stream_is_open_and_for_30_seconds_after_a_heartbeat()
    {
        var now = DateTime.UtcNow; var a = new PlaybackActivity { Clock = () => now };
        Assert.False(a.Busy);
        var stream = a.BeginStream();
        Assert.True(a.Busy); Assert.Equal(1, a.OpenStreams);
        var waiting = a.WaitIdleAsync(TimeSpan.FromMilliseconds(5), CancellationToken.None);
        await Task.Delay(40);
        Assert.False(waiting.IsCompleted);                   // paused while the song is being served
        stream.Dispose(); stream.Dispose();                  // disposing twice counts once
        Assert.Equal(0, a.OpenStreams);
        Assert.True(a.Busy);                                 // the stream just ended: a player is still playing
        now = now.AddSeconds(29); Assert.True(a.Busy);
        now = now.AddSeconds(2); Assert.False(a.Busy);       // 31 s without a heartbeat: idle
        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        a.Heartbeat(); Assert.True(a.Busy);
    }

    [Fact]
    public async Task The_probe_does_not_run_inside_the_desktop_app()
    {
        var old = Environment.GetEnvironmentVariable("FLACIE_PARENT_PID");
        Environment.SetEnvironmentVariable("FLACIE_PARENT_PID", "4242");
        try
        {
            var jf = new JellyfinClient(new HttpClient(), "dev");
            var svc = new FormatProbeService(null!, null!, Index(), jf, new PlaybackActivity(), NullLogger<FormatProbeService>.Instance);
            await svc.StartAsync(CancellationToken.None);
            await svc.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));    // returns at once instead of waiting 20 s and walking the library
            Assert.True(svc.ExecuteTask.IsCompletedSuccessfully);
            Assert.Equal(0, jf.Stats.LastMinute().Values.Sum());
        }
        finally { Environment.SetEnvironmentVariable("FLACIE_PARENT_PID", old); }
    }
}
