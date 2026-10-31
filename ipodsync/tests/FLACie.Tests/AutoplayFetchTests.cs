using FLACie.Core;
using FLACie.Server;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>Autoplay on the web: the suggestions the library lacks are fetched ahead, whatever else (the DJ) the player does as a song starts.</summary>
public class AutoplayFetchTests
{
    sealed class Js : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    sealed class Planner(List<CatalogSong> related) : IAutoplayPlanner
    {
        public readonly List<CatalogSong> Wanted = [];
        public readonly TaskCompletionSource Asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default) => Task.FromResult(related);
        public IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested) => [];
        public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach) { Wanted.AddRange(wanted.Take(max)); Asked.TrySetResult(); }
    }

    sealed class Dj : IDjService
    {
        public DjSettings Settings(UserSession s) => new() { Enabled = true };
        public void Save(UserSession s, DjSettings settings) { }
        public Task<DjCue?> PrepareAsync(UserSession s, DjRequest req, CancellationToken ct = default) => Task.FromResult<DjCue?>(null);
        public bool VoiceReady => true;
    }

    static Track T(int n) => new($"/m/{n}.flac", $"Song {n}", "A", "Al", "A", n, 1, 100_000, 2000, null, 0, TrackSource.Jellyfin, $"/m/{n}.flac", n.ToString("x32"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Songs_the_library_lacks_are_fetched_when_a_song_starts(bool withDj)
    {
        var songs = Enumerable.Range(0, 4).Select(T).ToList();
        var planner = new Planner([new("A", "Fetch Me One", "X", null, 200_000, 1, 1), new("A", "Fetch Me Two", "X", null, 200_000, 2, 1), new("A", "Song 1", "Al", null, 100_000, 1, 1)]);
        var p = new PlayerState(new Js());
        p.SetPlanner(new UserSession("jellyfin", null, null), planner);
        p.SetLibrary(() => new Library(songs));
        if (withDj) p.SetDj(new Dj(), DjSurface.Web);
        await p.Play(songs, 3);
        await Task.WhenAny(planner.Asked.Task, Task.Delay(5000));
        Assert.Equal(["Fetch Me One", "Fetch Me Two"], planner.Wanted.Select(w => w.Title));
    }
}
