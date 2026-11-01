using System.Net;
using System.Text;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace FLACie.Tests;

// Smart downloads through the download manager: a quick download that arrives below Hi-Res goes on the upgrade list, a Hi-Res one does not, a job can be
// started and followed by id (the phone's POST /api/download), and Autoplay's look-ahead asks for Hi-Res three places down the queue. The download
// services are a stand-in answering over a fake network: the file mover (with /retire, so YouTube races), YouTube behind it, and a Soulseek that has nothing.
public class SmartDownloadWiringTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("flacie-smart").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    sealed class Stand(bool retire) : HttpMessageHandler
    {
        public readonly List<string> Asked = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var u = r.RequestUri!.ToString();
            lock (Asked) Asked.Add(r.Method + " " + u);
            HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            var fm = r.RequestUri.Host.StartsWith("fm", StringComparison.Ordinal);
            if (fm && r.RequestUri.AbsolutePath == "/health") return Task.FromResult(retire ? Json("""{"ok":true,"routes":["move","fetch","probe","ytdl","file","retire"]}""") : Json("""{"error":"not found"}""", HttpStatusCode.NotFound));
            if (fm && r.RequestUri.AbsolutePath == "/ytdl") return Task.FromResult(Json("""{"ok":true,"ext":"m4a","bytes":4000000,"durationSec":200}"""));
            return Task.FromResult(Json("""{"error":"not found"}""", HttpStatusCode.NotFound));
        }
    }

    sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }

    string Fm = "";

    (DownloadManager M, UpgradeStore Upgrades, UserSession S, Stand Net) Rig(bool retire = true)
    {
        var net = new Stand(retire);
        var upgrades = new UpgradeStore(new DataPaths(dir));
        var m = new DownloadManager(new Factory(net), new WebCatalog(new HttpClient(net, false)), new DownloadLog(new DataPaths(dir)), new JellyfinClient(new HttpClient(net, false), "dev"), new DownloadDigest((_, _) => { }), null, upgrades);
        var s = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "Dad", "tok"), null) { Id = "u1" };
        // a file mover address of its own: what each file mover said to /health is remembered for ten minutes
        Fm = $"http://fm{Guid.NewGuid():N}.test";
        new DownloadServices("http://slskd.test", "k", "downloads", Fm, "k", "", "", "", new OpenSourceSettings(false, false, false, "", [], Ytdl: true)).WriteTo(s.Profile);
        return (m, upgrades, s, net);
    }

    [Fact]
    public async Task A_YouTube_copy_plays_and_goes_on_the_upgrade_list_once()
    {
        var (m, upgrades, s, _) = Rig();
        var r = await m.FetchAsync(s, "Search", "Artist", "Song", "Album", null, 200_000);
        Assert.True(r.Ok, r.Message);
        var job = Assert.Single(m.JobsOf(s));
        Assert.Equal("ytdl", job.Source);
        Assert.False(job.HiRes);
        Assert.Equal(Fm + "/file?path=" + Uri.EscapeDataString("Artist/Artist - Song.m4a"), job.TrackPath);
        var e = Assert.Single(upgrades.All());
        Assert.Equal("Artist/Artist - Song.m4a", e.File);
        Assert.Equal("m4a", e.Format);
        Assert.Equal("ytdl", e.Source);
        Assert.Equal(200_000, e.DurationMs);
        // the same song again does not go on the list twice
        await m.FetchAsync(s, "Search", "Artist", "Song", "Album", null, 200_000);
        Assert.Single(upgrades.All());
    }

    [Fact]
    public async Task With_an_older_file_mover_YouTube_still_runs_after_Soulseek_as_before()
    {
        var (m, _, s, net) = Rig(retire: false);
        var r = await m.FetchAsync(s, "Search", "Artist", "Song", "Album", null, 200_000);
        Assert.True(r.Ok, r.Message);
        Assert.Equal("ytdl", Assert.Single(m.JobsOf(s)).Source);
        // Soulseek was asked before YouTube: no racing without /retire
        var asked = net.Asked.ToList();
        Assert.True(asked.FindIndex(a => a.Contains("slskd.test")) < asked.FindIndex(a => a.Contains("/ytdl")), string.Join(" | ", asked));
    }

    [Fact]
    public async Task A_started_job_can_be_followed_by_its_id_and_a_song_on_its_way_is_shared()
    {
        var (m, _, s, _) = Rig();
        var job = m.Start(s, "Search", "Artist", "Song", "Album", 200_000, DownloadMode.Quick);
        Assert.NotNull(job);
        Assert.Contains(m.JobsOf(s), j => j.Id == job.Id);   // there at once, before the download has done anything
        Assert.Same(job, m.Start(s, "Search", "Artist", "Song", "Album", 200_000, DownloadMode.Quick));
        await WaitFor(() => job.Finished);
        Assert.Equal(DownloadStage.Done, job.Stage);
    }

    [Fact]
    public async Task A_HiRes_mode_download_asks_for_Hi_Res_first()
    {
        var (m, _, s, _) = Rig();
        var job = m.Start(s, "Autoplay", "Artist", "Song", "Album", 200_000, DownloadMode.HiRes)!;
        await WaitFor(() => job.Finished);
        Assert.Contains("in Hi-Res", job.Trail[0].Text);
        Assert.Equal(DownloadStage.Done, job.Stage);   // none on Soulseek: the quick way after all
    }

    [Fact]
    public async Task Autoplay_fetches_the_next_two_songs_quick_and_the_rest_in_Hi_Res()
    {
        var (m, _, s, _) = Rig();
        var states = new UserStateStore(new DataPaths(dir), new EphemeralDataProtectionProvider());
        states.For(s).Prefetch = true;
        var planner = new AutoplayPlanner(new WebCatalog(new HttpClient(new Stand(true), false)), m, states, new StorageGuard(states));
        var songs = Enumerable.Range(1, 4).Select(i => new CatalogSong("Artist", $"Ahead {i}", "Album", null, 200_000, i, 1)).ToList();
        planner.Prefetch(s, songs, 5, () => { }, placesBefore: 0);
        await WaitFor(() => m.JobsOf(s).Count == 4 && m.JobsOf(s).All(j => j.Trail.Count > 0));
        bool HiRes(string t) => m.JobsOf(s).Single(j => j.Title == t).Trail[0].Text.Contains("in Hi-Res");
        Assert.False(HiRes("Ahead 1"));
        Assert.False(HiRes("Ahead 2"));
        Assert.True(HiRes("Ahead 3"));
        Assert.True(HiRes("Ahead 4"));
    }

    [Fact]
    public async Task Songs_already_queued_push_the_look_ahead_into_Hi_Res()
    {
        var (m, _, s, _) = Rig();
        var states = new UserStateStore(new DataPaths(dir), new EphemeralDataProtectionProvider());
        states.For(s).Prefetch = true;
        var planner = new AutoplayPlanner(new WebCatalog(new HttpClient(new Stand(true), false)), m, states, new StorageGuard(states));
        planner.Prefetch(s, [new CatalogSong("Artist", "After The Queue", "Album", null, 200_000, 1, 1)], 5, () => { }, placesBefore: 5);
        await WaitFor(() => m.JobsOf(s).Count == 1 && m.JobsOf(s)[0].Trail.Count > 0);
        Assert.Contains("in Hi-Res", m.JobsOf(s)[0].Trail[0].Text);
    }

    static async Task WaitFor(Func<bool> done, int ms = 20_000)
    {
        var until = Environment.TickCount64 + ms;
        while (!done() && Environment.TickCount64 < until) await Task.Delay(50);
        Assert.True(done(), "timed out");
    }
}
