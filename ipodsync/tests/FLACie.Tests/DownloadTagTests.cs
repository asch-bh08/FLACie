using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// "Recently downloaded" tags (beta 54): the age label and the 60 minute window (pure, the phone has the same function), the order in which a song's status is
/// decided (running > recent finish > recent failure), the labels, a download the phone reports, and the limits on that report.
/// </summary>
public class DownloadTagTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("flacie-tag").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    const long Min = 60_000, Hour = 60 * Min;
    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ---- the pure rule ----

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(4_999, "just now")]
    [InlineData(5_000, "5s ago")]
    [InlineData(1_000 * 59 + 999, "59s ago")]
    [InlineData(60_000, "1 min ago")]
    [InlineData(61_000, "1 min ago")]
    [InlineData(10 * Min + 30_000, "10 min ago")]
    [InlineData(Hour - 1, "59 min ago")]
    public void The_age_label_says_just_now_then_seconds_then_minutes(long ageMs, string want)
    {
        Assert.Equal(want, DownloadTags.AgeLabel(1_760_000_000_000, 1_760_000_000_000 - ageMs));
    }

    [Fact]
    public void The_window_ends_at_sixty_minutes_exactly_like_the_phone()
    {
        var now = 1_760_000_000_000;
        Assert.Equal("59 min ago", DownloadTags.AgeLabel(now, now - Hour + 1));
        Assert.Null(DownloadTags.AgeLabel(now, now - Hour));            // the phone: "a window ago or more" is gone
        Assert.Null(DownloadTags.AgeLabel(now, now - Hour, compact: true));
    }

    [Theory]
    [InlineData(0, "Downloaded · just now")]
    [InlineData(4_999, "Downloaded · just now")]
    [InlineData(12_000, "Downloaded · 12s ago")]
    [InlineData(59_999, "Downloaded · 59s ago")]
    [InlineData(60_000, "Downloaded · 1m ago")]
    [InlineData(4 * Min, "Downloaded · 4m ago")]
    [InlineData(Hour - 1, "Downloaded · 59m ago")]
    public void The_compact_line_for_narrow_places_matches_the_phone(long ageMs, string want)
    {
        var now = 1_760_000_000_000;
        var st = new DownloadTagStatus(DownloadTagState.Recent, "", now - ageMs);
        Assert.Equal(want, st.Text(now, compact: true));
        Assert.Equal(want["Downloaded · ".Length..], DownloadTags.AgeLabel(now, now - ageMs, compact: true));
        // the full line is unchanged by the flag's existence
        Assert.StartsWith("Recently downloaded · ", st.Text(now));
        Assert.Equal(want.Contains("m ago") ? "Recently downloaded · " + (ageMs / Min) + " min ago" : "Recently downloaded · " + want["Downloaded · ".Length..], st.Text(now));
        Assert.Null(new DownloadTagStatus(DownloadTagState.Recent, "", now - Hour).Text(now, compact: true));
    }

    [Theory]
    [InlineData("Searching Soulseek...", "Downloading · Searching Soulseek...")]
    [InlineData("Waiting for a peer...", "Downloading · Waiting for a peer...")]
    [InlineData("Downloading 55%...", "Downloading 55%...")]
    [InlineData("Downloading from Internet Archive...", "Downloading from Internet Archive...")]
    [InlineData("Downloading", "Downloading")]
    [InlineData("  Downloading 40%...  ", "Downloading 40%...")]
    [InlineData("Filing into the library...", "Downloading · Filing into the library...")]
    [InlineData("downloading 3%...", "Downloading · downloading 3%...")]      // the rule is exact: a prefix test on the word as written
    [InlineData("", "Downloading")]
    public void A_stage_that_already_starts_with_Downloading_is_not_prefixed_again(string message, string want)
    {
        Assert.Equal(want, DownloadTags.DownloadingText(message));
        Assert.Equal(want, new DownloadTagStatus(DownloadTagState.Downloading, message).Text(1, compact: false));
        Assert.Equal(want, new DownloadTagStatus(DownloadTagState.Downloading, message).Text(1, compact: true));   // the short form does not change a running stage
    }

    [Theory]
    [InlineData(Hour + 1)]
    [InlineData(2 * Hour)]
    [InlineData(7L * 24 * Hour)]
    public void A_download_finished_more_than_an_hour_ago_has_no_label(long ageMs)
    {
        Assert.Null(DownloadTags.AgeLabel(1_760_000_000_000, 1_760_000_000_000 - ageMs));
        Assert.False(DownloadTags.IsRecent(1_760_000_000_000, 1_760_000_000_000 - ageMs));
    }

    [Fact]
    public void A_finish_time_in_the_future_is_believed_for_a_minute_at_most()
    {
        var now = 1_760_000_000_000;
        Assert.Equal("just now", DownloadTags.AgeLabel(now, now + 30_000));       // a clock a few seconds fast
        Assert.Equal("just now", DownloadTags.AgeLabel(now, now + Min));
        Assert.Null(DownloadTags.AgeLabel(now, now + Min + 1));
        Assert.Null(DownloadTags.AgeLabel(now, now + Hour));
    }

    [Fact]
    public void The_window_is_sixty_minutes_and_the_words_are_the_shared_ones()
    {
        Assert.Equal(TimeSpan.FromMinutes(60), DownloadTags.RecentWindow);
        Assert.Equal("Recently downloaded", DownloadTags.RecentWord);
        Assert.Equal("Downloading", DownloadTags.DownloadingWord);
        Assert.Equal("Couldn't download", DownloadTags.FailedText);
    }

    [Fact]
    public void The_whole_line_reads_the_way_the_phone_says_it()
    {
        var now = 1_760_000_000_000;
        Assert.Equal("Recently downloaded · 7s ago", new DownloadTagStatus(DownloadTagState.Recent, "", now - 7_000).Text(now));
        Assert.Equal("Recently downloaded · just now", new DownloadTagStatus(DownloadTagState.Recent, "", now - 100).Text(now));
        Assert.Equal("Recently downloaded · 12 min ago", new DownloadTagStatus(DownloadTagState.Recent, "", now - 12 * Min).Text(now));
        Assert.Equal("Downloading · Searching Soulseek...", new DownloadTagStatus(DownloadTagState.Downloading, "Searching Soulseek...").Text(now));
        Assert.Equal("Downloading", new DownloadTagStatus(DownloadTagState.Downloading, "").Text(now));
        Assert.Equal("Couldn't download", new DownloadTagStatus(DownloadTagState.Failed, "", now - Min).Text(now));
        Assert.Null(DownloadTagStatus.None.Text(now));
        // a recent line that has aged out between two draws says nothing
        Assert.Null(new DownloadTagStatus(DownloadTagState.Recent, "", now - 2 * Hour).Text(now));
    }

    [Fact]
    public void Status_order_is_running_then_recent_finish_then_recent_failure()
    {
        var now = 1_760_000_000_000;
        // running wins over everything
        var run = DownloadTags.Resolve("Searching Soulseek...", now - Min, now - 2 * Min, now - 3 * Min, now);
        Assert.Equal(DownloadTagState.Downloading, run.State); Assert.Equal("Searching Soulseek...", run.Message);
        // a recent finish wins over a failure, whichever came last
        var done = DownloadTags.Resolve(null, now - 10 * Min, now - Min, 0, now);
        Assert.Equal(DownloadTagState.Recent, done.State); Assert.Equal(now - 10 * Min, done.AtMs);
        // the newest of "finished" and "added as a new download"
        Assert.Equal(now - 2 * Min, DownloadTags.Resolve(null, now - 20 * Min, 0, now - 2 * Min, now).AtMs);
        // a failure alone
        var bad = DownloadTags.Resolve(null, 0, now - 5 * Min, 0, now);
        Assert.Equal(DownloadTagState.Failed, bad.State);
        // nothing recent: a normal library song
        Assert.Equal(DownloadTagState.None, DownloadTags.Resolve(null, now - 2 * Hour, now - 3 * Hour, now - 5 * Hour, now).State);
        Assert.Equal(DownloadTagState.None, DownloadTags.Resolve(null, 0, 0, 0, now).State);
    }

    [Fact]
    public void A_reported_finish_time_is_kept_between_ten_minutes_ago_and_now()
    {
        var now = 1_760_000_000_000;
        Assert.Equal(now - 5 * Min, DownloadTags.ClampReported(now - 5 * Min, now));
        Assert.Equal(now - 10 * Min, DownloadTags.ClampReported(now - 3 * Hour, now));   // not backdated
        Assert.Equal(now, DownloadTags.ClampReported(now + Hour, now));                   // not future dated
        Assert.Equal(now, DownloadTags.ClampReported(0, now));                            // missing: now
    }

    [Fact]
    public void Source_labels_match_the_phone()
    {
        Assert.Equal("New download", SourceLabel.Short(TrackSource.Cloud));
        Assert.Equal("Streaming from new download", SourceLabel.PlayingFrom(TrackSource.Cloud));
        Assert.Equal("Streaming from new download", SourceLabel.PlayingFromLine(TrackSource.Cloud));
        Assert.Equal("NAS", SourceLabel.Short(TrackSource.Nas));
        Assert.Equal("Playing from NAS", SourceLabel.PlayingFromLine(TrackSource.Nas));
        Assert.Equal("Playing from Jellyfin", SourceLabel.PlayingFromLine(TrackSource.Jellyfin));
        Assert.Contains("play", SourceLabel.StreamingHint);                                  // the hint stays
    }

    // ---- the lookup on a user's session ----

    sealed class Blocked : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); }
    }
    sealed class NoNet : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => throw new HttpRequestException("no network in tests");
    }
    sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }

    (DownloadManager M, DownloadLog Log, UserSession S) Make(string? dataDir = null, HttpMessageHandler? handler = null, string user = "tag-user")
    {
        var log = new DownloadLog(new DataPaths(dataDir ?? dir));
        var jf = new JellyfinClient(new HttpClient(new NoNet()), "dev");
        var m = new DownloadManager(new Factory(handler ?? new Blocked()), new WebCatalog(new HttpClient(new Blocked())), log, jf, new DownloadDigest((_, _) => { }));
        var s = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = user };
        return (m, log, s);
    }

    static async Task Until(Func<bool> done, int ms = 5000)
    {
        for (var i = 0; i < ms / 25 && !done(); i++) await Task.Delay(25);
        Assert.True(done(), "timed out");
    }

    static void SetUpServices(UserSession s) => new DownloadServices("http://slskd.test", "k", "downloads", "http://fm.test", "fk", "", "", "").WriteTo(s.Profile);

    static Track Cloud(string artist, string title, long addedMs, string path = "http://fm.test/file?path=Artist%2FArtist%20-%20Title.flac") =>
        new(path, title, artist, "Album", artist, 0, 0, 200_000, 0, null, addedMs, TrackSource.Cloud);

    [Fact]
    public void A_song_nothing_happened_to_has_no_tag()
    {
        var (m, _, s) = Make();
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "Artist", "Title").State);
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "", "").State);
    }

    [Fact]
    public async Task A_song_being_fetched_shows_its_stage_and_beats_an_earlier_finish()
    {
        var (m, _, s) = Make();
        SetUpServices(s);
        // it was downloaded 9 minutes ago (the phone said so) ...
        m.Reported(s, "Artist", "Title", "Album", 200_000, "soulseek", Now - 9 * Min, null, null);
        Assert.Equal(DownloadTagState.Recent, m.StatusOf(s, "Artist", "Title").State);
        // ... and is being fetched again (a Hi-Res copy, a retry): the running stage wins
        using var cts = new CancellationTokenSource();
        var task = m.FetchAsync(s, "Search", "Artist", "Title", "Album", null, 200_000, cts.Token);
        await Until(() => m.StatusOf(s, "Artist", "Title").Text(Now) == "Downloading · Searching Soulseek...");
        // the same song under another spelling is the same song (feat. tags and case do not matter)
        Assert.Equal(DownloadTagState.Downloading, m.StatusOf(s, "ARTIST", "Title (feat. Someone)").State);
        // a different song is not touched
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "Artist", "Other song").State);
        cts.Cancel();
        await Until(() => task.IsCompleted);
        // cancelled: not "Couldn't download" (nobody wanted it any more); the earlier finish is still what it says
        Assert.Equal(DownloadTagState.Recent, m.StatusOf(s, "Artist", "Title").State);
    }

    [Fact]
    public async Task A_failed_try_says_so_for_an_hour_and_never_hides_a_success()
    {
        var (m, log, s) = Make();
        m.Song(s, new CatalogSong("Artist", "Failing", "Album", null, 200_000, 1, 1));          // no download services: fails at once
        await Until(() => log.For(s.Id).Count == 1);
        var st = m.StatusOf(s, "Artist", "Failing");
        Assert.Equal(DownloadTagState.Failed, st.State);
        Assert.Equal("Couldn't download", st.Text(Now));
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "Artist", "Failing", null, Now + 61 * Min).State);   // an hour on: forgotten
        // a success of the same song, later or earlier, is what shows
        m.Reported(s, "Artist", "Failing", "Album", 0, null, Now, null, null);
        Assert.Equal(DownloadTagState.Recent, m.StatusOf(s, "Artist", "Failing").State);
    }

    [Fact]
    public void A_finished_download_is_recent_for_an_hour_and_then_a_normal_song()
    {
        var (m, _, s) = Make();
        var at = Now - 8 * Min;
        m.Reported(s, "Artist", "Title", "Album", 200_000, "soulseek", at, null, null);
        var t0 = m.StatusOf(s, "Artist", "Title");
        Assert.Equal(DownloadTagState.Recent, t0.State);
        Assert.Equal(at, t0.AtMs);
        Assert.Equal("Recently downloaded · 8 min ago", t0.Text(at + 8 * Min + 5_000));
        Assert.Equal("Recently downloaded · 59 min ago", m.StatusOf(s, "Artist", "Title", null, t0.AtMs + 59 * Min + 30_000).Text(t0.AtMs + 59 * Min + 30_000));
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "Artist", "Title", null, t0.AtMs + 61 * Min).State);
    }

    [Fact]
    public void A_new_download_song_counts_from_the_time_it_was_added_and_a_library_song_never_does()
    {
        var (m, _, s) = Make();
        Assert.Equal(DownloadTagState.Recent, m.StatusOf(s, "A", "T1", Cloud("A", "T1", Now - 10 * Min)).State);
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "A", "T2", Cloud("A", "T2", Now - 2 * Hour)).State);                 // a week old or two hours: no tag
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "A", "T3", Cloud("A", "T3", Now - 7L * 24 * Hour)).State);
        // a Jellyfin song "added" a minute ago (a file copied in by hand) was not downloaded here
        var jf = new Track("http://jf.test/Audio/abc/stream", "T4", "A", "Album", "A", 1, 1, 200_000, 2020, null, Now - Min, TrackSource.Jellyfin);
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "A", "T4", jf).State);
    }

    [Fact]
    public void A_new_download_song_older_than_an_hour_keeps_its_badge_and_has_an_empty_tag_line()
    {
        var (m, _, s) = Make();
        var old = Cloud("A", "Old One", Now - 3 * Hour);
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, old.Artist, old.Title, old).State);
        Assert.Null(m.StatusOf(s, old.Artist, old.Title, old).Text(Now));
        // the list badge and the source line only depend on where the song plays from, never on its age
        Assert.Equal("New download", SourceLabel.Short(old.Source));
        Assert.Equal("Streaming from new download", SourceLabel.PlayingFrom(old.Source));
    }

    [Fact]
    public async Task Autoplay_suggestions_being_fetched_are_listed_until_they_land()
    {
        var (m, _, s) = Make();
        SetUpServices(s);
        var guard = new StorageGuard(new UserStateStore(new DataPaths(dir), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));
        var planner = new AutoplayPlanner(new WebCatalog(new HttpClient(new Blocked())), m, new UserStateStore(new DataPaths(dir), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()), guard);
        var one = new CatalogSong("Artist", "Fetch One", "Album", null, 200_000, 0, 0);
        var two = new CatalogSong("Artist", "Fetch Two", "Album", null, 200_000, 0, 0);
        var other = new CatalogSong("Artist", "Not Fetched", "Album", null, 200_000, 0, 0);
        Assert.Empty(planner.Fetching(s, [one, two, other]));
        using var cts = new CancellationTokenSource();
        var t1 = m.FetchAsync(s, "Autoplay", one.Artist, one.Title, one.Album, null, one.DurationMs, cts.Token);
        var t2 = m.FetchAsync(s, "Autoplay", two.Artist, two.Title, two.Album, null, two.DurationMs, cts.Token);
        await Until(() => planner.Fetching(s, [one, two, other]).Count == 2);
        Assert.Equal(new[] { "Fetch One", "Fetch Two" }, planner.Fetching(s, [two, one, other]).Select(x => x.Title).OrderBy(x => x).ToArray());
        Assert.Equal("Fetch Two", planner.Fetching(s, [two, one, other])[0].Title);           // the lookup's order is kept
        Assert.Equal(2, m.RunningSongs(s).Count);
        Assert.Equal(DownloadTagState.Downloading, m.StatusOf(s, "Artist", "Fetch One").State);
        cts.Cancel();
        await Until(() => t1.IsCompleted && t2.IsCompleted);
        await Until(() => planner.Fetching(s, [one, two, other]).Count == 0);                   // gone from the list once over
        Assert.Empty(m.RunningSongs(s));
    }

    [Fact]
    public void Downloads_of_the_last_hour_survive_a_restart_and_older_ones_do_not_show()
    {
        var log = new DownloadLog(new DataPaths(dir));
        DownloadRecord Rec(string title, string outcome, DateTime at) => new() { Id = Guid.NewGuid().ToString("N"), UserId = "tag-user", Artist = "Artist", Title = title, Label = title, Kind = "Search", Outcome = outcome, StartedAt = at, FinishedAt = at };
        log.Add(Rec("Fresh", "done", DateTime.UtcNow.AddMinutes(-20)));
        log.Add(Rec("Two hours ago", "done", DateTime.UtcNow.AddHours(-2)));
        log.Add(Rec("Last week", "done", DateTime.UtcNow.AddDays(-7)));
        log.Add(Rec("Missed", "failed", DateTime.UtcNow.AddMinutes(-5)));
        log.Add(Rec("Missed long ago", "failed", DateTime.UtcNow.AddHours(-3)));
        var (m, _, s) = Make();                                    // a fresh manager over the same log folder: what a restart does
        Assert.Equal(DownloadTagState.Recent, m.StatusOf(s, "Artist", "Fresh").State);
        Assert.Equal("Recently downloaded · 20 min ago", m.StatusOf(s, "Artist", "Fresh").Text(Now));
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "Artist", "Two hours ago").State);
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "Artist", "Last week").State);
        Assert.Equal(DownloadTagState.Failed, m.StatusOf(s, "Artist", "Missed").State);
        Assert.Equal(DownloadTagState.None, m.StatusOf(s, "Artist", "Missed long ago").State);
        Assert.True(m.RecentlyFetched(s, "Artist", "Fresh"));      // and Autoplay does not fetch it again
        Assert.False(m.RecentlyFetched(s, "Artist", "Two hours ago"));
    }

    [Fact]
    public async Task What_the_phone_reports_is_logged_playable_and_not_fetched_again()
    {
        var (m, log, s) = Make();
        var finished = Now - 2 * Min;
        var track = Cloud("JME", "P", finished, "http://fm.test/file?path=JME%2FJME%20-%20P.flac");
        Assert.Null(s.Library.Find("P", "JME"));
        m.Reported(s, "JME", "P", "Famous?", 123_000, "soulseek", finished, "JME/JME - P.flac", track);
        // playable at once, as a new download song
        var have = s.Library.Find("P", "JME");
        Assert.NotNull(have);
        Assert.Equal(TrackSource.Cloud, have!.Source);
        Assert.Equal(track.Path, have.Path);
        // the tag, the log and the "already fetched" memory
        Assert.Equal(DownloadTagState.Recent, m.StatusOf(s, "JME", "P", have).State);
        var r = log.For(s.Id)[0];
        Assert.Equal("Phone", r.Kind); Assert.True(r.Done); Assert.Equal("soulseek", r.Source); Assert.Equal("JME/JME - P.flac", r.File);
        Assert.Equal("P", r.Title); Assert.Equal("JME", r.Artist); Assert.Equal("Famous?", r.Album); Assert.Equal(123_000, r.DurationMs);
        Assert.True(m.RecentlyFetched(s, "JME", "P"));
        // autoplay asking for it again gets "fetched recently" and no new job
        var again = await m.FetchAsync(s, "Autoplay", "JME", "P", "Famous?", null, 123_000);
        Assert.True(again.Ok); Assert.Empty(again.Tracks);
        Assert.Empty(m.JobsOf(s));
    }

    [Fact]
    public void A_report_with_no_path_still_marks_the_song_but_adds_nothing_to_the_library()
    {
        var (m, _, s) = Make();
        m.Reported(s, "JME", "P", "", 0, null, Now, null, null);
        Assert.Null(s.Library.Find("P", "JME"));
        Assert.Equal(DownloadTagState.Recent, m.StatusOf(s, "JME", "P").State);
    }

    // ---- limits on the report ----

    [Fact]
    public void At_most_two_hundred_reports_per_user_per_hour()
    {
        var user = "limit-" + Guid.NewGuid().ToString("N");
        var t = 1_760_000_000_000;
        for (var i = 0; i < DownloadManager.ReportsPerHour; i++) Assert.True(DownloadManager.TakeReportTurn(user, t + i), "report " + i);
        Assert.False(DownloadManager.TakeReportTurn(user, t + 1000));
        Assert.True(DownloadManager.TakeReportTurn("someone-else-" + user, t + 1000));        // per user
        Assert.False(DownloadManager.TakeReportTurn(user, t + Hour - 1));                       // the first ones are still inside the hour
        Assert.True(DownloadManager.TakeReportTurn(user, t + Hour + DownloadManager.ReportsPerHour));   // an hour later there is room again
    }

    [Theory]
    [InlineData("http://fm.test/file?path=JME%2FJME%20-%20P.flac", true)]
    [InlineData("http://fm.test/file?path=Artist/Album/01%20-%20Song.mp3", true)]
    [InlineData("http://fm.test/file?path=", false)]                                             // nothing after path=
    [InlineData("http://evil.test/file?path=JME%2FJME.flac", false)]                             // another host
    [InlineData("http://fm.test.evil.test/file?path=JME%2FJME.flac", false)]                     // looks like it, is not
    [InlineData("http://fm.test/move?path=JME%2FJME.flac", false)]                                // another endpoint of the same service
    [InlineData("http://fm.test/fetch", false)]
    [InlineData("http://fm.test/file?path=..%2F..%2Fetc%2Fpasswd", false)]                       // out of the music folder
    [InlineData("http://fm.test/file?path=JME%2F..%2F..%2Fx.flac", false)]
    [InlineData("http://fm.test/file?path=%2Fetc%2Fpasswd", false)]                              // absolute
    [InlineData("http://fm.test/file?path=C%3A%5CWindows%5Cx.flac", false)]
    [InlineData("http://fm.test/file?path=JME%2FP.flac&path=other", false)]                       // a second parameter
    [InlineData("http://fm.test/file?path=JME%2FP.flac%00.mp3", false)]                           // control character
    [InlineData("http://fm.test/file?path=a#b", false)]
    [InlineData("file:///C:/Windows/system.ini", false)]
    [InlineData("JME/JME - P.flac", false)]
    [InlineData("", false)]
    public void A_reported_path_is_only_accepted_when_it_is_a_file_of_the_users_own_file_mover(string path, bool ok)
    {
        Assert.Equal(ok, DownloadReports.AcceptPath("http://fm.test", path));
        Assert.Equal(ok, DownloadReports.AcceptPath("http://fm.test/", path));                   // a trailing slash in the setting is fine
    }

    [Fact]
    public void No_path_is_accepted_when_the_user_has_no_file_mover()
    {
        Assert.False(DownloadReports.AcceptPath("", "http://fm.test/file?path=JME%2FP.flac"));
        Assert.False(DownloadReports.AcceptPath("   ", "/file?path=JME%2FP.flac"));
        Assert.Equal("JME/JME - P.flac", DownloadReports.RelativeFile("http://fm.test/file?path=JME%2FJME%20-%20P.flac"));
    }
}
