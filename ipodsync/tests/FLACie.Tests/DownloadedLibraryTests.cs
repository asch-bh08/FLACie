using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// Songs fetched in the background (Autoplay fetches several at once) join the library as they land. Two that land at the same moment must both stay:
/// each one's arrival rebuilds the library, and a rebuild that started from the library before the other's must not throw the other song away.
/// </summary>
public class DownloadedLibraryTests
{
    static Track T(int n) => new($"cloud:/m/{n}.flac", $"Fetched {n}", $"Artist {n}", "Al", $"Artist {n}", 1, 1, 200_000, 2020, null, 0, TrackSource.Cloud, $"/m/{n}.flac");

    [Fact]
    public void Songs_that_land_at_the_same_moment_all_stay_in_the_library()
    {
        for (var round = 0; round < 50; round++)
        {
            var s = new UserSession("jellyfin", null, null);
            var songs = Enumerable.Range(0, 16).Select(i => T(round * 100 + i)).ToList();
            using var go = new ManualResetEventSlim();
            var threads = songs.Select(t => new Thread(() => { go.Wait(); s.AddDownloaded([t]); })).ToList();
            threads.ForEach(t => t.Start());
            go.Set();
            threads.ForEach(t => t.Join());
            var missing = songs.Where(t => s.Library.Find(t.Title, t.Artist) is null).Select(t => t.Title).ToList();
            Assert.True(missing.Count == 0, $"round {round}: lost from the library: {string.Join(", ", missing)}");
        }
    }
}
