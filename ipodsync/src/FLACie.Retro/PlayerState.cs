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
        Autoplay = on; RefreshSuggestions(); DjAhead();
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
            RefreshSuggestions(); DjAhead(); Changed?.Invoke();
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
        // x: the playlist the queue came from, so its DJ switch still counts after the reload
        var json = System.Text.Json.JsonSerializer.Serialize(new { q = seq.Skip(from).Take(300).Select(t => t.Path), i = at - from, p = Position, s = Shuffle, r = Repeat, x = ContextId });
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
            var contextId = r.TryGetProperty("x", out var x) && x.ValueKind == System.Text.Json.JsonValueKind.String ? x.GetString() : null;
            await StartAsync(tracks, Math.Clamp(r.GetProperty("i").GetInt32(), 0, tracks.Count - 1), (long)(at * 1000), paused, Context, contextId, keepOrder: true);
        }
        catch (Exception) { }
    }

    /// <summary>Joins this tab to the account's Connect: remote commands drive the tab that last played (or the first one open).</summary>
    public void Bind(Connect c) { connect = c; if (c.Host is null) c.Host = this; }

    async Task Load(bool autoplay, double startAt = 0)
    {
        var t = Current;
        if (t is null) return;
        var djBefore = heardTrack;   // the song being left, for the DJ's line over a song reached by a skip
        FlushPlay();
        if (connect is not null && !ReferenceEquals(connect.Host, this)) connect.Host = this; // this tab is the one playing now
        // for the DJ: whether the player got here by itself (the last song ended), and whether the song before was skipped in its first 10 s
        var auto = djAuto; djAuto = false;
        var skipped = djSkip; djSkip = false;
        if (djFresh) djFresh = false; else { djSkips[1] = djSkips[0]; djSkips[0] = !auto && Position < 10; }
        // the DJ's answer about this song is taken now, before anything below can ask about the song after it (the Autoplay lookup answers at once
        // from its cache, and its DjAhead used to throw this answer away, so the listener never heard the DJ when a song ended by itself)
        var djCueNow = DjTake(t);
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
        await DjStartedAsync(t, auto, skipped, djBefore, djCueNow);
        Changed?.Invoke();
    }

    /// <summary>Plays a list from a song. <paramref name="contextId"/>: the playlist it came from (its DJ key, <see cref="DjSwitches.KeyOf"/>), so its DJ switch counts.</summary>
    /// <param name="listenContext">Where the play came from for the listening log ("home"); otherwise it follows from the context (a playlist, a search).</param>
    public async Task Play(IReadOnlyList<Track> list, int index, string? context = null, string? contextId = null, string? listenContext = null)
    {
        if (list.Count == 0) return;
        if (Interceptor?.Play(list, index) == true) return;
        await StartAsync(list, index, 0, false, context, contextId, listenContext: listenContext);
    }

    // a queue handed over (Connect, a Jam, Alexa, the phone app) or a lone song added to an empty player is a new queue that came from no playlist here: the
    // playlist's DJ switch of whatever played before must not follow it (StartAsync also puts "this queue" back to untouched, as for every new queue)
    public Task PlayFromAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused) => StartAsync(list, index, positionMs, paused, Context, contextId: null);
    /// <summary>Takes a queue exactly as it will play (another player's, already shuffled or arranged): it is not shuffled again.</summary>
    public Task AdoptAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused) => StartAsync(list, index, positionMs, paused, Context, contextId: null, keepOrder: true);

    async Task StartAsync(IReadOnlyList<Track> list, int index, long positionMs, bool paused, string? context, string? contextId = null, bool keepOrder = false, string? listenContext = null)
    {
        if (list.Count == 0) return;
        Context = context; AutoplayFrom = -1; autoplayNext = []; addedWhy = []; enginePath = null; fetchedKeys.Clear(); this.listenContext = listenContext;
        // a new queue: the DJ's "this queue" switch starts untouched, and no skips are remembered
        ContextId = contextId; QueueDj = null; djFresh = true; djSkips[0] = djSkips[1] = false;
        Queue = list.ToList(); Index = Math.Clamp(index, 0, Queue.Count - 1);
        if (keepOrder) order = Enumerable.Range(0, Queue.Count).ToList(); else Reorder();
        await Load(!paused, positionMs / 1000.0);
    }

    public async Task ShuffleAll(IReadOnlyList<Track> list, string? context = null, string? contextId = null, string? listenContext = null) { Shuffle = true; if (list.Count > 0) await Play(list, Random.Shared.Next(list.Count), context, contextId, listenContext); }

    public async Task Toggle() { if (Interceptor?.Toggle() == true) return; await ToggleAsync(); }
    public async Task Seek(double seconds) { if (Interceptor?.Seek((long)(seconds * 1000)) == true) return; await SeekRawAsync((long)(seconds * 1000)); }

    public async Task ToggleAsync() { if (Current is null) return; if (Playing) await DjStopAsync(); await audio.ToggleAsync(); }
    public async Task PauseAsync() { await DjStopAsync(); await audio.PauseAsync(); }
    public async Task ResumeAsync() { if (Current is not null) await audio.PlayAsync(); }
    public async Task SeekRawAsync(long ms) { await DjStopAsync(); await audio.SeekAsync(ms / 1000.0); Position = ms / 1000.0; Changed?.Invoke(); }
    public async Task<bool> IsBufferedAsync() => await audio.ReadyAsync();

    public async Task Next() { if (Interceptor?.Next() == true) return; djSkip = true; try { await NextAsync(); } finally { djSkip = false; } }
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
        if (index >= 0 && index < Queue.Count) { Index = index; djSkip = true; try { await Load(true); } finally { djSkip = false; } }
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
        Stash(true); DjAhead(); Changed?.Invoke();
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
        RefreshSuggestions(); Stash(true); DjAhead(); Changed?.Invoke();
    }

    /// <summary>Puts a song back into Up next as its <paramref name="place"/>-th song (1 = straight after the current one): Undo after a removal.</summary>
    public void InsertUpNext(Track t, int place)
    {
        if (!CanEdit) { AddLast(t); return; }
        Queue.Add(t);
        var cur = order.IndexOf(Index);
        order.Insert(Math.Clamp(cur + place, cur + 1, order.Count), Queue.Count - 1);
        RefreshSuggestions(); Stash(true); DjAhead(); Changed?.Invoke();
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
        RefreshSuggestions(); Stash(true); DjAhead(); Changed?.Invoke();
    }
    public void AddLast(Track t) { if (Current is null) { _ = PlayFromAsync([t], 0, 0, false); return; } Queue.Add(t); order.Add(Queue.Count - 1); DjAhead(); Changed?.Invoke(); }

    public void ToggleShuffle() { Shuffle = !Shuffle; Reorder(keepCurrentFirst: true); DjAhead(); Changed?.Invoke(); }
    public void CycleRepeat() { Repeat = (Repeat + 1) % 3; DjAhead(); Changed?.Invoke(); }

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

    [JSInvokable] public void OnTime(double pos, double dur) { RetroHost.Heartbeat?.Invoke(); CountHeard(pos); Position = pos; if (dur > 0 && !double.IsInfinity(dur)) Duration = dur; Stash(false); RememberPlace(); SleepTick(); PlayTick(); if (djFor is null && DjOn && DjNearEnd()) DjAhead(); Changed?.Invoke(); }
    [JSInvokable] public void OnState(bool playing) { Playing = playing; PlayTick(); Changed?.Invoke(); }
    [JSInvokable] public async Task OnEnded()
    {
        if (SleepEndsHere()) { ClearPlace(); await SleepFireAsync(); return; }
        ClearPlace();
        if (OnAutoAdvance is { } hand) { hand(); return; }
        if (Repeat == 2) { await SeekRawAsync(0); await audio.PlayAsync(); }
        else { djAuto = true; try { await NextAsync(); } finally { djAuto = false; } }   // (the DJ speaks only on a move the player made by itself)
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

    // ---- the AI DJ (docs/spec/ai-dj.md): when a song starts, the DJ is asked about the next one in play order; when the player reaches that song by
    // itself (the last one ended: never a skip, Previous or a new queue), the DJ's clip plays over its start, or the device says the line when no clip
    // could be made. A skip, a seek, a pause or a new queue stops it at once. The clip is never a queue item and never counted as a play. ----

    IDjService? dj;
    /// <summary>The DJ, when the host has one (the server); null in the phone app, where every DJ control stays hidden.</summary>
    public IDjService? Dj => dj;
    /// <summary>Where this player is, for the DJ's "Where" ticks: the web or the Windows app.</summary>
    public DjSurface DjSurface { get; private set; } = DjSurface.Web;
    public void SetDj(IDjService d, DjSurface surface) { dj = d; DjSurface = surface; }
    /// <summary>The Now Playing "DJ for this queue" switch: null until it is touched. Every new queue starts untouched.</summary>
    public bool? QueueDj { get; private set; }
    /// <summary>The playlist the queue came from (its DJ key), when it came from one.</summary>
    public string? ContextId { get; private set; }
    /// <summary>The DJ speaks on this queue, by the switches in their order (this queue, the playlist, On/Off with this device's tick).</summary>
    public bool DjOn => dj is not null && session is not null && DjRules.EffectiveOn(dj.Settings(session), DjSurface, ContextId, QueueDj);

    /// <summary>The Now Playing DJ button (and the iPods' Playback row): turns the DJ the other way for this queue only.</summary>
    public async Task ToggleDjAsync()
    {
        if (dj is null) return;
        QueueDj = !DjOn;
        await DjRefreshAsync();
        Changed?.Invoke();
    }

    /// <summary>The DJ's settings changed (Settings, a playlist's switch, this queue's): the next song is asked about again, and a clip stops if the DJ is now off.</summary>
    public async Task DjRefreshAsync()
    {
        djAsking?.Cancel(); djFor = null; djCue = null;
        if (!DjOn) await DjStopAsync();
        DjAhead();
    }

    CancellationTokenSource? djAsking;
    string? djFor;              // the song (path) the DJ was last asked about
    DjCue? djCue;               // and what it answered (null: stays quiet, or no answer yet)
    bool djAnswered;            // the answer about djFor has come (djCue null then means the DJ chose to stay quiet)
    bool djAuto, djFresh;       // the song being loaded was reached by itself; the queue is new (no song before it in this queue)
    bool djSkip;                // the song being loaded was reached by Next or a tap in Up next (it may bring a line when "Talk after skips" is on)
    bool djActive;              // a clip or a line may still be playing
    readonly bool[] djSkips = [false, false];   // the last two song changes: was the song before skipped in its first 10 s

    /// <summary>The song the player moves to by itself when this one ends (what NextAsync would pick); null on repeat-one, at the very end, or when it isn't known yet (a shuffle about to be shuffled again).</summary>
    Track? DjNext()
    {
        if (Repeat == 2 || Index < 0) return null;
        var pos = order.IndexOf(Index);
        if (pos < 0) return null;
        if (pos + 1 < order.Count) return Queue[order[pos + 1]];
        if (Repeat == 1) return Shuffle || order[0] == Index ? null : Queue[order[0]];
        return Autoplay && autoplayNext.Count > 0 ? autoplayNext[0] : null;
    }

    /// <summary>What the player knows for certain: a favourite is loved. (Whether a song was never played is the server's to say, from the whole account's listening.)</summary>
    static DjBond BondOf(UserSession s, Track t) => s.Favorites.Any(f => f.Path == t.Path) ? DjBond.Loved : DjBond.Unknown;

    static DjSong SongOf(Track t) => new(t.Title, t.Artist, t.Album, t.AlbumArtist, t.Year, t.Genre, t.DurationMs, t.Path, t.JellyfinId, t.TrackNo, t.DiscNo);

    /// <summary>Asks the DJ about the song that comes next, once per song (the next song changing, after an edit of Up next, cancels the ask before it).
    /// Nothing is asked while the DJ is off for this queue. A clip in the answer is fetched ahead.</summary>
    void DjAhead()
    {
        if (dj is not { } d || session is not { } s) return;
        var cur = Current; var next = cur is null ? null : DjNext();
        if (cur is null || next is null || !DjOn) { djAsking?.Cancel(); djFor = null; djCue = null; return; }
        if (djFor == next.Path) return;
        // not yet: asked in the song's last [DjAskLead] (OnTime), so a song skipped before then costs nothing (the owner's DJ used its daily share on
        // lines for songs that were skipped: 115 clips made, 40 said). An answer kept about a song no longer next goes.
        if (!DjNearEnd()) { if (djFor is not null) { djAsking?.Cancel(); djFor = null; djCue = null; } return; }
        djAsking?.Cancel(); var cts = djAsking = new CancellationTokenSource();
        djFor = next.Path; djCue = null; djAnswered = false;
        var req = new DjRequest(DjSurface, SongOf(next), SongOf(cur), PrevPlayedOut: true, SkippedRecently: djSkips[0] || djSkips[1],
            PlaylistId: ContextId, QueueDj: QueueDj, Bond: BondOf(s, next));
        _ = DjAskAsync(d, s, req, next.Path, cts);
    }

    // the browser's distance from UTC in minutes (Date.getTimezoneOffset), read once per tab: this server's own clock is the container's (usually UTC), not the listener's
    int? tzOffset; bool tzKnown;
    async Task<int?> BrowserOffsetAsync()
    {
        if (tzKnown) return tzOffset;
        try { tzOffset = await js.InvokeAsync<int?>("flacie.utcOffset"); tzKnown = true; }
        catch (Exception) { }   // (not read: asked again next time, and this server's clock meanwhile)
        return tzOffset;
    }

    async Task DjAskAsync(IDjService d, UserSession s, DjRequest req, string path, CancellationTokenSource cts)
    {
        try
        {
            // the time where the listener is, for "good morning" and "happy Friday"
            var (hour, weekday) = DjSwitches.LocalTime(DateTime.UtcNow, await BrowserOffsetAsync());
            if (cts.IsCancellationRequested || djFor != path) return;
            req = req with { LocalHour = hour, Weekday = weekday };
            var cue = await d.PrepareAsync(s, req, cts.Token);
            if (cts.IsCancellationRequested || djFor != path) return;
            djCue = cue; djAnswered = true;
            if (cue?.ClipUrl is { Length: > 0 } url) await audio.DjPreloadAsync(url);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { try { RetroHost.DjFailed?.Invoke(e); } catch (Exception) { } }
    }

    /// <summary>How long before the end of a song the DJ is asked about the next one (writing the line and making its clip take a few seconds);
    /// null: as the song starts, as it was before.</summary>
    public TimeSpan? DjAskLead { get; set; } = DefaultDjAskLead;
    public static readonly TimeSpan DefaultDjAskLead = TimeSpan.FromSeconds(30);
    bool DjNearEnd() => DjAskLead is not { } lead || Duration <= 0 || Duration - Position <= lead.TotalSeconds;

    /// <summary>The answer kept about [t], the song now starting (null: none, or still on its way, which is then dropped so it is never played late).</summary>
    DjCue? DjTake(Track t)
    {
        if (djFor != t.Path) return null;
        var cue = djCue;
        if (!djAnswered && dj is { } d && session is { } s) Note(d, s, t, "timing: the DJ's answer had not come when the song started");
        djAsking?.Cancel(); djFor = null; djCue = null; djAnswered = false;
        return cue;
    }

    /// <summary>How long a song reached by a skip waits for a line asked for only then: later than this, the listener is into the song and the DJ keeps quiet.</summary>
    public static readonly TimeSpan DjSkipWait = TimeSpan.FromSeconds(3);
    int djGen;                  // goes up with every song start, pause and seek: a line asked for before it is never said after it

    /// <summary>A song started: the DJ speaks before it if [cue] (taken by <see cref="DjTake"/>) is about this very song and the player got here by itself, or by
    /// Next or a tap in Up next while "Talk after skips" is on. A skip with no answer kept asks now, and the line is said if it comes within <see cref="DjSkipWait"/>.
    /// Anything else stops the DJ. Then the next song is asked about.</summary>
    async Task DjStartedAsync(Track t, bool auto, bool skipped, Track? before, DjCue? cue)
    {
        if (dj is not { } d) return;
        await DjStopAsync();
        var gen = djGen;
        var talk = DjOn && (auto || (skipped && session is { } s0 && d.Settings(s0).TalkAfterSkips));
        if (talk && cue is not null) await DjSayCueAsync(cue);
        else if (cue is not null && session is { } sq)
            Note(d, sq, t, !DjOn ? "the DJ was turned off here" : skipped ? "the song was reached by a skip (Talk after skips is off)" : "the song was not reached by itself");
        else if (talk && skipped && session is { } s) _ = DjAskNowAsync(d, s, t, before, gen);
        DjAhead();
    }

    async Task DjSayCueAsync(DjCue cue)
    {
        try
        {
            bool captions = false, cueSound = false;
            if (dj is { } d && session is { } s && (cue.ClipUrl is { Length: > 0 } || cue.Backup))
            {
                var st = d.Settings(s);
                captions = st.Captions; cueSound = st.Cue;
                await audio.DjLevelsAsync(DjRules.Clamp(st.Volume), DjRules.Clamp(st.Duck));
                await audio.DjCueAsync(st.Cue);
            }
            var words = cue.Caption.Length > 0 ? cue.Caption : cue.Text;
            if (cue.ClipUrl is { Length: > 0 } url)
            {
                // timed to the song's intro when the server knows it: the clip's own length when the page has it, else a guess from its words
                var plan = DjTimingPlan.Now;
                int? clipMs = null;
                try { clipMs = await audio.DjClipMsAsync(url); } catch (Exception) { }
                var length = clipMs is > 0 ? clipMs.Value : DjTiming.EstimateMs(cue.Text);
                if (dj is { } d2 && session is { } s2 && d2.Settings(s2).Before) plan = DjTiming.Before(length);
                else if (cue.IntroMs is { } intro) plan = DjTiming.Plan(intro, length);
                await audio.DjPlayAsync(url, cue.Text, cue.Backup, plan.ClipAtMs, plan.SongDelayMs); djActive = true;
                if (captions) _ = DjCaptionsAsync(words, plan.ClipAtMs + (cueSound ? DjCaptions.CueLeadMs : 0), length);
            }
            else if (cue.Backup && cue.Text.Length > 0)
            {
                await audio.DjSayAsync(cue.Text); djActive = true;
                if (captions) _ = DjCaptionsAsync(words, 0, DjTiming.EstimateMs(cue.Text));
            }
        }
        catch (Exception) { }
    }

    /// <summary>A song reached by a skip that nobody asked about: the DJ is asked now, and its line is said only while this is still the song that started
    /// (a quicker skip, a pause or a seek since then drops it, so lines never pile up) and only within <see cref="DjSkipWait"/>.</summary>
    async Task DjAskNowAsync(IDjService d, UserSession s, Track t, Track? before, int gen)
    {
        try
        {
            var (hour, weekday) = DjSwitches.LocalTime(DateTime.UtcNow, await BrowserOffsetAsync());
            var req = new DjRequest(DjSurface, SongOf(t), before is null ? null : SongOf(before), PrevPlayedOut: false, SkippedRecently: true,
                PlaylistId: ContextId, QueueDj: QueueDj, LocalHour: hour, Weekday: weekday, Bond: BondOf(s, t));
            using var cts = new CancellationTokenSource(DjSkipWait);
            var cue = await d.PrepareAsync(s, req, cts.Token).WaitAsync(cts.Token);
            if (cue is null) return;
            if (gen != djGen || Current?.Path != t.Path || !DjOn) { Note(d, s, t, "timing: the song moved on before the line came"); return; }
            await DjSayCueAsync(cue);
        }
        catch (OperationCanceledException) { Note(d, s, t, $"timing: no line within {DjSkipWait.TotalSeconds:0} s of the skip"); }
        catch (TimeoutException) { Note(d, s, t, $"timing: no line within {DjSkipWait.TotalSeconds:0} s of the skip"); }
        catch (Exception e) { try { RetroHost.DjFailed?.Invoke(e); } catch (Exception) { } }
    }

    static void Note(IDjService d, UserSession s, Track t, string why) { try { d.NotSaid(s, t.Title, why); } catch (Exception) { } }

    /// <summary>The DJ's words showing now (the "Captions" switch), or null: Now Playing shows them over the lyrics or cover, the player bar as a line.</summary>
    public string? DjCaption { get; private set; }
    /// <summary>How the captions wait for their next phrase (a test makes it step by hand).</summary>
    public Func<int, CancellationToken, Task> DjWait { get; set; } = (ms, ct) => Task.Delay(ms, ct);
    CancellationTokenSource? djCaptionCts;

    /// <summary>Shows [text] in time with the clip: from [startInMs] (the clip's own start), phrase by phrase (<see cref="DjCaptions.Plan"/>), gone a moment
    /// after the clip's [clipMs]. A skip, a pause or a seek (<see cref="DjStopAsync"/>) takes it away at once.</summary>
    async Task DjCaptionsAsync(string text, int startInMs, int clipMs)
    {
        djCaptionCts?.Cancel();
        var cts = djCaptionCts = new CancellationTokenSource();
        var plan = DjCaptions.Plan(text, clipMs);
        if (plan.Count == 0) return;
        try
        {
            if (startInMs > 0) await DjWait(startInMs, cts.Token);
            var at = 0;
            foreach (var p in plan)
            {
                if (p.AtMs > at) await DjWait(p.AtMs - at, cts.Token);
                if (cts.IsCancellationRequested) return;
                at = p.AtMs; DjCaption = p.Text; Changed?.Invoke();
            }
            await DjWait(plan[^1].UntilMs - at, cts.Token);
        }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested || djCaptionCts != cts) return;
        DjCaption = null; Changed?.Invoke();
    }

    void DjCaptionOff()
    {
        djCaptionCts?.Cancel(); djCaptionCts = null;
        if (DjCaption is null) return;
        DjCaption = null; Changed?.Invoke();
    }

    async Task DjStopAsync()
    {
        djGen++;
        DjCaptionOff();
        if (!djActive) return;
        djActive = false;
        try { await audio.DjStopAsync(); } catch (Exception) { }
    }

    // ---- "talk to the DJ" (the mic button): the recognised words go to the server's request reader (RetroHost.DjAsk), the answer is carried out here
    // (DjAskRunner) and said in the DJ's voice at the DJ volume. Only words come here, never sound, and they are not kept. ----

    /// <summary>The mic button can be offered: the host reads requests and someone is signed in.</summary>
    public bool CanAskDj => RetroHost.DjAsk is not null && session is not null;
    /// <summary>A line that comes later (a song asked for has landed and is playing): the page shows it as a toast.</summary>
    public event Action<string>? DjAskSaid;

    public async Task<DjAskAnswer?> AskDjAsync(string text, CancellationToken ct = default)
    {
        if (RetroHost.DjAsk is not { } ask || session is not { } s || string.IsNullOrWhiteSpace(text)) return null;
        var a = await ask(s, text, Current?.Path, recent.ToList(), ct);
        await DjAskRunner.RunAsync(new AskPlayer(this, s), a);
        await DjAnswerAsync(s, a);
        if (a.Wait is { } w) _ = WaitForDownloadAsync(s, w);
        return a;
    }

    async Task DjAnswerAsync(UserSession s, DjAskAnswer a)
    {
        if (a.Say.Length == 0) return;
        try
        {
            if (dj is { } d) { var st = d.Settings(s); await audio.DjLevelsAsync(DjRules.Clamp(st.Volume), DjRules.Clamp(st.Duck)); }
            if (a.Clip is { Length: > 0 } url) { await audio.DjPlayAsync(url, a.Say, false, 0, 0); djActive = true; return; }
            if (a.DeviceVoice) { await audio.DjSayAsync(a.Say); djActive = true; return; }
        }
        catch (Exception) { }
    }

    /// <summary>A song fetched for a request: played as soon as it lands (looked for every 5 s, for 10 minutes at most).</summary>
    async Task WaitForDownloadAsync(UserSession s, DjAskWait w)
    {
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(5000);
            if (RetroHost.DjLanded?.Invoke(s, w.Artist, w.Title) is { } path && s.FindByPath(path) is { } t)
            {
                await Play([t], 0, t.Title);
                DjAskSaid?.Invoke($"Here it is: {t.Title}, by {t.Artist}.");
                return;
            }
        }
    }

    /// <summary>Puts [tracks] after the song playing; with [replace], instead of what was coming up.</summary>
    public void SetUpNext(IReadOnlyList<Track> tracks, bool replace)
    {
        if (tracks.Count == 0) return;
        if (Current is null) { _ = Play(tracks, 0); return; }
        if (!replace) { for (var i = tracks.Count - 1; i >= 0; i--) AddNext(tracks[i]); return; }
        if (order.Count != Queue.Count) Reorder();
        var pos = Math.Max(0, order.IndexOf(Index));
        var kept = order.Take(pos + 1).Select(i => Queue[i]).ToList();
        Queue = [.. kept, .. tracks]; order = Enumerable.Range(0, Queue.Count).ToList(); Index = kept.Count - 1; AutoplayFrom = -1;
        RefreshSuggestions(); Stash(true); DjAhead(); Changed?.Invoke();
    }

    sealed class AskPlayer(PlayerState p, UserSession s) : IDjAskPlayer
    {
        List<Track> Tracks(IReadOnlyList<string> paths) => paths.Select(s.FindByPath).OfType<Track>().ToList();
        public Task PlayAsync(IReadOnlyList<string> paths, string title, bool shuffle) =>
            shuffle ? p.ShuffleAll(Tracks(paths), title) : p.Play(Tracks(paths), 0, title);
        public Task UpNextAsync(IReadOnlyList<string> paths, bool replace) { p.SetUpNext(Tracks(paths), replace); return Task.CompletedTask; }
        public Task NextAsync() => p.Next();
        public Task PauseAsync() => p.PauseAsync();
        public Task ResumeAsync() => p.ResumeAsync();
        public Task NudgeVolumeAsync(double delta) => p.SetVolumeAsync(p.Volume + delta);
        public async Task SetDjAsync(bool on) { if (p.dj is null) return; p.QueueDj = on; await p.DjRefreshAsync(); p.Changed?.Invoke(); }
    }

    /// <summary>Stops the music and closes the player: the queue is emptied and the bar goes away. It is what the X on the bar does.</summary>
    public async Task StopAsync()
    {
        planning?.Cancel();
        FlushPlay();
        await DjStopAsync(); djAsking?.Cancel(); djFor = null; djCue = null; QueueDj = null; ContextId = null;
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
