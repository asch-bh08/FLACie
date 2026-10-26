using System.Diagnostics;
using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FLACie.Tests;

/// <summary>/api/recommend: what plays next must come at once (never waiting on the online related list), and Home's shelves hold still within the hour.</summary>
public class RecommendApiTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "flacie-recommend-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    static readonly DateTime Now = new(2026, 10, 25, 14, 10, 0, DateTimeKind.Utc);
    static UserSession Person(string id) => new("local", null, null) { Id = id };

    static Library Big(int n)
    {
        var genres = new[] { "Pop", "Rock", "Hip-Hop", "House", "Jazz", "Classical", "R&B", "Ambient" };
        return new Library(Enumerable.Range(0, n).Select(i => new Track($"jf:{i:x32}", $"Song {i}", $"Artist {i % 1000}", $"Album {i % 2400}", $"Artist {i % 1000}", i % 12 + 1, 1,
            200_000, 1990 + i % 35, $"a{i % 2400}", i, TrackSource.Jellyfin, Genre: genres[i % genres.Length], JellyfinId: $"{i:x32}")).ToList());
    }

    static List<ListenEvent> Plays(Library lib, int n) => Enumerable.Range(0, n).Select(i => lib.Songs[i * 13])
        .Select((t, i) => new ListenEvent($"e{i}", ListenKind.Play, new DateTimeOffset(Now.AddDays(-1 - i % 20)).ToUnixTimeMilliseconds(), t.Path, t.Title, t.Artist, 200, 200)).ToList();

    RelatedCache Related(Func<string, string, CancellationToken, Task<List<CatalogSong>>> fetch) => new(new DataPaths(dir), fetch);

    static IResult Next(RecommendApi.Inputs i, string session, int n = 10) => RecommendApi.Next(i, session, n, Now, 1);

    static List<JsonElement> PicksOf(IResult r)
    {
        var value = ((IValueHttpResult)r).Value;
        return JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("picks").EnumerateArray().ToList();
    }

    [Fact] public void Next_is_401_without_a_session()
    {
        var r = RecommendApi.Next(null, "", 10, Now, 1);
        Assert.Equal(401, ((IStatusCodeHttpResult)r).StatusCode);
    }

    [Fact] public void Next_answers_within_200_ms_for_a_10000_song_library()
    {
        var lib = Big(10_000);
        var inputs = new RecommendApi.Inputs(lib, [], [], [], [], Plays(lib, 300), _ => null, Related((_, _, _) => Task.FromResult(new List<CatalogSong>())));
        var session = string.Join(",", lib.Songs.Take(3).Select(t => t.Path));
        Next(inputs, session);   // warm up the JIT
        var sw = Stopwatch.StartNew();
        var picks = PicksOf(Next(inputs, session));
        Assert.True(sw.ElapsedMilliseconds < 200, $"{sw.ElapsedMilliseconds} ms");
        Assert.Equal(10, picks.Count);
    }

    [Fact] public void Next_with_an_owner_sized_log_on_disk_answers_within_200_ms()
    {
        var lib = Big(10_000);
        var ann = Person("ann");
        var log = new ListenLogStore(new DataPaths(dir));
        log.Append(ann, Enumerable.Range(0, 20_000).Select(i => lib.Songs[i * 7 % lib.Songs.Count]).Select((t, i) =>
            new ListenEvent($"e{i}", ListenKind.Play, new DateTimeOffset(Now.AddMinutes(-i * 20)).ToUnixTimeMilliseconds(), t.Path, t.Title, t.Artist, i % 5 == 0 ? 10 : 200, 200)));
        var related = Related((_, _, _) => Task.FromResult(new List<CatalogSong>()));
        var vibes = new VibeIndex(new DataPaths(dir));
        var session = string.Join(",", lib.Songs.Take(3).Select(t => t.Path));
        Next(RecommendApi.InputsOf(ann, log, vibes, related)! with { Lib = lib }, session);   // warm up the JIT (and load the log once)
        var sw = Stopwatch.StartNew();
        var inputs = RecommendApi.InputsOf(ann, log, vibes, related)! with { Lib = lib };
        var read = sw.ElapsedMilliseconds;
        var picks = PicksOf(Next(inputs, session));
        Assert.True(sw.ElapsedMilliseconds < 200, $"{sw.ElapsedMilliseconds} ms ({read} ms reading the log)");
        Assert.Equal(10, picks.Count);
    }

    [Fact] public void Next_does_not_wait_for_a_slow_related_lookup()
    {
        var lib = Big(2_000);
        var slow = Related(async (_, _, ct) => { await Task.Delay(5000, ct); return [new CatalogSong("Artist 1", "Not owned", "X", null, 1000, 1, 1)]; });
        var inputs = new RecommendApi.Inputs(lib, [], [], [], [], Plays(lib, 50), _ => null, slow);
        var sw = Stopwatch.StartNew();
        var picks = PicksOf(Next(inputs, lib.Songs[1].Path));
        Assert.True(sw.ElapsedMilliseconds < 200, $"{sw.ElapsedMilliseconds} ms");
        Assert.NotEmpty(picks);
        Assert.All(picks, p => { Assert.False(p.GetProperty("isNew").GetBoolean()); Assert.False(string.IsNullOrEmpty(p.GetProperty("path").GetString())); });
    }

    [Fact] public async Task Related_cache_hands_back_what_it_fetched_and_keeps_it_on_disk()
    {
        var calls = 0;
        Task<List<CatalogSong>> Fetch(string a, string t, CancellationToken ct) { calls++; return Task.FromResult(new List<CatalogSong> { new(a, "Other", "X", null, 1, 1, 1) }); }
        var cache = Related(Fetch);
        Assert.Empty(cache.Peek("Ann", "One"));
        await cache.Pending;
        Assert.Single(cache.Peek("Ann", "One"));
        Assert.Single(Related(Fetch).Peek("Ann", "One"));   // a new cache (a restart) reads it from data/related
        Assert.Equal(1, calls);
    }

    [Fact] public void Home_shelves_change_hourly_not_while_shown()
    {
        var lib = Big(3_000); var events = Plays(lib, 100);
        List<string> Ids(DateTime at) => HomeBuilder.Build(lib, [], [], [], at, events: events).SelectMany(s => s.Tracks.Select(t => s.Id + t.Path).Concat(s.Albums.Select(a => s.Id + a.Name))).ToList();
        Assert.Equal(Ids(Now), Ids(Now.AddMinutes(40)));
        Assert.NotEqual(Ids(Now), Ids(Now.AddHours(1)));
        Assert.Contains(HomeBuilder.Build(lib, [], [], [], Now, events: events), s => s.Id == "discover");
    }

    [Fact] public void A_play_in_both_the_history_and_the_log_counts_once()
    {
        var lib = Big(50); var t = lib.Songs[3];
        var at = new DateTimeOffset(Now.AddDays(-1)).ToUnixTimeMilliseconds();
        var history = new List<PlayedEntry> { new(t.Path, t.Title, t.Artist, at) };
        var events = new List<ListenEvent> { new("seed:h:1", ListenKind.Play, at, t.Path, t.Title, t.Artist, 200, 200) };
        var both = HomeBuilder.Taste(lib, history, [new PlaylistEntry(t.Path, t.Title, t.Artist)], [], Now, null, null, events);
        var logOnly = new TasteProfile(lib, [], [], [], Now, events: events);
        Assert.Equal(logOnly.Known(t), both.Known(t));
        // with no log yet the old lists still count
        Assert.True(HomeBuilder.Taste(lib, history, [], [], Now, null, null, null).Known(t) > 0);
    }
    [Fact] public void A_seeded_log_picks_the_same_songs_as_no_log()
    {
        var lib = Big(400);
        var history = Enumerable.Range(0, 60).Select(i => lib.Songs[i * 3]).Select((t, i) => new PlayedEntry(t.Path, t.Title, t.Artist, new DateTimeOffset(Now.AddDays(-i % 40)).ToUnixTimeMilliseconds())).ToList();
        var favs = Enumerable.Range(0, 5).Select(i => lib.Songs[i * 7 + 1]).Select(t => new PlaylistEntry(t.Path, t.Title, t.Artist)).ToList();
        var jellyfin = Enumerable.Range(0, 40).Select(i => new JfPlay(lib.Songs[i * 5 + 2].JellyfinId!, 1 + i * 3, new DateTimeOffset(Now.AddDays(-i)).ToUnixTimeMilliseconds(), false)).ToList();
        var seeded = ListenSeed.From(history, favs, jellyfin, lib);
        var without = HomeBuilder.Taste(lib, history, favs, [], Now, jellyfin, null, null);
        var with = HomeBuilder.Taste(lib, history, favs, [], Now, jellyfin, null, seeded);
        List<string> Top(TasteProfile p) => lib.Songs.OrderByDescending(p.Known).ThenBy(t => t.Path).Take(20).Select(t => t.Path).ToList();
        Assert.Equal(Top(without), Top(with));
    }

    [Fact] public void Plays_counted_by_jellyfin_or_seeded_from_the_history_are_not_a_like()
    {
        var lib = Big(50); var t = lib.Songs[3]; var u = lib.Songs[4];
        var at = new DateTimeOffset(Now.AddDays(-2)).ToUnixTimeMilliseconds();
        var history = Enumerable.Range(0, 4).Select(i => new PlayedEntry(u.Path, u.Title, u.Artist, at + i)).ToList();
        var jellyfin = new List<JfPlay> { new(t.JellyfinId!, 12, at, false) };
        var taste = HomeBuilder.Taste(lib, [], [], [], Now, jellyfin, null, ListenSeed.From(history, [], jellyfin, lib));
        Assert.False(taste.Likes(t));
        Assert.False(taste.Likes(u));
        // finished for real three times, it is
        var real = Enumerable.Range(0, 3).Select(i => new ListenEvent($"r{i}", ListenKind.Play, at + i, t.Path, t.Title, t.Artist, 200, 200)).ToList();
        Assert.True(HomeBuilder.Taste(lib, [], [], [], Now, jellyfin, null, real).Likes(t));
    }

    [Fact] public async Task Discover_is_about_half_songs_the_library_lacks_when_related_songs_are_cached()
    {
        var lib = Big(3_000); var events = Plays(lib, 100);
        var related = Enumerable.Range(0, 30).Select(i => new CatalogSong($"Stranger {i}", $"Not owned {i}", "Elsewhere", null, 180_000, 1, 1)).ToList();
        var d = Assert.Single(HomeBuilder.Build(lib, [], [], [], Now, events: events, related: _ => related), s => s.Id == "discover");
        var all = d.Tracks.Count + d.New.Count;
        Assert.InRange(d.New.Count, all * 0.4, all * 0.6);
        Assert.All(d.New, s => Assert.Null(lib.Find(s.Title, s.Artist)));
        // shown in /api/recommend/home as songs with no path
        var cache = Related((_, _, _) => Task.FromResult(related));
        var inputs = new RecommendApi.Inputs(lib, [], [], [], [], events, _ => null, cache);
        RecommendApi.Home(inputs, Now); await cache.Pending;   // the first look starts the lookups; the next one has them
        var json = JsonSerializer.SerializeToElement(((IValueHttpResult)RecommendApi.Home(inputs, Now)).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var row = json.GetProperty("shelves").EnumerateArray().First(s => s.GetProperty("id").GetString() == "discover");
        Assert.Contains(row.GetProperty("tracks").EnumerateArray(), t => t.GetProperty("isNew").GetBoolean() && t.GetProperty("path").ValueKind == JsonValueKind.Null);
    }

    [Fact] public void Discover_fills_from_the_library_while_nothing_is_cached()
    {
        var lib = Big(3_000); var events = Plays(lib, 100);
        var d = Assert.Single(HomeBuilder.Build(lib, [], [], [], Now, events: events, related: _ => []), s => s.Id == "discover");
        Assert.Empty(d.New);
        Assert.True(d.Tracks.Count >= 12);
    }

    [Fact] public async Task A_failing_lookup_is_not_tried_again_for_a_while()
    {
        var calls = 0;
        var cache = Related((_, _, _) => { Interlocked.Increment(ref calls); return Task.FromException<List<CatalogSong>>(new HttpRequestException("down")); });
        for (var i = 0; i < 5; i++) { cache.Peek("Ann", "One"); await cache.Pending; }
        Assert.Equal(1, calls);
    }

    [Fact] public async Task No_more_than_four_lookups_run_at_once()
    {
        int now = 0, most = 0;
        var cache = Related(async (_, _, _) =>
        {
            var n = Interlocked.Increment(ref now); lock (this) most = Math.Max(most, n);
            await Task.Delay(100); Interlocked.Decrement(ref now); return [];
        });
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 40; i++) cache.Peek("Artist " + i, "Song " + i);
        Assert.True(sw.ElapsedMilliseconds < 100, "Peek must not wait for a free slot");
        await cache.Pending;
        Assert.InRange(most, 1, 4);
    }
}
