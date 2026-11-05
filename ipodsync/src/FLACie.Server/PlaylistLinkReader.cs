using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Fetches a playlist or album link from Spotify or Deezer and reads its songs (<see cref="PlaylistLinks"/>), for the Import page's "Paste a link".
/// No account and no developer key: Spotify's public embed page (a playlist's first 100 songs), Deezer's open API (every page, up to 2,000 songs).
/// </summary>
public sealed class PlaylistLinkReader(IHttpClientFactory http)
{
    const int DeezerMax = 2000;

    /// <summary>The playlist behind [url]. Throws an InvalidDataException that says what went wrong, in words for the page. YouTube and SoundCloud
    /// links are read by flacie-ytdl through the person's file mover ([services]); Apple Music from its public page (F25.1).</summary>
    public async Task<ImportedPlaylist> ReadAsync(string url, CancellationToken ct = default, DownloadServices? services = null)
    {
        var link = PlaylistLinks.Recognise(url) ?? throw new InvalidDataException(PlaylistLinks.Refusal);
        var c = http.CreateClient("playlistlinks");
        if (link.Service is PlaylistLinks.Service.YouTube or PlaylistLinks.Service.SoundCloud) return await ViaYtdlAsync(link, services, ct);
        if (link.Service == PlaylistLinks.Service.Apple)
        {
            string page;
            try { page = await c.GetStringAsync(PlaylistLinks.FetchUrl(link), ct); }
            catch (HttpRequestException) { throw new InvalidDataException("Couldn't reach Apple Music. Try again in a minute."); }
            return AppleMusicPage.Parse(page) ?? throw new InvalidDataException("No songs found at that link. Is the playlist public?");
        }
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

    /// <summary>YouTube and SoundCloud through flacie-ytdl (POST /playlist {url} on the file mover, which forwards it): at most 500 songs.</summary>
    async Task<ImportedPlaylist> ViaYtdlAsync(PlaylistLinks.Link link, DownloadServices? services, CancellationToken ct)
    {
        var name = link.Service == PlaylistLinks.Service.YouTube ? "YouTube" : "SoundCloud";
        if (services is not { FileMoverUrl.Length: > 0 } svc) throw new InvalidDataException($"{name} links need FLACie's download services (the file mover) set up in Server admin.");
        var url = PlaylistLinks.FetchUrl(link);
        if (!PlaylistLinks.YtdlAllowed(url)) throw new InvalidDataException(PlaylistLinks.Refusal);
        using var req = new HttpRequestMessage(HttpMethod.Post, svc.FileMoverUrl.TrimEnd('/') + "/playlist")
        { Content = new StringContent(new System.Text.Json.Nodes.JsonObject { ["url"] = url }.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Api-Key", svc.FileMoverKey);
        string body;
        try
        {
            using var res = await http.CreateClient("downloads-long").SendAsync(req, ct);
            body = await res.Content.ReadAsStringAsync(ct);
            if ((int)res.StatusCode == 404) throw new InvalidDataException($"No songs found at that {name} link. Is it public?");
            if (!res.IsSuccessStatusCode) throw new InvalidDataException($"{name} could not be read just now (the yt-dlp service said {(int)res.StatusCode}). Try again later.");
        }
        catch (HttpRequestException) { throw new InvalidDataException($"Couldn't reach the yt-dlp service for {name}. Try again in a minute."); }
        return PlaylistLinks.FromYtdl(body, link.Service) ?? throw new InvalidDataException($"No songs found at that {name} link. Is it public?");
    }
}
