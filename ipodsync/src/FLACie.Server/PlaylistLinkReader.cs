using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Fetches a playlist or album link from Spotify or Deezer and reads its songs (<see cref="PlaylistLinks"/>), for the Import page's "Paste a link".
/// No account and no developer key: Spotify's public embed page (a playlist's first 100 songs), Deezer's open API (every page, up to 2,000 songs).
/// </summary>
public sealed class PlaylistLinkReader(IHttpClientFactory http)
{
    const int DeezerMax = 2000;

    /// <summary>The playlist behind [url]. Throws an InvalidDataException that says what went wrong, in words for the page.</summary>
    public async Task<ImportedPlaylist> ReadAsync(string url, CancellationToken ct = default)
    {
        var link = PlaylistLinks.Recognise(url) ?? throw new InvalidDataException("That isn't a Spotify or Deezer playlist or album link. Copy it with Share > Copy link.");
        var c = http.CreateClient("playlistlinks");
        string body;
        try { body = await c.GetStringAsync(PlaylistLinks.FetchUrl(link), ct); }
        catch (HttpRequestException) { throw new InvalidDataException($"Couldn't reach {link.Service}. Try again in a minute."); }
        if (link.Service == PlaylistLinks.Service.Spotify)
            return PlaylistLinks.FromSpotifyEmbed(body) ?? throw new InvalidDataException("No songs found at that link. Is the playlist public?");
        var list = PlaylistLinks.FromDeezer(body, out var next) ?? throw new InvalidDataException("No songs found at that link. Is the playlist public?");
        while (next is not null && list.Tracks.Count < DeezerMax)
        {
            var more = PlaylistLinks.FromDeezerPage(await c.GetStringAsync(next, ct), out next);
            if (more.Count == 0) break;
            list.Tracks.AddRange(more);
        }
        return list;
    }
}
