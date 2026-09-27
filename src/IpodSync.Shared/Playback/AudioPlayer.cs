using IpodSync.Core.ItunesDb;
using Microsoft.JSInterop;

namespace IpodSync.Shared.Playback;

/// <summary>Where the audio for a track comes from on this host: a URL the page's
/// &lt;audio&gt; element can load, possibly a converted copy.</summary>
public interface IMediaSource
{
    /// <summary>A URL for this track, or null when it can't be played here.</summary>
    Task<string?> UrlAsync(string deviceRoot, Track track, bool forceConversion = false, CancellationToken ct = default);

    /// <summary>Why playback is unavailable, if it is.</summary>
    string? Unavailable => null;
}

/// <summary>No playback (a host that can't reach the audio files).</summary>
public sealed class NoMediaSource(string reason) : IMediaSource
{
    public Task<string?> UrlAsync(string deviceRoot, Track track, bool forceConversion = false, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public string? Unavailable => reason;
}

/// <summary>Plays one track at a time. Implementations: the page's &lt;audio&gt; element
/// (Windows app and web), or Android's own media player.</summary>
public abstract class AudioPlayer
{
    public Track? Current { get; protected set; }
    public string? CurrentRoot { get; protected set; }
    public bool IsPlaying { get; protected set; }
    public bool IsLoading { get; protected set; }
    public double Position { get; protected set; }
    public double Duration { get; protected set; }
    public double Volume { get; protected set; } = 1;
    public string? Error { get; protected set; }

    /// <summary>Fires on every state change (also from timer/JS callbacks, so off the UI thread).</summary>
    public event Action? Changed;
    /// <summary>Fires when the track finishes on its own.</summary>
    public event Action? Ended;

    protected void Notify() => Changed?.Invoke();
    protected void RaiseEnded() => Ended?.Invoke();

    public abstract bool Available { get; }
    public virtual string? Unavailable => null;

    public abstract Task PlayAsync(string deviceRoot, Track track);
    public abstract Task ResumeAsync();
    public abstract Task PauseAsync();
    public abstract Task StopAsync();
    public abstract Task SeekAsync(double seconds);
    public abstract Task SetVolumeAsync(double volume);
}

/// <summary>
/// Drives an &lt;audio&gt; element through a small JS module. Used by the Windows app
/// (WebView2, with the iPod mapped to a virtual host) and by the web host (files served
/// by an endpoint). Apple Lossless is converted to FLAC first; if the engine still
/// refuses a file, the error handler retries once with a converted copy.
/// </summary>
public sealed class HtmlAudioPlayer(IJSRuntime js, IMediaSource media) : AudioPlayer, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<HtmlAudioPlayer>? _self;
    private bool _retriedWithConversion;

    public override bool Available => media.Unavailable is null;
    public override string? Unavailable => media.Unavailable;

    private async Task<IJSObjectReference> ModuleAsync()
    {
        if (_module is not null) return _module;
        _module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/IpodSync.Shared/ipodsync-audio.js");
        _self = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("init", _self);
        return _module;
    }

    public override async Task PlayAsync(string deviceRoot, Track track)
    {
        Current = track;
        CurrentRoot = deviceRoot;
        Error = null;
        IsLoading = true;
        IsPlaying = false;
        Position = 0;
        Duration = track.LengthMs / 1000.0;
        _retriedWithConversion = false;
        Notify();
        await LoadAsync(deviceRoot, track, convert: false);
    }

    private async Task LoadAsync(string deviceRoot, Track track, bool convert)
    {
        try
        {
            string? url = await media.UrlAsync(deviceRoot, track, convert);
            if (url is null)
            {
                IsLoading = false;
                Error = media.Unavailable ?? "This track can't be played here.";
                Notify();
                return;
            }
            var module = await ModuleAsync();
            await module.InvokeVoidAsync("load", url, true, Volume);
        }
        catch (Exception ex)
        {
            IsLoading = false;
            Error = ex.Message;
            Notify();
        }
    }

    public override async Task ResumeAsync() { if (_module is not null) await _module.InvokeVoidAsync("play"); }
    public override async Task PauseAsync() { if (_module is not null) await _module.InvokeVoidAsync("pause"); }
    public override async Task SeekAsync(double seconds) { if (_module is not null) await _module.InvokeVoidAsync("seek", seconds); }

    public override async Task StopAsync()
    {
        if (_module is not null) await _module.InvokeVoidAsync("stop");
        Current = null;
        IsPlaying = false;
        IsLoading = false;
        Position = Duration = 0;
        Notify();
    }

    public override async Task SetVolumeAsync(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        if (_module is not null) await _module.InvokeVoidAsync("volume", Volume);
        Notify();
    }

    // ---------------------------------------------------------------- JS callbacks

    [JSInvokable]
    public void OnState(double position, double duration, bool playing, bool loading)
    {
        Position = position;
        if (duration > 0) Duration = duration;
        IsPlaying = playing;
        IsLoading = loading;
        if (playing) Error = null;   // a retry that worked clears the first attempt's complaint
        Notify();
    }

    [JSInvokable]
    public void OnEnded()
    {
        IsPlaying = false;
        Position = Duration;
        Notify();
        RaiseEnded();
    }

    /// <summary>The engine refused the file: try once more with a converted copy (an ALAC
    /// file the library's metadata didn't flag, for instance).</summary>
    [JSInvokable]
    public async Task OnError(string message)
    {
        if (IsPlaying) return;       // a late error event from the attempt we already replaced
        if (!_retriedWithConversion && Current is { } t && CurrentRoot is { } root)
        {
            _retriedWithConversion = true;
            await LoadAsync(root, t, convert: true);
            return;
        }
        IsLoading = false;
        IsPlaying = false;
        Error = message;
        Notify();
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_module is not null) { await _module.InvokeVoidAsync("stop"); await _module.DisposeAsync(); } } catch { }
        _self?.Dispose();
    }
}
