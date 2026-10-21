using System.Security.Cryptography;
using System.Text.Json;

namespace FLACie.Core;

/// <summary>A song or a playlist someone shared: what it is called, who shared it, and its songs (title and artist, so it is found in any library).</summary>
public sealed record Share(string Kind, string Name, string By, long Made, List<PlaylistEntry> Entries);

/// <summary>
/// Shared songs and playlists, one small file each, under a random id that is the link (/s/{id}). Whoever opens the link signs in to FLACie as
/// themselves and plays the songs from their own library; nothing of the sharer's account goes with it. Ids are random (not guessable) and anything
/// that is not one finds nothing, so a link can never reach another file.
/// </summary>
public sealed class ShareStore(string dir)
{
    const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int MaxSongs = 2000;

    public string Create(string kind, string name, string by, IReadOnlyList<PlaylistEntry> entries)
    {
        if (entries.Count == 0) throw new ArgumentException("Nothing to share.", nameof(entries));
        Directory.CreateDirectory(dir);
        var share = new Share(kind == "song" ? "song" : "playlist", name.Trim(), by, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), entries.Take(MaxSongs).ToList());
        for (var tries = 0; ; tries++)
        {
            var id = RandomNumberGenerator.GetString(Alphabet, 10);
            var file = Path.Combine(dir, id + ".json");
            if (File.Exists(file) && tries < 5) continue;
            File.WriteAllText(file, JsonSerializer.Serialize(share));
            return id;
        }
    }

    public Share? Get(string id)
    {
        if (id.Length is < 8 or > 16 || !id.All(c => Alphabet.Contains(c))) return null;
        var file = Path.Combine(dir, id + ".json");
        try { return File.Exists(file) ? JsonSerializer.Deserialize<Share>(File.ReadAllText(file)) : null; }
        catch (Exception) { return null; }
    }
}
