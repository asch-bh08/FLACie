using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

// The race of a quick download (spec section 1): every source starts at once, the first playable copy plays, a Hi-Res transfer estimated to be
// playable within the grace is waited for (never longer than the grace), and the sources that lost are cancelled. These run against fake
// sources: each says how long it takes and, for Hi-Res, how its transfer is going.
public class DownloadRaceTests
{
    static Track Song(string file) => new($"http://fm/file?path={Uri.EscapeDataString(file)}", "Song", "Artist", "Album", "Artist", 0, 0, 200_000, 0, null, 0, TrackSource.Cloud);

    /// <summary>A source that delivers [file] after [ms], or nothing (null) when [file] is null. [obeysCancel] false: like the YouTube service, it
    /// carries on and files its copy even after the race has moved on.</summary>
    static RaceEntrant Fake(string source, bool hiRes, int ms, string? file, Action<RaceEntrant>? onStart = null, bool obeysCancel = true, List<string>? cancelled = null) =>
        new(source, hiRes, async (e, ct) =>
        {
            onStart?.Invoke(e);
            try { await Task.Delay(ms, obeysCancel ? ct : CancellationToken.None); }
            catch (OperationCanceledException) { Note(); throw; }
            if (ct.IsCancellationRequested && obeysCancel) { Note(); ct.ThrowIfCancellationRequested(); }
            return file is null ? null : Song(file);
            void Note() { if (cancelled is not null) lock (cancelled) cancelled.Add(source); }
        });

    /// <summary>Waits (up to 5 s, the whole suite runs at once) until [done]: a cancelled source notices on its own thread.</summary>
    static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(25);
        Assert.True(done(), "timed out");
    }

    [Fact]
    public async Task A_HiRes_copy_inside_the_grace_wins_over_a_quicker_YouTube_copy()
    {
        var strays = new List<RaceWin>();
        var youtube = Fake("ytdl", false, 50, "A/A - Song.m4a");
        // 3 MB left at 1 MB/s: about 5 s with the filing, inside 7 s; it actually lands at 400 ms
        var hires = Fake("soulseek", true, 400, "A/A - Song [Hi-Res].flac", e => e.Report(27_000_000, 30_000_000, 1_000_000));
        var win = await DownloadRace.RunAsync([youtube, hires], w => { lock (strays) strays.Add(w); }, default);
        Assert.NotNull(win);
        Assert.True(win.HiRes);
        Assert.Equal("soulseek", win.Source);
        // YouTube's copy was already filed: it is handed back to be retired, not left as a second copy in the library
        Assert.Single(strays);
        Assert.Equal("ytdl", strays[0].Source);
    }

    [Fact]
    public async Task A_HiRes_copy_outside_the_grace_loses_and_is_cancelled()
    {
        var cancelled = new List<string>();
        var youtube = Fake("ytdl", false, 50, "A/A - Song.m4a");
        // 20 MB left at 1 MB/s: 22 s, well outside the grace
        var hires = Fake("soulseek", true, 30_000, "A/A - Song [Hi-Res].flac", e => e.Report(10_000_000, 30_000_000, 1_000_000), cancelled: cancelled);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var win = await DownloadRace.RunAsync([youtube, hires], null, default);
        Assert.Equal("ytdl", win!.Source);
        Assert.False(win.HiRes);
        Assert.True(clock.ElapsedMilliseconds < 3_000, $"took {clock.ElapsedMilliseconds} ms: the quick copy must not wait for a Hi-Res copy outside the grace");
        await Until(() => { lock (cancelled) return cancelled.Contains("soulseek"); });
    }

    [Fact]
    public async Task A_HiRes_copy_is_never_waited_for_longer_than_the_grace()
    {
        var youtube = Fake("ytdl", false, 20, "A/A - Song.m4a");
        // promises to be quick, then never arrives
        var hires = Fake("soulseek", true, 60_000, "A/A - Song [Hi-Res].flac", e => e.Report(29_000_000, 30_000_000, 10_000_000));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // a grace of 2.6 s: the promise (0.1 s of transfer and 2 s of filing) fits at first, then no longer fits what is left, and the wait ends
        var win = await DownloadRace.RunAsync([youtube, hires], null, default, graceMs: 2_600);
        Assert.Equal("ytdl", win!.Source);
        Assert.InRange(clock.ElapsedMilliseconds, 300, 5_000);
    }

    [Fact]
    public async Task A_HiRes_transfer_with_no_speed_yet_is_not_waited_for()
    {
        var youtube = Fake("ytdl", false, 20, "A/A - Song.m4a");
        var hires = Fake("soulseek", true, 60_000, "A/A - Song [Hi-Res].flac");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var win = await DownloadRace.RunAsync([youtube, hires], null, default);
        Assert.Equal("ytdl", win!.Source);
        Assert.True(clock.ElapsedMilliseconds < 3_000);
    }

    [Fact]
    public async Task A_HiRes_transfer_that_fails_during_the_grace_lets_the_quick_copy_play_at_once()
    {
        var youtube = Fake("ytdl", false, 20, "A/A - Song.m4a");
        var hires = Fake("soulseek", true, 300, null, e => e.Report(29_000_000, 30_000_000, 1_000_000));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var win = await DownloadRace.RunAsync([youtube, hires], null, default);
        Assert.Equal("ytdl", win!.Source);
        Assert.True(clock.ElapsedMilliseconds < 3_000);
    }

    [Fact]
    public async Task A_HiRes_copy_that_arrives_first_plays_and_the_rest_are_cancelled()
    {
        var cancelled = new List<string>();
        var win = await DownloadRace.RunAsync([Fake("ytdl", false, 20_000, "A/A - Song.m4a", cancelled: cancelled), Fake("soulseek", true, 30, "A/A - Song [Hi-Res].flac"), Fake("archive", false, 20_000, "A/A - Song.mp3", cancelled: cancelled)], null, default);
        Assert.True(win!.HiRes);
        await Until(() => { lock (cancelled) return cancelled.Count == 2; });
        Assert.Equal(["archive", "ytdl"], cancelled.Order());
    }

    [Fact]
    public async Task The_first_of_several_quick_copies_plays_and_a_late_one_is_handed_back()
    {
        var strays = new List<RaceWin>();
        var win = await DownloadRace.RunAsync([Fake("soulseek-normal", false, 30, "A/A - Song.flac"), Fake("ytdl", false, 300, "A/A - Song.m4a", obeysCancel: false)], w => { lock (strays) strays.Add(w); }, default);
        Assert.Equal("soulseek-normal", win!.Source);
        await Until(() => { lock (strays) return strays.Count > 0; });
        Assert.Single(strays);
        Assert.Equal("ytdl", strays[0].Source);
    }

    [Fact]
    public async Task A_late_copy_at_the_same_path_as_the_winner_is_never_handed_back()
    {
        // the YouTube service answers "already there" for a file of the same name: retiring it would retire the song that plays
        var strays = new List<RaceWin>();
        var landed = new TaskCompletionSource();
        var late = new RaceEntrant("ytdl", false, async (_, _) => { await Task.Delay(200); landed.TrySetResult(); return Song("A/A - Song.m4a"); });
        await DownloadRace.RunAsync([Fake("soulseek", false, 30, "A/A - Song.m4a"), late], w => { lock (strays) strays.Add(w); }, default);
        await landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);   // its copy has been looked at by now
        Assert.Empty(strays);
    }

    [Fact]
    public async Task Nothing_found_anywhere_is_null_and_a_source_that_throws_counts_as_nothing()
    {
        var broken = new RaceEntrant("archive", false, (_, _) => throw new HttpRequestException("down"));
        Assert.Null(await DownloadRace.RunAsync([Fake("ytdl", false, 20, null), broken, Fake("soulseek", true, 40, null)], null, default));
    }

    [Fact]
    public async Task A_real_cancel_cancels_the_race()
    {
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DownloadRace.RunAsync([Fake("ytdl", false, 20_000, "x.m4a")], null, cts.Token));
    }

    [Fact]
    public void The_speed_is_measured_from_the_reports_when_a_source_does_not_say_it()
    {
        long now = 0;
        var e = new RaceEntrant("soulseek", true, (_, _) => Task.FromResult<Track?>(null)) { Clock = () => now };
        e.Report(0, 30_000_000);
        now = 1_000; e.Report(2_000_000, 30_000_000);
        now = 3_000; e.Report(6_000_000, 30_000_000);
        Assert.Equal(2_000_000, e.BytesPerSecond, 0);
        Assert.Equal(24_000_000, e.Remaining);
    }
}
