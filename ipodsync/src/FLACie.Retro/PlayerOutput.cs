using FLACie.Core;
using Microsoft.JSInterop;

namespace FLACie.Server;

/// <summary>
/// What plays the songs <see cref="PlayerState"/> chooses. On the web it is the page's audio element (<see cref="PageOutput"/>, flacie.js); the Android
/// app plays through its own media service instead, so music goes on with the screen off and has lock-screen and headset controls. Whichever it is
/// reports back through the player's OnTime, OnState, OnEnded, OnNext and OnPrev.
/// </summary>
public interface IPlayerOutput
{
    /// <summary>Opens a song (<paramref name="url"/> is the page's /stream address for it) and plays it when <paramref name="autoplay"/> is set.</summary>
    Task LoadAsync(DotNetObjectReference<PlayerState> player, Track track, string url, string title, string artist, string album, string? art, bool autoplay, double durationSecs, double startAt, string? nextUrl);
    Task ToggleAsync();
    Task PlayAsync();
    Task PauseAsync();
    Task SeekAsync(double seconds);
    Task<bool> ReadyAsync();
    /// <summary>Sets the volume (0 to 1) when given one; answers the volume.</summary>
    Task<double> VolumeAsync(double? volume);
    Task RateAsync(double speed);
    /// <summary>Level volume: the gain that evens out loud and quiet songs.</summary>
    Task LoudAsync(double factor);
    Task CrossfadeAsync(int seconds, bool fadeIn, bool fadeOut);
    /// <summary>The sleep timer's fade (1 = full).</summary>
    Task FadeAsync(double gain);
    Task StopAsync();
    // the AI DJ (docs/spec/ai-dj.md): a short clip on a second small player over the start of a song, the music ducked under it. Never a queue item or a play.
    /// <summary>Fetches the DJ's clip for the next song ahead of time (a server-relative MP3).</summary>
    Task DjPreloadAsync(string url);
    /// <summary>Plays the DJ's clip over the song that has just started, with the music ducked while it plays. When the clip fails before it starts and
    /// <paramref name="backup"/> is on, the device's own voice says <paramref name="text"/> instead. <paramref name="clipAtMs"/>: the place in the song where the
    /// clip starts (0 = now); <paramref name="songDelayMs"/>: how long the song is held back under the clip, at most <see cref="DjTiming.MaxSongDelayMs"/>
    /// (it plays silently, then from its start). See <see cref="DjTiming"/>.</summary>
    Task DjPlayAsync(string url, string text, bool backup, int clipAtMs, int songDelayMs);
    /// <summary>How long the clip fetched ahead lasts, in ms; null when it is not known yet (still loading, or not the clip fetched ahead).</summary>
    Task<int?> DjClipMsAsync(string url);
    /// <summary>Says the DJ's line with the device's own voice (when no clip could be made), ducking the music the same way.</summary>
    Task DjSayAsync(string text);
    /// <summary>Stops a clip or a line at once and brings the music back up.</summary>
    Task DjStopAsync();
    /// <summary>The person's "DJ volume" (the voice, % of the music's volume) and "Music under the DJ" (%), set before each line.</summary>
    Task DjLevelsAsync(int volume, int duck) => Task.CompletedTask;
}

/// <summary>The page's own audio element, through flacie.js.</summary>
public sealed class PageOutput(IJSRuntime js) : IPlayerOutput
{
    public async Task LoadAsync(DotNetObjectReference<PlayerState> player, Track track, string url, string title, string artist, string album, string? art, bool autoplay, double durationSecs, double startAt, string? nextUrl) =>
        await js.InvokeVoidAsync("flacie.load", player, url, title, artist, album, art, autoplay, durationSecs, startAt, nextUrl);
    public async Task ToggleAsync() => await js.InvokeVoidAsync("flacie.toggle");
    public async Task PlayAsync() => await js.InvokeVoidAsync("flacie.play");
    public async Task PauseAsync() => await js.InvokeVoidAsync("flacie.pause");
    public async Task SeekAsync(double seconds) => await js.InvokeVoidAsync("flacie.seek", seconds);
    public async Task<bool> ReadyAsync() => await js.InvokeAsync<bool>("flacie.ready");
    public async Task<double> VolumeAsync(double? volume) => await js.InvokeAsync<double>("flacie.volume", volume);
    public async Task RateAsync(double speed) => await js.InvokeVoidAsync("flacie.rate", speed);
    public async Task LoudAsync(double factor) => await js.InvokeVoidAsync("flacie.loud", factor);
    public async Task CrossfadeAsync(int seconds, bool fadeIn, bool fadeOut) => await js.InvokeVoidAsync("flacie.xfade", seconds, fadeIn, fadeOut);
    public async Task FadeAsync(double gain) => await js.InvokeVoidAsync("flacie.fade", gain);
    public async Task StopAsync() => await js.InvokeVoidAsync("flacie.stop");
    public async Task DjPreloadAsync(string url) => await js.InvokeVoidAsync("flacie.djPreload", url);
    public async Task DjPlayAsync(string url, string text, bool backup, int clipAtMs, int songDelayMs) => await js.InvokeVoidAsync("flacie.djPlay", url, text, backup, clipAtMs, songDelayMs);
    public async Task<int?> DjClipMsAsync(string url) => await js.InvokeAsync<int?>("flacie.djClipMs", url);
    public async Task DjSayAsync(string text) => await js.InvokeVoidAsync("flacie.djSay", text);
    public async Task DjStopAsync() => await js.InvokeVoidAsync("flacie.djStop");
    public async Task DjLevelsAsync(int volume, int duck) => await js.InvokeVoidAsync("flacie.djLevels", volume, duck);
}
