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
}
