using System.Diagnostics;
using FLACie.Core;
using Xunit;
using Xunit.Abstractions;

namespace FLACie.Tests;

/// <summary>The owner's Home took 1-2 s to show. It works its shelves and moods out from the whole library every time it opens; with a library the owner's
/// size (11,000 songs) that must stay quick.</summary>
public class HomeSpeedTests(ITestOutputHelper output)
{
    static Library Big()
    {
        var rnd = new Random(7); var genres = new[] { "Pop", "Rock", "Hip-Hop", "House", "Jazz", "Classical", "R&B", "Ambient" };
        var songs = Enumerable.Range(0, 11_000).Select(i => new Track($"jf:{i:x32}", $"Song {i}", $"Artist {i % 1100}", $"Album {i % 2600}", $"Artist {i % 1100}", i % 12 + 1, 1,
            180_000 + rnd.Next(120_000), 1990 + i % 35, $"a{i % 2600}", i, TrackSource.Jellyfin, Genre: genres[i % genres.Length], JellyfinId: $"{i:x32}")).ToList();
        return new Library(songs);
    }

    [Fact]
    public void Home_is_worked_out_quickly_for_a_big_library()
    {
        var lib = Big();
        var history = Enumerable.Range(0, 200).Select(i => new PlayedEntry($"jf:{i * 37:x32}", $"Song {i * 37}", $"Artist {i * 37 % 1100}", DateTimeOffset.UtcNow.AddHours(-i).ToUnixTimeMilliseconds())).ToList();
        HomeBuilder.Build(lib, history, [], [], DateTime.UtcNow, null, null); HomeBuilder.MoodsAvailable(lib);   // warm up the JIT
        // the best of three (other tests run alongside): it was 530 ms before each song's keys were remembered, about 120 ms after
        long build = long.MaxValue, moods = long.MaxValue;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 3; i++)
        {
            sw.Restart(); HomeBuilder.Build(lib, history, [], [], DateTime.UtcNow, null, null); build = Math.Min(build, sw.ElapsedMilliseconds);
            sw.Restart(); HomeBuilder.MoodsAvailable(lib); moods = Math.Min(moods, sw.ElapsedMilliseconds);
        }
        sw.Restart();
        Recommender.Recommend(lib, new TasteProfile(lib, history, [], [], DateTime.UtcNow, null, null), DateTime.UtcNow, 20, 31, 2, []);
        var rec = sw.ElapsedMilliseconds; sw.Restart();
        lib.RecentAlbums(14);
        output.WriteLine($"Build {build} ms, MoodsAvailable {moods} ms (of which Quick picks {rec} ms, Recently added {sw.ElapsedMilliseconds} ms)");
        Assert.True(build + moods < 250, $"Build {build} ms + moods {moods} ms");
    }
}
