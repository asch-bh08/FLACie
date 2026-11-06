using System.Text.Json;

namespace FLACie.Core;

/// <summary>
/// The cover a song was found with in search, kept for it once it is downloaded (owner, 6 Nov: The Rhythm of the Night showed one cover in search and
/// another once downloaded, from the picture inside the Hi-Res copy's file, often a compilation's). By the song's match key, saved to disk; the library
/// shows it (<see cref="Apply"/>) and the cover address answers it for the song's Jellyfin picture, so the phone sees it too.
/// </summary>
public sealed class DownloadCovers
{
    public static DownloadCovers? Current { get; set; }

    readonly string path;
    readonly object lk = new();
    Dictionary<string, string>? byKey;

    public DownloadCovers(string path) { this.path = path; }

    Dictionary<string, string> Load()
    {
        if (byKey is not null) return byKey;
        try { byKey = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { byKey = []; }
        return byKey;
    }

    /// <summary>Remembers [artUrl] (a catalogue picture's http(s) address) as the cover of that song.</summary>
    public void Remember(string title, string artist, string? artUrl)
    {
        if (artUrl is not { Length: > 0 } || !Uri.TryCreate(artUrl, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return;
        var key = Matching.MatchKey(title, artist);
        lock (lk)
        {
            var m = Load();
            if (m.TryGetValue(key, out var had) && had == artUrl) return;
            m[key] = artUrl;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(m));
                File.Move(tmp, path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The cover key the song was found with, or null.</summary>
    public string? For(Track t)
    {
        lock (lk) return Load().TryGetValue(Matching.MatchKey(t), out var url) ? Art.ExternalKey(url) : null;
    }

    /// <summary>The songs with their remembered covers (the list itself when none has one).</summary>
    public IReadOnlyList<Track> Apply(IReadOnlyList<Track> tracks)
    {
        lock (lk) if (Load().Count == 0) return tracks;
        List<Track>? result = null;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (For(tracks[i]) is not { } k || k == tracks[i].ArtKey) continue;
            result ??= [.. tracks];
            result[i] = tracks[i] with { ArtKey = k };
        }
        return result ?? tracks;
    }
}
