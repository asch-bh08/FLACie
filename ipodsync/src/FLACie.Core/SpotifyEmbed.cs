using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>One song of a playlist from outside (title, lead artist, length in ms; 0 when not known).</summary>
public sealed record WebTrack(string Title, string Artist, long DurationMs = 0);

/// <summary>What a playlist is for: an artist's best-of, a decade, a genre, a chart, or anything else (a mix, a remix set).</summary>
public enum WebPlaylistKind { Artist, Decade, Genre, Chart, Other }

/// <summary>
/// F23: a playlist from Spotify, Deezer, SoundCloud or FLACie's own. Only text is kept: names and titles capped at 200 characters. [Subject] is what the
/// purpose is about (the artist's name, "1990", "rock", the chart's name). SoundCloud's [Likes], [Plays], [Reposts], [PostedUtc] and [Verified] rank it.
/// </summary>
public sealed record WebPlaylist(string Source, string Id, string Name, WebPlaylistKind Kind, string Subject, IReadOnlyList<WebTrack> Tracks,
    long Likes = 0, long Plays = 0, long Reposts = 0, DateTime? PostedUtc = null, bool Verified = false, bool Editor = false, int Size = 0, string? Picture = null)
{
    // (500 songs: a SoundCloud playlist found by name can be long, 325 in the owner's example; Spotify's embed gives 100 at most anyway)
    public const int MaxText = 200, MaxTracks = 500;
    /// <summary>What Spotify's embed and Deezer's open API give at once: 100 songs.</summary>
    public const int PageTracks = 100;
    /// <summary>What it is for: one list per purpose is shown (<see cref="WebPlaylistPick.OnePerPurpose"/>).</summary>
    public string Purpose => Kind switch { WebPlaylistKind.Other => $"other:{Source}:{Id}", _ => $"{Kind}:{Matching.NormTitle(Subject)}".ToLowerInvariant() };
}

/// <summary>
/// F23.1: Spotify's public embed page (open.spotify.com/embed/playlist/&lt;id&gt;, no key): its __NEXT_DATA__ JSON holds the playlist's name and its first
/// 100 songs (title, artists, length). Only this host is ever asked, only for an id of Spotify's own shape.
/// </summary>
public static partial class SpotifyEmbed
{
    public const string Host = "open.spotify.com";
    [GeneratedRegex(@"^[A-Za-z0-9]{22}$")] private static partial Regex IdRx();
    [GeneratedRegex(@"<script[^>]*id=""__NEXT_DATA__""[^>]*>(.*?)</script>", RegexOptions.Singleline)] private static partial Regex NextData();
    [GeneratedRegex(@"[\p{C}]")] private static partial Regex Control();

    public static bool ValidId(string id) => IdRx().IsMatch(id);
    public static string? Url(string id) => ValidId(id) ? $"https://{Host}/embed/playlist/{id}" : null;

    /// <summary>Text from outside, made safe to keep and show: no control characters, trimmed, at most 200 characters.</summary>
    public static string Clean(string? s)
    {
        var t = Control().Replace(s ?? "", " ").Trim();
        return t.Length > WebPlaylist.MaxText ? t[..WebPlaylist.MaxText] : t;
    }

    /// <summary>The playlist's name and songs, or null when the page holds none (Spotify changed it, or it was not a playlist).</summary>
    public static (string Name, List<WebTrack> Tracks)? Parse(string html)
    {
        if (NextData().Match(html ?? "") is not { Success: true } m) return null;
        JsonNode? root;
        try { root = JsonNode.Parse(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)); } catch (JsonException) { return null; }
        if (Find(root, 0) is not { } entity || entity["trackList"] is not JsonArray list) return null;
        static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        var tracks = list.OfType<JsonObject>()
            .Select(t => new WebTrack(Clean(S(t["title"])), Clean(LeadArtist(S(t["subtitle"]))), t["duration"] is JsonValue d && d.TryGetValue<double>(out var ms) ? (long)ms : 0))
            .Where(t => t.Title.Length > 0 && t.Artist.Length > 0).Take(WebPlaylist.PageTracks).ToList();
        var name = Clean(S(entity["name"]) is { Length: > 0 } n ? n : S(entity["title"]));
        return name.Length == 0 || tracks.Count == 0 ? null : (name, tracks);
    }

    /// <summary>The first object holding a "trackList" (Spotify moves it about between versions of the page).</summary>
    static JsonObject? Find(JsonNode? n, int depth)
    {
        if (depth > 12 || n is null) return null;
        if (n is JsonObject o)
        {
            if (o["trackList"] is JsonArray) return o;
            foreach (var kv in o) if (Find(kv.Value, depth + 1) is { } f) return f;
        }
        else if (n is JsonArray a) foreach (var x in a) if (Find(x, depth + 1) is { } f) return f;
        return null;
    }

    static string LeadArtist(string s) => s.Split([",", " ", " & "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
}

/// <summary>
/// F23 protections: each source (spotify, deezer, soundcloud) is asked at most once a second, has an on/off switch, and after a block, an error or a
/// rate limit is left alone for <see cref="Rest"/> with no retry loop.
/// </summary>
public sealed class WebSourceGate(Func<DateTime> now)
{
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(1), Rest = TimeSpan.FromHours(6);
    readonly Dictionary<string, DateTime> last = [], restUntil = [];
    readonly HashSet<string> off = [];
    bool loaded;

    /// <summary>Where the switches are kept (Server admin), or null to keep them in memory only.</summary>
    public string? File { get; init; }

    public void Switch(string source, bool on)
    {
        lock (off)
        {
            Load();
            if (on) off.Remove(source); else off.Add(source);
            try { if (File is not null) { Directory.CreateDirectory(Path.GetDirectoryName(File)!); System.IO.File.WriteAllText(File, JsonSerializer.Serialize(off)); } } catch (IOException) { }
        }
    }

    void Load()
    {
        if (loaded) return; loaded = true;
        try { if (File is not null && System.IO.File.Exists(File) && JsonSerializer.Deserialize<List<string>>(System.IO.File.ReadAllText(File)) is { } l) off.UnionWith(l); }
        catch (Exception e) when (e is IOException or JsonException) { }
    }
    public bool IsOn(string source) { lock (off) { Load(); return !off.Contains(source); } }

    /// <summary>Whether [source] may be asked now (taking the turn); false while it is off, resting, or was asked under a second ago.</summary>
    public bool TryTake(string source)
    {
        lock (off)
        {
            Load();
            var t = now();
            if (off.Contains(source) || (restUntil.TryGetValue(source, out var r) && t < r)) return false;
            if (last.TryGetValue(source, out var l) && t - l < Gap) return false;
            last[source] = t; return true;
        }
    }

    /// <summary>The source refused or failed: leave it alone for six hours.</summary>
    public void Failed(string source) { lock (off) restUntil[source] = now() + Rest; }
    public DateTime? RestingUntil(string source) { lock (off) return restUntil.TryGetValue(source, out var r) && r > now() ? r : null; }
}

/// <summary>
/// F23 protections: the playlists known, in one small store (not a file per list): at most <see cref="Max"/>; a list nobody searched, opened or played
/// for <see cref="Unused"/> is dropped; a list is re-read at most daily (<see cref="Stale"/>).
/// </summary>
public sealed class WebPlaylistStore(string? file, Func<DateTime> now)
{
    public const int Max = 2000;
    public static readonly TimeSpan Unused = TimeSpan.FromDays(30), Stale = TimeSpan.FromDays(1);
    sealed record Entry(WebPlaylist List, DateTime Fetched, DateTime Used);
    readonly Dictionary<string, Entry> lists = Load(file);

    static string Key(string source, string id) => source + ":" + id;
    public int Count { get { lock (lists) return lists.Count; } }

    public void Put(WebPlaylist p)
    {
        lock (lists) { lists[Key(p.Source, p.Id)] = new(p, now(), now()); Prune(); }
    }

    /// <summary>The list, marked as used (searched, opened or played); null when unknown.</summary>
    public WebPlaylist? Use(string source, string id)
    {
        lock (lists) { if (!lists.TryGetValue(Key(source, id), out var e)) return null; lists[Key(source, id)] = e with { Used = now() }; return e.List; }
    }

    /// <summary>Whether the list should be read again (unknown, or read over a day ago).</summary>
    public bool NeedsFetch(string source, string id) { lock (lists) return !lists.TryGetValue(Key(source, id), out var e) || now() - e.Fetched > Stale; }

    public IReadOnlyList<WebPlaylist> All { get { lock (lists) return lists.Values.Select(e => e.List).ToList(); } }

    void Prune()
    {
        var t = now();
        foreach (var k in lists.Where(kv => t - kv.Value.Used > Unused).Select(kv => kv.Key).ToList()) lists.Remove(k);
        if (lists.Count > Max) foreach (var k in lists.OrderBy(kv => kv.Value.Used).Take(lists.Count - Max).Select(kv => kv.Key).ToList()) lists.Remove(k);
    }

    public void Save()
    {
        if (file is null) return;
        List<Entry> snap; lock (lists) { Prune(); snap = lists.Values.ToList(); }
        try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(snap)); File.Move(file + ".tmp", file, true); }
        catch (IOException) { }
    }

    static Dictionary<string, Entry> Load(string? file)
    {
        try
        {
            if (file is not null && File.Exists(file) && JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(file)) is { } saved)
                return saved.Take(Max).ToDictionary(e => Key(e.List.Source, e.List.Id));
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return [];
    }
}
