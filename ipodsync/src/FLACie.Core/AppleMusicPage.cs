using System.Text.Json;
using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// F25: an Apple Music playlist or album page (music.apple.com, public, no account). The page's serialized-server-data holds each song's title,
/// artists and length; its ld+json (MusicPlaylist / MusicAlbum) the list's name and the songs' names, used when the first is missing.
/// </summary>
public static partial class AppleMusicPage
{
    [GeneratedRegex(@"<script[^>]*id=""serialized-server-data""[^>]*>(.*?)</script>", RegexOptions.Singleline)] private static partial Regex ServerData();
    [GeneratedRegex(@"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline)] private static partial Regex LdJson();

    public static ImportedPlaylist? Parse(string html)
    {
        string? name = null; var tracks = new List<ImportedTrack>();
        if (LdJson().Match(html ?? "") is { Success: true } ld)
            try
            {
                using var doc = JsonDocument.Parse(ld.Groups[1].Value);
                var r = doc.RootElement;
                if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
            }
            catch (JsonException) { }
        if (ServerData().Match(html ?? "") is { Success: true } sd)
            try
            {
                using var doc = JsonDocument.Parse(sd.Groups[1].Value);
                var seen = new HashSet<string>();
                Walk(doc.RootElement, tracks, seen, 0);
            }
            catch (JsonException) { }
        name = SpotifyEmbed.Clean(name);
        return tracks.Count == 0 ? null : new(name.Length > 0 ? name : "Apple Music playlist", tracks);
    }

    /// <summary>The song rows: objects with a title, an artistName and a length (in page order, one per id).</summary>
    static void Walk(JsonElement e, List<ImportedTrack> into, HashSet<string> seen, int depth)
    {
        if (depth > 40 || into.Count >= 500) return;
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (S(e, "title") is { Length: > 0 } title && S(e, "artistName") is { Length: > 0 } artist && e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
            {
                if (seen.Add(S(e, "id") ?? title + "|" + artist))
                    into.Add(new(SpotifyEmbed.Clean(Lead(artist)), SpotifyEmbed.Clean(title)));
                return;
            }
            foreach (var p in e.EnumerateObject()) Walk(p.Value, into, seen, depth + 1);
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x, into, seen, depth + 1);
    }

    /// <summary>"Drake & Don Toliver" -> "Drake": the lead artist, as a search matches best.</summary>
    static string Lead(string artists) => Regex.Split(artists, @"\s*(?:,|&| x | feat\.? | ft\.? )\s*", RegexOptions.IgnoreCase)[0].Trim();
    static string? S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
