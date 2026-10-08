using System.Net.Http.Json;
using System.Text.Json;

namespace IpodSync.Core.Jellyfin;

public sealed record JellyfinUser(string Id, string Name);
public sealed record JellyfinPlaylist(string Id, string Name);
public sealed record JellyfinItem(string Id, string Name, string? Artist, string? Album, long RunTimeTicks)
{
    public TimeSpan Duration => TimeSpan.FromTicks(RunTimeTicks);
}

/// <summary>
/// Thin wrapper over the bits of Jellyfin's REST API playlist sync needs:
/// listing users, searching the audio library, and creating/extending
/// playlists. Auth is a server-issued API key (Settings -> Advanced -> API
/// Keys in the Jellyfin dashboard) sent as the X-Emby-Token header -- never a
/// username/password.
/// </summary>
public sealed class JellyfinClient(HttpClient http, string baseUrl, string apiKey)
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    private HttpRequestMessage Req(HttpMethod method, string pathAndQuery)
    {
        var req = new HttpRequestMessage(method, $"{_baseUrl}{pathAndQuery}");
        req.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Client=\"ipodsync\", Device=\"FLACie\", DeviceId=\"flacie-windows\", Version=\"1\", Token=\"{apiKey}\"");
        return req;
    }

    public async Task<(bool Ok, string? ServerName, string? Error)> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.SendAsync(Req(HttpMethod.Get, "/System/Info"), ct);
            if (!resp.IsSuccessStatusCode) return (false, null, $"HTTP {(int)resp.StatusCode}");
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            string? name = json.TryGetProperty("ServerName", out var n) ? n.GetString() : null;
            return (true, name, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<List<JellyfinUser>> GetUsersAsync(CancellationToken ct = default)
    {
        using var resp = await http.SendAsync(Req(HttpMethod.Get, "/Users"), ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.EnumerateArray()
            .Select(u => new JellyfinUser(u.GetProperty("Id").GetString()!, u.GetProperty("Name").GetString()!))
            .ToList();
    }

    public async Task<List<JellyfinPlaylist>> GetPlaylistsAsync(string userId, CancellationToken ct = default)
    {
        using var resp = await http.SendAsync(Req(HttpMethod.Get, $"/Users/{userId}/Items?IncludeItemTypes=Playlist&Recursive=true"), ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!json.TryGetProperty("Items", out var items)) return [];
        return items.EnumerateArray()
            .Select(i => new JellyfinPlaylist(i.GetProperty("Id").GetString()!, i.GetProperty("Name").GetString()!))
            .ToList();
    }

    /// <summary>Finds a library item id for a track by title, preferring an
    /// artist match among the results. Matching by tag text rather than file
    /// identity is inherently approximate -- there is no shared key between an
    /// iPod's tracks and Jellyfin's library.</summary>
    public async Task<string?> FindAudioItemIdAsync(string title, string? artist, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        string q = Uri.EscapeDataString(title);
        using var resp = await http.SendAsync(Req(HttpMethod.Get, $"/Items?IncludeItemTypes=Audio&Recursive=true&SearchTerm={q}&Limit=25"), ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!json.TryGetProperty("Items", out var items)) return null;

        string? firstId = null;
        foreach (var item in items.EnumerateArray())
        {
            string id = item.GetProperty("Id").GetString()!;
            firstId ??= id;
            string? name = item.TryGetProperty("Name", out var n) ? n.GetString() : null;
            string? albumArtist = item.TryGetProperty("AlbumArtist", out var a) ? a.GetString() : null;
            if (string.Equals(name, title, StringComparison.OrdinalIgnoreCase) &&
                (artist is null || string.Equals(albumArtist, artist, StringComparison.OrdinalIgnoreCase)))
                return id;
        }
        return firstId; // no exact match -- best-effort fall back to the top search hit
    }

    public async Task<string> CreatePlaylistAsync(string userId, string name, IEnumerable<string> itemIds, CancellationToken ct = default)
    {
        var req = Req(HttpMethod.Post, "/Playlists");
        req.Content = JsonContent.Create(new { Name = name, Ids = itemIds.ToArray(), UserId = userId });
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("Id").GetString()!;
    }

    public async Task AddToPlaylistAsync(string playlistId, string userId, IEnumerable<string> itemIds, CancellationToken ct = default)
    {
        string ids = string.Join(',', itemIds);
        if (ids.Length == 0) return;
        using var resp = await http.SendAsync(Req(HttpMethod.Post, $"/Playlists/{playlistId}/Items?ids={ids}&userId={userId}"), ct);
        resp.EnsureSuccessStatusCode();
    }

    public Task<List<JellyfinItem>> GetPlaylistItemsAsync(string playlistId, string userId, CancellationToken ct = default) =>
        ItemsAsync($"/Playlists/{playlistId}/Items?userId={userId}", ct);

    /// <summary>Newest additions to the whole audio library (a reasonable "home" view).</summary>
    public Task<List<JellyfinItem>> GetRecentAudioAsync(string userId, int limit = 100, CancellationToken ct = default) =>
        ItemsAsync($"/Users/{userId}/Items?IncludeItemTypes=Audio&Recursive=true&SortBy=DateCreated&SortOrder=Descending&Limit={limit}", ct);

    public Task<List<JellyfinItem>> SearchAudioAsync(string userId, string term, int limit = 50, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(term) ? Task.FromResult(new List<JellyfinItem>())
            : ItemsAsync($"/Users/{userId}/Items?IncludeItemTypes=Audio&Recursive=true&SearchTerm={Uri.EscapeDataString(term)}&Limit={limit}", ct);

    private async Task<List<JellyfinItem>> ItemsAsync(string pathAndQuery, CancellationToken ct)
    {
        using var resp = await http.SendAsync(Req(HttpMethod.Get, pathAndQuery), ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!json.TryGetProperty("Items", out var items)) return [];
        return items.EnumerateArray().Select(ToItem).ToList();
    }

    private static JellyfinItem ToItem(JsonElement i)
    {
        string? artist = i.TryGetProperty("AlbumArtist", out var aa) ? aa.GetString()
            : i.TryGetProperty("Artists", out var ars) && ars.GetArrayLength() > 0 ? ars[0].GetString() : null;
        return new JellyfinItem(
            i.GetProperty("Id").GetString()!,
            i.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
            artist,
            i.TryGetProperty("Album", out var al) ? al.GetString() : null,
            i.TryGetProperty("RunTimeTicks", out var rt) ? rt.GetInt64() : 0);
    }

    /// <summary>A URL an &lt;audio&gt; element (or anything else that just fetches a URL) can
    /// stream directly -- the api_key query form exists specifically because such elements
    /// can't set a custom header. Requests the original file (static=true), not a server
    /// transcode: ipodsync has its own transcoder tuned for whichever host is asking.</summary>
    public string StreamUrl(string itemId) => $"{_baseUrl}/Audio/{itemId}/stream?static=true&ApiKey={Uri.EscapeDataString(apiKey)}";
}
