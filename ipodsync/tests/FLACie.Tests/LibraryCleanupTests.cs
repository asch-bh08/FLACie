using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F23.4: the clean-up moves duplicates, worse copies and undecodable files to the holding folder, never the only copy of a song.</summary>
public class LibraryCleanupTests
{
    static int n;
    static Track T(string title, string file, long size, long ms = 200_000, long added = 0) =>
        new($"/m/{++n}", title, "Artist", "Album", "Artist", 1, 1, ms, 2000, null, added, TrackSource.Jellyfin, file, Size: size);

    [Fact]
    public void Exact_duplicates_worse_copies_and_undecodable_files_move_never_the_only_copy()
    {
        var flac = T("Song A", "A/a.flac", 30_000_000, added: 1);
        var dup = T("Song A", "A/a (2).flac", 30_000_000, added: 5);           // the same file again
        var mp3 = T("Song A", "A/a.mp3", 8_000_000);                          // a worse copy
        var opus = T("Song B", "B/b.opus", 4_000_000);
        var m4a = T("Song B", "B/b.m4a", 6_000_000);                          // better than the opus
        var brokenOnly = T("Song C", "C/c.flac", 30_000_000);                 // fails to decode, but the only copy
        var broken = T("Song D", "D/d.flac", 30_000_000);                     // fails to decode, a good mp3 is there
        var goodMp3 = T("Song D", "D/d.mp3", 8_000_000);
        var live = T("Song A", "A/a live.flac", 30_000_000, ms: 260_000);     // another recording of Song A: kept
        var bad = new HashSet<Track> { brokenOnly, broken };
        var moves = LibraryCleanup.Plan([flac, dup, mp3, opus, m4a, brokenOnly, broken, goodMp3, live], bad.Contains).ToDictionary(m => m.Track, m => m.Why);
        Assert.Equal(CleanupReason.Duplicate, moves[dup]);
        Assert.Equal(CleanupReason.WorseCopy, moves[mp3]);
        Assert.Equal(CleanupReason.WorseCopy, moves[opus]);
        Assert.Equal(CleanupReason.Undecodable, moves[broken]);
        Assert.Equal(4, moves.Count);
        Assert.DoesNotContain(flac, moves.Keys); Assert.DoesNotContain(m4a, moves.Keys); Assert.DoesNotContain(goodMp3, moves.Keys);
        Assert.DoesNotContain(brokenOnly, moves.Keys); Assert.DoesNotContain(live, moves.Keys);
    }

    sealed class Mover : IFileMover
    {
        public List<string> Retired = []; public int Purged; public HashSet<string> NoAudio = [];
        public Task<bool> CanRetireAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RetireAsync(string path, CancellationToken ct = default) { Retired.Add(path); return Task.FromResult(true); }
        public Task<ProbeFacts?> ProbeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<ProbeFacts?>(NoAudio.Contains(path) ? new("", 0, 0, 100, 0) : new("flac", 44100, 16, 100, 200_000));
        public Task<bool> PurgeHeldAsync(int days, CancellationToken ct = default) { Purged = days; return Task.FromResult(true); }
    }

    [Fact]
    public async Task It_runs_only_when_the_disk_is_low_and_moves_through_the_holding_folder()
    {
        var me = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };
        var songs = new[] { T("Song A", "/music/A/a.flac", 30_000_000), T("Song A", "/music/A/a.mp3", 8_000_000), T("Song B", "/music/B/b.flac", 30_000_000),
            T("Song B", "/music/B/b copy.flac", 31_000_000), T("Elsewhere", "/other/x.mp3", 1), T("Elsewhere", "/other/y.mp3", 1) };
        var fm = new Mover { NoAudio = ["B/b copy.flac"] };
        long free = 500L * 1000 * 1000 * 1000;
        var run = new LibraryCleanupRun(_ => (free, free * 2), _ => fm, "/music", null, () => DateTime.UtcNow);
        Assert.Equal(0, await run.RunAsync(me, songs, default));                 // plenty of room: nothing happens
        Assert.Empty(fm.Retired);
        free = 50L * 1000 * 1000 * 1000;
        Assert.Equal(2, await run.RunAsync(me, songs, default));
        Assert.Equal(["A/a.mp3", "B/b copy.flac"], fm.Retired.Order());        // the worse copy and the one with no audio; nothing outside /music
        Assert.Equal(LibraryCleanupRun.HoldDays, fm.Purged);
        Assert.Contains(run.Moved, r => r.Why == CleanupReason.Undecodable && r.File == "B/b copy.flac");
    }
}
