using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>The listening log: skips and finishes, no double counting, time order, and folding old plays.</summary>
public class ListenLogTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "flacie-listen-" + Guid.NewGuid().ToString("N"));
    readonly UserSession me = new("local", null, null) { Id = "ann" };
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    static long Ms(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();
    static ListenEvent Play(string id, long at, double heard = 100, double length = 200, string path = "/m/a.flac") => new(id, ListenKind.Play, at, path, "A", "X", heard, length);

    [Fact] public void Skip_is_under_30_seconds()
    {
        Assert.True(ListenClass.IsSkip(Play("1", 0, 29)));
        Assert.False(ListenClass.IsSkip(Play("2", 0, 31)));
    }

    [Fact] public void Finish_is_90_percent()
    {
        Assert.True(ListenClass.IsFinish(Play("1", 0, 180)));
        Assert.False(ListenClass.IsFinish(Play("2", 0, 170)));
    }

    [Fact] public void Append_ignores_an_id_already_kept()
    {
        var store = new ListenLogStore(new DataPaths(dir));
        store.Append(me, [Play("1", 10)]);
        store.Append(me, [Play("1", 10), Play("1", 10)]);
        Assert.Single(store.Read(me));
        Assert.Single(new ListenLogStore(new DataPaths(dir)).Read(me));
    }

    [Fact] public void Read_returns_events_in_time_order()
    {
        var store = new ListenLogStore(new DataPaths(dir));
        store.Append(me, [Play("b", 30), Play("a", 10), Play("c", 20)]);
        Assert.Equal(["a", "c", "b"], store.Read(me).Select(e => e.Id));
    }

    [Fact] public void Thin_folds_events_older_than_a_year_into_daily_totals()
    {
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var day = now.AddDays(-400).Date;
        var store = new ListenLogStore(new DataPaths(dir));
        store.Append(me, [Play("o1", Ms(day.AddHours(9)), 100), Play("o2", Ms(day.AddHours(15)), 50), Play("new", Ms(now.AddDays(-2)), 70)]);
        store.Thin(me, now);
        var got = store.Read(me);
        Assert.Equal(2, got.Count);
        Assert.Equal(150, got[0].Heard);
        Assert.Equal(ListenKind.Play, got[0].Kind);
        Assert.Equal("new", got[1].Id);
        Assert.Equal(70, got[1].Heard);
    }

    string LogFile() => Path.Combine(dir, "listen", "ann.jsonl");

    [Fact] public void Thin_keeps_a_damaged_line_as_it_was()
    {
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var store = new ListenLogStore(new DataPaths(dir));
        store.Append(me, [Play("o1", Ms(now.AddDays(-400)), 100), Play("new", Ms(now.AddDays(-2)), 70)]);
        File.AppendAllText(LogFile(), "{not json\n");
        store.Thin(me, now);
        Assert.Contains("{not json", File.ReadAllLines(LogFile()));
        Assert.Equal(2, store.Read(me).Count);
    }

    [Fact] public void Append_after_a_torn_last_line_starts_on_a_new_line()
    {
        var store = new ListenLogStore(new DataPaths(dir));
        store.Append(me, [Play("1", 10)]);
        File.AppendAllText(LogFile(), "{\"id\":\"tor");
        store.Append(me, [Play("2", 20)]);
        Assert.Contains("2", store.Read(me).Select(e => e.Id));
    }
}
