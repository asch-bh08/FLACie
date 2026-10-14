using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server.Alexa;

/// <summary>The public https address Alexa reaches this server at (the stream and cover links must use it). FLACIE_PUBLIC_URL wins; otherwise it is learned from the last verified skill request.</summary>
public sealed class AlexaPublic(IConfiguration conf)
{
    volatile string? learned;
    public string Base => conf["FLACIE_PUBLIC_URL"] is { Length: > 0 } c ? c.TrimEnd('/') : learned ?? "";
    public void Learn(HttpRequest r)
    {
        // Alexa only plays https on port 443
        if (!r.Host.HasValue) return;
        var host = r.Host.Host; if (r.Host.Port is { } p && p != 443) return;
        learned = "https://" + host;
    }
}

/// <summary>One linked person's FLACie, for the voice side.</summary>
public sealed class SessionAlexaUser(UserSession s, JellyfinClient jf, DownloadManager downloads, WebCatalog catalog, Action<string, string, bool, string> ended) : IAlexaUser
{
    public UserSession Session { get; } = s;
    public string Name => s.DisplayName;
    public bool Ready => !s.Loading && s.Library.Songs.Count > 0 && s.Problem is null;
    public string? Problem => s.Problem;
    public IReadOnlyList<Track> Songs => s.Library.Songs;

    IReadOnlyList<Playlist> lists = []; string listSig = "";
    /// <summary>The playlists, the same list object until one changes (so the matcher is not rebuilt for every request).</summary>
    public IReadOnlyList<Playlist> Playlists
    {
        get
        {
            var now = s.Playlists;
            var sig = string.Join('|', now.Select(p => p.Id + ":" + p.Modified + ":" + p.Entries.Count));
            lock (this) { if (sig != listSig) { listSig = sig; lists = now; } return lists; }
        }
    }

    public Track? Find(string path) => s.FindByPath(path);
    public Track? FindSong(string artist, string title) => s.Library.Find(title, artist) ?? s.Library.FindLoose(title, artist);

    public async Task RateAsync(Track t, int stars)
    {
        s.SetRating(t, stars);
        if (s.Jellyfin is { } a && t.JellyfinId is { Length: > 0 } id) { try { await jf.SetRatingAsync(a, id, stars); } catch (Exception) { } }
        _ = Task.Run(async () => { try { await s.SaveAsync(jf); } catch (Exception) { } });
    }

    public UserSession.PlaylistAdd AddToPlaylist(string playlistName, Track t, out string used)
    {
        var all = s.Playlists;
        var want = Phonetic.FormOf(playlistName);
        var best = all.Select(p => (p, sim: Math.Max(Phonetic.Similarity(want, Phonetic.FormOf(p.Name)), Phonetic.Fold(p.Name).StartsWith(want.Fold, StringComparison.Ordinal) && want.Fold.Length >= 4 ? 0.9 : 0)))
            .OrderByDescending(x => x.sim).FirstOrDefault();
        used = best.p?.Name ?? playlistName;
        if (best.p is null || best.sim < 0.75) return UserSession.PlaylistAdd.NoPlaylist;
        var r = s.AddToPlaylist(best.p.Id, t);
        if (r == UserSession.PlaylistAdd.Added) _ = Task.Run(async () => { try { await s.SaveAsync(jf); } catch (Exception) { } });
        return r;
    }

    public IReadOnlyList<Track> SimilarTo(FLACie.Core.Group artist, int take)
    {
        var genres = artist.Tracks.Select(t => t.Genre).Where(g => g.Length > 0).GroupBy(g => g, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key).ToList();
        var years = artist.Tracks.Where(t => t.Year > 0).Select(t => t.Year).ToList(); var year = years.Count > 0 ? (int)years.Average() : 0;
        var seed = Matching.PrimaryArtist(artist.Name);
        if (genres.Count == 0) return [];
        return s.Library.Songs.Where(t => Matching.PrimaryArtist(t.Artist) != seed && genres.Any(g => t.Genre.Equals(g, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => year > 0 && t.Year > 0 ? Math.Abs(t.Year - year) / 5 : 0).ThenBy(_ => Random.Shared.Next()).Take(take).ToList();
    }

    public async Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(6));
        List<CatalogSong> found;
        try { found = await catalog.SongsAsync(words, 5, limit.Token); } catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        var song = found.FirstOrDefault();
        if (song is null) return null;
        if (FindSong(song.Artist, song.Title) is { JellyfinId.Length: > 0 }) return new(song.Artist, song.Title, true);
        if (s.Jellyfin is null && s.Nas is null) return null;
        var task = downloads.FetchAsync(s, "Alexa", song.Artist, song.Title, song.Album, song.ArtUrl, song.DurationMs);
        _ = task.ContinueWith(t => ended(song.Artist, song.Title, t.IsCompletedSuccessfully && t.Result.Ok, t.IsCompletedSuccessfully ? t.Result.Message : "Stopped"), TaskScheduler.Default);
        return new(song.Artist, song.Title);
    }

    public IReadOnlyList<AlexaDownload> Downloads() => downloads.JobsOf(s).Where(j => j.Kind == "Alexa")
        .Select(j => new AlexaDownload(j.Artist, j.Title, j.Stage == DownloadStage.Done ? "done" : j.Stage == DownloadStage.Failed ? "failed" : "running", j.Message, j.At)).ToList();
}

/// <summary>Turns a link into the person's session. One <see cref="SessionAlexaUser"/> per link, so the voice index lives as long as the library does.</summary>
public sealed class SessionAlexaDirectory(AlexaStore store, SessionStore sessions, JellyfinClient jf, DownloadManager downloads, WebCatalog catalog, Lazy<AlexaEngine> engine, AlexaProactive proactive) : IAlexaDirectory
{
    readonly ConcurrentDictionary<string, SessionAlexaUser> users = new();

    public (string LinkId, IAlexaUser User)? Resolve(string alexaUserId)
    {
        if (store.AccountFor(alexaUserId) is not { } account) return null;
        var linkId = AlexaStore.HashUser(alexaUserId);
        var session = sessions.For(SessionStore.Principal(account, null));
        if (users.TryGetValue(linkId, out var have) && ReferenceEquals(have.Session, session)) return (linkId, have);
        var user = new SessionAlexaUser(session, jf, downloads, catalog, (artist, title, ok, msg) =>
        {
            engine.Value.DownloadEnded(linkId, artist, title, ok, msg);
            _ = proactive.NotifyAsync(alexaUserId, title, artist, ok, store.IsNotifiable(alexaUserId));
        });
        users[linkId] = user;
        return (linkId, user);
    }

    /// <summary>The session behind a link id, for the stream endpoint (which has a link id from a signed URL, not an Alexa user id).</summary>
    public UserSession? SessionOf(string linkId, out JellyfinAccount? account)
    {
        account = store.AccountForLink(linkId);
        return account is null ? null : sessions.For(SessionStore.Principal(account, null));
    }

    public void Forget(string linkId) => users.TryRemove(linkId, out _);
}

/// <summary>"A progressive response": a short message spoken while the answer is still being worked out.</summary>
public sealed class HttpProgressive(IHttpClientFactory hf) : IProgressive
{
    public async Task SendAsync(string apiEndpoint, string accessToken, string requestId, string speech, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, apiEndpoint.TrimEnd('/') + "/v1/directives");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent(new JsonObject { ["header"] = new JsonObject { ["requestId"] = requestId }, ["directive"] = new JsonObject { ["type"] = "VoicePlayer.Speak", ["speech"] = Phonetic.SpeechSafe(speech) } }.ToJsonString(), Encoding.UTF8, "application/json");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(3));
        try { using var res = await hf.CreateClient("catalog").SendAsync(req, limit.Token); } catch (Exception) when (!ct.IsCancellationRequested) { }
    }
}

/// <summary>
/// A notification on the Echo when a requested song is ready ("Blinding Lights is now available on FLACie"). Needs the skill's Login with Amazon security profile
/// (FLACIE_ALEXA_CLIENT_ID and FLACIE_ALEXA_CLIENT_SECRET in the server's environment, set by the owner) and the person's permission. Without those it does nothing,
/// and the same message is read out the next time they talk to FLACie.
/// </summary>
public sealed class AlexaProactive(IHttpClientFactory hf, IConfiguration conf, ILogger<AlexaProactive> log)
{
    string? token; DateTime tokenUntil;
    public bool Configured => conf["FLACIE_ALEXA_CLIENT_ID"] is { Length: > 0 } && conf["FLACIE_ALEXA_CLIENT_SECRET"] is { Length: > 0 };

    public async Task<bool> NotifyAsync(string alexaUserId, string title, string artist, bool ok, bool allowed)
    {
        if (!allowed || !Configured) return false;
        try
        {
            var http = hf.CreateClient("catalog");
            if (token is null || DateTime.UtcNow > tokenUntil)
            {
                using var tr = await http.PostAsync("https://api.amazon.com/auth/o2/token", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "client_credentials", ["scope"] = "alexa::proactive_events", ["client_id"] = conf["FLACIE_ALEXA_CLIENT_ID"]!, ["client_secret"] = conf["FLACIE_ALEXA_CLIENT_SECRET"]! }));
                if (!tr.IsSuccessStatusCode) { log.LogWarning("Alexa notification: could not get a token ({Status})", (int)tr.StatusCode); return false; }
                var j = JsonNode.Parse(await tr.Content.ReadAsStringAsync());
                token = (string?)j?["access_token"]; tokenUntil = DateTime.UtcNow.AddSeconds(((int?)j?["expires_in"] ?? 300) - 60);
            }
            var now = DateTime.UtcNow;
            JsonObject ev = ok
                ? new() { ["name"] = "AMAZON.MediaContent.Available", ["payload"] = new JsonObject { ["availability"] = new JsonObject { ["startTime"] = now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), ["provider"] = new JsonObject { ["name"] = "FLACie" }, ["method"] = "STREAM" }, ["content"] = new JsonObject { ["name"] = title, ["contentType"] = "SINGLE" } } }
                : new() { ["name"] = "AMAZON.MessageAlert.Activated", ["payload"] = new JsonObject { ["state"] = new JsonObject { ["status"] = "UNREAD", ["freshness"] = "NEW" }, ["messageGroup"] = new JsonObject { ["creator"] = new JsonObject { ["name"] = "FLACie" }, ["count"] = 1 } } };
            var body = new JsonObject
            {
                ["timestamp"] = now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), ["referenceId"] = Guid.NewGuid().ToString("N"), ["expiryTime"] = now.AddHours(2).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["event"] = ev, ["relevantAudience"] = new JsonObject { ["type"] = "Unicast", ["payload"] = new JsonObject { ["user"] = alexaUserId } },
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.amazonalexa.com/v1/proactiveEvents/stages/development") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) log.LogWarning("Alexa notification was refused ({Status})", (int)res.StatusCode);
            return res.IsSuccessStatusCode;
        }
        catch (Exception e) { log.LogWarning("Alexa notification failed: {Kind}", e.GetType().Name); return false; }
    }
}
