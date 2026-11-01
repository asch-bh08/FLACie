using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

// Smart downloads (docs/superpowers/specs/2026-11-01-smart-downloads-design.md): the quickest copy plays first, a Hi-Res copy wins when it is
// only a little slower (7 seconds), and anything that arrived below Hi-Res is upgraded in the background. These are the plain rules behind it.
public class SmartDownloadRulesTests
{
    // ---- the grace for Hi-Res: the owner's own examples (YouTube 10 s, Hi-Res 15 s: Hi-Res; Hi-Res 20 s: YouTube now, upgrade later) ----

    [Fact]
    public void A_HiRes_copy_five_seconds_behind_is_waited_for()
    {
        // 5 MB left at 1.25 MB/s is 4 s, plus the filing: inside 7 s
        Assert.True(SmartDownloads.WaitForHiRes(remainingBytes: 5_000_000, bytesPerSecond: 1_250_000));
    }

    [Fact]
    public void A_HiRes_copy_ten_seconds_behind_is_not_waited_for()
    {
        Assert.False(SmartDownloads.WaitForHiRes(remainingBytes: 10_000_000, bytesPerSecond: 1_000_000));
    }

    [Fact]
    public void A_small_file_on_a_fast_peer_wins_even_when_it_started_late()
    {
        // 40 MB of Hi-Res at 20 MB/s: 2 s to go
        Assert.True(SmartDownloads.WaitForHiRes(40_000_000, 20_000_000));
    }

    [Fact]
    public void A_transfer_with_no_measured_speed_yet_is_never_waited_for()
    {
        Assert.False(SmartDownloads.WaitForHiRes(30_000_000, 0));
        Assert.Null(SmartDownloads.EstimateMs(30_000_000, 0));
    }

    [Fact]
    public void The_estimate_is_the_remaining_bytes_at_the_measured_speed_plus_the_filing_time()
    {
        Assert.Equal(3_000 + SmartDownloads.FilingMs, SmartDownloads.EstimateMs(3_000_000, 1_000_000));
        // a transfer already complete is only the filing away
        Assert.Equal(SmartDownloads.FilingMs, SmartDownloads.EstimateMs(0, 1_000_000));
        Assert.True(SmartDownloads.WaitForHiRes(0, 1_000_000));
    }

    [Fact]
    public void The_grace_is_seven_seconds() => Assert.Equal(7_000, SmartDownloads.GraceMs);

    // ---- quick or Hi-Res for a song further down the queue ----

    [Theory]
    [InlineData(1, DownloadMode.Quick)]      // the next song: wanted soon, the quickest copy
    [InlineData(2, DownloadMode.Quick)]
    [InlineData(3, DownloadMode.HiRes)]      // three or more places down: time for Hi-Res
    [InlineData(10, DownloadMode.HiRes)]
    [InlineData(0, DownloadMode.Quick)]      // the song being skipped to now
    public void The_mode_follows_the_place_in_the_queue(int placesAhead, DownloadMode mode) =>
        Assert.Equal(mode, SmartDownloads.ModeFor(placesAhead));

    // ---- when a song that found no Hi-Res copy is tried again ----

    [Theory]
    [InlineData(1, 1)]           // after the first try: an hour
    [InlineData(2, 6)]           // then six hours
    [InlineData(3, 24)]          // then a day
    [InlineData(4, 24 * 7)]      // then weekly, for ever
    [InlineData(40, 24 * 7)]
    public void Retries_wait_an_hour_six_hours_a_day_then_a_week(int attempts, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours), SmartDownloads.RetryAfter(attempts));

    // ---- what a new copy must be before the old file is retired ----

    [Fact]
    public void A_complete_HiRes_copy_of_the_same_length_passes()
    {
        Assert.Null(SmartDownloads.Verify(expectedBytes: 90_000_000, gotBytes: 90_000_000, oldMs: 200_000, newMs: 201_500, codec: "flac", bitDepth: 24, sampleRate: 96_000));
    }

    [Fact]
    public void A_copy_more_than_three_seconds_longer_or_shorter_fails()
    {
        Assert.NotNull(SmartDownloads.Verify(90_000_000, 90_000_000, 200_000, 204_000, "flac", 24, 96_000));
        Assert.NotNull(SmartDownloads.Verify(90_000_000, 90_000_000, 200_000, 196_000, "flac", 24, 96_000));
        Assert.Null(SmartDownloads.Verify(90_000_000, 90_000_000, 200_000, 197_000, "flac", 24, 96_000));
    }

    [Fact]
    public void A_short_transfer_fails()
    {
        Assert.NotNull(SmartDownloads.Verify(90_000_000, 89_999_000, 200_000, 200_000, "flac", 24, 96_000));
        Assert.NotNull(SmartDownloads.Verify(90_000_000, 0, 200_000, 200_000, "flac", 24, 96_000));
    }

    [Theory]
    [InlineData("flac", 24, 44_100, true)]     // above 16 bits
    [InlineData("flac", 16, 96_000, true)]     // above 48 kHz
    [InlineData("flac", 16, 48_000, false)]    // CD-like: not Hi-Res
    [InlineData("flac", 16, 44_100, false)]
    [InlineData("mp3", 24, 96_000, false)]     // not lossless whatever it claims
    [InlineData("alac", 24, 48_000, true)]
    public void Hi_Res_is_lossless_above_16_bits_or_above_48_kHz(string codec, int depth, int rate, bool hiRes)
    {
        Assert.Equal(hiRes, SmartDownloads.IsHiRes(codec, depth, rate));
        Assert.Equal(hiRes, SmartDownloads.Verify(1, 1, 100_000, 100_000, codec, depth, rate) is null);
    }

    [Fact]
    public void The_lossless_step_needs_lossless_but_not_Hi_Res()
    {
        Assert.Null(SmartDownloads.Verify(30_000_000, 30_000_000, 200_000, 200_000, "flac", 16, 44_100, wantHiRes: false));
        Assert.NotNull(SmartDownloads.Verify(8_000_000, 8_000_000, 200_000, 200_000, "mp3", 0, 44_100, wantHiRes: false));
    }

    [Fact]
    public void An_unknown_old_length_cannot_pass()
    {
        Assert.NotNull(SmartDownloads.Verify(90_000_000, 90_000_000, 0, 200_000, "flac", 24, 96_000));
    }

    // ---- which song is upgraded next ----

    static UpgradeEntry E(string title, int addedDaysAgo) => new()
    {
        UserId = "u", Artist = "A", Title = title, File = $"A/A - {title}.m4a", Format = "m4a",
        AddedAt = DateTime.UtcNow.AddDays(-addedDaysAgo), NextTry = DateTime.UtcNow.AddMinutes(-1),
    };

    [Fact]
    public void Songs_in_a_queue_come_first_then_the_most_recently_played_then_the_oldest()
    {
        var now = DateTime.UtcNow;
        var list = new[] { E("Old", 30), E("Older", 40), E("Played", 5), E("Played Long Ago", 6), E("Queued", 1) };
        var queued = new HashSet<string> { Matching.MatchKey("Queued", "A") };
        var played = new Dictionary<string, long>
        {
            [Matching.MatchKey("Played", "A")] = new DateTimeOffset(now.AddMinutes(-5)).ToUnixTimeMilliseconds(),
            [Matching.MatchKey("Played Long Ago", "A")] = new DateTimeOffset(now.AddDays(-3)).ToUnixTimeMilliseconds(),
        };
        Assert.Equal(["Queued", "Played", "Played Long Ago", "Older", "Old"], UpgradeList.Order(list, queued, played, now).Select(e => e.Title));
    }

    [Fact]
    public void A_song_not_due_yet_waits_its_turn_even_in_a_queue()
    {
        var now = DateTime.UtcNow;
        var later = E("Later", 1); later.NextTry = now.AddHours(1);
        var due = E("Due", 1);
        Assert.Equal(["Due"], UpgradeList.Order([later, due], new HashSet<string> { Matching.MatchKey("Later", "A") }, new Dictionary<string, long>(), now).Select(e => e.Title));
    }
}
