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

    public async Task LoadAsync(JellyfinClient jf, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        Loading = true; Changed?.Invoke();
        try
        {
            // newest profile copy from every account this user has
            var copies = new List<JsonObject>();
            if (Jellyfin is { } a) try { if (await jf.PullProfileAsync(a, ct) is { } p) copies.Add(p); } catch (UnauthorizedAccessException) when (Kind == "nas") { Jellyfin = null; }
            if (Nas is { } n) try { if (NasClient.ReadText(n, n.ProfilePath) is { } s && JsonNode.Parse(s) is JsonObject p) copies.Add(p); } catch (Exception) when (Kind == "jellyfin") { }
            if (copies.Count > 0) Profile = copies.MaxBy(p => p["updated"]?.GetValue<long>() ?? 0)!;

            // the other account comes back from the profile: a NAS sign-in gets the Jellyfin account and vice versa
            if (Jellyfin is null && Profile["account"] is JsonObject acc && acc["token"]?.GetValue<string>() is { Length: > 0 } tok)
                Jellyfin = new JellyfinAccount(acc["server"]!.GetValue<string>(), acc["userId"]!.GetValue<string>(), acc["user"]?.GetValue<string>() ?? "", tok);
            if (Nas is null && Profile["services"]?["nas"] is JsonObject ns && ns["host"]?.GetValue<string>() is { Length: > 0 } host)
                Nas = new NasAccount(host, S(ns, "share"), S(ns, "folder"), S(ns, "user"), S(ns, "pass"), S(ns, "domain"));

            List<Track> jfTracks = [], nasTracks = [];
            Problem = null;
            if (Jellyfin is { } ja)
                try { jfTracks = await jf.AllAudioAsync(ja, ct); }
                catch (UnauthorizedAccessException) { if (Kind == "nas") Jellyfin = null; else throw; }
                catch (Exception e) { Problem = $"Jellyfin: {e.Message}"; }
            if (Nas is { } na)
                try { nasTracks = await Task.Run(() => NasClient.Scan(na), ct); }
                catch (Exception e) { Problem = (Problem is null ? "" : Problem + " · ") + $"NAS: {e.Message}"; }
            Library = Library.Merge(jfTracks, nasTracks);
            LoadedAt = DateTime.UtcNow;
        }
        finally { Loading = false; gate.Release(); Changed?.Invoke(); }
    }

    static string S(JsonObject o, string k) => o[k]?.GetValue<string>() ?? "";

    // ---- save ----

    /// <summary>Writes the profile to every account this user has, with the accounts themselves in it.</summary>
    public async Task SaveAsync(JellyfinClient jf, CancellationToken ct = default)
    {
        Profile["updated"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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

    public void AddToPlaylist(string id, Track t)
    {
        if (FindPlaylist(id) is not { } o) return;
        var tr = o["tracks"] as JsonArray ?? new JsonArray();
        if (tr.OfType<JsonObject>().Any(x => S(x, "p") == t.Path)) return;
        tr.Add(EntryJson(t)); o["tracks"] = tr; o["m"] = Now(); Changed?.Invoke();
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

    public IReadOnlyList<Track> Resolve(IEnumerable<PlaylistEntry> entries) => entries.Select(e => Library.Resolve(e.Path, e.Title, e.Artist)).OfType<Track>().ToList();
}
