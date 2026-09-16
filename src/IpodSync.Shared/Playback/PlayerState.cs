using IpodSync.Core.ItunesDb;

namespace IpodSync.Shared.Playback;

/// <summary>
/// The play queue and the now-playing bar's state. Playback is read-only: it opens the
/// iPod's audio files and never writes to the device (play counts on the iPod are the
/// iPod's own; this doesn't touch them).
/// </summary>
public sealed class PlayerState(AudioPlayer player)
{
    private readonly Random _rng = new();
    private List<Track> _queue = [];
    private List<int>? _shuffleOrder;   // indexes into _queue
    private int _index = -1;

    public AudioPlayer Player { get; } = player;
    public event Action? Changed;

    public IReadOnlyList<Track> Queue => _queue;
    public int Index => _index;
    public Track? Current => Player.Current;
    public string? QueueName { get; private set; }
    public bool Shuffle { get; private set; }
    public bool Repeat { get; private set; }
    public bool Available => Player.Available;
    public string? Unavailable => Player.Unavailable;

    private bool _hooked;

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        Player.Changed += () => Changed?.Invoke();
        Player.Ended += () => _ = AdvanceAsync(1, auto: true);
    }

    public bool IsCurrent(Track t) => Player.Current?.Id == t.Id;

    /// <summary>Plays one track and makes <paramref name="queue"/> what comes next.</summary>
    public async Task PlayAsync(string deviceRoot, Track track, IEnumerable<Track>? queue = null, string? queueName = null)
    {
        Hook();
        var list = (queue ?? [track]).ToList();
        if (list.Count == 0) list = [track];
        _queue = list;
        QueueName = queueName;
        _index = Math.Max(0, list.FindIndex(t => t.Id == track.Id));
        ReshuffleFromCurrent();
        Changed?.Invoke();
        await Player.PlayAsync(deviceRoot, track);
    }

    public async Task ToggleAsync(string deviceRoot)
    {
        Hook();
        if (Player.Current is null)
        {
            if (_queue.Count > 0) await PlayAsync(deviceRoot, _queue[Math.Max(0, _index)], _queue, QueueName);
            return;
        }
        if (Player.IsPlaying) await Player.PauseAsync(); else await Player.ResumeAsync();
    }

    public Task NextAsync() => AdvanceAsync(1, auto: false);
    public Task PreviousAsync() => Player.Position > 3 ? Player.SeekAsync(0) : AdvanceAsync(-1, auto: false);

    private async Task AdvanceAsync(int delta, bool auto)
    {
        if (_queue.Count == 0 || Player.CurrentRoot is not { } root) return;
        int next = NextIndex(delta);
        if (next < 0)
        {
            if (auto) { await Player.StopAsync(); }
            return;
        }
        _index = next;
        Changed?.Invoke();
        await Player.PlayAsync(root, _queue[_index]);
    }

    private int NextIndex(int delta)
    {
        if (_shuffleOrder is { } order)
        {
            int pos = order.IndexOf(_index);
            int target = pos + delta;
            if (target < 0) target = Repeat ? order.Count - 1 : -1;
            if (target >= order.Count) target = Repeat ? 0 : -1;
            return target < 0 ? -1 : order[target];
        }
        int n = _index + delta;
        if (n < 0) return Repeat ? _queue.Count - 1 : -1;
        if (n >= _queue.Count) return Repeat ? 0 : -1;
        return n;
    }

    public void SetShuffle(bool on)
    {
        Shuffle = on;
        ReshuffleFromCurrent();
        Changed?.Invoke();
    }

    public void SetRepeat(bool on) { Repeat = on; Changed?.Invoke(); }

    private void ReshuffleFromCurrent()
    {
        if (!Shuffle || _queue.Count == 0) { _shuffleOrder = null; return; }
        var rest = Enumerable.Range(0, _queue.Count).Where(i => i != _index).OrderBy(_ => _rng.Next()).ToList();
        _shuffleOrder = _index >= 0 ? [_index, .. rest] : rest;
    }

    public Task SeekAsync(double seconds) => Player.SeekAsync(seconds);
    public Task SetVolumeAsync(double v) => Player.SetVolumeAsync(v);

    public async Task StopAsync()
    {
        await Player.StopAsync();
        _queue = [];
        _index = -1;
        QueueName = null;
        Changed?.Invoke();
    }

    /// <summary>After a write the library is re-read; keep playing, but point the queue at the
    /// new Track objects so titles and ids stay in step (dropping any that are gone).</summary>
    public void Rebind(ItunesDatabase db)
    {
        if (_queue.Count == 0) return;
        var byId = db.Tracks.ToDictionary(t => t.Id);
        var current = _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
        _queue = _queue.Select(t => byId.TryGetValue(t.Id, out var fresh) ? fresh : null).OfType<Track>().ToList();
        _index = current is null ? -1 : _queue.FindIndex(t => t.Id == current.Id);
        ReshuffleFromCurrent();
        Changed?.Invoke();
    }
}
