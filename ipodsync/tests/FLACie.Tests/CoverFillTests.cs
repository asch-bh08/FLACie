using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F1.3: the background pass that saves every song's cover once, and stops on the catalog's quota without remembering misses.</summary>
public sealed class CoverFillTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "coverfill-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    static Track Song(string title, string artist) =>
        new($"/m/{artist}/{title}", title, artist, "", artist, 1, 1, 200000, 2000, Art.LookupKey(artist, "", title), 1, TrackSource.Cloud);

    static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    [Fact]
    public async Task Every_song_gets_its_cover_saved_once()
    {
        var store = new CoverStore(dir); var asked = 0;
        var fill = new CoverFill(store, (a, al, t, ct) => { asked++; return Task.FromResult<(byte[]?, CatalogAnswer)>(([1, 2, 3], CatalogAnswer.Found)); }, TimeSpan.FromSeconds(1), NoWait);
        var songs = new[] { Song("Lose Yourself", "Eminem"), Song("Toxic", "Britney Spears"), Song("Lose Yourself", "Eminem") };
        var p = await fill.RunAsync(songs, default);
        Assert.Equal((2, 2, 2, null), (p.Total, p.Done, p.Saved, p.Stopped));
        Assert.All(songs, s => Assert.NotNull(store.Find(s.ArtKey!, CoverStore.Other)));
        await fill.RunAsync(songs, default);
        Assert.Equal(2, asked);   // saved covers are not looked up again
    }

    [Fact]
    public async Task A_quota_error_stops_the_pass_and_nothing_is_remembered_as_missing()
    {
        var store = new CoverStore(dir); var quota = true; var asked = new List<string>();
        var fill = new CoverFill(store, (a, al, t, ct) =>
        {
            asked.Add(t);
            return Task.FromResult<(byte[]?, CatalogAnswer)>(quota && t == "Toxic" ? (null, CatalogAnswer.Unasked) : ([9], CatalogAnswer.Found));
        }, TimeSpan.FromSeconds(1), NoWait);
        var songs = new[] { Song("Lose Yourself", "Eminem"), Song("Toxic", "Britney Spears"), Song("Firework", "Katy Perry") };
        var p = await fill.RunAsync(songs, default);
        Assert.NotNull(p.Stopped);
        Assert.Equal(1, p.Saved);
        Assert.DoesNotContain("Firework", asked);
        quota = false;
        p = await fill.RunAsync(songs, default);
        Assert.Null(p.Stopped);
        Assert.Equal(3, p.Done);
        Assert.All(songs, s => Assert.NotNull(store.Find(s.ArtKey!, CoverStore.Other)));
    }
}
