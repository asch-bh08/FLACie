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
    /// <summary>Where the queue came from ("Pop", an album, "Favourites"), shown as "Playing from".</summary>
    public string? Context { get; private set; }
    public bool Autoplay { get; private set; } = true;
    /// <summary>Songs that will be added when the queue runs out (similar to what is playing), and where the added ones start.</summary>
    public IReadOnlyList<Track> AutoplayNext => autoplayNext;
    public int AutoplayFrom { get; private set; } = -1;
    List<Track> autoplayNext = [];
    Func<Library>? library;
    public void SetLibrary(Func<Library> f) => library = f;
    Func<Track, IReadOnlyList<Track>>? related;
    /// <summary>Songs that sit in the same playlists as the given one: the best hint of what goes with it.</summary>
    public void SetRelated(Func<Track, IReadOnlyList<Track>> f) => related = f;
    public async Task LoadAutoplayAsync() { try { Autoplay = await js.InvokeAsync<string?>("flacie.unstash", "flacie.autoplay") != "0"; } catch (Exception) { } RefreshSuggestions(); Changed?.Invoke(); }
    public async Task SetAutoplayAsync(bool on)
    {
        Autoplay = on; RefreshSuggestions();
        try { await js.InvokeVoidAsync("flacie.stash", "flacie.autoplay", on ? "1" : "0"); } catch (Exception) { }
        Changed?.Invoke();
    }

    /// <summary>Songs that go with the one playing. First the ones the online lookup suggests and the library already has, then the library's own
    /// idea of what fits (same artist, shared playlists, same genre), then anything. Never a song already queued or played lately, and never
    /// the same song on another release (same title and lead artist counts as the same song).</summary>
    void RefreshSuggestions()
    {
        autoplayNext = [];
        if (!Autoplay || Current is not { } cur || library?.Invoke() is not { } lib) return;
        var seen = Queue.Select(Matching.MatchKey).ToHashSet();
        seen.Add(Matching.MatchKey(cur));
        if (session is not null) foreach (var h in session.History.Take(40)) seen.Add(Matching.MatchKey(h.Title, h.Artist));
        bool Fresh(Track t) => t.DurationMs is 0 or > 40_000 && !seen.Contains(Matching.MatchKey(t));
        var res = new List<Track>(); var taken = new HashSet<string>();
        void Take(IEnumerable<Track> src, int max)
        {
            var n = 0;
            foreach (var t in src)
            {
                if (n >= max || res.Count >= 10) return;
                if (!Fresh(t) || !taken.Add(Matching.MatchKey(t))) continue;
                res.Add(t); n++;
            }
        }
        var artist = Matching.PrimaryArtist(cur.Artist);
        var rnd = Random.Shared;
        // suggestions from the lookup, in its order (the artist's own hits first, then similar artists'), if the library has them
        Take(suggested.Select(s => lib.Find(s.Title, s.Artist)).OfType<Track>(), 6);
        Take(lib.Songs.Where(t => Matching.PrimaryArtist(t.Artist) == artist).OrderBy(_ => rnd.Next()), 3);
        Take((related?.Invoke(cur) ?? []).OrderBy(_ => rnd.Next()), 4);
        if (cur.Genre.Length > 0) Take(lib.Songs.Where(t => t.Genre.Equals(cur.Genre, StringComparison.OrdinalIgnoreCase)).OrderBy(_ => rnd.Next()), 4);
        Take(lib.Songs.OrderBy(_ => rnd.Next()), 10);
        autoplayNext = res;
    }

    // ---- looking ahead: what the lookup suggests, and fetching what the library lacks ----

    List<CatalogSong> suggested = [];
    UserSession? session;
    AutoplayPlanner? planner;
    CancellationTokenSource? planning;
    public void SetPlanner(UserSession s, AutoplayPlanner p) { session = s; planner = p; }

    async Task PlanAsync(Track seed)
    {
        planning?.Cancel(); var cts = planning = new CancellationTokenSource();
        if (!Autoplay || planner is null || session is null || library?.Invoke() is not { } lib) return;
        try
        {
            suggested = await planner.RelatedAsync(seed, cts.Token);
            if (cts.IsCancellationRequested) return;
            RefreshSuggestions(); Changed?.Invoke();
            var s = session;
            var missing = suggested.Where(x => lib.Find(x.Title, x.Artist) is null && Matching.MatchKey(x.Title, x.Artist) != Matching.MatchKey(seed)).Take(8);
            planner.Prefetch(s, missing, 5, () => { RefreshSuggestions(); Changed?.Invoke(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public event Action? Changed;
    /// <summary>A page that starts music asks the player bar to open the full-screen player.</summary>
    public event Action? FullScreenRequested;
    public void RequestFullScreen() => FullScreenRequested?.Invoke();
    /// <summary>A song began (for the play history).</summary>
    public event Action<Track>? Started;
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
        for (var i = 0; i < 180 && s.Library.Songs.Count == 0; i++) await Task.Delay(500);
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
        RefreshSuggestions(); _ = PlanAsync(t); Started?.Invoke(t);
        Changed?.Invoke();
    }

    public async Task Play(IReadOnlyList<Track> list, int index, string? context = null)
    {
        if (list.Count == 0) return;
        if (Interceptor?.Play(list, index) == true) return;
        await StartAsync(list, index, 0, false, context);
    }

    public Task PlayFromAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused) => StartAsync(list, index, positionMs, paused, Context);

    async Task StartAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused, string? context)
    {
        if (list.Count == 0) return;
        Context = context; AutoplayFrom = -1; autoplayNext = [];
        Queue = list.ToList(); Index = Math.Clamp(index, 0, Queue.Count - 1);
        Reorder();
        await Load(!paused, positionMs / 1000.0);
    }

    public async Task ShuffleAll(IReadOnlyList<Track> list, string? context = null) { Shuffle = true; if (list.Count > 0) await Play(list, Random.Shared.Next(list.Count), context); }

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
        else if (Autoplay && autoplayNext.Count > 0)
        {
            // the queue ran out: carry on with songs like the last one, so the music never just stops
            AutoplayFrom = Queue.Count; var start = Queue.Count;
            Queue.AddRange(autoplayNext); autoplayNext = [];
            for (var i = start; i < Queue.Count; i++) order.Add(i);
            Index = start;
        }
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

    /// <summary>Stops the music and closes the player: the queue is emptied and the bar goes away. It is what the X on the bar does.</summary>
    public async Task StopAsync()
    {
        planning?.Cancel();
        try { await js.InvokeVoidAsync("flacie.stop"); } catch (Exception) { }
        Queue = []; order = []; Index = -1; Playing = false; Position = 0; Duration = 0; autoplayNext = []; suggested = []; AutoplayFrom = -1; Context = null;
        if (stashKey is not null) { try { await js.InvokeVoidAsync("flacie.stash", stashKey, ""); } catch (Exception) { } }
        Changed?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        if (connect is not null && ReferenceEquals(connect.Host, this)) connect.Host = null;
        self?.Dispose();
        return ValueTask.CompletedTask;
    }
}
