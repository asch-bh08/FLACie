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
    /// <summary>The stream of the song that comes after this one, which the page starts fetching once the current song plays, so skipping is instant.
    /// Not for repeat-one, for Apple Lossless (the server decodes it: not worth doing twice) or for formats Jellyfin has to convert.</summary>
    public string? PrefetchUrl()
    {
        if (Repeat == 2 || Index < 0) return null;
        var pos = order.IndexOf(Index);
        if (pos < 0 || pos + 1 >= order.Count) return null;
        var n = Queue[order[pos + 1]];
        var ext = MediaInfo.FromExtension(n.FilePath ?? "");
        if (ext is "m4a" or "alac" or "mp4" || MediaInfo.NotPlayableInBrowsers.Contains(ext) || FormatIndex.Current?.Get(n)?.Codec == "alac") return null;
        return "/stream?p=" + Uri.EscapeDataString(n.Path);
    }

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
    /// <summary>Suggestions for the song playing that are being downloaded right now and are not in the library yet: the queue lists them as "Downloading · ..." rows
    /// and they turn into ordinary rows (tagged "Recently downloaded") when they land.</summary>
    public IReadOnlyList<CatalogSong> AutoplayFetching
    {
        get
        {
            if (!Autoplay || planner is null || session is null || suggested.Count == 0 || library?.Invoke() is not { } lib) return [];
            return planner.Fetching(session, suggested).Where(f => lib.Find(f.Title, f.Artist) is null).ToList();
        }
    }
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
    PlayTracker? plays;
    /// <summary>Reports listening to the "music played" tracker (which only ever speaks for plays outside Jellyfin).</summary>
    public void SetPlays(PlayTracker p) => plays = p;
    void PlayTick() { if (plays is not null && session is not null && Current is { } c) plays.Tick(session.Id, c.Path, Playing); }

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
        // the songs in the order they will be reached (so a move or a removal in Up next, and a shuffle, come back as they were), a few before the current one and many after
        var seq = order.Count == Queue.Count ? order.Select(i => Queue[i]).ToList() : Queue;
        var at = Math.Max(0, order.Count == Queue.Count ? order.IndexOf(Index) : Index);
        var from = Math.Max(0, at - 50);
        var json = System.Text.Json.JsonSerializer.Serialize(new { q = seq.Skip(from).Take(300).Select(t => t.Path), i = at - from, p = Position, s = Shuffle, r = Repeat });
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
            // the list was saved in play order: it is played as it stands, not shuffled again
            await StartAsync(tracks, Math.Clamp(r.GetProperty("i").GetInt32(), 0, tracks.Count - 1), (long)(r.GetProperty("p").GetDouble() * 1000), true, Context, keepOrder: true);
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
        if (startAt <= 0) startAt = ResumeFor(t);   // a long item (audiobook, podcast, long mix) starts where it was left
        await js.InvokeVoidAsync("flacie.load", self, "/stream?p=" + Uri.EscapeDataString(t.Path), t.Title, t.Artist, t.Album,
            t.ArtKey is null ? null : "/art/" + Uri.EscapeDataString(t.ArtKey) + "?v=3", autoplay, t.DurationMs / 1000.0, startAt, PrefetchUrl());
        Position = startAt; Duration = t.DurationMs / 1000.0;
        await ApplyItemSpeedAsync(t);
        if (plays is not null && session is not null) plays.Begin(session.Id, session.DisplayName, t.Path, t.Title, t.Artist, "FLACie Web", Context);
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

    async Task StartAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused, string? context, bool keepOrder = false)
    {
        if (list.Count == 0) return;
        Context = context; AutoplayFrom = -1; autoplayNext = [];
        Queue = list.ToList(); Index = Math.Clamp(index, 0, Queue.Count - 1);
        if (keepOrder) order = Enumerable.Range(0, Queue.Count).ToList(); else Reorder();
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
        else if (Repeat == 1) { if (Shuffle) Reorder(); Index = order[0]; }   // (a shuffled queue is shuffled again; a queue the person arranged is played again as arranged)
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

    // ---- editing Up next (see QueueEdit for the arithmetic) ----

    /// <summary>The queue can be edited here (not while another device is being remote-controlled).</summary>
    public bool CanEdit => Interceptor is null && Current is not null && order.Count == Queue.Count;

    /// <summary>Moves an upcoming song (by its queue index) one place up or down in Up next, or to the top ("Play next"). The current song, and what is playing, are untouched.</summary>
    public void MoveUpNext(int queueIndex, QueueEdit.Step step)
    {
        if (!CanEdit) return;
        if (QueueEdit.Reorder(order, Index, false, queueIndex, step) is not { } moved) return;
        order = moved;
        Stash(true); Changed?.Invoke();
    }

    /// <summary>Takes a song out of the queue. Another song than the playing one: nothing else changes. The playing one: the song that follows starts (it stays paused if
    /// playback was paused); with nothing after it the one before becomes current, paused. The last song out closes the player.</summary>
    public async Task RemoveFromQueue(int queueIndex)
    {
        if (!CanEdit || queueIndex < 0 || queueIndex >= Queue.Count) return;
        if (QueueEdit.Remove(order, Index, queueIndex, Repeat == 1) is not { } r) return;
        var wasPlaying = Playing;
        Queue.RemoveAt(queueIndex);
        if (AutoplayFrom >= 0) { if (queueIndex < AutoplayFrom) AutoplayFrom--; if (AutoplayFrom >= Queue.Count) AutoplayFrom = -1; }
        if (Queue.Count == 0 || r.Current < 0) { order = []; Index = -1; await StopAsync(); return; }
        order = r.Order; Index = r.Current;
        if (r.CurrentRemoved) { await Load(autoplay: !r.Stopped && wasPlaying); return; }
        RefreshSuggestions(); Stash(true); Changed?.Invoke();
    }

    public void PlayNext(Track t) { if (Interceptor?.Enqueue(t, true) == true) return; AddNext(t); }
    public void AddToQueue(Track t) { if (Interceptor?.Enqueue(t, false) == true) return; AddLast(t); }

    public void AddNext(Track t)
    {
        if (Current is null) { _ = PlayFromAsync([t], 0, 0, false); return; }
        if (order.Count != Queue.Count) Reorder();
        var at = Index + 1;
        Queue.Insert(at, t);
        // right after the current song in play order, whatever that order is (the queue's, a shuffle's, or one the person arranged); later indices shift up
        var (o, cur) = QueueEdit.InsertNext(order, Index, at);
        order = o; Index = cur;
        if (AutoplayFrom >= at) AutoplayFrom++;
        RefreshSuggestions(); Stash(true); Changed?.Invoke();
    }
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

    /// <summary>How many songs follow the current one in play order.</summary>
    public int UpNextCount { get { var pos = order.IndexOf(Index); return pos < 0 ? 0 : order.Count - pos - 1; } }

    public IEnumerable<(int Index, Track Track)> UpNext(int n)
    {
        var pos = order.IndexOf(Index);
        return order.Skip(pos + 1).Take(n).Select(i => (i, Queue[i]));
    }

    // ---- from flacie.js ----

    [JSInvokable] public void OnTime(double pos, double dur) { PlaybackActivity.Current?.Heartbeat(); Position = pos; if (dur > 0 && !double.IsInfinity(dur)) Duration = dur; Stash(false); RememberPlace(); SleepTick(); PlayTick(); Changed?.Invoke(); }
    [JSInvokable] public void OnState(bool playing) { Playing = playing; PlayTick(); Changed?.Invoke(); }
    [JSInvokable] public async Task OnEnded()
    {
        if (SleepEndsHere()) { ClearPlace(); await SleepFireAsync(); return; }
        ClearPlace();
        if (OnAutoAdvance is { } hand) { hand(); return; }
        if (Repeat == 2) { await SeekRawAsync(0); await js.InvokeVoidAsync("flacie.play"); } else await NextAsync();
    }
    [JSInvokable] public Task OnNext() => Next();
    [JSInvokable] public Task OnPrev() => Prev();

    // ---- speed, long items and the sleep timer (the rules are in SleepAndSpeed.cs, shared with the Android app's behaviour) ----

    public double Speed { get; private set; } = 1;
    ItemMemory speedMem = new(), placeMem = new(100);
    double spokenSpeed = 1; bool memoryLoaded; long lastPlaceSave;

    /// <summary>Reads the remembered speeds and places kept in this browser (once).</summary>
    public async Task LoadSpeedMemoryAsync()
    {
        if (memoryLoaded) return;
        memoryLoaded = true;
        try
        {
            speedMem = ItemMemory.Decode(await js.InvokeAsync<string?>("flacie.unstash", "flacie.speeds"));
            placeMem = ItemMemory.Decode(await js.InvokeAsync<string?>("flacie.unstash", "flacie.places"), 100);
            if (double.TryParse(await js.InvokeAsync<string?>("flacie.unstash", "flacie.spokenspeed"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)) spokenSpeed = SpeedPolicy.Clamp(s);
        }
        catch (Exception) { }
    }

    async Task ApplyItemSpeedAsync(Track t)
    {
        await LoadSpeedMemoryAsync();
        var saved = speedMem.Get(SpeedPolicy.KeyOf(t.Path));
        await ApplySpeedAsync(saved ?? (SpeedPolicy.IsSpoken(t.Genre) ? spokenSpeed : 1));
    }

    async Task ApplySpeedAsync(double s)
    {
        Speed = SpeedPolicy.Clamp(s);
        try { await js.InvokeVoidAsync("flacie.rate", Speed); } catch (Exception) { }
    }

    /// <summary>Sets the speed (pitch kept) and remembers it for this item; on spoken word it is also the default for the next one.</summary>
    public async Task SetSpeedAsync(double s)
    {
        await LoadSpeedMemoryAsync();
        await ApplySpeedAsync(s);
        if (Current is { } t)
        {
            var spoken = SpeedPolicy.IsSpoken(t.Genre);
            speedMem.Set(SpeedPolicy.KeyOf(t.Path), spoken || Speed != 1 ? Speed : null);
            if (spoken) spokenSpeed = Speed;
            try
            {
                await js.InvokeVoidAsync("flacie.stash", "flacie.speeds", speedMem.Encode());
                if (spoken) await js.InvokeVoidAsync("flacie.stash", "flacie.spokenspeed", Speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception) { }
        }
        Changed?.Invoke();
    }

    /// <summary>Skip back 10 s / forward 30 s inside the song.</summary>
    public Task SkipByAsync(double seconds) => Seek(Math.Clamp(Position + seconds, 0, Math.Max(0, Duration - 1)));

    double ResumeFor(Track t) => SpeedPolicy.ResumeAt(placeMem.Get(SpeedPolicy.KeyOf(t.Path)) is { } p ? (long)p : null, t.DurationMs) / 1000.0;

    void RememberPlace()
    {
        if (Current is not { } t || !SpeedPolicy.IsLong(t.DurationMs)) return;
        var now = Environment.TickCount64;
        if (now - lastPlaceSave < 5_000) return;
        lastPlaceSave = now;
        var ms = (long)(Position * 1000);
        if (SpeedPolicy.ShouldSavePlace(ms, t.DurationMs)) placeMem.Set(SpeedPolicy.KeyOf(t.Path), ms);
        else if (ms >= t.DurationMs - SpeedPolicy.FinishedMs) placeMem.Set(SpeedPolicy.KeyOf(t.Path), null);
        else return;
        _ = js.InvokeVoidAsync("flacie.stash", "flacie.places", placeMem.Encode()).AsTask().ContinueWith(_ => { });
    }

    void ClearPlace()
    {
        if (Current is not { } t || !SpeedPolicy.IsLong(t.DurationMs)) return;
        placeMem.Set(SpeedPolicy.KeyOf(t.Path), null);
        _ = js.InvokeVoidAsync("flacie.stash", "flacie.places", placeMem.Encode()).AsTask().ContinueWith(_ => { });
    }

    public SleepMode SleepMode { get; private set; }
    DateTime sleepEndsUtc; string? sleepArmedPath; double fade = 1;

    List<Track> PlayOrderTracks() => order.Select(i => Queue[i]).ToList();

    /// <summary>Time left until the timer fires, in real time (a faster speed shortens what is left of a song); null when off or not known.</summary>
    public long? SleepRemainingMs()
    {
        var pos = (long)(Position * 1000);
        long? left = SleepMode switch
        {
            SleepMode.Minutes => Math.Max(0, (long)(sleepEndsUtc - DateTime.UtcNow).TotalMilliseconds),
            SleepMode.EndOfTrack => SleepPlan.TrackRemaining(pos, Current?.DurationMs ?? 0),
            SleepMode.EndOfAlbum => SleepPlan.AlbumRemaining(PlayOrderTracks(), order.IndexOf(Index), pos),
            _ => null,
        };
        return left is { } l && SleepMode != SleepMode.Minutes ? (long)(l / Speed) : left;
    }

    public string SleepStatus()
    {
        if (SleepMode == SleepMode.Off) return "Off";
        var left = SleepRemainingMs() is { } l ? SleepPlan.FormatLeft(l) : null;
        return SleepMode == SleepMode.Minutes ? left ?? "Timer" : SleepPlan.Label(SleepMode) + (left is null ? "" : $" ({left})");
    }

    public async Task SetSleepAsync(SleepMode mode, int minutes = 0)
    {
        SleepMode = mode; sleepArmedPath = mode == SleepMode.EndOfTrack ? Current?.Path : null;
        if (mode == SleepMode.Minutes) sleepEndsUtc = DateTime.UtcNow.AddMinutes(minutes);
        if (mode == SleepMode.Off) await SetFadeAsync(1);
        Changed?.Invoke();
    }

    /// <summary>Adds the extend minutes to what is left (a song or album timer becomes a minutes timer).</summary>
    public async Task ExtendSleepAsync()
    {
        if (SleepMode == SleepMode.Off) return;
        var now = DateTime.UtcNow;
        var left = SleepRemainingMs() ?? 0;
        sleepEndsUtc = SleepPlan.Extend(now.AddMilliseconds(left), now); SleepMode = SleepMode.Minutes; sleepArmedPath = null;
        await SetFadeAsync(1);
        Changed?.Invoke();
    }

    async Task SetFadeAsync(double g) { fade = g; try { await js.InvokeVoidAsync("flacie.fade", g); } catch (Exception) { } }

    bool SleepEndsHere() => SleepMode switch
    {
        SleepMode.EndOfTrack => true,
        SleepMode.EndOfAlbum => SleepPlan.AlbumEndsAfter(PlayOrderTracks(), order.IndexOf(Index)),
        _ => false,
    };

    async Task SleepFireAsync()
    {
        SleepMode = SleepMode.Off; sleepArmedPath = null;
        await SetFadeAsync(1);
        try { await js.InvokeVoidAsync("flacie.pause"); } catch (Exception) { }
        Changed?.Invoke();
    }

    void SleepTick()
    {
        if (SleepMode == SleepMode.Off) return;
        // "end of this song": a different song has started (a skip): the moment has passed
        if (SleepMode == SleepMode.EndOfTrack && sleepArmedPath is not null && Current?.Path != sleepArmedPath) { _ = SleepFireAsync(); return; }
        var left = SleepRemainingMs();
        if (left is { } l && l <= (SleepMode == SleepMode.Minutes ? 0 : 250)) { _ = SleepFireAsync(); return; }
        var g = left is { } r ? SleepPlan.FadeGain(r) : 1;
        if (Math.Abs(g - fade) > 0.01) _ = SetFadeAsync(g);
    }

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
