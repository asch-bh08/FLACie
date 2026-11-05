using FLACie.Core;
using FLACie.Server;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>F13 on the web player: the shuffle toggle shuffles only what comes after the current song, and off puts the order back.</summary>
public class QueueShuffleWebTests
{
    sealed class Js : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
    }

    static Track T(int n) => new($"/m/{n}.flac", $"Song {n}", $"Artist {n}", "A", $"Artist {n}", n, 1, 200_000, 2000, null, 0, TrackSource.Jellyfin, $"/m/{n}.flac", n.ToString("x32"));

    [Fact]
    public async Task Toggle_on_keeps_the_current_song_and_shuffles_the_rest_off_restores_the_order()
    {
        var p = new PlayerState(new Js());
        var songs = Enumerable.Range(0, 30).Select(T).ToList();
        await p.Play(songs, 3, "Pop");
        p.ToggleShuffle();
        Assert.True(p.Shuffle);
        Assert.Equal("Song 3", p.Current!.Title);
        var next = p.UpNext(100).Select(x => x.Index).ToList();
        Assert.Equal(Enumerable.Range(4, 26), next.Order());   // only the songs after it, none played again
        Assert.NotEqual(Enumerable.Range(4, 26), next);
        p.ToggleShuffle();
        Assert.False(p.Shuffle);
        Assert.Equal("Song 3", p.Current!.Title);
        Assert.Equal(Enumerable.Range(4, 26), p.UpNext(100).Select(x => x.Index));
    }
}
