using Android.Media;
using IpodSync.Core.ItunesDb;
using IpodSync.Shared.Playback;
using Stream = Android.Media.Stream;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// Playback on the phone through Android's own media player, straight from the iPod's
/// files. Android decodes Apple Lossless, AAC, MP3 and WAV natively, so nothing has to be
/// converted (there is no ffmpeg here). Read-only: it only opens files.
/// </summary>
public sealed class AndroidAudioPlayer : AudioPlayer, IDisposable
{
    private MediaPlayer? _player;
    private System.Threading.Timer? _ticker;

    public override bool Available => true;

    public override Task PlayAsync(string deviceRoot, Track track)
    {
        if (track.RelativePath is not { } rel) return Task.CompletedTask;
        string file = Path.Combine(deviceRoot, rel.Replace('/', Path.DirectorySeparatorChar));

        Current = track;
        CurrentRoot = deviceRoot;
        Error = null;
        Position = 0;
        Duration = track.LengthMs / 1000.0;
        IsLoading = true;
        IsPlaying = false;
        Notify();

        try
        {
            Release();
            _player = new MediaPlayer();
            _player.SetAudioAttributes(new AudioAttributes.Builder()!
                .SetContentType(AudioContentType.Music)!
                .SetUsage(AudioUsageKind.Media)!.Build()!);
            _player.SetDataSource(file);
            _player.Completion += (_, _) => { IsPlaying = false; Position = Duration; Notify(); RaiseEnded(); };
            _player.Error += (_, e) =>
            {
                IsLoading = false;
                IsPlaying = false;
                Error = $"Android couldn't play this track ({e.What}).";
                Notify();
            };
            _player.Prepared += (_, _) =>
            {
                IsLoading = false;
                Duration = _player.Duration / 1000.0;
                _player.SetVolume((float)Volume, (float)Volume);
                _player.Start();
                IsPlaying = true;
                StartTicker();
                Notify();
            };
            _player.PrepareAsync();
        }
        catch (Exception ex)
        {
            IsLoading = false;
            Error = ex.Message;
            Notify();
        }
        return Task.CompletedTask;
    }

    public override Task ResumeAsync()
    {
        if (_player is not null) { _player.Start(); IsPlaying = true; StartTicker(); Notify(); }
        return Task.CompletedTask;
    }

    public override Task PauseAsync()
    {
        if (_player is not null && _player.IsPlaying) { _player.Pause(); IsPlaying = false; Notify(); }
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Release();
        Current = null;
        IsPlaying = false;
        IsLoading = false;
        Position = Duration = 0;
        Notify();
        return Task.CompletedTask;
    }

    public override Task SeekAsync(double seconds)
    {
        if (_player is not null)
        {
            _player.SeekTo((int)(seconds * 1000));
            Position = seconds;
            Notify();
        }
        return Task.CompletedTask;
    }

    public override Task SetVolumeAsync(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        _player?.SetVolume((float)Volume, (float)Volume);
        Notify();
        return Task.CompletedTask;
    }

    private void StartTicker() =>
        _ticker ??= new System.Threading.Timer(_ =>
        {
            try
            {
                if (_player is null || !_player.IsPlaying) return;
                Position = _player.CurrentPosition / 1000.0;
                Notify();
            }
            catch { /* player torn down mid-tick */ }
        }, null, 250, 250);

    private void Release()
    {
        _ticker?.Dispose();
        _ticker = null;
        try { _player?.Stop(); } catch { }
        _player?.Release();
        _player?.Dispose();
        _player = null;
    }

    public void Dispose() => Release();
}
