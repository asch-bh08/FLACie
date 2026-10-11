using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace FLACie.Tests;

/// <summary>Download log, failed rows, Retry / Retry all, and the digest's Kind filter. No network: a user without download services fails every song at once.</summary>
public class DownloadTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dl").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    sealed class NoNet : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => throw new HttpRequestException("no network in tests");
    }
    sealed class Factory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(new NoNet(), false); }

    static DownloadRecord Rec(string user, string artist, string title, bool done, DateTime at, string kind = "Search", bool album = false) => new()
    {
        Id = Guid.NewGuid().ToString("N"), UserId = user, Artist = artist, Title = title, Label = title + " · " + artist, Kind = kind, Outcome = done ? "done" : "failed",
        StartedAt = at, FinishedAt = at, IsAlbum = album,
    };

    [Fact]
    public async Task Downloads_finishing_together_are_all_written_to_the_log_file()
    {
        var log = new DownloadLog(new DataPaths(dir));
        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() => log.Add(Rec("u1", "A", "T" + i, i % 2 == 0, DateTime.UtcNow)))));
        Assert.Equal(64, log.For("u1").Count);
        var again = new DownloadLog(new DataPaths(dir));                       // what a restart reads back
        var lines = File.ReadAllLines(Path.Combine(dir, "downloads", "u1.jsonl"));
        Assert.True(lines.Length == 64, $"{lines.Length} lines; first: {lines.FirstOrDefault()}");
        Assert.Equal(64, again.For("u1").Count);
    }

    [Fact]
    public void A_corrupt_line_in_the_log_file_is_skipped_not_fatal()
    {
        Directory.CreateDirectory(Path.Combine(dir, "downloads"));
        var good = System.Text.Json.JsonSerializer.Serialize(Rec("u1", "A", "T", true, DateTime.UtcNow));
        File.WriteAllLines(Path.Combine(dir, "downloads", "u1.jsonl"), [good, "{not json", "", good]);
        Assert.Equal(2, new DownloadLog(new DataPaths(dir)).For("u1").Count);
    }

    [Fact]
    public void A_user_id_cannot_walk_out_of_the_downloads_folder()
    {
        var log = new DownloadLog(new DataPaths(dir));
        log.Add(Rec("../../evil", "A", "T", true, DateTime.UtcNow));
        Assert.False(File.Exists(Path.Combine(Directory.GetParent(dir)!.FullName, "evil.jsonl")));
        Assert.Single(Directory.GetFiles(Path.Combine(dir, "downloads"), "*.jsonl"));
    }

    [Fact]
    public void Only_a_failed_song_can_be_retried_never_an_album_or_a_nameless_row()
    {
        var t = DateTime.UtcNow;
        Assert.True(DownloadManager.CanRetry(Rec("u", "A", "T", false, t)));
        Assert.False(DownloadManager.CanRetry(Rec("u", "A", "T", true, t)));
        Assert.False(DownloadManager.CanRetry(Rec("u", "A", "T", false, t, album: true)));
        Assert.False(DownloadManager.CanRetry(Rec("u", "", "T", false, t)));
        Assert.False(DownloadManager.CanRetry(Rec("u", "A", "", false, t)));
    }

    (DownloadManager M, DownloadLog Log, UserSession S, List<(string, string)> Said) Make()
    {
        var paths = new DataPaths(dir);
        var log = new DownloadLog(paths);
        var said = new List<(string, string)>();
        var digest = new DownloadDigest((t, m) => { lock (said) said.Add((t, m)); });
        var jf = new JellyfinClient(new HttpClient(new NoNet()), "dev");
        var m = new DownloadManager(new Factory(), new WebCatalog(new HttpClient(new NoNet())), log, jf, digest);
        var s = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "user1" };
        return (m, log, s, said);
    }

    static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(25);
        Assert.True(done(), "timed out");
    }

    [Fact]
    public async Task A_song_asked_for_without_download_services_fails_visibly_and_is_logged_once()
    {
        var (m, log, s, said) = Make();
        m.Song(s, new CatalogSong("Artist", "Title", "Album", null, 200000, 1, 1));
        await Until(() => log.For(s.Id).Count == 1);
        var r = log.For(s.Id)[0];
        Assert.Equal("failed", r.Outcome);
        Assert.Contains("aren't set up", r.Message);
        Assert.Equal("Search", r.Kind);
        Assert.Empty(said);                                 // a failure is never announced as "downloaded"
        Assert.True(DownloadManager.CanRetry(r));
    }

    [Fact]
    public async Task Retry_all_asks_again_only_for_songs_whose_latest_try_failed_and_never_twice()
    {
        var (m, log, s, _) = Make();
        var t = DateTime.UtcNow.AddHours(-2);
        var mine = new List<DownloadRecord>
        {
            Rec(s.Id, "A", "Failed once", false, t),
            Rec(s.Id, "B", "Failed then done", false, t), Rec(s.Id, "B", "Failed then done", true, t.AddMinutes(5)),
            Rec(s.Id, "C", "Failed twice", false, t), Rec(s.Id, "C", "Failed twice", false, t.AddMinutes(9)),
            Rec(s.Id, "D", "An album", false, t, album: true),
            Rec(s.Id, "E", "Done then failed", true, t), Rec(s.Id, "E", "Done then failed", false, t.AddMinutes(30)),
        };
        var n = m.RetryAll(s, mine);
        Assert.Equal(3, n);                                  // A, C (once, not twice) and E (a later failure after the success)
        await Until(() => log.For(s.Id).Count == 3);
        Assert.Equal(0, m.RetryAll(s, [Rec(s.Id, "A", "Failed once", false, t)], 0));   // max 0 starts nothing
        // the same songs while one of them is still running are not started again
        Assert.Equal(new[] { "A", "C", "E" }, log.For(s.Id).Select(r => r.Artist).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Retry_all_respects_its_limit()
    {
        var (m, log, s, _) = Make();
        var t = DateTime.UtcNow.AddHours(-1);
        var mine = Enumerable.Range(0, 20).Select(i => Rec(s.Id, "A" + i, "T" + i, false, t.AddSeconds(i))).ToList();
        Assert.Equal(5, m.RetryAll(s, mine, 5));
        await Until(() => log.For(s.Id).Count == 5);
    }
}
