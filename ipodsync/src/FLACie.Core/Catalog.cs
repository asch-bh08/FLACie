using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FLACie.Core;

public sealed record CatalogSong(string Artist, string Title, string Album, string? ArtUrl, long DurationMs, int TrackNo, int DiscNo);

/// <summary>[Id] is a Deezer album id, or an iTunes collection id when [Itunes].</summary>
public sealed record CatalogAlbum(long Id, string Artist, string Title, string? ArtUrl, int TrackCount, string Year, string Type = "", bool Itunes = false)
{
    /// <summary>"Single", "EP" or "Album" as shown in results.</summary>
    public string Kind =>
        Type == "single" || Title.EndsWith("- Single") || (Type.Length == 0 && TrackCount is >= 1 and <= 3) ? "Single"
        : Type == "ep" || Title.EndsWith("- EP") || (Type.Length == 0 && TrackCount is >= 4 and <= 6) ? "EP" : "Album";
    public string CleanTitle => Regex.Replace(Title, @"\s*-\s*(Single|EP)$", "");
}

/// <summary>
/// Search results for music that isn't in the library yet (ported from the Android app's WebCatalog). Deezer's public API
/// (free, no key) ranks by popularity, so "blinding lights" lists The Weeknd first instead of the covers that share the
/// title; iTunes Search is the fallback when Deezer can't be reached. Albums carry their tracklist, which an album download needs.
/// </summary>
public sealed class WebCatalog(HttpClient http)
{
    static readonly string[] VariantWords = ["remix", "mix", "live", "acoustic", "instrumental", "karaoke", "sped", "slowed", "reverb", "nightcore", "version", "cover", "tribute", "style", "edit", "demo", "8d", "lullaby", "piano", "jazz"];
    static readonly Regex EditionWords = new(@"\s*[(\[](deluxe|expanded|anniversary|remaster)[^)\]]*[)\]]", RegexOptions.IgnoreCase);
    static readonly Regex CoverActs = new(@"karaoke|tribute|lullaby|kidz bop|in the style of|made famous|cover|8.bit|piano version|instrumental", RegexOptions.IgnoreCase);
    static readonly Regex Brackets = new(@"[(\[]([^)\]]*)[)\]]|\s-\s(.*)$");

    /// <summary>Songs matching every typed word, most popular first, originals before remixes the user didn't type, one entry per song.</summary>
    public async Task<List<CatalogSong>> SongsAsync(string query, int limit = 30, CancellationToken ct = default)
    {
        var raw = await DeezerSongs(query, ct) ?? await ItunesSongs(query, ct) ?? [];
        var typed = Words(query); var sw = Matching.SearchWords(query);
        var coverTyped = CoverActs.IsMatch(string.Join(" ", typed));
        return raw.Where(s => SearchRank.Matches(query, s.Title, s.Artist, s.Album))
            .OrderBy(s => Variant(s.Title, typed) + (!coverTyped && CoverActs.IsMatch($"{s.Artist} {s.Album} {s.Title}") ? 2 : 0))
            .DistinctBy(s => Norm(Matching.PrimaryArtist(s.Artist)) + "|" + Norm(BaseTitle(s.Title))).Take(limit).ToList();
    }

    public async Task<List<CatalogAlbum>> AlbumsAsync(string query, int limit = 12, CancellationToken ct = default)
    {
        var raw = await DeezerAlbums(query, ct) ?? await ItunesAlbums(query, ct) ?? [];
        var sw = Matching.SearchWords(query);
        return raw.Where(a => SearchRank.Matches(query, a.Title, a.Artist))
            .DistinctBy(a => Norm(a.Artist) + "|" + Norm(EditionWords.Replace(a.CleanTitle, "")) + "|" + a.Kind).Take(limit).ToList();
    }

    /// <summary>Albums plus, first, the album the top song comes from ("blinding lights" offers After Hours, not just same-named singles).</summary>
    public async Task<List<CatalogAlbum>> AlbumsWithTopAsync(string query, CatalogSong? top, CancellationToken ct = default)
    {
        var baseList = await AlbumsAsync(query, 12, ct);
        if (top is null || top.Album.Length == 0) return baseList;
        var q = $"{top.Artist} {top.Album}";
        var own = ((await DeezerAlbums(q, ct)) ?? (await ItunesAlbums(q, ct)))?
            .FirstOrDefault(a => Norm(Matching.PrimaryArtist(a.Artist)) == Norm(Matching.PrimaryArtist(top.Artist)) && Norm(a.CleanTitle) == Norm(top.Album));
        return own is null ? baseList : new[] { own }.Concat(baseList).DistinctBy(a => a.Id).ToList();
    }

    /// <summary>An album's songs in disc/track order.</summary>
    public async Task<List<CatalogSong>> TracksAsync(CatalogAlbum album, CancellationToken ct = default)
    {
        if (album.Itunes)
        {
            var it = await Json($"https://itunes.apple.com/lookup?id={album.Id}&entity=song&limit=200", ct);
            return (it?["results"] as JsonArray ?? []).OfType<JsonObject>().Where(o => S(o, "wrapperType") == "track").Select(ItunesSong)
                .OrderBy(s => s.DiscNo).ThenBy(s => s.TrackNo).ToList();
        }
        var dz = await Json($"https://api.deezer.com/album/{album.Id}/tracks?limit=200", ct);
        return (dz?["data"] as JsonArray ?? []).OfType<JsonObject>().Select(o =>
                new CatalogSong(S(o["artist"], "name") is { Length: > 0 } n ? n : album.Artist, S(o, "title"), album.CleanTitle, album.ArtUrl,
                    L(o, "duration") * 1000, (int)L(o, "track_position"), Math.Max(1, (int)L(o, "disk_number"))))
            .OrderBy(s => s.DiscNo).ThenBy(s => s.TrackNo).ToList();
    }

    /// <summary>Songs that go with the given one: the artist's other well-known songs, then the best-known songs of artists listeners of this
    /// artist also play (Deezer's "related artists"), interleaved so one artist doesn't take over. Never the song itself or another release of it.</summary>
    public async Task<List<CatalogSong>> RelatedAsync(string artist, string title, int limit = 24, CancellationToken ct = default)
    {
        var lead = Matching.PrimaryArtist(artist);
        // similar artists: MusicBrainz finds the artist, ListenBrainz's listening data says who listeners of that artist also play
        var similar = new List<string>();
        try
        {
            var mb = (await Json($"https://musicbrainz.org/ws/2/artist?query=artist:{Enc("\"" + lead + "\"")}&fmt=json&limit=1", ct))?["artists"] as JsonArray;
            if (S(mb?.FirstOrDefault(), "id") is { Length: > 0 } mbid
                && await Json($"https://labs.api.listenbrainz.org/similar-artists/json?artist_mbids={mbid}&algorithm=session_based_days_9000_session_300_contribution_5_threshold_15_limit_50_skip_30", ct) is JsonArray sim)
                similar = sim.OfType<JsonObject>().Select(o => S(o, "name")).Where(n => n.Length > 0 && Norm(n) != Norm(lead)).Take(8).ToList();
        }
        catch (Exception) { }
        var self = Norm(BaseTitle(title));
        async Task<List<CatalogSong>> Of(string name, int take)
        {
            var j = await Json($"https://api.deezer.com/search?q={Enc(name)}&limit=40", ct);
            return (j?["data"] as JsonArray ?? []).OfType<JsonObject>().Select(o => new CatalogSong(S(o["artist"], "name"), S(o, "title"), S(o["album"], "title"),
                    S(o["album"], "cover_xl") is { Length: > 0 } c ? c : null, L(o, "duration") * 1000, 0, 0))
                .Where(s => Norm(Matching.PrimaryArtist(s.Artist)) == Norm(Matching.PrimaryArtist(name)) && Norm(BaseTitle(s.Title)) != self && !Variant(s.Title) && !CoverActs.IsMatch(s.Album + " " + s.Title))
                .DistinctBy(s => Norm(BaseTitle(s.Title))).Take(take).ToList();
        }
        var tasks = new List<Task<List<CatalogSong>>> { Of(lead, 5) };
        tasks.AddRange(similar.Select(n => Of(n, 3)));
        var lists = (await Task.WhenAll(tasks)).ToList();
        var res = new List<CatalogSong>();
        for (var i = 0; res.Count < limit && lists.Any(l => i < l.Count); i++)
            foreach (var l in lists) if (i < l.Count) res.Add(l[i]);
        return res.DistinctBy(s => Norm(Matching.PrimaryArtist(s.Artist)) + "|" + Norm(BaseTitle(s.Title))).Take(limit).ToList();
    }

    static bool Variant(string title) => VariantWords.Any(w => title.Contains(w, StringComparison.OrdinalIgnoreCase)) && Brackets.IsMatch(title);

    /// <summary>The current iTunes chart of top songs (Deezer's own chart endpoints stopped answering): 0 is overall; the other ids are
    /// 132 Pop, 116 Rap/Hip Hop, 152 Rock, 113 Dance, 165 R&amp;B, 85 Alternative, 106 Electronic.</summary>
    public async Task<List<CatalogSong>> ChartAsync(int genre = 0, int limit = 50, CancellationToken ct = default)
    {
        int? g = genre switch { 132 => 14, 116 => 18, 152 => 21, 113 => 17, 165 => 15, 85 => 20, 106 => 7, _ => null };
        var j = await Json($"https://itunes.apple.com/us/rss/topsongs/limit={limit}{(g is null ? "" : "/genre=" + g)}/json", ct);
        string Lbl(JsonNode? n, string k) => S(n?[k], "label");
        return (j?["feed"]?["entry"] as JsonArray ?? []).OfType<JsonObject>().Select(o =>
        {
            var img = (o["im:image"] as JsonArray)?.LastOrDefault();
            return new CatalogSong(Lbl(o, "im:artist"), Lbl(o, "im:name"), Lbl(o["im:collection"], "im:name"), S(img, "label") is { Length: > 0 } u ? Big(u.Replace("170x170bb", "100x100bb")) : null, 0, 0, 0);
        }).Where(s => s.Title.Length > 0).ToList();
    }

    /// <summary>"Song (feat. X)" -> "Song"; used to match file names and fold duplicates.</summary>
    public static string BaseTitle(string t) => Regex.Replace(t, @"\s*[(\[](feat|ft|with)[^)\]]*[)\]]", "", RegexOptions.IgnoreCase).Trim();

    // ---- Deezer ----

    async Task<List<CatalogSong>?> DeezerSongs(string q, CancellationToken ct)
    {
        if (await Json($"https://api.deezer.com/search?q={Enc(q)}&limit=50", ct) is not { } j || j["data"] is not JsonArray arr) return null;
        return arr.OfType<JsonObject>().Select(o => new CatalogSong(S(o["artist"], "name"), S(o, "title"), S(o["album"], "title"),
            S(o["album"], "cover_xl") is { Length: > 0 } c ? c : null, L(o, "duration") * 1000, 0, 0)).ToList();
    }

    async Task<List<CatalogAlbum>?> DeezerAlbums(string q, CancellationToken ct)
    {
        if (await Json($"https://api.deezer.com/search/album?q={Enc(q)}&limit=30", ct) is not { } j || j["data"] is not JsonArray arr) return null;
        return arr.OfType<JsonObject>().Select(o => new CatalogAlbum(L(o, "id"), S(o["artist"], "name"), S(o, "title"),
            S(o, "cover_xl") is { Length: > 0 } c ? c : null, (int)L(o, "nb_tracks"), "", S(o, "record_type"))).ToList();
    }

    // ---- iTunes (fallback) ----

    async Task<List<CatalogSong>?> ItunesSongs(string q, CancellationToken ct) =>
        await Json($"https://itunes.apple.com/search?term={Enc(q)}&media=music&entity=song&limit=50", ct) is { } j && j["results"] is JsonArray a ? a.OfType<JsonObject>().Select(ItunesSong).ToList() : null;

    async Task<List<CatalogAlbum>?> ItunesAlbums(string q, CancellationToken ct) =>
        await Json($"https://itunes.apple.com/search?term={Enc(q)}&media=music&entity=album&limit=30", ct) is { } j && j["results"] is JsonArray a
            ? a.OfType<JsonObject>().Select(o => new CatalogAlbum(L(o, "collectionId"), S(o, "artistName"), S(o, "collectionName"), Big(S(o, "artworkUrl100")),
                (int)L(o, "trackCount"), S(o, "releaseDate").Length >= 4 ? S(o, "releaseDate")[..4] : "", Itunes: true)).ToList() : null;

    static CatalogSong ItunesSong(JsonObject o) => new(S(o, "artistName"), S(o, "trackName"), Regex.Replace(S(o, "collectionName"), @"\s*-\s*(Single|EP)$", ""),
        Big(S(o, "artworkUrl100")), L(o, "trackTimeMillis"), (int)L(o, "trackNumber"), (int)L(o, "discNumber"));

    // ---- ranking ----

    /// <summary>1 when the title has extra words in brackets or after " - " (a remix, live take, cover...) that weren't typed.</summary>
    static int Variant(string title, List<string> typed)
    {
        var extra = string.Join(" ", Brackets.Matches(title).Select(m => (m.Groups[1].Value + m.Groups[2].Value).ToLowerInvariant())
            .Where(e => !e.StartsWith("feat") && !e.StartsWith("ft.") && !e.StartsWith("with ")));
        return VariantWords.Any(extra.Contains) && !typed.Any(extra.Contains) ? 1 : 0;
    }

    static List<string> Words(string q) => q.ToLowerInvariant().Split(' ', '-').Where(w => w.Length > 0).ToList();
    static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    static string? Big(string u) => u.Length == 0 ? null : u.Replace("100x100bb", "600x600bb");
    static string Enc(string s) => Uri.EscapeDataString(s.Trim());
    static string S(JsonNode? o, string k) => o?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static long L(JsonNode? o, string k) => o?[k] is JsonValue v ? (v.TryGetValue<long>(out var l) ? l : v.TryGetValue<double>(out var d) ? (long)d : 0) : 0;

    async Task<JsonNode?> Json(string url, CancellationToken ct)
    {
        try { return JsonNode.Parse(await http.GetStringAsync(url, ct)); } catch (Exception) { return null; }
    }
}
