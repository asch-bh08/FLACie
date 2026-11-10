using System.Diagnostics;
using FLACie.Core;
using FLACie.Server;
using Xunit;
using Xunit.Abstractions;

namespace FLACie.Tests;

/// <summary>Timings with a 20,000-song library (a big real collection): building the library, searching it, Home and the Explore filters.</summary>
public class BigLibraryBench(ITestOutputHelper o)
{
    static readonly string[] Genres = ["Rock", "Pop", "Jazz", "Electronic", "Hip-Hop", "Classical", "Folk"];
    static readonly string[] Words = ["love", "night", "dream", "fire", "heart", "rain", "blue", "summer", "dance", "light", "gold", "river", "storm", "song", "home"];

    internal static List<Track> Tracks(int n, int artists = 900) => Enumerable.Range(0, n).Select(i =>
    {
        var a = i % artists; var al = i % 2400;
        var title = $"{Words[i % Words.Length]} {Words[(i / 7) % Words.Length]} {i}";
        return new Track($"/music/big/{a}/{al}/{i}.flac", title, $"Artist {a}", $"Album {al}", $"Artist {a}", 1 + i % 12, 1, 200_000 + i % 90_000, 1960 + i % 60,
            "jf" + i.ToString("x32") + "~t", 1_700_000_000_000 + i * 60_000L, TrackSource.Jellyfin, null, i.ToString("x32"), Genres[i % Genres.Length]);
    }).ToList();

    static long Ms(Action a, int reps = 1) { a(); var sw = Stopwatch.StartNew(); for (var i = 0; i < reps; i++) a(); return sw.ElapsedMilliseconds / reps; }

    // a timing report, not a check: run it by hand (dotnet test --filter BigLibraryBench --logger "console;verbosity=detailed") after removing the Skip
    [Fact(Skip = "timing report, run by hand")] public void Timings_with_20000_songs()
    {
        var tracks = Tracks(20_000);
        Library lib = null!;
        o.WriteLine($"library build: {Ms(() => lib = new Library(tracks))} ms");
        o.WriteLine($"search 'love': {Ms(() => lib.Search("love"), 5)} ms");
        o.WriteLine($"search 'artist 12': {Ms(() => lib.Search("artist 12"), 5)} ms");
        o.WriteLine($"search 'lvoe' (typo): {Ms(() => lib.Search("lvoe"), 5)} ms");
        var words = Matching.SearchWords("lvoe");
        o.WriteLine($"fuzzy songs 'lvoe': {Ms(() => lib.Songs.Where(t => Matching.FuzzyHit(words, t.Title, t.Artist, t.Album)).Take(100).ToList(), 3)} ms");
        var hist = tracks.Take(400).Select((t, i) => new PlayedEntry(t.Path, t.Title, t.Artist, 1_700_000_000_000 + i)).ToList();
        var favs = tracks.Skip(1000).Take(200).Select(t => new PlaylistEntry(t.Path, t.Title, t.Artist)).ToList();
        o.WriteLine($"home build: {Ms(() => HomeBuilder.Build(lib, hist, favs, [], DateTime.UtcNow), 3)} ms");
        o.WriteLine($"moods available: {Ms(() => HomeBuilder.MoodsAvailable(lib), 3)} ms");
        o.WriteLine($"recent albums: {Ms(() => lib.RecentAlbums(60), 5)} ms");
        o.WriteLine($"browse genres: {Ms(() => lib.Songs.Where(t => t.Genre.Trim().Length > 0).GroupBy(t => t.Genre.Trim(), StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(12).Select(g => g.Key).ToList(), 5)} ms");
        var x = new ExploreState { Query = "love" };
        o.WriteLine($"explore songs 'love': {Ms(() => x.Songs(lib), 5)} ms");
        x.Query = ""; x.Genre = "Jazz";
        o.WriteLine($"explore songs genre: {Ms(() => x.Songs(lib), 5)} ms");
        o.WriteLine($"made playlists build: {Ms(() => MadePlaylists.Build(lib, null))} ms");
        o.WriteLine($"find loose x500: {Ms(() => { foreach (var t in tracks.Take(500)) lib.FindLoose(t.Title + " (Radio Edit)", t.Artist); })} ms");
    }

    // the ready-made playlists (Search asks for them on its first search) took 13 s with 20,000 songs; now well under a second
    [Fact] public void Ready_made_playlists_for_20000_songs_are_built_in_well_under_three_seconds()
    {
        var lib = new Library(Tracks(20_000));
        var sw = Stopwatch.StartNew();
        var made = MadePlaylists.Build(lib, null);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Contains(made, m => m.Id.StartsWith("made:radio:") && m.Paths.Count == MadePlaylists.Take);
    }
}
