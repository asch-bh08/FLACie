using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>A playlist from the shared profile; entries keep path + title + artist so another device can find its own copy.</summary>
public sealed record PlaylistEntry(string Path, string Title, string Artist);
public sealed record Playlist(string Id, string Name, long Modified, string? JellyfinId, List<PlaylistEntry> Entries);

/// <summary>
/// One signed-in user: the account they signed in with (Jellyfin or NAS), the other account if the profile has it, the
/// profile itself (services, playlists, favourites; the Android app's format, so every device shares it) and the merged
/// library. The profile is read from every account the user has (newest copy wins) and written back to all of them.
/// </summary>
public sealed class UserSession
{
    public string Kind { get; }
    /// <summary>A stable key for this account on this server (the server keeps its own per-account files under it).</summary>
    public string Id { get; init; } = "";
    public JellyfinAccount? Jellyfin { get; private set; }
    public NasAccount? Nas { get; private set; }
    public JsonObject Profile { get; private set; } = NewProfile();
    public Library Library { get; private set; } = Library.Empty;
    public string? Problem { get; private set; }
    public bool Loading { get; private set; }
    /// <summary>What Jellyfin counts this user has played (all apps), for the Quick picks. Empty for a NAS-only sign-in or until it has been read.</summary>
    public IReadOnlyList<JfPlay> PlayStats { get; private set; } = [];
    public DateTime PlayStatsAt { get; private set; }
    /// <summary>Reads the play counts again unless they are less than [maxAge] old. Never throws: the Quick picks just go without.</summary>
    public async Task RefreshPlayStatsAsync(JellyfinClient jf, TimeSpan maxAge, CancellationToken ct = default)
    {
        if (Jellyfin is not { } a || DateTime.UtcNow - PlayStatsAt < maxAge) return;
        PlayStatsAt = DateTime.UtcNow;   // set first, so a slow or failing read is not started again by every page
        using var prio = JellyfinGate.Use(GatePriority.Background);   // the Quick picks can wait for a quiet moment
        try { PlayStats = await jf.PlayStatsAsync(a, ct); Changed?.Invoke(); } catch (Exception) { }
    }
    public DateTime LoadedAt { get; private set; }
    public event Action? Changed;
    /// <summary>Jellyfin songs another device sent that the loaded library doesn't list; they can still be streamed.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, Track> Extra { get; } = new();
    public Track? FindByPath(string path) => Library.ByPath(path) ?? Extra.GetValueOrDefault(path);
    readonly SemaphoreSlim gate = new(1, 1);

    public UserSession(string kind, JellyfinAccount? jellyfin, NasAccount? nas) { Kind = kind; Jellyfin = jellyfin; Nas = nas; }

    public string DisplayName => Kind == "nas" && Nas is { } n ? $"{(n.User.Length == 0 ? "guest" : n.User)} on {n.Host}" : Jellyfin?.UserName ?? "";

    static JsonObject NewProfile() => new() { ["v"] = 2, ["updated"] = 0L, ["services"] = new JsonObject(), ["favorites"] = new JsonArray(), ["playlists"] = new JsonArray(), ["deleted"] = new JsonArray(), ["hidden"] = new JsonArray() };

    // ---- load ----
    /// <summary>Where this account's last library is kept (set by the server), so a restart shows it at once while the fresh one loads.</summary>
    public string? CachePath { get; set; }

    void LoadCache()
    {
        if (CachePath is null || Library.Songs.Count > 0 || !File.Exists(CachePath)) return;
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(CachePath));
            if (list is { Count: > 0 }) { Library = WithDownloads(new Library(list)); Changed?.Invoke(); }
        }
        catch (Exception) { }
    }

    void SaveCache()
    {
        if (CachePath is null || Library.Songs.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var tmp = CachePath + ".tmp";
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(Library.Songs.Where(t => t.Source != TrackSource.Cloud).ToList()));
            File.Move(tmp, CachePath, true);
            // the cursor and the set of Jellyfin ids it covers travel with the library: after a restart only what changed since is asked for
            if (SyncCursor is { } cur && CursorFile is { } cfile) File.WriteAllText(cfile, cur);
            if (knownIds is { } ids && IdsFile is { } ifile) { File.WriteAllLines(ifile + ".tmp", ids); File.Move(ifile + ".tmp", ifile, true); }
        }
        catch (Exception) { }
    }

    /// <summary>The Jellyfin id of every song seen in the listings and deltas so far (before the library merged duplicates away), normalised. Lets a restart tell what Jellyfin
    /// removed without listing every song again. Null when not known (an older cache): the next sync lists everything once.</summary>
    HashSet<string>? knownIds;
    string? IdsFile => CachePath is null ? null : CachePath + ".ids";
    public int KnownSongCount => knownIds?.Count ?? 0;

    /// <summary>Drops the saved library, cursor and id list (the Clear cache button): the next start lists everything again.</summary>
    public void ForgetCache()
    {
        foreach (var f in new[] { CachePath, CursorFile, IdsFile }) try { if (f is not null && File.Exists(f)) File.Delete(f); } catch (Exception) { }
    }


    public async Task LoadAsync(JellyfinClient jf, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        Loading = true; LoadCache(); Changed?.Invoke();
        // with a saved library to show, the refresh is background work; with nothing to show yet, the person is waiting for it
        using var prio = JellyfinGate.Use(Library.Songs.Count > 0 ? GatePriority.Background : GatePriority.Visible);
        try
        {
            // newest profile copy from every account this user has
            var copies = new List<JsonObject>(); var profileOk = true; string? profileProblem = null;
            if (Jellyfin is { } a) try { if (await jf.PullProfileAsync(a, ct) is { } p) copies.Add(p); } catch (UnauthorizedAccessException) when (Kind == "nas") { Jellyfin = null; } catch (Exception e) when (e is not UnauthorizedAccessException) { profileOk = false; profileProblem = "Couldn't load your profile: " + e.Message; }
            if (Nas is { } n) try { if (NasClient.ReadText(n, n.ProfilePath) is { } s && JsonNode.Parse(s) is JsonObject p) copies.Add(p); } catch (Exception) when (Kind == "jellyfin") { }
            if (copies.Count > 0) Profile = copies.MaxBy(p => p["updated"]?.GetValue<long>() ?? 0)!;
            // until the profile has really been read, nothing is saved back (an empty one would wipe the real one)
            if (profileOk) { ProfileLoaded = true; profileSeen = Profile["updated"]?.GetValue<long>() ?? 0; ProfileRev++; Changed?.Invoke(); }
            if (Jellyfin is { } adm) try { IsAdmin = (await jf.GetAsync(adm, "/Users/Me", ct))?["Policy"]?["IsAdministrator"]?.GetValue<bool>() == true; } catch (Exception) { }

            // the other account comes back from the profile: a NAS sign-in gets the Jellyfin account and vice versa
            if (Jellyfin is null && Profile["account"] is JsonObject acc && acc["token"]?.GetValue<string>() is { Length: > 0 } tok)
                Jellyfin = new JellyfinAccount(acc["server"]!.GetValue<string>(), acc["userId"]!.GetValue<string>(), acc["user"]?.GetValue<string>() ?? "", tok);
            if (Nas is null && Profile["services"]?["nas"] is JsonObject ns && ns["host"]?.GetValue<string>() is { Length: > 0 } host)
                Nas = new NasAccount(host, S(ns, "share"), S(ns, "folder"), S(ns, "user"), S(ns, "pass"), S(ns, "domain"));

            List<Track> jfTracks = [], nasTracks = [];
            Problem = profileProblem;
            if (Jellyfin is { } ja)
                try
                {
                    // a first sync (nothing saved from last time) shows songs as the pages arrive; a refresh waits and swaps in the whole list
                    var firstSync = Library.Songs.Count == 0; var published = 0;
                    // listing the library is background work (it is held while a song is being served or Jellyfin is slow); the songs show as pages arrive
                    using var listingPrio = JellyfinGate.Use(GatePriority.Background);
                    // a library kept from last time with a cursor: ask only for what changed since, and trust it when the ids still add up to Jellyfin's count
                    var merged = firstSync ? null : await TryDeltaAsync(jf, ja, ct);
                    if (merged is not null) jfTracks = merged;
                    else
                    {
                        var listing = await jf.ListAudioAsync(ja, null, ct, firstSync ? partial =>
                        {
                            if (partial.Count <= published) return;
                            published = partial.Count; Library = WithDownloads(Library.Merge(partial, [])); Changed?.Invoke();
                        } : null, FactsSink);
                        jfTracks = listing.Tracks; SyncCursor = listing.MaxSaved;
                        knownIds = listing.Tracks.Select(t => Norm(t.JellyfinId)).Where(s => s.Length > 0).ToHashSet();
                    }
                }
                catch (UnauthorizedAccessException) { if (Kind == "nas") Jellyfin = null; else throw; }
                catch (Exception e) { Problem = $"Jellyfin: {e.Message}"; }
            if (Nas is { } na)
                try { nasTracks = await Task.Run(() => NasClient.Scan(na), ct); }
                catch (Exception e) { Problem = (Problem is null ? "" : Problem + " · ") + $"NAS: {e.Message}"; }
            // a failed fetch must not replace the library kept from last time with nothing
            if (!(jfTracks.Count == 0 && nasTracks.Count == 0 && Problem is not null && Library.Songs.Count > 0)) Library = WithDownloads(Library.Merge(jfTracks, nasTracks));
            LoadedAt = DateTime.UtcNow; SaveCache();
            // (the play counts for the Quick picks are read by the Home page when it is opened, not here: two big requests nobody may ever need)
            if (StripQualityTags()) try { await SaveAsync(jf, ct); } catch (Exception) { }
        }
        finally { Loading = false; gate.Release(); Changed?.Invoke(); }
    }

    /// <summary>Where each song's real audio facts (from the library listing) are handed to, normally the server's format index.</summary>
    public Action<Track, MediaInfo>? FactsSink { get; set; }
    /// <summary>The newest DateLastSaved seen in the Jellyfin listing: the next sync asks only for songs saved after it.</summary>
    public string? SyncCursor { get; private set; }

    string? CursorFile => CachePath is null ? null : CachePath + ".cursor";

    async Task<List<Track>?> TryDeltaAsync(JellyfinClient jf, JellyfinAccount a, CancellationToken ct)
    {
        try
        {
            SyncCursor ??= CursorFile is { } cf && File.Exists(cf) ? File.ReadAllText(cf).Trim() : null;
            knownIds ??= IdsFile is { } idf && File.Exists(idf) ? File.ReadAllLines(idf).Where(s => s.Length > 0).ToHashSet() : null;
            if (SyncCursor is not { Length: > 0 } cursor || knownIds is null) return null;   // nothing to build on (a first run, or an older cache): list everything once
            var known = knownIds;
            var l = await jf.ListAudioAsync(a, cursor, ct, null, FactsSink, id => known.Contains(id));
            var changed = new HashSet<string>(l.Tracks.Select(t => Norm(t.JellyfinId)));
            var merged = Library.Songs.Where(t => t.Source == TrackSource.Jellyfin && !changed.Contains(Norm(t.JellyfinId))).Concat(l.Tracks).ToList();
            var ids = new HashSet<string>(knownIds); ids.UnionWith(changed);
            // one tiny request: does Jellyfin's count still match the songs we know? If not, something was removed (or never seen): find out which (ids only)
            var total = await JellyfinSongCountAsync(jf, ct);
            if (total >= 0 && total != ids.Count)
            {
                var now = (await jf.ListAudioIdsAsync(a, ct)).Select(Norm).ToHashSet();
                if (now.Any(id => !ids.Contains(id))) return null;   // songs this library never saw: list everything again
                ids.IntersectWith(now);
                merged = merged.Where(t => now.Contains(Norm(t.JellyfinId))).ToList();
            }
            knownIds = ids;
            if (l.MaxSaved is not null) SyncCursor = l.MaxSaved;
            return merged;
        }
        catch (UnauthorizedAccessException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        // Jellyfin refused the question itself (a cursor it cannot read): start over with a full listing
        catch (HttpRequestException h) when (h.StatusCode == System.Net.HttpStatusCode.BadRequest) { return null; }
        // Jellyfin is slow, failing or its breaker is open: keep what we have (it is background work), the next change message or the
        // 15-minute check asks again. Listing 10,000 songs again now, against a server that is struggling, is the one thing not to do.
        catch (Exception) { return Library.Songs.Where(t => t.Source == TrackSource.Jellyfin).ToList(); }
    }

    static string Norm(string? id) => (id ?? "").Replace("-", "").ToLowerInvariant();

    /// <summary>Folds a delta into the library: songs Jellyfin saved again replace their old record (or are added), removed ones disappear. NAS songs and downloads stay.</summary>
    public void ApplyDelta(IReadOnlyList<Track> changed, IReadOnlyCollection<string> removedIds)
    {
        var gone = removedIds.Select(Norm).ToHashSet();
        var drop = new HashSet<string>(gone.Concat(changed.Select(t => Norm(t.JellyfinId))));
        var jfNow = Library.Songs.Where(t => t.Source == TrackSource.Jellyfin && !drop.Contains(Norm(t.JellyfinId))).Concat(changed).ToList();
        var nas = Library.Songs.Where(t => t.Source == TrackSource.Nas).ToList();
        Library = WithDownloads(Library.Merge(jfNow, nas));
        if (knownIds is { } ids) { ids.ExceptWith(gone); foreach (var t in changed) { var n = Norm(t.JellyfinId); if (n.Length > 0) ids.Add(n); } }
        Changed?.Invoke();
    }

    /// <summary>Asks Jellyfin only for what changed since the last sync (background priority), folds it in and moves the cursor on. Returns how many songs changed or went.
    /// <paramref name="removedIds"/> are songs a LibraryChanged message from Jellyfin's socket said were removed.</summary>
    public async Task<int> SyncDeltaAsync(JellyfinClient jf, IReadOnlyCollection<string>? removedIds = null, CancellationToken ct = default)
    {
        if (Jellyfin is not { } a || SyncCursor is null || Loading) return 0;
        using var prio = JellyfinGate.Use(GatePriority.Background);
        var known = knownIds;
        var listing = await jf.ListAudioAsync(a, SyncCursor, ct, null, FactsSink, known is null ? null : id => known.Contains(id));
        var gone = removedIds ?? [];
        if (listing.Tracks.Count == 0 && gone.Count == 0) return 0;
        ApplyDelta(listing.Tracks, gone);
        if (listing.MaxSaved is not null) SyncCursor = listing.MaxSaved;
        SaveCache();
        return listing.Tracks.Count + gone.Count;
    }

    /// <summary>Checks that the songs this library knows are the ones Jellyfin has: one tiny count request, and only when it differs a listing of ids (no streams, no pictures).
    /// Songs Jellyfin no longer has are dropped (their number is returned). -1 when Jellyfin has songs this library never saw (the caller lists again).</summary>
    public async Task<int> ReconcileAsync(JellyfinClient jf, CancellationToken ct = default)
    {
        if (Jellyfin is not { } a || knownIds is not { } known || Loading) return 0;
        using var prio = JellyfinGate.Use(GatePriority.Background);
        var total = await JellyfinSongCountAsync(jf, ct);
        if (total < 0 || total == known.Count) return 0;
        var now = (await jf.ListAudioIdsAsync(a, ct)).Select(Norm).ToHashSet();
        if (now.Any(id => !known.Contains(id))) return -1;
        var gone = known.Where(id => !now.Contains(id)).ToList();
        if (gone.Count == 0) return 0;
        ApplyDelta([], gone);
        SaveCache();
        return gone.Count;
    }

    /// <summary>How many songs Jellyfin says the user has (one tiny request), to notice removals a socket message missed.</summary>
    public async Task<int> JellyfinSongCountAsync(JellyfinClient jf, CancellationToken ct = default)
    {
        if (Jellyfin is not { } a) return -1;
        using var prio = JellyfinGate.Use(GatePriority.Background);
        return (await jf.GetAsync(a, $"/Users/{a.UserId}/Items?IncludeItemTypes=Audio&Recursive=true&Limit=1&EnableImages=false&EnableUserData=false&Fields=", ct))?["TotalRecordCount"]?.GetValue<int>() ?? -1;
    }

    static string S(JsonObject o, string k) => o[k]?.GetValue<string>() ?? "";

    static readonly System.Text.RegularExpressions.Regex QualityTag = new(@"\s*\((f?lac|alac|mp3|aac|wav|ogg|opus)\)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    /// <summary>A playlist name without a trailing quality tag such as "(LAC)" or "(FLAC)".</summary>
    public static string CleanName(string name) => QualityTag.Replace(name, "").Trim() is { Length: > 0 } c ? c : name;

    /// <summary>Drops the quality tag from every playlist name. True when anything was renamed.</summary>
    bool StripQualityTags()
    {
        var changed = false;
        lock (listLock)
            foreach (var o in (Profile["playlists"] as JsonArray ?? []).OfType<JsonObject>())
                if (o["n"]?.GetValue<string>() is { } n && CleanName(n) != n) { o["n"] = CleanName(n); o["m"] = Now(); changed = true; }
        return changed;
    }


    // ---- downloads ----

    readonly List<Track> downloaded = [];
    public DownloadServices Services => DownloadServices.From(Profile);

    /// <summary>Songs downloaded in this session play at once; a later library reload finds them again through Jellyfin or the NAS.</summary>
    public void AddDownloaded(IEnumerable<Track> tracks)
    {
        lock (downloaded) downloaded.AddRange(tracks);
        Library = WithDownloads(Library);
        Changed?.Invoke();
    }

    Library WithDownloads(Library l)
    {
        List<Track> mine; lock (downloaded) mine = downloaded.ToList();
        if (mine.Count == 0) return l;
        var seen = l.Songs.Select(Matching.MergeKey).ToHashSet();
        var add = mine.Where(t => seen.Add(Matching.MergeKey(t))).ToList();
        return add.Count == 0 ? l : new Library(l.Songs.Concat(add).ToList());
    }
    // ---- save ----

    /// <summary>Writes the profile to every account this user has, with the accounts themselves in it.</summary>
    public bool ProfileLoaded { get; private set; }
    /// <summary>The account is an administrator of its Jellyfin server.</summary>
    public bool IsAdmin { get; private set; }
    /// <summary>Goes up each time the profile is read from the account (pages that show it start again).</summary>
    public int ProfileRev { get; private set; }

    public async Task SaveAsync(JellyfinClient jf, CancellationToken ct = default)
    {
        if (!ProfileLoaded) return;
        // one save at a time touches the shared profile object: an import saves from several tasks at once, and a JsonObject read from Jellyfin is initialised lazily,
        // so two threads setting a key together wrote it twice (an unreadable profile: the next sign-in failed to load the library)
        string compact, indented;
        lock (listLock)
        {
            Profile["updated"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); profileSeen = Math.Max(profileSeen, Profile["updated"]!.GetValue<long>());
            Profile["v"] = 2;
            if (Jellyfin is { } a) Profile["account"] = new JsonObject { ["server"] = a.Server, ["userId"] = a.UserId, ["user"] = a.UserName, ["token"] = a.Token };
            var services = Profile["services"] as JsonObject ?? new JsonObject();
            if (Nas is { } n) services["nas"] = new JsonObject { ["host"] = n.Host, ["share"] = n.Share, ["folder"] = n.Folder, ["user"] = n.User, ["pass"] = n.Password, ["domain"] = n.Domain };
            Profile["services"] = services;
            compact = Profile.ToJsonString();
            indented = Nas is null ? "" : Profile.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        Exception? failure = null;
        if (Nas is { } na) try { await Task.Run(() => NasClient.WriteText(na, na.ProfilePath, indented), ct); } catch (Exception e) { failure = e; }
        if (Jellyfin is { } ja) try { await jf.PushProfileAsync(ja, compact, ct); } catch (Exception e) { failure = e; }
        if (failure is not null) throw failure;
    }

    long profileSeen;

    /// <summary>
    /// Looks at the account's copy of the profile again and folds in what other devices changed since (a song added to a playlist on the phone, a favourite,
    /// a playlist made or deleted): playlists by id with the newest edit winning, deletions, favourites added. Without this the web only ever read the profile
    /// once at sign-in, never showed the phone's edits, and its next save wrote its old copy back over them. True when something changed.
    /// </summary>
    public async Task<bool> RefreshProfileAsync(JellyfinClient jf, CancellationToken ct = default)
    {
        if (!ProfileLoaded || Jellyfin is not { } a) return false;
        JsonObject? remote;
        try { remote = await jf.PullProfileAsync(a, ct); } catch (Exception) { return false; }
        var seen = remote?["updated"]?.GetValue<long>() ?? 0;
        if (remote is null || seen <= profileSeen) return false;
        var changed = false;
        lock (listLock)
        {
            var mine = Profile["playlists"] as JsonArray ?? new JsonArray();
            var deleted = Profile["deleted"] as JsonArray ?? new JsonArray();
            var gone = deleted.Select(d => d?.GetValue<string>()).ToHashSet();
            foreach (var id in (remote["deleted"] as JsonArray ?? []).Select(d => d?.GetValue<string>()).OfType<string>())
            {
                if (!gone.Add(id)) continue;
                deleted.Add(id); changed = true;
                if (mine.OfType<JsonObject>().FirstOrDefault(o => S(o, "id") == id) is { } dead) mine.Remove(dead);
            }
            foreach (var rp in (remote["playlists"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var id = S(rp, "id"); if (id.Length == 0 || gone.Contains(id)) continue;
                var lp = mine.OfType<JsonObject>().FirstOrDefault(o => S(o, "id") == id);
                if (lp is null) { mine.Add(JsonNode.Parse(rp.ToJsonString())); changed = true; }
                else if ((rp["m"]?.GetValue<long>() ?? 0) > (lp["m"]?.GetValue<long>() ?? 0))
                {
                    lp["n"] = rp["n"]?.DeepClone(); lp["m"] = rp["m"]?.DeepClone(); lp["tracks"] = rp["tracks"]?.DeepClone(); changed = true;
                }
            }
            var favs = Profile["favorites"] as JsonArray ?? new JsonArray();
            foreach (var rf in (remote["favorites"] as JsonArray ?? []).OfType<JsonObject>())
                if (!favs.OfType<JsonObject>().Any(f => S(f, "p") == S(rf, "p") || Matching.MatchKey(S(f, "t"), S(f, "a")) == Matching.MatchKey(S(rf, "t"), S(rf, "a")))) { favs.Add(JsonNode.Parse(rf.ToJsonString())); changed = true; }
            Profile["playlists"] = mine; Profile["deleted"] = deleted; Profile["favorites"] = favs;
            // ratings: per song the newest choice wins
            var ratings = Profile["ratings"] as JsonArray ?? new JsonArray();
            foreach (var rr in (remote["ratings"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var lr = ratings.OfType<JsonObject>().FirstOrDefault(r => S(r, "p") == S(rr, "p") || Matching.MatchKey(S(r, "t"), S(r, "a")) == Matching.MatchKey(S(rr, "t"), S(rr, "a")));
                if (lr is null) { ratings.Add(JsonNode.Parse(rr.ToJsonString())); changed = true; }
                else if ((rr["w"]?.GetValue<long>() ?? 0) > (lr["w"]?.GetValue<long>() ?? 0)) { lr["r"] = rr["r"]?.DeepClone(); lr["w"] = rr["w"]?.DeepClone(); changed = true; }
            }
            Profile["ratings"] = ratings;
        }
        profileSeen = seen;
        if (changed) { ProfileRev++; Changed?.Invoke(); }
        return changed;
    }

    /// <summary>Adds the other kind of account, so the profile is kept in both.</summary>
    public void AddJellyfin(JellyfinAccount a) { Jellyfin = a; Changed?.Invoke(); }
    public void AddNas(NasAccount n) { Nas = n; Changed?.Invoke(); }

    // ---- small settings kept with the account (in the profile under "settings": key -> { json, at }), so they follow the person to every device ----

    /// <summary>A setting kept with the account (its JSON, and when it was set, in Unix ms); null when there is none.</summary>
    public (string Json, long At)? GetSetting(string key)
    {
        lock (listLock)
            return Profile["settings"]?[key] is JsonObject o && o["json"] is JsonValue j && j.TryGetValue<string>(out var json)
                ? (json, o["at"] is JsonValue a && a.TryGetValue<long>(out var at) ? at : 0) : null;
    }

    /// <summary>Keeps a setting with the account (saved with the profile: SaveAsync). One set earlier than the one kept is ignored.</summary>
    public void SetSetting(string key, string json, long at)
    {
        lock (listLock)
        {
            var all = Profile["settings"] as JsonObject ?? new JsonObject();
            if (all[key] is JsonObject old && old["at"] is JsonValue a && a.TryGetValue<long>(out var was) && was > at) return;
            all[key] = new JsonObject { ["json"] = json, ["at"] = at };
            Profile["settings"] = all;
        }
    }

    // ---- favourites and playlists (in the profile) ----

    public IReadOnlyList<PlaylistEntry> Favorites => (Profile["favorites"] as JsonArray ?? []).OfType<JsonObject>().Select(Entry).ToList();

    public bool IsFavorite(Track t) => Favorites.Any(f => f.Path == t.Path || Matching.MatchKey(f.Title, f.Artist) == Matching.MatchKey(t));

    public void ToggleFavorite(Track t)
    {
        var arr = Profile["favorites"] as JsonArray ?? new JsonArray();
        var hit = arr.OfType<JsonObject>().FirstOrDefault(f => S(f, "p") == t.Path || Matching.MatchKey(S(f, "t"), S(f, "a")) == Matching.MatchKey(t));
        if (hit is not null) arr.Remove(hit); else arr.Insert(0, EntryJson(t));
        Profile["favorites"] = arr; Changed?.Invoke();
    }

    // ---- star ratings (in the profile: 1 to 5, kept with the song's path and title so they follow it across sources) ----

    JsonObject? RatingEntry(Track t) => (Profile["ratings"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(r => S(r, "p") == t.Path || Matching.MatchKey(S(r, "t"), S(r, "a")) == Matching.MatchKey(t));

    /// <summary>The song's stars, 0 when it is not rated.</summary>
    public int Rating(Track t) => RatingEntry(t) is { } r ? Ratings.Clamp(r["r"]?.GetValue<int>() ?? 0) : 0;

    /// <summary>Rates a song (0 clears it). The time is kept so the newest choice wins when two devices disagree.</summary>
    public void SetRating(Track t, int stars)
    {
        stars = Ratings.Clamp(stars);
        var arr = Profile["ratings"] as JsonArray ?? new JsonArray();
        var hit = RatingEntry(t);
        if (hit is not null) arr.Remove(hit);
        if (stars > 0) arr.Insert(0, new JsonObject { ["p"] = t.Path, ["t"] = t.Title, ["a"] = t.Artist, ["r"] = stars, ["w"] = Now() });
        else arr.Insert(0, new JsonObject { ["p"] = t.Path, ["t"] = t.Title, ["a"] = t.Artist, ["r"] = 0, ["w"] = Now() });   // a cleared rating is kept (with its time) so the clearing wins over an older rating elsewhere
        while (arr.Count > 5000) arr.RemoveAt(arr.Count - 1);
        Profile["ratings"] = arr; Changed?.Invoke();
    }

    /// <summary>The rated songs, newest rating first (cleared ones left out).</summary>
    public IReadOnlyList<(PlaylistEntry Entry, int Stars)> Rated => (Profile["ratings"] as JsonArray ?? []).OfType<JsonObject>()
        .Where(r => (r["r"]?.GetValue<int>() ?? 0) > 0).Select(r => (Entry(r), r["r"]!.GetValue<int>())).ToList();

    // ---- play history (in the profile, so Listen again follows the account) ----

    public IReadOnlyList<PlayedEntry> History => (Profile["history"] as JsonArray ?? []).OfType<JsonObject>()
        .Select(o => new PlayedEntry(S(o, "p"), S(o, "t"), S(o, "a"), o["w"]?.GetValue<long>() ?? 0)).ToList();

    /// <summary>Notes that a song started. Returns true when the history changed enough to be worth saving (at most about every two minutes).</summary>
    public bool RecordPlay(Track t)
    {
        var arr = Profile["history"] as JsonArray ?? (JsonArray)(Profile["history"] = new JsonArray());
        if (arr.Count > 0 && arr[0] is JsonObject last && S(last, "p") == t.Path && Now() - (last["w"]?.GetValue<long>() ?? 0) < 60_000) return false;
        arr.Insert(0, new JsonObject { ["p"] = t.Path, ["t"] = t.Title, ["a"] = t.Artist, ["w"] = Now() });
        while (arr.Count > 200) arr.RemoveAt(arr.Count - 1);
        Changed?.Invoke();
        if (Now() - lastHistorySave < 120_000) return false;
        lastHistorySave = Now();
        return true;
    }
    long lastHistorySave;

    public IReadOnlyList<Playlist> Playlists
    {
        get
        {
            var hidden = (Profile["hidden"] as JsonArray ?? []).Select(x => x?.GetValue<string>()).ToHashSet();
            return (Profile["playlists"] as JsonArray ?? []).OfType<JsonObject>().Where(o => !hidden.Contains(S(o, "id")))
                .Select(o => new Playlist(S(o, "id"), S(o, "n"), o["m"]?.GetValue<long>() ?? 0, o["jf"] is JsonValue v && v.TryGetValue<string>(out var jfId) && jfId.Length > 0 ? jfId : null,
                    (o["tracks"] as JsonArray ?? []).OfType<JsonObject>().Select(Entry).ToList()))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public Playlist CreatePlaylist(string name)
    {
        var o = new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["n"] = name, ["m"] = Now(), ["jf"] = null, ["tracks"] = new JsonArray() };
        (Profile["playlists"] as JsonArray ?? (JsonArray)(Profile["playlists"] = new JsonArray())).Add(o);
        Changed?.Invoke();
        return Playlists.First(p => p.Id == S(o, "id"));
    }

    readonly object listLock = new();

    /// <summary>Makes a playlist hold exactly these songs, in this order (an automatic list such as a chart is rebuilt, not appended to).</summary>
    public void ReplacePlaylistTracks(string id, IEnumerable<Track> tracks)
    {
        lock (listLock)
        {
            if (FindPlaylist(id) is not { } o) return;
            var arr = new JsonArray();
            foreach (var t in tracks.DistinctBy(Matching.MatchKey)) arr.Add(EntryJson(t));
            o["tracks"] = arr; o["m"] = Now();
        }
        Changed?.Invoke();
    }


    /// <summary>Renames a playlist.</summary>
    public void RenamePlaylist(string id, string name)
    {
        lock (listLock) { if (FindPlaylist(id) is { } o) { o["n"] = name; o["m"] = Now(); } }
        Changed?.Invoke();
    }
    /// <summary>The playlist with this name (any case), made if it isn't there.</summary>
    public Playlist PlaylistNamed(string name)
    {
        lock (listLock) return Playlists.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? CreatePlaylist(name);
    }
    public enum PlaylistAdd { Added, Already, NoPlaylist }

    /// <summary>Adds the song unless the playlist already has it (the same file, or the same title and artist).</summary>
    public PlaylistAdd AddToPlaylist(string id, Track t)
    {
        lock (listLock)
        {
            if (FindPlaylist(id) is not { } o) return PlaylistAdd.NoPlaylist;
            var tr = o["tracks"] as JsonArray ?? new JsonArray();
            if (tr.OfType<JsonObject>().Any(x => S(x, "p") == t.Path || Matching.MatchKey(S(x, "t"), S(x, "a")) == Matching.MatchKey(t))) return PlaylistAdd.Already;
            tr.Add(EntryJson(t)); o["tracks"] = tr; o["m"] = Now();
        }
        Changed?.Invoke();
        return PlaylistAdd.Added;
    }

    /// <summary>Takes a song back out (the Undo after an add).</summary>
    public void RemoveFromPlaylist(string id, Track t)
    {
        if (FindPlaylist(id) is not { } o || o["tracks"] is not JsonArray tr) return;
        var i = tr.OfType<JsonObject>().ToList().FindIndex(x => S(x, "p") == t.Path || Matching.MatchKey(S(x, "t"), S(x, "a")) == Matching.MatchKey(t));
        if (i >= 0) RemoveFromPlaylist(id, i);
    }

    public void RemoveFromPlaylist(string id, int index)
    {
        if (FindPlaylist(id) is not { } o || o["tracks"] is not JsonArray tr || index < 0 || index >= tr.Count) return;
        tr.RemoveAt(index); o["m"] = Now(); Changed?.Invoke();
    }

    /// <summary>Moves the entry at [from] to [to] (the playlist's own order, so songs missing from this library keep their place).</summary>
    public void MoveInPlaylist(string id, int from, int to)
    {
        lock (listLock)
        {
            if (FindPlaylist(id) is not { } o || o["tracks"] is not JsonArray tr || from < 0 || from >= tr.Count || to < 0 || to >= tr.Count || from == to) return;
            var item = tr[from]; tr.RemoveAt(from); tr.Insert(to, item); o["m"] = Now();
        }
        Changed?.Invoke();
    }

    public void DeletePlaylist(string id)
    {
        if (FindPlaylist(id) is not { } o) return;
        (Profile["playlists"] as JsonArray)!.Remove(o);
        (Profile["deleted"] as JsonArray ?? (JsonArray)(Profile["deleted"] = new JsonArray())).Add(id);
        Changed?.Invoke();
    }

    JsonObject? FindPlaylist(string id) => (Profile["playlists"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(o => S(o, "id") == id);
    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static PlaylistEntry Entry(JsonObject o) => new(S(o, "p"), S(o, "t"), S(o, "a"));
    static JsonObject EntryJson(Track t) => new() { ["p"] = t.Path, ["t"] = t.Title, ["a"] = t.Artist };

    public Track? ResolveEntry(PlaylistEntry e) => Library.Resolve(e.Path, e.Title, e.Artist) ?? Library.FindLoose(e.Title, e.Artist);
    public IReadOnlyList<Track> Resolve(IEnumerable<PlaylistEntry> entries) => entries.Select(ResolveEntry).OfType<Track>().ToList();
}
