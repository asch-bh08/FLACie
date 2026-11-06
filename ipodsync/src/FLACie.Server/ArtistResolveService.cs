using System.Text.Json;
using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>Reads and writes a song's Artists and AlbumArtist where they live (Jellyfin's own metadata; a fake in the tests).</summary>
public interface IArtistWriter
{
    Task<(IReadOnlyList<string> Artists, string AlbumArtist)?> ReadAsync(string itemId, CancellationToken ct);
    Task<bool> WriteAsync(string itemId, IReadOnlyList<string> artists, string albumArtist, CancellationToken ct);
}

/// <summary>Jellyfin's item update: the item as Jellyfin has it, with Artists, ArtistItems, AlbumArtist and AlbumArtists replaced, posted back to
/// /Items/{id} (an administrator's account). The music files are not touched.</summary>
public sealed class JellyfinArtistWriter(JellyfinClient jf, JellyfinAccount a) : IArtistWriter
{
    public async Task<(IReadOnlyList<string> Artists, string AlbumArtist)?> ReadAsync(string itemId, CancellationToken ct)
    {
        if (await jf.GetAsync(a, $"/Users/{a.UserId}/Items/{itemId}", ct) is not JsonObject o) return null;
        var artists = (o["Artists"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList() ?? [];
        return (artists, o["AlbumArtist"]?.GetValue<string>() ?? "");
    }

    public async Task<bool> WriteAsync(string itemId, IReadOnlyList<string> artists, string albumArtist, CancellationToken ct)
    {
        if (await jf.GetAsync(a, $"/Users/{a.UserId}/Items/{itemId}", ct) is not JsonObject o) return false;
        o["Artists"] = new JsonArray(artists.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        o["ArtistItems"] = new JsonArray(artists.Select(x => (JsonNode?)new JsonObject { ["Name"] = x }).ToArray());
        o["AlbumArtist"] = albumArtist;
        o["AlbumArtists"] = new JsonArray(new JsonObject { ["Name"] = albumArtist });
        try { await jf.PostAsync(a, $"/Items/{itemId}", o, ct); return true; }
        catch (Exception e) when (e is HttpRequestException or JellyfinUnavailableException) { return false; }
    }
}

/// <summary>
/// F24: the dry-run list, the approval and the backup. Planning only reads (the library, and the catalog for "is this The X's song?", cached);
/// nothing is written to Jellyfin until the owner approves the list in Server admin. Each written change keeps the values Jellyfin had before
/// (read from Jellyfin at the moment of writing), so it can be undone, one song or all. After approval, songs that arrive later are checked the same
/// way after a library sync and fixed with their own backup.
/// </summary>
public sealed class ArtistResolveService(string? file, Func<DateTime> now)
{
    public sealed record Applied(ArtistChange Change, IReadOnlyList<string> BackupArtists, string BackupAlbumArtist, DateTime AtUtc, bool Undone = false);
    sealed class State
    {
        public List<ArtistChange> Pending { get; set; } = [];
        public DateTime? PlannedAt { get; set; }
        public bool Approved { get; set; }
        public List<Applied> Done { get; set; } = [];
        public Dictionary<string, bool> Catalog { get; set; } = [];
        public string? Approver { get; set; }
        /// <summary>Kinds of change the owner said to leave ("Eagles" to "The Eagles"): never proposed again, for songs that come later too.</summary>
        public List<string> Left { get; set; } = [];
        /// <summary>How the catalog writes a name ("Outkast" to "OutKast"; "" when it has no artist of that name), asked once per name.</summary>
        public Dictionary<string, string> Spellings { get; set; } = [];
    }
    readonly State state = Load(file);
    readonly SemaphoreSlim one = new(1, 1);

    public IReadOnlyList<ArtistChange> Pending { get { lock (state) return state.Pending.ToList(); } }
    public IReadOnlyList<Applied> Done { get { lock (state) return state.Done.ToList(); } }
    public bool Approved { get { lock (state) return state.Approved; } }
    public DateTime? PlannedAt { get { lock (state) return state.PlannedAt; } }
    /// <summary>The account that approved the list (its Jellyfin login writes the later fixes).</summary>
    public string? Approver { get { lock (state) return state.Approver; } }

    /// <summary>Works out the dry-run list (asking the catalog what the library cannot say, once per song); writes nothing to Jellyfin.</summary>
    /// <summary>[spell]: how the catalog writes a name ("" when it has none, null when it could not be asked).</summary>
    public async Task<IReadOnlyList<ArtistChange>> PlanAsync(IReadOnlyList<Track> songs, Func<string, string, CancellationToken, Task<bool?>> catalog, CancellationToken ct,
        Func<string, CancellationToken, Task<string?>>? spell = null)
    {
        await one.WaitAsync(ct);
        try
        {
            foreach (var (title, artist) in ArtistResolve.CatalogQuestions(songs).Take(500))
            {
                var key = artist + "\n" + title;
                lock (state) if (state.Catalog.ContainsKey(key)) continue;
                if (await catalog(title, artist, ct) is { } yes) lock (state) state.Catalog[key] = yes;
            }
            // capitals the library cannot settle by itself ("Outkast" or "OutKast"): the catalog's way, asked once per name
            if (spell is not null)
                foreach (var name in ArtistResolve.SpellingQuestions(songs).Take(600))
                {
                    lock (state) if (state.Spellings.ContainsKey(name)) continue;
                    if (await spell(name, ct) is { } w) lock (state) state.Spellings[name] = w;
                }
            List<ArtistChange> plan;
            lock (state)
            {
                // a song fixed before is not proposed again: one put back by hand (Undo) stays as it was, it is not fixed again by itself (owner, 6 Nov)
                var done = state.Done.Select(d => d.Change.ItemId).ToHashSet();
                var left = state.Left.ToHashSet();
                plan = ArtistResolve.Plan(songs, (t, a) => state.Catalog.TryGetValue(a + "\n" + t, out var y) ? y : null,
                        n => state.Spellings.TryGetValue(n, out var w) && w.Length > 0 ? w : null)
                    .Where(c => !done.Contains(c.ItemId) && !left.Contains(Kind(c))).ToList();
                state.Pending = plan; state.PlannedAt = now();
            }
            Save();
            return plan;
        }
        finally { one.Release(); }
    }

    /// <summary>What kind of change it is: the tag as it was and the artists it becomes ("eagles>the eagles").</summary>
    public static string Kind(ArtistChange c) =>
        string.Join("|", c.OldArtists.Select(a => string.Join(' ', Matching.SearchWords(a)))) + ">" + string.Join("|", c.NewArtists.Select(a => string.Join(' ', Matching.SearchWords(a))));

    /// <summary>"Leave as it is": this kind of change is taken off the list and never proposed again, for songs that come later too.</summary>
    public void Leave(ArtistChange c)
    {
        var kind = Kind(c);
        lock (state)
        {
            if (!state.Left.Contains(kind)) state.Left.Add(kind);
            state.Pending = state.Pending.Where(p => Kind(p) != kind).ToList();
        }
        Save();
    }

    /// <summary>The owner approved the list: each change is written to Jellyfin, the values Jellyfin had kept first. Returns how many were written.</summary>
    public async Task<int> ApproveAsync(IArtistWriter writer, CancellationToken ct, string? approver = null)
    {
        List<ArtistChange> todo; lock (state) { todo = state.Pending.ToList(); state.Approved = true; state.Approver = approver ?? state.Approver; }
        var n = await WriteAsync(writer, todo, ct);
        lock (state) state.Pending = [];
        Save();
        return n;
    }

    /// <summary>After a library sync, once approved: new songs that need a fix are fixed the same way (each with its backup).</summary>
    public async Task<int> AfterSyncAsync(IReadOnlyList<Track> songs, IArtistWriter writer, Func<string, string, CancellationToken, Task<bool?>> catalog, CancellationToken ct,
        Func<string, CancellationToken, Task<string?>>? spell = null)
    {
        if (!Approved) return 0;
        var plan = await PlanAsync(songs, catalog, ct, spell);
        var n = await WriteAsync(writer, plan, ct);
        lock (state) state.Pending = [];
        Save();
        return n;
    }

    async Task<int> WriteAsync(IArtistWriter writer, IReadOnlyList<ArtistChange> todo, CancellationToken ct)
    {
        var n = 0;
        foreach (var c in todo)
        {
            var before = await writer.ReadAsync(c.ItemId, ct);
            if (before is not { } b) continue;
            if (!await writer.WriteAsync(c.ItemId, c.NewArtists, c.NewAlbumArtist, ct)) continue;
            lock (state) state.Done.Add(new(c, b.Artists, b.AlbumArtist, now()));
            n++;
        }
        return n;
    }

    /// <summary>Puts the values back that Jellyfin had before ([itemId], or every change when null). Returns how many were undone.</summary>
    public async Task<int> UndoAsync(IArtistWriter writer, string? itemId, CancellationToken ct)
    {
        List<Applied> todo; lock (state) todo = state.Done.Where(d => !d.Undone && (itemId is null || d.Change.ItemId == itemId)).ToList();
        var n = 0;
        foreach (var d in todo)
        {
            if (!await writer.WriteAsync(d.Change.ItemId, d.BackupArtists, d.BackupAlbumArtist, ct)) continue;
            lock (state) { var i = state.Done.IndexOf(d); if (i >= 0) state.Done[i] = d with { Undone = true }; }
            n++;
        }
        // undoing everything also ends the approval: new songs are no longer fixed by themselves
        if (itemId is null) lock (state) state.Approved = false;
        Save();
        return n;
    }

    void Save()
    {
        if (file is null) return;
        try { string json; lock (state) json = JsonSerializer.Serialize(state); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file + ".tmp", json); File.Move(file + ".tmp", file, true); }
        catch (IOException) { }
    }

    static State Load(string? file)
    {
        try { if (file is not null && File.Exists(file) && JsonSerializer.Deserialize<State>(File.ReadAllText(file)) is { } s) return s; }
        catch (Exception e) when (e is IOException or JsonException) { }
        return new();
    }
}

/// <summary>F24: once the owner approved the list, new songs are checked every 15 minutes (after the library syncs) with the approver's Jellyfin login.</summary>
public sealed class ArtistResolveSyncService(SessionStore sessions, ArtistResolveService fixes, JellyfinClient jf, WebCatalog catalog, ILogger<ArtistResolveSyncService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(20), stop);
            while (!stop.IsCancellationRequested)
            {
                if (fixes.Approved && sessions.Active.FirstOrDefault(s => s.Id == fixes.Approver && !s.Loading) is { Jellyfin: { } a } s)
                {
                    try
                    {
                        var n = await fixes.AfterSyncAsync(s.Library.Songs, new JellyfinArtistWriter(jf, a), (t, ar, ct) => ArtistResolveCatalog.AskAsync(catalog, t, ar, ct), stop,
                            (n, ct) => ArtistResolveCatalog.SpellAsync(catalog, n, ct));
                        if (n > 0) log.LogInformation("Artist names: fixed {N} new songs in Jellyfin", n);
                    }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Artist name check failed"); }
                }
                // new downloads and imports are fixed soon after they land (owner, 6 Nov: the same rules for every new song, by themselves)
                await Task.Delay(TimeSpan.FromMinutes(15), stop);
            }
        }
        catch (OperationCanceledException) { }
    }
}

/// <summary>"Is [title] by [artist]?" from the catalog (Deezer): true when it lists the song under that exact artist, null when it could not be asked.</summary>
public static class ArtistResolveCatalog
{
    public static async Task<bool?> AskAsync(WebCatalog catalog, string title, string artist, CancellationToken ct) =>
        await catalog.SongRankAsync(artist, title, ct) is { } rank ? rank > 0 : null;

    /// <summary>How the catalog writes [name] ("" when it has no artist of that name), or null when it could not be asked.</summary>
    public static async Task<string?> SpellAsync(WebCatalog catalog, string name, CancellationToken ct) =>
        await catalog.ArtistSpellingAsync(name, ct) is var (n, unasked) && !unasked ? n ?? "" : null;
}
