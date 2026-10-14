using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server.Alexa;

/// <summary>What a catalog holds right now: one JSON document per catalog type, and a hash per entity so the next export can say what changed.</summary>
public sealed record CatalogExport(IReadOnlyDictionary<string, JsonObject> Catalogs, IReadOnlyDictionary<string, string> Hashes, IReadOnlyDictionary<string, int> Counts);

/// <summary>
/// The library as the content catalogs of Alexa's Music Skill API (AMAZON.MusicRecording, MusicAlbum, MusicGroup, MusicPlaylist; catalog format 2.0): stable ids, the names and
/// spoken aliases, and a delta against the previous export (changed and new entities, and the deleted ones). Pure and in memory (it reads the library the delta sync already keeps
/// current), so exporting 10,000 songs asks Jellyfin for nothing. Today the custom skill matches words itself (<see cref="VoiceMatcher"/>); this feeds the music-skill path
/// (see docs/alexa/PLAN.md, "Later") and can be downloaded from Settings.
/// </summary>
public static class AlexaCatalog
{
    /// <summary>Used when a song has no date added, so an export of the same library is byte for byte the same (and a delta is empty).</summary>
    static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public const int MaxEntities = 500_000;
    public const string Recording = "AMAZON.MusicRecording", Album = "AMAZON.MusicAlbum", Group = "AMAZON.MusicGroup", PlaylistType = "AMAZON.MusicPlaylist";

    static string Short(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16].ToLowerInvariant();
    static string Iso(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    public static string ArtistId(string artist) => "flacie:a:" + Short(Phonetic.Fold(Phonetic.StripTags(artist)));
    public static string AlbumId(Track t) => "flacie:l:" + Short(Phonetic.Fold(t.Album) + "|" + Phonetic.Fold(Phonetic.StripTags(t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist)));
    public static string TrackId(Track t) => "flacie:t:" + (t.JellyfinId is { Length: > 0 } j ? j : Short(Matching.MatchKey(t) + "|" + t.Path));
    public static string PlaylistId(Playlist p) => "flacie:p:" + p.Id;

    static string Clip(string s) { s = s.Trim(); return s.Length <= 512 ? s : s[..512]; }

    static JsonObject Names(string name, IEnumerable<string> aliases, string lang, DateTime updated, int popularity)
    {
        var o = new JsonObject { ["names"] = new JsonArray(new JsonObject { ["language"] = lang, ["value"] = Clip(name) }) };
        var al = aliases.Select(Clip).Where(a => a.Length > 0 && !a.Equals(Clip(name), StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).Select(a => (JsonNode?)a).ToArray();
        if (al.Length > 0) o["alternateNames"] = new JsonArray(new JsonObject { ["language"] = lang, ["values"] = new JsonArray(al) });
        o["popularity"] = new JsonObject { ["default"] = Math.Clamp(popularity, 1, 100) };
        o["lastUpdatedTime"] = Iso(updated);
        return o;
    }

    static JsonObject Ref(string id, string name, string lang) => new() { ["id"] = id, ["names"] = new JsonArray(new JsonObject { ["language"] = lang, ["value"] = Clip(name) }) };

    static JsonObject Wrap(string type, string lang, string country, List<JsonObject> entities) => new()
    {
        ["type"] = type, ["version"] = "2.0", ["locales"] = new JsonArray(new JsonObject { ["country"] = country, ["language"] = lang }), ["entities"] = new JsonArray(entities.Select(e => (JsonNode?)e).ToArray()),
    };

    public static CatalogExport Build(IReadOnlyList<Track> songs, IReadOnlyList<Playlist> playlists, string lang = "en", string country = "US")
    {
        var recordings = new List<JsonObject>(); var albums = new List<JsonObject>(); var groups = new List<JsonObject>(); var lists = new List<JsonObject>();
        var hashes = new Dictionary<string, string>();
        void Keep(List<JsonObject> into, JsonObject e) { into.Add(e); hashes[(string)e["id"]!] = Short(e.ToJsonString()); }

        // songs: one per (title, artist) once version tags are dropped, as the voice matcher does
        var seenTrack = new HashSet<string>();
        var perArtist = new Dictionary<string, (string Name, int Count, DateTime Newest)>();
        var perAlbum = new Dictionary<string, (Track First, string ArtistId, string ArtistName, int Count, DateTime Newest)>();
        foreach (var t in songs.OrderBy(x => x.Title.Length))
        {
            if (t.Title.Trim().Length == 0 || t.Artist.Trim().Length == 0) continue;
            if (!seenTrack.Add(Phonetic.Fold(t.Title) + "|" + Phonetic.Fold(Phonetic.StripTags(t.Artist)))) continue;
            var added = t.AddedMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(t.AddedMs).UtcDateTime : Epoch;
            var artistName = Phonetic.StripTags(t.Artist); var aid = ArtistId(artistName);
            var e = Names(Phonetic.CleanTitle(t.Title), AlexaAliases(t.Title, MatchKind.Track), lang, added, 50);
            e["id"] = TrackId(t);
            e["artists"] = new JsonArray(Ref(aid, artistName, lang));
            if (t.Album.Trim().Length > 0) e["albums"] = new JsonArray(Ref(AlbumId(t), t.Album, lang));
            Keep(recordings, e);
            perArtist[aid] = perArtist.TryGetValue(aid, out var pa) ? (pa.Name, pa.Count + 1, added > pa.Newest ? added : pa.Newest) : (artistName, 1, added);
            if (t.Album.Trim().Length > 0)
            {
                var alid = AlbumId(t); var alArtist = Phonetic.StripTags(t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist);
                perAlbum[alid] = perAlbum.TryGetValue(alid, out var pl) ? (pl.First, pl.ArtistId, pl.ArtistName, pl.Count + 1, added > pl.Newest ? added : pl.Newest) : (t, ArtistId(alArtist), alArtist, 1, added);
            }
        }
        var maxCount = Math.Max(1, perArtist.Values.Select(v => v.Count).DefaultIfEmpty(1).Max());
        foreach (var (id, v) in perArtist)
        {
            var e = Names(v.Name, AlexaAliases(v.Name, MatchKind.Artist), lang, v.Newest, 20 + 70 * v.Count / maxCount);
            e["id"] = id; Keep(groups, e);
        }
        foreach (var (id, v) in perAlbum)
        {
            var e = Names(Phonetic.CleanTitle(v.First.Album), AlexaAliases(v.First.Album, MatchKind.Album), lang, v.Newest, 40);
            e["id"] = id; e["artists"] = new JsonArray(Ref(v.ArtistId, v.ArtistName, lang)); Keep(albums, e);
        }
        foreach (var p in playlists)
        {
            if (p.Name.Trim().Length == 0 || p.Entries.Count == 0) continue;
            var e = Names(p.Name, AlexaAliases(p.Name, MatchKind.Playlist), lang, p.Modified > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(p.Modified).UtcDateTime : Epoch, 60);
            e["id"] = PlaylistId(p); Keep(lists, e);
        }
        var cats = new Dictionary<string, JsonObject>
        {
            [Recording] = Wrap(Recording, lang, country, recordings), [Album] = Wrap(Album, lang, country, albums), [Group] = Wrap(Group, lang, country, groups), [PlaylistType] = Wrap(PlaylistType, lang, country, lists),
        };
        return new(cats, hashes, new Dictionary<string, int> { [Recording] = recordings.Count, [Album] = albums.Count, [Group] = groups.Count, [PlaylistType] = lists.Count });
    }

    static IEnumerable<string> AlexaAliases(string name, MatchKind kind) => AliasGenerator.AliasesFor(name, kind);

    /// <summary>The upload that brings an Alexa-side catalog from the previous export to this one: new and changed entities, and a tombstone for every one that is gone.</summary>
    public static IReadOnlyDictionary<string, JsonObject> Delta(CatalogExport now, IReadOnlyDictionary<string, string> previous, DateTime at, string lang = "en", string country = "US")
    {
        var res = new Dictionary<string, JsonObject>();
        foreach (var (type, cat) in now.Catalogs)
        {
            var changed = (cat["entities"] as JsonArray ?? []).OfType<JsonObject>().Where(e => !previous.TryGetValue((string)e["id"]!, out var h) || h != now.Hashes[(string)e["id"]!]).ToList();
            var prefix = type switch { Recording => "flacie:t:", Album => "flacie:l:", Group => "flacie:a:", _ => "flacie:p:" };
            var gone = previous.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !now.Hashes.ContainsKey(k))
                .Select(k => new JsonObject { ["id"] = k, ["lastUpdatedTime"] = Iso(at), ["deleted"] = true }).ToList();
            res[type] = Wrap(type, lang, country, [.. changed.Select(e => JsonNode.Parse(e.ToJsonString())!.AsObject()), .. gone]);
        }
        return res;
    }
}
