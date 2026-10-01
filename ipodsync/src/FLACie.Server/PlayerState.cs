using FLACie.Core;
using Microsoft.JSInterop;

namespace FLACie.Server;

/// <summary>The player for one browser tab: the queue lives here, the audio element in flacie.js plays it. Audio comes
/// from /stream on this server; covers from /art. It is also the <see cref="IPlayerHost"/> that Connect and Jams drive.</summary>
public sealed class PlayerState(IJSRuntime js) : IPlayerHost, IAsyncDisposable
{
    public List<Track> Queue { get; private set; } = [];
    public int Index { get; private set; } = -1;
    public bool Playing { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool Shuffle { get; private set; }
    public int Repeat { get; private set; } // 0 off, 1 all, 2 one
    public double Volume { get; private set; } = 1;
    double lastVolume = 1;
    public async Task LoadVolumeAsync() { Volume = await js.InvokeAsync<double>("flacie.volume", null); if (Volume > 0) lastVolume = Volume; Changed?.Invoke(); }
    public async Task SetVolumeAsync(double v) { Volume = await js.InvokeAsync<double>("flacie.volume", Math.Clamp(v, 0, 1)); if (Volume > 0) lastVolume = Volume; Changed?.Invoke(); }
    public Task ToggleMuteAsync() => SetVolumeAsync(Volume > 0 ? 0 : lastVolume);
    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public event Action? Changed;
    public IPlayInterceptor? Interceptor { get; set; }
    public Action? OnAutoAdvance { get; set; }
    DotNetObjectReference<PlayerState>? self;
    List<int> order = [];
    Connect? connect;

    IReadOnlyList<Track> IPlayerHost.Queue => Queue;
    public long PositionMs => (long)(Position * 1000);

    // ---- remembering the queue across a reload (kept in this browser, per account) ----

    string? stashKey; long lastStash;
    public void SetStashKey(string key) => stashKey = "flacie.queue." + key;

    void Stash(bool force)
    {
        if (stashKey is null || Current is null) return;
        var now = Environment.TickCount64;
        if (!force && now - lastStash < 5_000) return;
        lastStash = now;
        var from = Math.Max(0, Index - 50);
        var json = System.Text.Json.JsonSerializer.Serialize(new { q = Queue.Skip(from).Take(300).Select(t => t.Path), i = Index - from, p = Position, s = Shuffle, r = Repeat });
        _ = js.InvokeVoidAsync("flacie.stash", stashKey, json).AsTask().ContinueWith(_ => { });
    }

    /// <summary>After a reload: puts the last queue back, paused at the second it was left, once the library has loaded.</summary>
    public async Task RestoreAsync(UserSession s)
    {
        if (stashKey is null || Current is not null) return;
        string? json;
        try { json = await js.InvokeAsync<string?>("flacie.unstash", stashKey); } catch (Exception) { return; }
        if (string.IsNullOrEmpty(json)) return;
        for (var i = 0; i < 180 && (s.Loading || s.Library.Songs.Count == 0); i++) await Task.Delay(500);
        if (Current is not null) return; // the user already picked something
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            var tracks = r.GetProperty("q").EnumerateArray().Select(e => s.FindByPath(e.GetString() ?? "")).OfType<Track>().ToList();
            if (tracks.Count == 0) return;
            Shuffle = r.TryGetProperty("s", out var sh) && sh.GetBoolean(); Repeat = r.TryGetProperty("r", out var rp) ? rp.GetInt32() : 0;
            await PlayFromAsync(tracks, Math.Clamp(r.GetProperty("i").GetInt32(), 0, tracks.Count - 1), (long)(r.GetProperty("p").GetDouble() * 1000), true);
        }
        catch (Exception) { }
    }

    /// <summary>Joins this tab to the account's Connect: remote commands drive the tab that last played (or the first one open).</summary>
    public void Bind(Connect c) { connect = c; if (c.Host is null) c.Host = this; }

    async Task Load(bool autoplay, double startAt = 0)
    {
        var t = Current;
        if (t is null) return;
        if (connect is not null && !ReferenceEquals(connect.Host, this)) connect.Host = this; // this tab is the one playing now
        self ??= DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("flacie.load", self, "/stream?p=" + Uri.EscapeDataString(t.Path), t.Title, t.Artist, t.Album,
            t.ArtKey is null ? null : "/art/" + Uri.EscapeDataString(t.ArtKey), autoplay, t.DurationMs / 1000.0, startAt);
        Position = startAt; Duration = t.DurationMs / 1000.0;
        Stash(true);
        Changed?.Invoke();
    }

    public async Task Play(IReadOnlyList<Track> list, int index)
    {
        if (list.Count == 0) return;
        if (Interceptor?.Play(list, index) == true) return;
        await PlayFromAsync(list, index, 0, false);
    }

    public async Task PlayFromAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused)
    {
        if (list.Count == 0) return;
        Queue = list.ToList(); Index = Math.Clamp(index, 0, Queue.Count - 1);
        Reorder();
        await Load(!paused, positionMs / 1000.0);
    }

    public async Task ShuffleAll(IReadOnlyList<Track> list) { Shuffle = true; if (list.Count > 0) await Play(list, Random.Shared.Next(list.Count)); }

    public async Task Toggle() { if (Interceptor?.Toggle() == true) return; await ToggleAsync(); }
    public async Task Seek(double seconds) { if (Interceptor?.Seek((long)(seconds * 1000)) == true) return; await SeekRawAsync((long)(seconds * 1000)); }

    public async Task ToggleAsync() { if (Current is null) return; await js.InvokeVoidAsync("flacie.toggle"); }
    public async Task PauseAsync() => await js.InvokeVoidAsync("flacie.pause");
    public async Task ResumeAsync() { if (Current is not null) await js.InvokeVoidAsync("flacie.play"); }
    public async Task SeekRawAsync(long ms) { await js.InvokeVoidAsync("flacie.seek", ms / 1000.0); Position = ms / 1000.0; Changed?.Invoke(); }
    public async Task<bool> IsBufferedAsync() => await js.InvokeAsync<bool>("flacie.ready");

    public async Task Next() { if (Interceptor?.Next() == true) return; await NextAsync(); }
    public async Task Prev() { if (Interceptor?.Prev() == true) return; await PrevAsync(); }

    public async Task NextAsync()
    {
        if (Queue.Count == 0) return;
        var pos = order.IndexOf(Index);
        if (pos + 1 < order.Count) Index = order[pos + 1];
        else if (Repeat == 1) { Reorder(); Index = order[0]; }
        else { await js.InvokeVoidAsync("flacie.pause"); return; }
        await Load(true);
    }

    public async Task PrevAsync()
    {
        if (Queue.Count == 0) return;
        if (Position > 3) { await SeekRawAsync(0); return; }
        var pos = order.IndexOf(Index);
        if (pos > 0) Index = order[pos - 1];
        await Load(true);
    }

    public async Task SkipTo(int index)
    {
        if (Interceptor?.SkipTo(index) == true) return;
        if (index >= 0 && index < Queue.Count) { Index = index; await Load(true); }
    }

    public void PlayNext(Track t) { if (Interceptor?.Enqueue(t, true) == true) return; AddNext(t); }
    public void AddToQueue(Track t) { if (Interceptor?.Enqueue(t, false) == true) return; AddLast(t); }

    public void AddNext(Track t) { if (Current is null) { _ = PlayFromAsync([t], 0, 0, false); return; } Queue.Insert(Index + 1, t); Reorder(keepCurrentFirst: true); Changed?.Invoke(); }
    public void AddLast(Track t) { if (Current is null) { _ = PlayFromAsync([t], 0, 0, false); return; } Queue.Add(t); order.Add(Queue.Count - 1); Changed?.Invoke(); }

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

    [JSInvokable] public void OnTime(double pos, double dur) { Position = pos; if (dur > 0 && !double.IsInfinity(dur)) Duration = dur; Stash(false); Changed?.Invoke(); }
    [JSInvokable] public void OnState(bool playing) { Playing = playing; Changed?.Invoke(); }
    [JSInvokable] public async Task OnEnded()
    {
        if (OnAutoAdvance is { } hand) { hand(); return; }
        if (Repeat == 2) { await SeekRawAsync(0); await js.InvokeVoidAsync("flacie.play"); } else await NextAsync();
    }
    [JSInvokable] public Task OnNext() => Next();
    [JSInvokable] public Task OnPrev() => Prev();

    public ValueTask DisposeAsync()
    {
        if (connect is not null && ReferenceEquals(connect.Host, this)) connect.Host = null;
        self?.Dispose();
        return ValueTask.CompletedTask;
    }
}
