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
