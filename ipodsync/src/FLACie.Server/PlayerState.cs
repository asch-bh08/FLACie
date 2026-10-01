using FLACie.Core;
using Microsoft.JSInterop;

namespace FLACie.Server;

/// <summary>The player for one browser tab: the queue lives here, the audio element in flacie.js plays it. Audio comes
/// from /stream on this server; covers from /art.</summary>
public sealed class PlayerState(IJSRuntime js) : IAsyncDisposable
{
    public List<Track> Queue { get; private set; } = [];
    public int Index { get; private set; } = -1;
    public bool Playing { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool Shuffle { get; private set; }
    public int Repeat { get; private set; } // 0 off, 1 all, 2 one
    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public event Action? Changed;
    DotNetObjectReference<PlayerState>? self;
    List<int> order = [];

    async Task Load(bool autoplay)
    {
        var t = Current;
        if (t is null) return;
        self ??= DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("flacie.load", self, "/stream?p=" + Uri.EscapeDataString(t.Path), t.Title, t.Artist, t.Album,
            t.ArtKey is null ? null : "/art/" + Uri.EscapeDataString(t.ArtKey), autoplay, (t.DurationMs / 1000.0));
        Position = 0; Duration = t.DurationMs / 1000.0;
        Changed?.Invoke();
    }

    public async Task Play(IReadOnlyList<Track> list, int index)
    {
        if (list.Count == 0) return;
        Queue = list.ToList(); Index = Math.Clamp(index, 0, Queue.Count - 1);
        Reorder();
        await Load(true);
    }

    public async Task ShuffleAll(IReadOnlyList<Track> list) { Shuffle = true; if (list.Count > 0) await Play(list, Random.Shared.Next(list.Count)); }

    public async Task Toggle() { if (Current is null) return; await js.InvokeVoidAsync("flacie.toggle"); }
    public async Task Seek(double seconds) { await js.InvokeVoidAsync("flacie.seek", seconds); Position = seconds; Changed?.Invoke(); }

    public async Task Next()
    {
        if (Queue.Count == 0) return;
        var pos = order.IndexOf(Index);
        if (pos + 1 < order.Count) Index = order[pos + 1];
        else if (Repeat == 1) { Reorder(); Index = order[0]; }
        else { await js.InvokeVoidAsync("flacie.pause"); return; }
        await Load(true);
    }

    public async Task Prev()
    {
        if (Queue.Count == 0) return;
        if (Position > 3) { await Seek(0); return; }
        var pos = order.IndexOf(Index);
        if (pos > 0) Index = order[pos - 1];
        await Load(true);
    }

    public async Task SkipTo(int index) { if (index >= 0 && index < Queue.Count) { Index = index; await Load(true); } }

    public void PlayNext(Track t) { if (Current is null) { _ = Play([t], 0); return; } Queue.Insert(Index + 1, t); Reorder(keepCurrentFirst: true); Changed?.Invoke(); }
    public void AddToQueue(Track t) { if (Current is null) { _ = Play([t], 0); return; } Queue.Add(t); order.Add(Queue.Count - 1); Changed?.Invoke(); }

    public void ToggleShuffle() { Shuffle = !Shuffle; Reorder(keepCurrentFirst: true); Changed?.Invoke(); }
    public void CycleRepeat() { Repeat = (Repeat + 1) % 3; Changed?.Invoke(); }

    /// <summary>Play order: the queue as is, or shuffled with the current song first.</summary>
    void Reorder(bool keepCurrentFirst = true)
    {
        order = Enumerable.Range(0, Queue.Count).ToList();
        if (!Shuffle) return;
        var rest = order.Where(i => i != Index).OrderBy(_ => Random.Shared.Next()).ToList();
        order = keepCurrentFirst && Index >= 0 ? [Index, .. rest] : rest;
    }

    public IEnumerable<(int Index, Track Track)> UpNext(int n)
    {
        var pos = order.IndexOf(Index);
        return order.Skip(pos + 1).Take(n).Select(i => (i, Queue[i]));
    }

    // ---- from flacie.js ----

    [JSInvokable] public void OnTime(double pos, double dur) { Position = pos; if (dur > 0 && !double.IsInfinity(dur)) Duration = dur; Changed?.Invoke(); }
    [JSInvokable] public void OnState(bool playing) { Playing = playing; Changed?.Invoke(); }
    [JSInvokable] public async Task OnEnded() { if (Repeat == 2) { await Seek(0); await js.InvokeVoidAsync("flacie.play"); } else await Next(); }
    [JSInvokable] public Task OnNext() => Next();
    [JSInvokable] public Task OnPrev() => Prev();

    public ValueTask DisposeAsync() { self?.Dispose(); return ValueTask.CompletedTask; }
}
