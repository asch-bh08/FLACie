using FLACie.Core;
using FLACie.Server;
using Microsoft.JSInterop;
using Xunit;

namespace FLACie.Tests;

/// <summary>F26.5: what the DJ bar says while the DJ works.</summary>
public class DjBarTests
{
    [Theory]
    [InlineData("play my favourite prodigy songs", "Finding your favourites…")]
    [InlineData("Play my faves.", "Finding your favourites…")]
    [InlineData("make a backroads playlist", "Making your playlist…")]
    [InlineData("play Firestarter", "Looking for Firestarter…")]
    [InlineData("play something chill", "Thinking…")]
    [InlineData("what's this song?", "Thinking…")]
    public void The_bar_says_what_the_dj_is_doing(string heard, string doing) => Assert.Equal(doing, DjBarWords.Doing(heard));

    sealed class Js : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
    }

    [Fact]
    public void The_state_keeps_what_was_heard_until_the_dj_is_quiet_again()
    {
        var p = new PlayerState(new Js()); var changes = 0; p.Changed += () => changes++;
        p.SetDjState(DjBarState.Listening);
        p.SetDjState(DjBarState.Thinking, heard: "play my favourite prodigy songs", doing: "Finding your favourites…");
        Assert.Equal(("play my favourite prodigy songs", "Finding your favourites…"), (p.DjHeard, p.DjDoing));
        p.SetDjState(DjBarState.Downloading, doing: "Downloading Breathe…");
        Assert.Equal("play my favourite prodigy songs", p.DjHeard);                 // still what it heard
        p.SetDjState(DjBarState.None);
        Assert.Null(p.DjHeard); Assert.Null(p.DjDoing);
        Assert.Equal(4, changes);
    }
}
