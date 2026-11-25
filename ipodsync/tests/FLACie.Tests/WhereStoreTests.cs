using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 25 Nov: "a way to pick where the phone was like 1 hour ago". Each account keeps a day of play states per device, sampled and capped.</summary>
public class WhereStoreTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "flacie-where-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }
    WhereStore Store() => new(new DataPaths(dir));

    const long T0 = 1_800_000_000_000;   // a fixed "now" for the tests
    const long Min = 60_000;
    static WhereState S(long at, string song, long pos = 0, bool paused = false, string dev = "phone1", string? ctx = "Road trip", List<string>? queue = null) =>
        WhereStore.From(new WhereReport(dev, dev == "phone1" ? "Pixel 8" : dev, null, "Song " + song, "Artist", "Album", null, "id-" + song, null, pos, 240_000, paused,
            queue ?? ["id-a", "id-" + song, "id-z"], 1, ctx), at)!;

    [Fact]
    public void A_new_song_is_kept_at_once_the_same_song_once_a_minute()
    {
        var w = Store();
        Assert.True(w.Record("acct", S(T0, "a")));
        Assert.False(w.Record("acct", S(T0 + 20_000, "a", 20_000)));            // the same song 20 s later: nothing new
        Assert.True(w.Record("acct", S(T0 + Min, "a", 60_000)));                // a minute on
        Assert.True(w.Record("acct", S(T0 + Min + 5_000, "b")));                 // the song changed
        Assert.True(w.Record("acct", S(T0 + Min + 9_000, "b", 4_000, paused: true)));   // paused
        Assert.False(w.Record("acct", S(T0 + 5 * Min, "b", 4_000, paused: true)));      // still paused: nothing new however long
    }

    [Fact]
    public void A_state_that_arrives_after_a_newer_one_is_dropped()
    {
        var w = Store();
        w.Record("acct", S(T0 + Min, "a"));
        Assert.False(w.Record("acct", S(T0, "b")));
    }

    [Fact]
    public void Accounts_and_devices_are_kept_apart()
    {
        var w = Store();
        w.Record("ann", S(T0, "a"));
        w.Record("ann", S(T0 + 1000, "b", dev: "web:Edge on Windows"));
        w.Record("bob", S(T0, "c"));
        var ann = w.Devices("ann", T0 + 2000);
        Assert.Equal(["web:Edge on Windows", "phone1"], ann.Select(d => d.DeviceId));   // the device seen last first
        Assert.Equal(["Song c"], w.All("bob", T0 + 2000).Select(x => x.Title));
        Assert.Null(w.Find("bob", ann[0].States[0].Id, T0 + 2000));                     // one account can't fetch another's state
    }

    [Fact]
    public void Older_than_a_day_is_dropped()
    {
        var w = Store();
        w.Record("acct", S(T0, "old"));
        w.Record("acct", S(T0 + WhereStore.KeepMs + Min, "new"));
        Assert.Equal(["Song new"], w.All("acct", T0 + WhereStore.KeepMs + Min).Select(x => x.Title));
    }

    [Fact]
    public void A_song_the_device_moved_on_from_keeps_where_it_was_left_and_one_each_ten_minutes()
    {
        var w = Store();
        // a 25-minute mix sampled every minute, then the next song
        for (var m = 0; m <= 25; m++) w.Record("acct", S(T0 + m * Min, "mix", m * Min));
        Assert.Equal(26, w.All("acct", T0 + 26 * Min).Count);   // still on it: every minute kept
        w.Record("acct", S(T0 + 26 * Min, "next"));
        var mix = w.All("acct", T0 + 26 * Min).Where(x => x.Title == "Song mix").Select(x => x.PositionMs / Min).ToList();
        Assert.Equal([5L, 15L, 25L], mix);   // where it was left (25) and ten minutes apart before that
    }

    [Fact]
    public void At_most_MaxStates_per_account_the_newest_kept()
    {
        var w = Store();
        for (var i = 0; i < WhereStore.MaxStates + 50; i++) w.Record("acct", S(T0 + i * 1000L, "s" + i));
        var all = w.All("acct", T0 + (WhereStore.MaxStates + 50) * 1000L);
        Assert.Equal(WhereStore.MaxStates, all.Count);
        Assert.Equal("Song s" + (WhereStore.MaxStates + 49), all[^1].Title);
    }

    [Fact]
    public void States_and_their_queues_survive_a_restart()
    {
        var q = Enumerable.Range(0, 50).Select(i => "q" + i).ToList();
        Store().Record("acct", S(T0, "a", 133_000, queue: q));
        var back = Store().All("acct", T0 + 1000).Single();
        Assert.Equal((133_000L, "Road trip", 50, 1), (back.PositionMs, back.Context, back.Queue.Count, back.Index));
        // one queue shared by a run of states is written once
        var w = Store();
        for (var m = 1; m <= 5; m++) w.Record("acct", S(T0 + m * Min, "a", m * Min, queue: q));
        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(dir, "where")).Single());
        Assert.Equal(1, text.Split("\"q49\"").Length - 1);
    }

    [Fact]
    public void A_report_is_cut_to_the_limits_and_a_long_queue_keeps_the_song()
    {
        var q = Enumerable.Range(0, 1000).Select(i => "id" + i).ToList();
        var s = WhereStore.From(new WhereReport("dev", new string('d', 300), null, new string('t', 900), "A", null, null, "id700", null, -5, 1, false, q, 700, null), T0)!;
        Assert.Equal(WhereStore.MaxQueue, s.Queue.Count);
        Assert.Equal("id700", s.Queue[s.Index]);
        Assert.Equal(60, s.Device.Length);
        Assert.Equal(200, s.Title.Length);
        Assert.Equal(0, s.PositionMs);
        Assert.Equal(T0, s.At);   // the server's clock, not the device's
        Assert.Null(WhereStore.From(new WhereReport("dev", "x", null, "", "", null, null, null, null, 0, 0, false, null, 0, null), T0));   // nothing playing
    }

    [Theory]
    [InlineData(10_000, "just now")]
    [InlineData(5 * Min, "5 min ago")]
    [InlineData(59 * Min, "59 min ago")]
    [InlineData(61 * Min, "1 h ago")]
    [InlineData(23 * 60 * Min, "23 h ago")]
    [InlineData(25 * 60 * Min, "yesterday")]
    public void Relative_times(long ago, string words) => Assert.Equal(words, DeviceLabels.Ago(T0 - ago, T0));

    [Fact]
    public void Positions_and_kinds_in_words()
    {
        Assert.Equal("at 2:13 in Road trip", DeviceLabels.Where(133_000, "Road trip"));
        Assert.Equal("at 1:02:05", DeviceLabels.Where(3_725_000, null));
        Assert.Equal("Phone", DeviceLabels.Kind("Pixel 8 Phone"));
        Assert.Equal("Phone", DeviceLabels.Kind("SM-S918B"));
        Assert.Equal("Browser", DeviceLabels.Kind("Edge on Windows"));
        Assert.Equal("Computer", DeviceLabels.Kind("FLACie for Windows"));
        Assert.Equal("Player", DeviceLabels.Kind("Kitchen"));
    }

    // ---- the endpoints ----

    static UserSession Person(string id) => new("local", null, null) { Id = id };
    static WhereReport Report(string song = "a", string dev = "phone1") => new(dev, "Pixel 8", "Phone", "Song " + song, "Artist", null, null, "id-" + song, null, 1000, 2000, false, ["id-" + song], 0, "Mix");

    [Fact]
    public void Endpoints_need_a_caller()
    {
        var w = Store();
        Assert.Equal(401, ((IStatusCodeHttpResult)WhereApi.Report(w, null, Report(), T0)).StatusCode);
        Assert.Equal(401, ((IStatusCodeHttpResult)WhereApi.List(w, null, T0)).StatusCode);
        Assert.Equal(401, ((IStatusCodeHttpResult)WhereApi.One(w, null, "x", T0)).StatusCode);
    }

    [Fact]
    public void A_phone_cannot_write_as_a_browser_or_send_nothing()
    {
        var w = Store(); var ann = Person("ann");
        Assert.Equal(400, ((IStatusCodeHttpResult)WhereApi.Report(w, ann, Report(dev: "web:Edge on Windows"), T0)).StatusCode);
        Assert.Equal(400, ((IStatusCodeHttpResult)WhereApi.Report(w, ann, null, T0)).StatusCode);
        Assert.Empty(w.All("ann", T0));
    }

    [Fact]
    public async Task Report_then_list_then_fetch_one_with_its_queue()
    {
        var w = Store(); var ann = Person("ann");
        Assert.Equal(200, ((IStatusCodeHttpResult)WhereApi.Report(w, ann, Report("a"), T0)).StatusCode);
        WhereApi.Report(w, ann, Report("b"), T0 + Min);
        var list = await Body(WhereApi.List(w, ann, T0 + 2 * Min));
        var dev = list.GetProperty("devices")[0];
        Assert.Equal("Pixel 8", dev.GetProperty("device").GetString());
        var states = dev.GetProperty("states");
        Assert.Equal("Song b", states[0].GetProperty("title").GetString());   // newest first
        Assert.Equal("Mix", states[0].GetProperty("context").GetString());
        Assert.False(states[0].TryGetProperty("queue", out _));               // the list is light: the queue comes with one state
        var one = await Body(WhereApi.One(w, ann, states[1].GetProperty("id").GetString()!, T0 + 2 * Min));
        Assert.Equal("id-a", one.GetProperty("queue")[0].GetString());
        Assert.Equal(404, ((IStatusCodeHttpResult)WhereApi.One(w, ann, "nope", T0)).StatusCode);
    }

    // the body as the endpoint writes it (camelCase, as ASP.NET's JSON results are)
    static Task<System.Text.Json.JsonElement> Body(IResult r) => Task.FromResult(System.Text.Json.JsonSerializer.SerializeToElement(((IValueHttpResult)r).Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
}
