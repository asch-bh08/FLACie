using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>Covers on disk (kept while there is room, least recently used out only when the disk is low), the cache report and Clear, the shared cover lookup, the next song's early read.</summary>
public class CacheTests
{
    static string Dir() => Directory.CreateTempSubdirectory().FullName;
    const long GB = 1L << 30;

    static async Task Fill(CoverStore s, int n, int bytes = 400)
    {
        for (var i = 0; i < n; i++) await s.SaveAsync("jf" + i + "~tag", CoverStore.Row, new byte[bytes], "image/webp");
    }

    [Fact]
    public async Task With_plenty_of_free_space_nothing_is_ever_deleted_however_many_covers_there_are()
    {
        var s = new CoverStore(Dir()) { Space = () => (500 * GB, 1000 * GB) };
        await Fill(s, 120);                                         // 120 saves: the check ran several times
        Assert.Equal(0, s.EvictIfLow());
        Assert.Equal(120, s.Size().Count);
    }

    [Fact]
    public async Task A_cover_is_found_again_by_key_and_variant_and_the_full_picture_is_a_different_file()
    {
        var s = new CoverStore(Dir()) { Space = () => (500 * GB, 1000 * GB) };
        await s.SaveAsync("jfabc~t1", CoverStore.Row, [1, 2, 3], "image/webp");
        await s.SaveAsync("jfabc~t1", CoverStore.Full, [9, 9, 9, 9], "image/jpeg");
        Assert.EndsWith(".webp", s.Find("jfabc~t1", CoverStore.Row));
        Assert.EndsWith(".jpg", s.Find("jfabc~t1", CoverStore.Full));
        Assert.Null(s.Find("jfabc~t2", CoverStore.Row));            // another image tag is another cover: the changed picture is fetched, the old one never re-checked
    }

    [Fact]
    public async Task When_free_space_is_low_the_least_recently_used_covers_go_first_and_only_as_many_as_needed()
    {
        var dir = Dir();
        long startBytes = 0;
        var s = new CoverStore(dir) { MinFloorBytes = 2000 };
        s.Space = () => (1000 + (startBytes - s.Size().Bytes), 10_000);   // minimum = max(2000, 10 %) = 2000; deleting a file gives its bytes back
        await Fill(s, 10);
        // age them: file 0 is the oldest used, file 9 the newest ...
        var files = new DirectoryInfo(dir).GetFiles().OrderBy(f => f.Name).ToList();
        var byKey = Enumerable.Range(0, 10).Select(i => s.Find("jf" + i + "~tag", CoverStore.Row)!).ToList();
        for (var i = 0; i < 10; i++) File.SetLastAccessTimeUtc(byKey[i], DateTime.UtcNow.AddHours(-10 + i));
        // ... but cover 0 was looked at just now (a page showed it): it must survive
        Assert.NotNull(s.Find("jf0~tag", CoverStore.Row));
        File.SetLastAccessTimeUtc(byKey[0], DateTime.UtcNow);
        startBytes = s.Size().Bytes;
        var (free0, _) = s.Space();
        Assert.True(free0 < s.MinFreeBytes);
        var gone = s.EvictIfLow();
        // wants 1.25 x 2000 - 1000 = 1500 bytes -> 4 files of 400
        Assert.Equal(4, gone);
        Assert.NotNull(s.Find("jf0~tag", CoverStore.Row));          // recently used: kept
        Assert.Null(s.Find("jf1~tag", CoverStore.Row)); Assert.Null(s.Find("jf2~tag", CoverStore.Row));
        Assert.Null(s.Find("jf3~tag", CoverStore.Row)); Assert.Null(s.Find("jf4~tag", CoverStore.Row));
        Assert.NotNull(s.Find("jf5~tag", CoverStore.Row)); Assert.NotNull(s.Find("jf9~tag", CoverStore.Row));
        Assert.Equal(6, s.Size().Count);
        Assert.True(s.Space().Free >= s.MinFreeBytes);              // there is room again, and nothing more is deleted
        Assert.Equal(0, s.EvictIfLow());
    }

    [Fact]
    public async Task The_cache_report_counts_covers_library_lyrics_and_formats_and_Clear_empties_all_but_the_format_index()
    {
        var root = Dir();
        var covers = new CoverStore(Path.Combine(root, "art")) { Space = () => (500 * GB, 1000 * GB) };
        await Fill(covers, 5, 1000);
        Directory.CreateDirectory(Path.Combine(root, "library")); Directory.CreateDirectory(Path.Combine(root, "lyrics"));
        File.WriteAllBytes(Path.Combine(root, "library", "a.json"), new byte[3000]);
        File.WriteAllBytes(Path.Combine(root, "library", "a.json.cursor"), new byte[30]);
        File.WriteAllBytes(Path.Combine(root, "lyrics", "x.lrc"), new byte[500]);
        File.WriteAllBytes(Path.Combine(root, "audiofacts.json"), new byte[700]);
        var cache = new CacheService(new DataPaths(root), covers);
        var r = cache.Report();
        Assert.Equal(5000, r.Covers); Assert.Equal(5, r.CoverFiles);
        Assert.Equal(3030, r.Library); Assert.Equal(500, r.Lyrics); Assert.Equal(700, r.FormatIndex);
        Assert.Equal(5000 + 3030 + 500 + 700, r.Total);
        var freed = cache.Clear();
        Assert.Equal(5000 + 3030 + 500, freed);
        var after = cache.Report();
        Assert.Equal(0, after.CoverFiles); Assert.Equal(0, after.Library); Assert.Equal(0, after.Lyrics); Assert.Equal(700, after.FormatIndex);
    }

    [Fact]
    public async Task Low_disk_space_also_frees_the_oldest_lyrics_after_the_covers_and_plenty_of_space_frees_nothing()
    {
        var root = Dir();
        long free = 500 * GB;
        var covers = new CoverStore(Path.Combine(root, "art")) { Space = () => (free, 1000 * GB) };
        Directory.CreateDirectory(Path.Combine(root, "lyrics"));
        var lyr = Path.Combine(root, "lyrics");
        for (var i = 0; i < 3; i++) { File.WriteAllBytes(Path.Combine(lyr, i + ".lrc"), new byte[100]); File.SetLastWriteTimeUtc(Path.Combine(lyr, i + ".lrc"), DateTime.UtcNow.AddDays(-30 + i)); File.SetLastAccessTimeUtc(Path.Combine(lyr, i + ".lrc"), DateTime.UtcNow.AddDays(-30 + i)); }
        var cache = new CacheService(new DataPaths(root), covers);
        Assert.Equal(0, cache.EvictIfLow());
        Assert.Equal(3, Directory.GetFiles(lyr).Length);
        free = 1 * GB;      // low, and deleting tiny files never changes it: everything that can go, oldest first, does
        Assert.True(cache.EvictIfLow() >= 3);
        Assert.Empty(Directory.GetFiles(lyr));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task One_lookup_is_cancelled_only_when_every_caller_has_given_up()
    {
        var f = new SingleFlight<string>();
        var started = new TaskCompletionSource(); var token = CancellationToken.None; var cancelled = new TaskCompletionSource();
        Task<string> Ask(CancellationToken ct) => f.RunAsync("jfabc", async c =>
        {
            token = c; c.Register(() => cancelled.TrySetResult()); started.TrySetResult();
            await Task.Delay(Timeout.Infinite, c); return "never";
        }, ct);
        using var a = new CancellationTokenSource(); using var b = new CancellationTokenSource();
        var ta = Ask(a.Token); var tb = Ask(b.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        a.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ta);
        await Task.Delay(50);
        Assert.False(cancelled.Task.IsCompleted);                   // b still wants it
        Assert.Equal(1, f.Started); Assert.Equal(0, f.Abandoned);
        b.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tb);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));    // nobody is waiting: the call to Jellyfin is dropped
        Assert.Equal(1, f.Abandoned);
        // and the next asker starts a fresh lookup
        Assert.Equal("ok", await f.RunAsync("jfabc", _ => Task.FromResult("ok")));
        Assert.Equal(2, f.Started);
    }

    [Fact]
    public void The_next_songs_early_read_is_held_to_the_first_megabyte()
    {
        var cap = "bytes=0-" + (MediaEndpoints.PrefetchBytes - 1);
        Assert.Equal(cap, MediaEndpoints.PrefetchRange(""));
        Assert.Equal(cap, MediaEndpoints.PrefetchRange("bytes=0-"));                       // what an audio element asks for
        Assert.Equal(cap, MediaEndpoints.PrefetchRange("bytes=0-99999999"));
        Assert.Equal("bytes=0-1000", MediaEndpoints.PrefetchRange("bytes=0-1000"));        // already small
        Assert.Equal("bytes=5000000-", MediaEndpoints.PrefetchRange("bytes=5000000-"));    // a seek elsewhere is left alone
    }

    [Fact]
    public async Task The_format_index_flushes_itself_so_a_restart_of_the_Windows_apps_copy_keeps_the_facts()
    {
        var dir = Dir();
        FormatIndex.FlushDelay = TimeSpan.FromMilliseconds(50);
        var idx = new FormatIndex(new DataPaths(dir));
        var t = new Track("p", "T", "A", "Al", "A", 1, 1, 1000, 2000, null, 0, TrackSource.Jellyfin, "/m/x.flac", "0123456789abcdef0123456789abcdef");
        idx.Set(t, new MediaInfo("flac", "flac", 2800, 96000, 24, 2, 0, 0, "", "Jellyfin"));
        var until = Environment.TickCount64 + 15_000;
        while (!File.Exists(Path.Combine(dir, "audiofacts.json")) && Environment.TickCount64 < until) await Task.Delay(100);
        Assert.True(File.Exists(Path.Combine(dir, "audiofacts.json")));
        Assert.True(new FormatIndex(new DataPaths(dir)).Get(t)!.HiRes);
    }

    [Fact]
    public void Badge_and_info_agree_on_a_raw_PCM_variant_inside_a_lossless_container()
    {
        var idx = new FormatIndex(new DataPaths(Dir()));
        var t = new Track("p2", "T", "A", "Al", "A", 1, 1, 1000, 2000, null, 0, TrackSource.Jellyfin, "/m/x.wav", "0123456789abcdef0123456789abcde0");
        var mi = new MediaInfo("wav", "pcm_f32le", 4608, 96000, 32, 2, 0, 0, "", "Jellyfin");
        idx.Set(t, mi);
        Assert.Equal(mi.HiRes, idx.Get(t)!.HiRes);
        Assert.True(idx.Get(t)!.HiRes);
    }
}
