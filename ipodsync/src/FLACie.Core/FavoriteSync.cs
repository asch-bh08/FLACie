using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>
/// Merging favourites between devices: each favourite carries when it was made ("w", ms) and each removal is kept, with its time, in the profile's
/// "favgone"; for every song (by title and artist) the latest of the two wins. Before, devices only added each other's favourites together, so a song
/// unfavourited on one device came straight back from any other that still had it. The Android app merges by the same rule (FavoriteSync.kt).
/// </summary>
public static class FavoriteSync
{
    public const int KeepRemovals = 1000;

    static string Key(JsonObject o) => Matching.MatchKey(S(o, "t"), S(o, "a")) is { Length: > 1 } k ? k : "p:" + S(o, "p");
    static string S(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static long W(JsonObject o) => o["w"] is JsonValue v && v.TryGetValue<long>(out var w) ? w : 0;

    public static (JsonArray Favorites, JsonArray Removed) Merge(JsonArray localFavs, JsonArray localGone, JsonArray remoteFavs, JsonArray remoteGone)
    {
        // the latest removal of each song
        var gone = new Dictionary<string, JsonObject>();
        foreach (var g in localGone.Concat(remoteGone).OfType<JsonObject>())
            if (!gone.TryGetValue(Key(g), out var had) || W(g) > W(had)) gone[Key(g)] = g;
        // the latest favouriting of each song, in the order they were listed (local first: newest at the top)
        var favs = new Dictionary<string, JsonObject>(); var order = new List<string>();
        foreach (var f in localFavs.Concat(remoteFavs).OfType<JsonObject>())
        {
            var k = Key(f);
            if (!favs.TryGetValue(k, out var had)) { favs[k] = f; order.Add(k); }
            else if (W(f) > W(had)) favs[k] = f;
        }
        var outFavs = new JsonArray();
        foreach (var k in order)
            if (!gone.TryGetValue(k, out var g) || W(favs[k]) > W(g)) outFavs.Add(favs[k].DeepClone());
        var outGone = new JsonArray(gone.Values.OrderByDescending(W).Take(KeepRemovals).Select(g => g.DeepClone()).ToArray());
        return (outFavs, outGone);
    }
}
