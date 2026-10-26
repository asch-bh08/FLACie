using FLACie.Core;
using Microsoft.JSInterop;

namespace FLACie.Server;

/// <summary>The player for one browser tab: the queue lives here, the audio element in flacie.js plays it. Audio comes
/// from /stream on this server; covers from /art. It is also the <see cref="IPlayerHost"/> that Connect and Jams drive.</summary>
public sealed class PlayerState(IJSRuntime js, IPlayerOutput? output = null) : IPlayerHost, IAsyncDisposable
{
    /// <summary>What makes the sound: the page's audio element (flacie.js), or the host's own player (the Android app's media service).</summary>
    readonly IPlayerOutput audio = output ?? new PageOutput(js);

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
        if (ext is "m4a" or "alac" or "mp4" || MediaInfo.NotPlayableInBrowsers.Contains(ext) || RetroHost.KnownCodec?.Invoke(n) == "alac") return null;
        return "/stream?p=" + Uri.EscapeDataString(n.Path);
    }

    public async Task LoadVolumeAsync() { Volume = await audio.VolumeAsync(null); if (Volume > 0) lastVolume = Volume; Changed?.Invoke(); }
    public async Task SetVolumeAsync(double v) { Volume = await audio.VolumeAsync(Math.Clamp(v, 0, 1)); if (Volume > 0) lastVolume = Volume; Changed?.Invoke(); }
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
        autoplayNext = res; autoplayWhy = [];
        UseEngine(cur, seen);
    }

    // ---- the listening log: how much of each song was really heard (a seek does not count as listening) ----

    Track? heardTrack; double heard, heardFrom; string? heardContext, listenContext;

    void CountHeard(double pos)
    {
        // a tick moves on by about a second while a song plays; a bigger jump is a seek, a backwards one a replay
        if (heardTrack is not null && pos > heardFrom && pos - heardFrom <= 3) heard += pos - heardFrom;
        heardFrom = pos;
    }

    /// <summary>Where a song at this queue place came from, for the log: Autoplay, the playlist (its id), the Home page or a search.</summary>
    string? ListenContextOf(int queueIndex)
    {
        if (AutoplayFrom >= 0 && queueIndex >= AutoplayFrom) return "autoplay";
        if (listenContext is { Length: > 0 }) return listenContext;
        if (Context is not { Length: > 0 } name) return null;
        if (session?.Playlists.FirstOrDefault(p => p.Name == name) is { } pl) return pl.Id;
        return name.StartsWith("Search", StringComparison.Ordinal) || name.StartsWith("Explore", StringComparison.Ordinal) ? "search" : null;
    }

    /// <summary>The song that was playing is over (or left): tells the log how long it was heard.</summary>
    void FlushPlay()
    {
        try
        {
            if (heardTrack is { } t && session is { } s && heard >= 1)
                RetroHost.Listen?.Invoke(s, ListenEvent.Of(ListenKind.Play, t, Math.Round(heard, 1), t.DurationMs > 0 ? t.DurationMs / 1000.0 : Duration, heardContext));
        }
        catch (Exception) { }   // a log that fails must never stop the next song or the stop
        heardTrack = null; heard = 0;
    }

    // ---- Autoplay from the recommendation engine, where the host has one: it picks from what the person has played, hearted and skipped ----

    string? enginePath; List<(Track Track, string Why)>? enginePicks;
    Dictionary<string, string> autoplayWhy = [], addedWhy = [];
    readonly List<string> recent = [];
    readonly HashSet<string> fetchedKeys = [];

    /// <summary>Why the song playing was picked, when Autoplay added it from the engine's picks; null otherwise.</summary>
    public string? WhyCurrent => Current is { } c && AutoplayFrom >= 0 && Index >= AutoplayFrom && addedWhy.TryGetValue(c.Path, out var w) ? w : null;

    /// <summary>Swaps the engine's picks in for the list above, but only when nothing the person chose is left to play (at most one song follows the current one).
    /// They are asked for once per song; until they arrive, or if the host has no engine or it fails, the list above stands.</summary>
    void UseEngine(Track cur, HashSet<string> seen)
    {
        if (RetroHost.RecommendNext is not { } ask || session is not { } s || UpNext(2).Count() > 1) return;
        if (enginePath != cur.Path) { enginePath = cur.Path; enginePicks = null; _ = AskEngineAsync(ask, s, cur.Path); return; }
        if (enginePicks is null) return;
        var fresh = enginePicks.Where(p => !seen.Contains(Matching.MatchKey(p.Track))).Take(10).ToList();
        if (fresh.Count == 0) return;
        // songs fetched just now for this Autoplay stay at the front (they carry their "Recently downloaded" tag), the engine's picks follow
        var fetched = autoplayNext.Where(t => fetchedKeys.Contains(Matching.MatchKey(t))).ToList();
        fresh = fresh.Where(p => !fetched.Any(f => f.Path == p.Track.Path)).ToList();
        autoplayNext = fetched.Concat(fresh.Select(p => p.Track)).Take(10).ToList();
        autoplayWhy = fresh.ToDictionary(p => p.Track.Path, p => p.Why, StringComparer.Ordinal);
    }

    async Task AskEngineAsync(Func<UserSession, IReadOnlyList<string>, int, IReadOnlyList<(Track Track, string Why)>?> ask, UserSession s, string path)
    {
        try
        {
            var played = recent.ToList();
            var picks = await Task.Run(() => ask(s, played, 10));
            if (picks is null || enginePath != path) return;
            enginePicks = picks.ToList();
            RefreshSuggestions(); Changed?.Invoke();
        }
        catch (Exception) { }   // the library's own suggestions stay
    }

    // ---- looking ahead: what the lookup suggests, and fetching what the library lacks ----

    List<CatalogSong> suggested = [];
    UserSession? session;
    IAutoplayPlanner? planner;
    CancellationTokenSource? planning;
    public void SetPlanner(UserSession s, IAutoplayPlanner p) { session = s; planner = p; }
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
            foreach (var m in missing) fetchedKeys.Add(Matching.MatchKey(m.Title, m.Artist));
            planner.Prefetch(s, missing, 5, () => { RefreshSuggestions(); Changed?.Invoke(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public event Action? Changed;
    /// <summary>Something the player shows changed from outside it (a chosen cover): draw it again now, paused or not.</summary>
    public void Redraw() => Changed?.Invoke();
    /// <summary>A page that starts music asks the player bar to open the full-screen player.</summary>
    public event Action? FullScreenRequested;
    public void RequestFullScreen() => FullScreenRequested?.Invoke();
    /// <summary>A song began (for the play history).</summary>
    public event Action<Track>? Started;
    public IPlayInterceptor? Interceptor { get; set; }
    public Action? OnAutoAdvance { get; set; }
    DotNetObjectReference<PlayerState>? self;
    List<int> order = [];
    /// <summary>The play order (play position → queue position): the queue as it will actually play, shuffle included.</summary>
    public IReadOnlyList<int> PlayOrder => order;
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
        // the page reloaded itself after a server restart (flacie.recoverReload): what was playing plays on, from where the browser had got to
        string? recover = null;
        try { recover = await js.InvokeAsync<string?>("flacie.takeRecover"); } catch (Exception) { }
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
            var at = r.GetProperty("p").GetDouble(); var paused = true;
            if (!string.IsNullOrEmpty(recover))
            {
                using var rd = System.Text.Json.JsonDocument.Parse(recover);
                paused = !rd.RootElement.GetProperty("playing").GetBoolean();
                if (rd.RootElement.GetProperty("t").GetDouble() is var t0 && t0 > 0) at = t0;
            }
            await StartAsync(tracks, Math.Clamp(r.GetProperty("i").GetInt32(), 0, tracks.Count - 1), (long)(at * 1000), paused, Context, keepOrder: true);
        }
        catch (Exception) { }
    }

    /// <summary>Joins this tab to the account's Connect: remote commands drive the tab that last played (or the first one open).</summary>
    public void Bind(Connect c) { connect = c; if (c.Host is null) c.Host = this; }

    async Task Load(bool autoplay, double startAt = 0)
    {
        var t = Current;
        if (t is null) return;
        FlushPlay();
        if (connect is not null && !ReferenceEquals(connect.Host, this)) connect.Host = this; // this tab is the one playing now
        self ??= DotNetObjectReference.Create(this);
        if (startAt <= 0) startAt = ResumeFor(t);   // a long item (audiobook, podcast, long mix) starts where it was left
        await audio.LoadAsync(self, t, "/stream?p=" + Uri.EscapeDataString(t.Path), t.Title, t.Artist, t.Album,
            t.ArtKey is null ? null : "/art/" + Uri.EscapeDataString(CoverOverrides.Map(t.ArtKey)!) + "?v=3", autoplay, t.DurationMs / 1000.0, startAt, PrefetchUrl());
        Position = startAt; Duration = t.DurationMs / 1000.0;
        heardTrack = t; heard = 0; heardFrom = startAt; heardContext = ListenContextOf(Index);
        recent.Add(t.Path); if (recent.Count > 5) recent.RemoveAt(0);
        await ApplyItemSpeedAsync(t);
        await ApplyLevelAsync(t);
        await ApplyFadeAsync(t);
        if (plays is not null && session is not null) plays.Begin(session.Id, session.DisplayName, t.Path, t.Title, t.Artist, "FLACie Web", Context);
        Stash(true);
        RefreshSuggestions(); _ = PlanAsync(t); Started?.Invoke(t);
        Changed?.Invoke();
    }

    /// <param name="listenContext">Where the play came from for the listening log ("home"); otherwise it follows from the context (a playlist, a search).</param>
    public async Task Play(IReadOnlyList<Track> list, int index, string? context = null, string? listenContext = null)
    {
        if (list.Count == 0) return;
        if (Interceptor?.Play(list, index) == true) return;
        await StartAsync(list, index, 0, false, context, listenContext: listenContext);
    }

    public Task PlayFromAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused) => StartAsync(list, index, positionMs, paused, Context);
    /// <summary>Takes a queue exactly as it will play (another player's, already shuffled or arranged): it is not shuffled again.</summary>
    public Task AdoptAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused) => StartAsync(list, index, positionMs, paused, Context, keepOrder: true);

    async Task StartAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused, string? context, bool keepOrder = false, string? listenContext = null)
    {
        if (list.Count == 0) return;
        Context = context; AutoplayFrom = -1; autoplayNext = []; addedWhy = []; enginePath = null; fetchedKeys.Clear(); this.listenContext = listenContext;
        Queue = list.ToList(); Index = Math.Clamp(index, 0, Queue.Count - 1);
        if (keepOrder) order = Enumerable.Range(0, Queue.Count).ToList(); else Reorder();
        await Load(!paused, positionMs / 1000.0);
    }

    public async Task ShuffleAll(IReadOnlyList<Track> list, string? context = null, string? listenContext = null) { Shuffle = true; if (list.Count > 0) await Play(list, Random.Shared.Next(list.Count), context, listenContext); }

    public async Task Toggle() { if (Interceptor?.Toggle() == true) return; await ToggleAsync(); }
    public async Task Seek(double seconds) { if (Interceptor?.Seek((long)(seconds * 1000)) == true) return; await SeekRawAsync((long)(seconds * 1000)); }

    public async Task ToggleAsync() { if (Current is null) return; await audio.ToggleAsync(); }
    public async Task PauseAsync() => await audio.PauseAsync();
    public async Task ResumeAsync() { if (Current is not null) await audio.PlayAsync(); }
    public async Task SeekRawAsync(long ms) { await audio.SeekAsync(ms / 1000.0); Position = ms / 1000.0; Changed?.Invoke(); }
    public async Task<bool> IsBufferedAsync() => await audio.ReadyAsync();

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
            foreach (var w in autoplayWhy) addedWhy[w.Key] = w.Value;
            for (var i = start; i < Queue.Count; i++) order.Add(i);
            Index = start;
        }
        else { await audio.PauseAsync(); return; }
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

    /// <summary>Plays these songs of the queue (queue indices, e.g. one album in album order) one after another from here; then the play order carries on
    /// with what was still to come (a shuffle stays a shuffle). Remote-controlling another device: that device skips to the first of them.</summary>
    public async Task PlayRun(IReadOnlyList<int> run)
    {
        if (run.Count == 0) return;
        if (Interceptor is not null) { await SkipTo(run[0]); return; }
        if (order.Count != Queue.Count) Reorder();
        var next = QueueEdit.PlayRunNext(order, Index, run);
        var pos = next.IndexOf(Index);
        if (pos + 1 >= next.Count || !run.Contains(next[pos + 1])) return;
        order = next; Index = next[pos + 1];
        await Load(true);
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

    /// <summary>The person does not want this song: it leaves Up next and the Autoplay list, and if it is playing now the next song starts.</summary>
    public async Task Dislike(string path)
    {
        autoplayNext = autoplayNext.Where(t => t.Path != path).ToList();
        enginePicks = enginePicks?.Where(p => p.Track.Path != path).ToList();
        autoplayWhy.Remove(path);
        if (Queue.FirstOrDefault(t => t.Path == path) is { } gone) fetchedKeys.Remove(Matching.MatchKey(gone));
        if (!CanEdit) { if (Current?.Path == path) await NextAsync(); return; }
        var playing = Current?.Path == path;
        // every other copy goes first (a guard keeps a surprise from looping for ever)
        for (var guard = Queue.Count + 1; guard > 0 && CanEdit; guard--)
        {
            var other = Enumerable.Range(0, Queue.Count).FirstOrDefault(i => i != Index && Queue[i].Path == path, -1);
            if (other < 0) break;
            await RemoveFromQueue(other);
        }
        if (!playing || Current?.Path != path) return;
        // the playing copy: what follows starts; with nothing to follow the player stops rather than pauses on it
        var follows = order.IndexOf(Index) + 1 < order.Count || (Repeat == 1 && Queue.Count > 1);
        if (follows) { await RemoveFromQueue(Index); return; }
        if (Autoplay && autoplayNext.Count > 0 && Repeat != 1)
        {
            await NextAsync();   // Autoplay carries on, then the disliked copy is taken out behind it
            var old = Queue.FindIndex(t => t.Path == path);
            if (old >= 0 && old != Index) await RemoveFromQueue(old);
            return;
        }
        await StopAsync();
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

    /// <summary>Puts a song back into Up next as its <paramref name="place"/>-th song (1 = straight after the current one): Undo after a removal.</summary>
    public void InsertUpNext(Track t, int place)
    {
        if (!CanEdit) { AddLast(t); return; }
        Queue.Add(t);
        var cur = order.IndexOf(Index);
        order.Insert(Math.Clamp(cur + place, cur + 1, order.Count), Queue.Count - 1);
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

    [JSInvokable] public void OnTime(double pos, double dur) { RetroHost.Heartbeat?.Invoke(); CountHeard(pos); Position = pos; if (dur > 0 && !double.IsInfinity(dur)) Duration = dur; Stash(false); RememberPlace(); SleepTick(); PlayTick(); Changed?.Invoke(); }
    [JSInvokable] public void OnState(bool playing) { Playing = playing; PlayTick(); Changed?.Invoke(); }
    [JSInvokable] public async Task OnEnded()
    {
        if (SleepEndsHere()) { ClearPlace(); await SleepFireAsync(); return; }
        ClearPlace();
        if (OnAutoAdvance is { } hand) { hand(); return; }
        if (Repeat == 2) { await SeekRawAsync(0); await audio.PlayAsync(); } else await NextAsync();
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
        try { await audio.RateAsync(Speed); } catch (Exception) { }
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

    /// <summary>Even out volume: songs are turned up or down by the gain Jellyfin measured (kept in this browser; off by default).</summary>
    public bool LevelVolume { get; private set; }
    bool levelLoaded;

    async Task ApplyLevelAsync(Track t)
    {
        if (!levelLoaded) { levelLoaded = true; try { LevelVolume = await js.InvokeAsync<string?>("flacie.unstash", "flacie.level") == "1"; } catch (Exception) { } }
        try { await audio.LoudAsync(Loudness.Factor(t.GainDb, LevelVolume)); } catch (Exception) { }
    }

    public async Task SetLevelVolumeAsync(bool on)
    {
        LevelVolume = on; levelLoaded = true;
        try { await js.InvokeVoidAsync("flacie.stash", "flacie.level", on ? "1" : "0"); } catch (Exception) { }
        if (Current is { } t) await ApplyLevelAsync(t);
        Changed?.Invoke();
    }

    /// <summary>Fade between songs, in seconds (0 = off; kept in this browser).</summary>
    public int FadeSecs { get; private set; }
    bool fadeLoaded;

    async Task ApplyFadeAsync(Track t)
    {
        if (!fadeLoaded) { fadeLoaded = true; try { if (int.TryParse(await js.InvokeAsync<string?>("flacie.unstash", "flacie.fade"), out var n) && FadeBetween.Choices.Contains(n)) FadeSecs = n; } catch (Exception) { } }
        var pos = order.IndexOf(Index);
        Track? prev = pos > 0 ? Queue[order[pos - 1]] : null, next = pos >= 0 && pos + 1 < order.Count ? Queue[order[pos + 1]] : null;
        var (fin, fout) = FadeBetween.Flags(prev, t, next);
        try { await audio.CrossfadeAsync(FadeSecs, fin, fout); } catch (Exception) { }
    }

    public async Task SetFadeSecsAsync(int secs)
    {
        FadeSecs = FadeBetween.Choices.Contains(secs) ? secs : 0; fadeLoaded = true;
        try { await js.InvokeVoidAsync("flacie.stash", "flacie.fade", FadeSecs.ToString()); } catch (Exception) { }
        if (Current is { } t) await ApplyFadeAsync(t);
        Changed?.Invoke();
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

    async Task SetFadeAsync(double g) { fade = g; try { await audio.FadeAsync(g); } catch (Exception) { } }

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
        try { await audio.PauseAsync(); } catch (Exception) { }
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
        FlushPlay();
        try { await audio.StopAsync(); } catch (Exception) { }
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
