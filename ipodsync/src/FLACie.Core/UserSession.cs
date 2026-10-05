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
        }
        catch (Exception) { }
    }


    public async Task LoadAsync(JellyfinClient jf, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        Loading = true; LoadCache(); Changed?.Invoke();
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
                try { jfTracks = await jf.AllAudioAsync(ja, ct); }
                catch (UnauthorizedAccessException) { if (Kind == "nas") Jellyfin = null; else throw; }
                catch (Exception e) { Problem = $"Jellyfin: {e.Message}"; }
            if (Nas is { } na)
                try { nasTracks = await Task.Run(() => NasClient.Scan(na), ct); }
                catch (Exception e) { Problem = (Problem is null ? "" : Problem + " · ") + $"NAS: {e.Message}"; }
            // a failed fetch must not replace the library kept from last time with nothing
            if (!(jfTracks.Count == 0 && nasTracks.Count == 0 && Problem is not null && Library.Songs.Count > 0)) Library = WithDownloads(Library.Merge(jfTracks, nasTracks));
            LoadedAt = DateTime.UtcNow; SaveCache();
            if (StripQualityTags()) try { await SaveAsync(jf, ct); } catch (Exception) { }
        }
        finally { Loading = false; gate.Release(); Changed?.Invoke(); }
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
        Profile["updated"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); profileSeen = Math.Max(profileSeen, Profile["updated"]!.GetValue<long>());
        Profile["v"] = 2;
        if (Jellyfin is { } a) Profile["account"] = new JsonObject { ["server"] = a.Server, ["userId"] = a.UserId, ["user"] = a.UserName, ["token"] = a.Token };
        var services = Profile["services"] as JsonObject ?? new JsonObject();
        if (Nas is { } n) services["nas"] = new JsonObject { ["host"] = n.Host, ["share"] = n.Share, ["folder"] = n.Folder, ["user"] = n.User, ["pass"] = n.Password, ["domain"] = n.Domain };
        Profile["services"] = services;
        Exception? failure = null;
        if (Nas is { } na) try { await Task.Run(() => NasClient.WriteText(na, na.ProfilePath, Profile.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })), ct); } catch (Exception e) { failure = e; }
        if (Jellyfin is { } ja) try { await jf.PushProfileAsync(ja, Profile, ct); } catch (Exception e) { failure = e; }
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
        }
        profileSeen = seen;
        if (changed) { ProfileRev++; Changed?.Invoke(); }
        return changed;
    }

    /// <summary>Adds the other kind of account, so the profile is kept in both.</summary>
    public void AddJellyfin(JellyfinAccount a) { Jellyfin = a; Changed?.Invoke(); }
    public void AddNas(NasAccount n) { Nas = n; Changed?.Invoke(); }

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
