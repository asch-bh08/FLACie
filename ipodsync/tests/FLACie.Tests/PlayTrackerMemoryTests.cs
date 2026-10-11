using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Memory, clock and thread edges of the "music played" tracker (workstream E, beta 49 test run).</summary>
public class PlayTrackerMemoryTests
{
    DateTime now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    readonly List<(string Title, string Message)> sent = [];
    bool on = true;
    PlayTracker Make() => new((t, m) => { lock (sent) sent.Add((t, m)); }, () => now, () => on);

    [Fact]
    public void A_source_decision_whose_song_was_never_loaded_is_forgotten_after_an_hour()
    {
        // /stream and /api/played "progress" call Served without a Begin: those entries have no song to be retired with
        var p = Make();
        for (var i = 0; i < 300; i++) p.Served("u1", "phone:k" + i, "NAS");
        Assert.Equal(300, p.Tracked.Sources);
        now = now.AddMinutes(61); p.Sweep();
        Assert.Equal((0, 0), p.Tracked);
    }

    [Fact]
    public void A_client_sending_endless_new_keys_cannot_grow_the_tracker_without_limit()
    {
        var p = Make();
        for (var i = 0; i < 30_000; i++)
        {
            now = now.AddMilliseconds(1);
            p.Served("u1", "phone:k" + i, "NAS");
            p.Begin("u1", "Dad", "phone:k" + i, "T", "A", "phone");
        }
        var (songs, sources) = p.Tracked;
        Assert.InRange(songs, 1, 6000);
        Assert.InRange(sources, 1, 6000);
    }

    [Fact]
    public void Nothing_is_tracked_while_plays_are_switched_off()
    {
        on = false;
        var p = Make();
        p.Served("u1", "k", "NAS"); p.Begin("u1", "Dad", "k", "T", "A", "d"); p.Tick("u1", "k", true);
        Assert.Equal((0, 0), p.Tracked);
        Assert.Empty(sent);
    }

    [Fact]
    public void A_clock_that_jumps_back_adds_no_listening_and_nothing_throws()
    {
        var p = Make();
        p.Served("u1", "k", "NAS"); p.Begin("u1", "Dad", "k", "T", "A", "d");
        p.Tick("u1", "k", true);
        now = now.AddSeconds(10); p.Tick("u1", "k", true, 15);
        now = now.AddHours(-3); p.Tick("u1", "k", true, 15);      // the clock was set back: counts as nothing
        now = now.AddSeconds(5); p.Tick("u1", "k", true, 15);
        Assert.Empty(sent);                                      // 10 s + 0 + 5 s is still short of 20 s
        now = now.AddSeconds(6); p.Tick("u1", "k", true, 15);
        Assert.Single(sent);                                     // 21 s of real listening
        p.Sweep();
        now = now.AddMinutes(11); p.Sweep();
        Assert.Equal(2, sent.Count);                             // and the summary still comes
    }

    [Fact]
    public void Many_threads_reporting_at_once_do_not_break_the_tracker()
    {
        var p = Make();
        var errors = new List<Exception>();
        Parallel.For(0, 8, w =>
        {
            try
            {
                for (var i = 0; i < 2000; i++)
                {
                    var key = $"w{w}-{i % 50}";
                    p.Served("u" + w, key, "NAS"); p.Begin("u" + w, "User" + w, key, "T" + i, "A", "d");
                    p.Tick("u" + w, key, true, 30); p.Tick("u" + w, key, false, 30);
                    if (i % 100 == 0) p.Sweep();
                }
            }
            catch (Exception e) { lock (errors) errors.Add(e); }
        });
        Assert.Empty(errors);
    }

    [Fact]
    public void The_download_digest_survives_threads_and_a_clock_that_goes_back()
    {
        var sentD = new List<(string, string)>();
        var d = new DownloadDigest((t, m) => { lock (sentD) sentD.Add((t, m)); }, () => now);
        var errors = new List<Exception>();
        Parallel.For(0, 8, w =>
        {
            try { for (var i = 0; i < 500; i++) { d.Done("u" + w, "U" + w, $"S{i} - X"); if (i % 50 == 0) d.Sweep(); } }
            catch (Exception e) { lock (errors) errors.Add(e); }
        });
        Assert.Empty(errors);
        now = now.AddHours(-2); d.Sweep();        // back in time: nothing breaks, nothing is lost
        now = now.AddHours(3); d.Sweep();
        Assert.True(sentD.Count > 0);
    }

    [Fact]
    public void A_burst_of_thousands_of_downloads_stays_small_and_is_still_summarised()
    {
        var sentD = new List<(string, string)>();
        var d = new DownloadDigest((t, m) => sentD.Add((t, m)), () => now);
        for (var i = 0; i < 20_000; i++) d.Done("u1", "Dad", $"Song {i} - Artist");
        now = now.AddMinutes(2); d.Sweep();
        Assert.Equal(4, sentD.Count);                       // 3 live + one summary
        Assert.Contains("19997 more", sentD[3].Item1);
    }
}
