using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>A Jellyfin that is just enough for the library listing: items with DateLastSaved, MinDateLastSaved (inclusive, like Jellyfin), paging, counts, ids-only listings.</summary>
sealed class FakeJellyfin : HttpMessageHandler
{
    public readonly List<JsonObject> Items = [];
    public readonly List<string> Requests = [];
    public Func<HttpRequestMessage, Task>? Before;
    public bool FailDelta;
    long clock = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;

    public static string Hex(int n) => n.ToString("x32");
    public string Stamp() => new DateTime(clock += 10_000_000L, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);   // each save is a second later

    public JsonObject Song(int n, string? name = null, string? album = null) => new()
    {
        ["Id"] = Hex(n), ["Name"] = name ?? $"Song {n}", ["Artists"] = new JsonArray($"Artist {n % 7}"), ["AlbumArtist"] = $"Artist {n % 7}", ["Album"] = album ?? $"Album {n / 12}",
        ["AlbumId"] = Hex(100000 + n / 12), ["AlbumPrimaryImageTag"] = "albumtag" + n / 12, ["ImageTags"] = new JsonObject { ["Primary"] = "own" + n },
        ["IndexNumber"] = n % 12 + 1, ["ParentIndexNumber"] = 1, ["RunTimeTicks"] = 1800000000L, ["ProductionYear"] = 2020, ["Path"] = $"/music/{n}.flac",
        ["DateCreated"] = "2025-01-01T00:00:00.0000000Z", ["DateLastSaved"] = Stamp(),
        ["MediaSources"] = new JsonArray(new JsonObject
        {
            ["Container"] = "flac", ["Path"] = $"/music/{n}.flac", ["Size"] = 30000000L, ["Bitrate"] = 1000000,
            ["MediaStreams"] = new JsonArray(new JsonObject { ["Type"] = "Audio", ["Codec"] = "flac", ["Channels"] = 2, ["BitRate"] = 1000000, ["SampleRate"] = n % 5 == 0 ? 96000 : 44100, ["BitDepth"] = n % 5 == 0 ? 24 : 16 }),
        }),
    };

    public FakeJellyfin(int songs) { for (var i = 1; i <= songs; i++) Items.Add(Song(i)); }

    public void Touch(int n, string newName) { var i = Items.FindIndex(o => o["Id"]!.GetValue<string>() == Hex(n)); Items[i] = Song(n, newName); }

    static HttpResponseMessage Json(JsonNode n) => new(HttpStatusCode.OK) { Content = new StringContent(n.ToJsonString(), Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var u = req.RequestUri!;
        lock (Requests) Requests.Add(u.PathAndQuery);
        if (Before is not null) await Before(req);
        var path = u.AbsolutePath; var q = System.Web.HttpUtility.ParseQueryString(u.Query);
        if (path.StartsWith("/DisplayPreferences/")) return Json(new JsonObject());
        if (path == "/Users/Me") return Json(new JsonObject { ["Id"] = "u1", ["Name"] = "tester", ["Policy"] = new JsonObject { ["IsAdministrator"] = true } });
        if (path == "/Users/u1/Items" && q["IncludeItemTypes"] == "Audio")
        {
            IEnumerable<JsonObject> set = Items;
            var min = q["MinDateLastSaved"];
            if (min is not null)
            {
                if (FailDelta) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                var m = DateTime.Parse(min, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                set = set.Where(o => DateTime.Parse(o["DateLastSaved"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) >= m);   // inclusive, like Jellyfin
            }
            var all = set.ToList();
            var start = int.Parse(q["StartIndex"] ?? "0"); var limit = int.Parse(q["Limit"] ?? "100");
            var page = all.Skip(start).Take(limit).Select(o => (JsonNode)JsonNode.Parse(o.ToJsonString())!).ToList();
            if (q["Fields"] is { Length: 0 }) foreach (var p in page.Cast<JsonObject>()) { foreach (var k in p.Select(x => x.Key).Where(k => k != "Id").ToList()) p.Remove(k); }
            var arr = new JsonArray(); foreach (var p in page) arr.Add(p);
            return Json(new JsonObject { ["Items"] = arr, ["TotalRecordCount"] = all.Count, ["StartIndex"] = start });
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public int Count(Func<string, bool> pick) { lock (Requests) return Requests.Count(pick); }
}

public class SyncTests
{
    static readonly JellyfinAccount Acct = new("http://jf", "u1", "tester", "tok");

    static (UserSession S, JellyfinClient Jf, List<(Track, MediaInfo)> Facts) Session(FakeJellyfin fake, string cache)
    {
        var jf = new JellyfinClient(new HttpClient(fake), "dev");
        var facts = new List<(Track, MediaInfo)>();
        var s = new UserSession("jellyfin", Acct, null) { Id = "me", CachePath = cache, FactsSink = (t, m) => { lock (facts) facts.Add((t, m)); } };
        return (s, jf, facts);
    }

    static string Cache() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "library", "me.json");

    static bool IsListing(string r) => r.Contains("/Users/u1/Items?") && r.Contains("IncludeItemTypes=Audio") && r.Contains("Fields=Path");
    static bool IsDelta(string r) => IsListing(r) && r.Contains("MinDateLastSaved=");
    static bool IsFull(string r) => IsListing(r) && !r.Contains("MinDateLastSaved=");
    static bool IsCount(string r) => r.Contains("Limit=1&") && r.Contains("IncludeItemTypes=Audio");
    static bool IsIds(string r) => r.Contains("Limit=2000");
    static bool IsOneSong(string r) => System.Text.RegularExpressions.Regex.IsMatch(r, @"^/Users/u1/Items/[0-9a-f]{32}");

    [Fact]
    public async Task The_first_sync_lists_the_library_in_pages_with_the_streams_and_needs_no_per_song_request()
    {
        var fake = new FakeJellyfin(1200);
        var (s, jf, facts) = Session(fake, Cache());
        await s.LoadAsync(jf);
        Assert.Equal(1200, s.Library.Songs.Count);
        Assert.Equal(3, fake.Count(IsFull));                       // 500 + 500 + 200
        Assert.Equal(0, fake.Count(IsOneSong));
        Assert.Equal(1200, facts.Count);                          // every song's real facts came with the listing
        Assert.Contains(facts, f => f.Item2.HiRes);
        Assert.NotNull(s.SyncCursor);
        Assert.Equal(1200, s.KnownSongCount);
        Assert.True(File.Exists(s.CachePath + ".cursor") && File.Exists(s.CachePath + ".ids"));
    }

    [Fact]
    public async Task Songs_appear_as_pages_arrive_not_only_when_the_whole_listing_is_done()
    {
        var fake = new FakeJellyfin(1200);
        var release = new TaskCompletionSource();
        fake.Before = async r => { if (IsFull(r.RequestUri!.PathAndQuery) && r.RequestUri.Query.Contains("StartIndex=0") is false) await release.Task; };
        var (s, jf, _) = Session(fake, Cache());
        var load = s.LoadAsync(jf);
        var until = Environment.TickCount64 + 5000;
        while (s.Library.Songs.Count < 500 && Environment.TickCount64 < until) await Task.Delay(10);
        Assert.Equal(500, s.Library.Songs.Count);                 // the first page is on screen while the other two are still on their way
        Assert.False(load.IsCompleted);
        release.SetResult();
        await load;
        Assert.Equal(1200, s.Library.Songs.Count);
    }

    [Fact]
    public async Task After_a_restart_only_changes_are_asked_for_changed_songs_replace_new_ones_arrive_and_removed_ones_go()
    {
        var cache = Cache();
        var fake = new FakeJellyfin(1200);
        var (s1, jf1, _) = Session(fake, cache);
        await s1.LoadAsync(jf1);
        var cursor1 = s1.SyncCursor;

        fake.Touch(5, "Renamed Five");                      // changed in Jellyfin
        fake.Items.Add(fake.Song(1201));                           // added
        fake.Items.RemoveAll(o => o["Id"]!.GetValue<string>() == FakeJellyfin.Hex(10));   // deleted
        fake.Requests.Clear();

        var (s2, jf2, facts2) = Session(fake, cache);              // "restart": a new session over the same cache files
        await s2.LoadAsync(jf2);
        Assert.Equal(cursor1, s1.SyncCursor);
        Assert.Equal(0, fake.Count(IsFull));                       // no full listing
        Assert.Equal(1, fake.Count(IsDelta));
        Assert.Equal(0, fake.Count(IsOneSong));
        // the question carried the cursor moved on by one tick (MinDateLastSaved is inclusive: the newest song must not come back every time)
        Assert.Contains(fake.Requests, r => r.Contains("MinDateLastSaved=" + Uri.EscapeDataString(JellyfinClient.AfterCursor(cursor1!))));
        Assert.Equal(2, facts2.Count);                             // the two changed songs only (the renamed one and the new one)
        Assert.Equal(1200, s2.Library.Songs.Count);               // 1200 - 1 removed + 1 added
        Assert.Contains(s2.Library.Songs, t => t.Title == "Renamed Five");
        Assert.DoesNotContain(s2.Library.Songs, t => t.Title == "Song 5");
        Assert.Contains(s2.Library.Songs, t => t.Title == "Song 1201");
        Assert.DoesNotContain(s2.Library.Songs, t => t.JellyfinId == FakeJellyfin.Hex(10));
        Assert.True(string.CompareOrdinal(s2.SyncCursor, cursor1) > 0);
        // and the next start has the new cursor on disk
        Assert.Equal(s2.SyncCursor, File.ReadAllText(cache + ".cursor").Trim());
    }

    [Fact]
    public async Task A_warm_start_with_nothing_changed_costs_a_delta_and_a_count_not_a_listing()
    {
        var cache = Cache();
        var fake = new FakeJellyfin(300);
        var (s1, jf1, _) = Session(fake, cache);
        await s1.LoadAsync(jf1);
        fake.Requests.Clear();
        var (s2, jf2, facts2) = Session(fake, cache);
        await s2.LoadAsync(jf2);
        Assert.Equal(300, s2.Library.Songs.Count);
        Assert.Empty(facts2);                                       // the newest song did not come back
        Assert.Equal(0, fake.Count(IsFull));
        Assert.Equal(1, fake.Count(IsDelta));
        Assert.Equal(1, fake.Count(IsCount));
        Assert.Equal(0, fake.Count(IsIds));                         // the count matched: no id list needed
        Assert.True(fake.Requests.Count <= 5, "profile, who am I, delta, count: " + string.Join(" | ", fake.Requests));
    }

    [Fact]
    public async Task Duplicates_that_the_library_merges_away_do_not_make_every_start_a_full_listing()
    {
        var cache = Cache();
        var fake = new FakeJellyfin(50);
        // the same song twice in Jellyfin (a FLAC and an MP3 of it): the library keeps one, Jellyfin counts two
        fake.Items.Add(fake.Song(7)); fake.Items[^1]["Id"] = FakeJellyfin.Hex(9007);
        var (s1, jf1, _) = Session(fake, cache);
        await s1.LoadAsync(jf1);
        Assert.Equal(50, s1.Library.Songs.Count);
        Assert.Equal(51, s1.KnownSongCount);
        fake.Requests.Clear();
        var (s2, jf2, _) = Session(fake, cache);
        await s2.LoadAsync(jf2);
        Assert.Equal(0, fake.Count(IsFull));
        Assert.Equal(0, fake.Count(IsIds));
        Assert.Equal(50, s2.Library.Songs.Count);
    }

    [Fact]
    public async Task A_delta_with_no_change_does_not_touch_the_library_and_a_socket_message_that_names_a_removed_song_drops_it()
    {
        var fake = new FakeJellyfin(100);
        var (s, jf, _) = Session(fake, Cache());
        await s.LoadAsync(jf);
        var lib = s.Library;
        Assert.Equal(0, await s.SyncDeltaAsync(jf));
        Assert.Same(lib, s.Library);                               // not rebuilt, not saved again
        var n = await s.SyncDeltaAsync(jf, [FakeJellyfin.Hex(3), FakeJellyfin.Hex(4).ToUpperInvariant()]);
        Assert.Equal(2, n);
        Assert.Equal(98, s.Library.Songs.Count);
        Assert.DoesNotContain(s.Library.Songs, t => t.JellyfinId == FakeJellyfin.Hex(3));
        Assert.Equal(98, s.KnownSongCount);
    }

    [Fact]
    public async Task A_changed_song_is_replaced_by_a_delta_without_a_full_listing()
    {
        var fake = new FakeJellyfin(100);
        var (s, jf, _) = Session(fake, Cache());
        await s.LoadAsync(jf);
        fake.Touch(42, "Forty Two Renamed");
        fake.Requests.Clear();
        Assert.Equal(1, await s.SyncDeltaAsync(jf));
        Assert.Equal(1, fake.Count(IsDelta)); Assert.Equal(0, fake.Count(IsFull));
        Assert.Single(s.Library.Songs, t => t.JellyfinId == FakeJellyfin.Hex(42));
        Assert.Equal("Forty Two Renamed", s.Library.Songs.First(t => t.JellyfinId == FakeJellyfin.Hex(42)).Title);
        Assert.Equal(100, s.Library.Songs.Count);
    }

    [Fact]
    public async Task Reconcile_notices_songs_removed_without_a_message_with_one_count_and_one_id_list()
    {
        var fake = new FakeJellyfin(100);
        var (s, jf, _) = Session(fake, Cache());
        await s.LoadAsync(jf);
        Assert.Equal(0, await s.ReconcileAsync(jf));               // nothing differs
        fake.Items.RemoveAll(o => o["Id"]!.GetValue<string>() is var id && (id == FakeJellyfin.Hex(1) || id == FakeJellyfin.Hex(2)));
        fake.Requests.Clear();
        Assert.Equal(2, await s.ReconcileAsync(jf));
        Assert.Equal(98, s.Library.Songs.Count);
        Assert.Equal(1, fake.Count(IsCount)); Assert.Equal(1, fake.Count(IsIds)); Assert.Equal(0, fake.Count(IsFull));
        // a song Jellyfin has that this library never saw: the caller must list again
        fake.Items.Add(fake.Song(500)); fake.Items[^1]["DateLastSaved"] = "2001-01-01T00:00:00.0000000Z";   // older than the cursor: a delta would never bring it
        Assert.Equal(-1, await s.ReconcileAsync(jf));
    }

    [Fact]
    public async Task When_Jellyfin_fails_the_delta_keeps_the_saved_library_instead_of_listing_everything_again()
    {
        var cache = Cache();
        var fake = new FakeJellyfin(200);
        var (s1, jf1, _) = Session(fake, cache);
        await s1.LoadAsync(jf1);
        fake.FailDelta = true; fake.Requests.Clear();
        var (s2, jf2, _) = Session(fake, cache);
        await s2.LoadAsync(jf2);
        Assert.Equal(200, s2.Library.Songs.Count);
        Assert.Equal(0, fake.Count(IsFull));
        Assert.True(fake.Count(IsDelta) <= 3, "the first try and at most two retries");
    }

    [Fact]
    public void The_cursor_is_moved_on_by_one_tick_so_the_inclusive_question_does_not_return_the_last_song_again()
    {
        var c = "2026-03-01T12:00:05.1234567Z";
        var after = JellyfinClient.AfterCursor(c);
        Assert.Equal("2026-03-01T12:00:05.1234568Z", after);
        Assert.Equal("not a date", JellyfinClient.AfterCursor("not a date"));
    }

    [Fact]
    public void The_songs_of_an_album_share_one_cover_unless_it_is_a_compilation()
    {
        string KeyOf(JsonObject o) => JellyfinClient.ToTrack("http://jf", o).ArtKey!;
        var fake = new FakeJellyfin(0);
        var a = KeyOf(fake.Song(24)); var b = KeyOf(fake.Song(25));
        Assert.Equal(a, b);                                        // two songs of album 2: one cover, one request, one file
        Assert.Equal("jf" + FakeJellyfin.Hex(100002) + "~albumtag2", a);
        // no album picture: the song's own
        var s = fake.Song(30); s.Remove("AlbumPrimaryImageTag");
        Assert.Equal("jf" + FakeJellyfin.Hex(30) + "~own30", KeyOf(s));
        // a compilation's songs get their own album's cover from the catalog (the existing rule is kept), whatever pictures Jellyfin has
        var comp = fake.Song(40, album: "Now That's What I Call Music 99"); comp["AlbumArtist"] = "Various Artists";
        Assert.StartsWith("it", KeyOf(comp));
    }

    [Fact]
    public void A_listing_item_with_odd_shapes_is_still_a_song()
    {
        var fake = new FakeJellyfin(0);
        var s = fake.Song(3); s["Genres"] = "Rock"; s["Artists"] = "Solo";   // plain strings where Jellyfin sends arrays
        var t = JellyfinClient.ToTrack("http://jf", s);
        Assert.Equal("Song 3", t.Title);
    }

    [Fact]
    public void The_cover_of_what_another_device_plays_has_the_same_key_the_library_uses()
    {
        var fake = new FakeJellyfin(0);
        var item = fake.Song(24);
        Assert.Equal(JellyfinClient.ToTrack("http://jf", item).ArtKey, JellyfinClient.ItemArtKey(item));
        Assert.Null(JellyfinClient.ItemArtKey(null));
    }
}
