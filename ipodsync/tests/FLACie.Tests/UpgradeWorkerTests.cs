using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

// The upgrade list (spec section 2): a song that arrived below Hi-Res is looked for again in the background, one song at a time, and its file is only
// swapped once the new copy is verified. The old file is moved aside by the file mover (/retire), never deleted, and never before the new one passes.
// These run against a fake file mover and a fake Soulseek fetch.
public class UpgradeWorkerTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("flacie-upgrades").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    const string Old = "Artist/Artist - Song.m4a";
    const string New = "Artist/Artist - Song [Hi-Res].flac";

    sealed class FakeMover : IFileMover
    {
        public bool HasRetire = true;
        public readonly List<string> Calls = [];
        public readonly Dictionary<string, ProbeFacts> Files = [];
        public Task<bool> CanRetireAsync(CancellationToken ct = default) => Task.FromResult(HasRetire);
        public Task<bool> RetireAsync(string path, CancellationToken ct = default) { lock (Calls) Calls.Add("retire " + path); return Task.FromResult(Files.Remove(path)); }
        public Task<ProbeFacts?> ProbeAsync(string path, CancellationToken ct = default)
        {
            lock (Calls) Calls.Add("probe " + path);
            return Task.FromResult<ProbeFacts?>(Files.TryGetValue(path, out var f) ? f : new ProbeFacts("", 0, 0, 0, 0, Gone: true));
        }
    }

    sealed class Rig
    {
        public required string Dir; public required UpgradeStore Store; public required DownloadLog Log; public required FakeMover Mover; public required UpgradeWorker Worker; public required UserSession Session;
        public readonly List<bool> Fetches = [];   // each fetch asked for: false = Hi-Res, true = the lossless step
        public Func<bool, UpgradeCopy?> Answer = _ => null;
        public bool Allowed = true;
        public int Rescans;
    }

    static Track Cloud(string file) => new($"http://fm/file?path={Uri.EscapeDataString(file)}", "Song", "Artist", "Album", "Artist", 0, 0, 200_000, 0, null, 0, TrackSource.Cloud);

    Rig Make(bool inLog = true, string oldFile = Old, string source = "ytdl")
    {
        var d = Directory.CreateDirectory(Path.Combine(dir, Guid.NewGuid().ToString("N"))).FullName;
        var log = new DownloadLog(new DataPaths(d));
        var s = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "Dad", "tok"), null) { Id = "u1" };
        new DownloadServices("http://slskd.test", "k", "downloads", "http://fm.test", "k", "", "", "").WriteTo(s.Profile);
        if (inLog) log.Add(new DownloadRecord { Id = "r1", UserId = "u1", Title = "Song", Artist = "Artist", Outcome = "done", Source = source, File = oldFile, FinishedAt = DateTime.UtcNow });
        var mover = new FakeMover();
        mover.Files[oldFile] = new ProbeFacts("aac", 44_100, 0, 6_000_000, 200_400);
        var store = new UpgradeStore(new DataPaths(d));
        Rig? rig = null;
        var worker = new UpgradeWorker(store, log, () => rig!.Allowed, id => id == "u1" ? s : null, _ => true, _ => mover,
            (_, _, lossless, _) => { rig!.Fetches.Add(lossless); return Task.FromResult(rig.Answer(lossless)); },
            _ => { rig!.Rescans++; return Task.CompletedTask; });
        rig = new Rig { Dir = d, Store = store, Log = log, Mover = mover, Worker = worker, Session = s };
        store.Add(new UpgradeEntry { UserId = "u1", Artist = "Artist", Title = "Song", Album = "Album", DurationMs = 200_000, File = oldFile, Format = Path.GetExtension(oldFile).TrimStart('.'), Source = source });
        return rig;
    }

    /// <summary>A Hi-Res copy the fake Soulseek "filed": [facts] is what /probe will say of it.</summary>
    static UpgradeCopy Filed(FakeMover m, ProbeFacts facts, string file = New, long expected = 90_000_000)
    {
        m.Files[file] = facts;
        return new UpgradeCopy(Cloud(file), file, expected);
    }

    static readonly ProbeFacts GoodHiRes = new("flac", 96_000, 24, 90_000_000, 201_000);

    [Fact]
    public async Task The_old_file_is_retired_only_after_the_new_one_verifies()
    {
        var r = Make();
        r.Answer = _ => Filed(r.Mover, GoodHiRes);
        await r.Worker.RunOnceAsync(default);
        // the new copy is read before anything is moved, and the old file is the one retired
        var probeNew = r.Mover.Calls.IndexOf("probe " + New);
        var retireOld = r.Mover.Calls.IndexOf("retire " + Old);
        Assert.True(probeNew >= 0 && retireOld > probeNew, string.Join(" | ", r.Mover.Calls));
        Assert.DoesNotContain("retire " + New, r.Mover.Calls);
        Assert.Empty(r.Store.All());                                   // done: off the list
        Assert.Equal(1, r.Rescans);                                    // Jellyfin is asked to look again
        // the new file is in the download log, so it counts as FLACie's own download from now on
        Assert.Contains(r.Log.For("u1"), x => x.Done && x.File == New && x.Kind == "Upgrade");
    }

    [Fact]
    public async Task A_failed_verify_keeps_the_old_file_and_moves_the_new_one_aside()
    {
        var r = Make();
        // a "Hi-Res" copy that is really 16-bit 44.1 kHz
        r.Answer = _ => Filed(r.Mover, new ProbeFacts("flac", 44_100, 16, 90_000_000, 201_000));
        await r.Worker.RunOnceAsync(default);
        Assert.DoesNotContain("retire " + Old, r.Mover.Calls);
        Assert.Contains("retire " + New, r.Mover.Calls);
        Assert.True(r.Mover.Files.ContainsKey(Old));
        var e = Assert.Single(r.Store.All());
        Assert.Equal(1, e.Attempts);
        Assert.Equal(Old, e.File);
        Assert.InRange(e.NextTry, DateTime.UtcNow.AddMinutes(55), DateTime.UtcNow.AddMinutes(65));
    }

    [Fact]
    public async Task A_copy_of_another_length_or_cut_short_is_not_swapped_in()
    {
        var r = Make();
        r.Answer = _ => Filed(r.Mover, GoodHiRes with { DurationMs = 230_000 });
        await r.Worker.RunOnceAsync(default);
        Assert.DoesNotContain("retire " + Old, r.Mover.Calls);

        var r2 = Make(oldFile: "Artist/Artist - Song.mp3");
        r2.Answer = _ => Filed(r2.Mover, GoodHiRes with { Size = 50_000_000 }, expected: 90_000_000);
        await r2.Worker.RunOnceAsync(default);
        Assert.DoesNotContain("retire Artist/Artist - Song.mp3", r2.Mover.Calls);
    }

    [Fact]
    public async Task Without_retire_on_the_file_mover_nothing_is_downloaded_and_the_list_is_kept()
    {
        var r = Make();
        r.Mover.HasRetire = false;
        r.Answer = _ => Filed(r.Mover, GoodHiRes);
        await r.Worker.RunOnceAsync(default);
        Assert.Empty(r.Fetches);
        Assert.Single(r.Store.All());
        Assert.Equal(0, r.Store.All()[0].Attempts);
    }

    [Fact]
    public async Task Nothing_happens_while_downloads_are_off()
    {
        var r = Make();
        r.Allowed = false;
        await r.Worker.RunOnceAsync(default);
        Assert.Empty(r.Fetches);
        Assert.Single(r.Store.All());
    }

    [Fact]
    public async Task A_file_FLACie_did_not_download_is_never_upgraded()
    {
        var r = Make(inLog: false);
        r.Answer = _ => Filed(r.Mover, GoodHiRes);
        await r.Worker.RunOnceAsync(default);
        Assert.Empty(r.Fetches);
        Assert.DoesNotContain(r.Mover.Calls, c => c.StartsWith("retire"));
        Assert.Empty(r.Store.All());   // it should not have been on the list: taken off
    }

    [Fact]
    public async Task A_song_whose_file_is_gone_leaves_the_list()
    {
        var r = Make();
        r.Mover.Files.Clear();
        await r.Worker.RunOnceAsync(default);
        Assert.Empty(r.Fetches);
        Assert.Empty(r.Store.All());
    }

    [Fact]
    public async Task Nothing_found_waits_an_hour_then_six_hours()
    {
        var r = Make(oldFile: "Artist/Artist - Song.flac", source: "soulseek");   // lossless already: no lossless step
        r.Mover.Files["Artist/Artist - Song.flac"] = new ProbeFacts("flac", 44_100, 16, 30_000_000, 200_000);
        await r.Worker.RunOnceAsync(default);
        var e = Assert.Single(r.Store.All());
        Assert.Equal(1, e.Attempts);
        Assert.InRange(e.NextTry, DateTime.UtcNow.AddMinutes(55), DateTime.UtcNow.AddMinutes(65));
        e.NextTry = DateTime.UtcNow.AddSeconds(-1);
        await r.Worker.RunOnceAsync(default);
        Assert.Equal(2, r.Store.All()[0].Attempts);
        Assert.InRange(r.Store.All()[0].NextTry, DateTime.UtcNow.AddHours(5.9), DateTime.UtcNow.AddHours(6.1));
        Assert.Equal([false, false], r.Fetches);
    }

    [Fact]
    public async Task A_YouTube_copy_with_no_HiRes_anywhere_is_swapped_for_lossless_and_stays_on_the_list()
    {
        var r = Make();
        const string Lossless = "Artist/Artist - Song.flac";
        r.Answer = lossless => lossless ? Filed(r.Mover, new ProbeFacts("flac", 44_100, 16, 30_000_000, 200_500), Lossless, 30_000_000) : null;
        await r.Worker.RunOnceAsync(default);
        Assert.Equal([false, true], r.Fetches);
        Assert.Contains("retire " + Old, r.Mover.Calls);
        var e = Assert.Single(r.Store.All());
        Assert.True(e.LosslessTaken);
        Assert.Equal(Lossless, e.File);
        Assert.Equal(1, e.Attempts);
        // the next look is for Hi-Res only
        e.NextTry = DateTime.UtcNow.AddSeconds(-1);
        await r.Worker.RunOnceAsync(default);
        Assert.Equal([false, true, false], r.Fetches);
    }

    [Fact]
    public async Task The_list_is_kept_across_a_restart_and_never_has_a_song_twice()
    {
        var r = Make();
        Assert.False(r.Store.Add(new UpgradeEntry { UserId = "u1", Artist = "Artist", Title = "Song", File = "Artist/Artist - Song.mp3" }));
        Assert.True(r.Store.Add(new UpgradeEntry { UserId = "u2", Artist = "Artist", Title = "Song", File = Old }));   // someone else's download is their own
        var again = new UpgradeStore(new DataPaths(r.Dir));
        Assert.Equal(2, again.All().Count);
        Assert.True(File.Exists(Path.Combine(r.Dir, UpgradeStore.FileName)));
    }
}
