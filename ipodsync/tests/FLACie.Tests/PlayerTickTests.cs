using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Components.Shared;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>The player's twice-a-second clock tick is marked as such, so song lists, Home and Search are not drawn again for it while music plays
/// (with a 20,000-song library that cost the server about a third of a core); only the views that show the time follow it.</summary>
public class PlayerTickTests
{
    sealed class FakeJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    [Fact] public void A_clock_tick_is_marked_and_a_play_or_pause_is_not()
    {
        var p = new PlayerState(new FakeJs());
        var seen = new List<bool>();
        p.Changed += () => seen.Add(p.Ticking);
        p.OnTime(1.0, 200);
        p.OnState(true);
        p.OnTime(1.5, 200);
        p.Redraw();
        Assert.Equal([true, false, true, false], seen);
        Assert.False(p.Ticking);
    }

    [Fact] public void A_tick_handler_that_throws_does_not_leave_the_player_marked_as_ticking()
    {
        var p = new PlayerState(new FakeJs());
        p.Changed += () => throw new InvalidOperationException("boom");
        Assert.Throws<InvalidOperationException>(() => p.OnTime(1.0, 200));
        Assert.False(p.Ticking);
    }

    sealed class RecordingJs : IJSRuntime
    {
        public readonly List<string> Calls = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            lock (Calls) Calls.Add(identifier);
            return ValueTask.FromResult(identifier == "flacie.stashPlace" && typeof(T) == typeof(bool) ? (T)(object)true : default(T)!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    static Track T(int n) => new($"/m/{n}.flac", $"Song {n}", "A", "Al", "A", n, 1, 100_000, 2000, null, 0, TrackSource.Jellyfin, $"/m/{n}.flac", n.ToString("x32"));

    // the queue kept for a reload: the songs are saved when the queue changes; while the same queue plays only the second is saved
    [Fact] public async Task The_saved_queue_is_sent_whole_only_when_it_changes()
    {
        var js = new RecordingJs();
        var p = new PlayerState(js);
        var songs = Enumerable.Range(0, 5).Select(T).ToList();
        p.SetLibrary(() => new Library(songs));
        p.SetStashKey("me");
        await p.Play(songs, 0);
        int Count(string id) { lock (js.Calls) return js.Calls.Count(c => c == id); }
        Assert.Equal(1, Count("flacie.stash"));
        await p.Play(songs, 0);   // the same queue at the same song: only the place
        Assert.Equal(1, Count("flacie.stash"));
        Assert.Equal(1, Count("flacie.stashPlace"));
        p.AddLast(T(9)); p.AddNext(T(8));   // a changed queue is saved whole
        Assert.True(Count("flacie.stash") >= 2);
    }

    // the album grid and the Artists page skip the player's changes, but a chosen cover (Redraw) still draws them again
    [Fact] public void A_redraw_is_counted_and_a_tick_or_a_pause_is_not()
    {
        var p = new PlayerState(new FakeJs());
        p.OnTime(1.0, 200); p.OnState(true);
        Assert.Equal(0, p.Redraws);
        p.Redraw(); p.Redraw();
        Assert.Equal(2, p.Redraws);
    }

    // the views that show a clock, a progress line or a countdown follow every tick; the lists and pages do not
    [Theory]
    [InlineData(typeof(FLACie.Server.Components.Shared.PlayerBar), true)]
    [InlineData(typeof(FLACie.Server.Components.Shared.NowPlaying), true)]
    [InlineData(typeof(FLACie.Server.Components.Shared.SpeedSleep), true)]
    [InlineData(typeof(FLACie.Server.Components.Shared.TrackList), false)]
    [InlineData(typeof(FLACie.Server.Components.Pages.Home), false)]
    [InlineData(typeof(FLACie.Server.Components.Pages.Search), false)]
    [InlineData(typeof(FLACie.Server.Components.Pages.Songs), false)]
    public void Only_views_that_show_the_time_follow_the_clock(Type view, bool follows)
    {
        var prop = typeof(UserComponent).GetProperty("FollowsTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(view);
        Assert.Equal(follows, (bool)prop.GetValue(instance)!);
    }
}
