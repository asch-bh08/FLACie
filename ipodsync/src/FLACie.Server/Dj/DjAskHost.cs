using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server.Alexa;

namespace FLACie.Server.Dj;

/// <summary>One signed-in person's FLACie for the DJ's request reader: their library, playlists, favourites, plays and downloads (the same download path as Alexa's).</summary>
public sealed class SessionAskUser(UserSession s, SessionAlexaUser alexa, Func<RecommendApi.Inputs?> inputs, Func<Track, Vibe?> vibe, MusicBrainzClient? mb = null) : IDjAskUser
{
    public UserSession Session => s;
    public string Account => s.Id;
    public bool Ready => alexa.Ready;
    public IReadOnlyList<Track> Songs => alexa.Songs;
    public IReadOnlyList<Playlist> Playlists => alexa.Playlists;
    public Track? Find(string path) => alexa.Find(path);
    public Track? FindSong(string artist, string title) => alexa.FindSong(artist, title);
    public IReadOnlyList<Track> Favourites => s.Favorites.Select(f => s.FindByPath(f.Path) ?? s.Library.Find(f.Title, f.Artist)).OfType<Track>().ToList();

    public IReadOnlyList<Track> MostPlayed(int take)
    {
        // Jellyfin's play counts, and this browser's own history for songs Jellyfin doesn't count
        var plays = new Dictionary<string, int>(StringComparer.Ordinal);
        var byId = s.PlayStats.Where(p => p.Plays > 0).ToDictionary(p => p.Id, p => p.Plays);
        foreach (var t in s.Library.Songs) if (t.JellyfinId is { } id && byId.TryGetValue(id, out var n)) plays[t.Path] = n;
        foreach (var h in s.History) if (!plays.ContainsKey(h.Path) || byId.Count == 0) plays[h.Path] = plays.GetValueOrDefault(h.Path) + 1;
        return plays.OrderByDescending(x => x.Value).Select(x => s.FindByPath(x.Key)).OfType<Track>().Take(take).ToList();
    }

    HashSet<string>? heard; DateTime heardAt;
    public bool Heard(Track t)
    {
        if (heard is null || DateTime.UtcNow - heardAt > TimeSpan.FromMinutes(1))
        {
            var ids = s.PlayStats.Where(p => p.Plays > 0).Select(p => p.Id).ToHashSet();
            heard = s.Library.Songs.Where(x => x.JellyfinId is { } id && ids.Contains(id)).Select(x => x.Path).Concat(s.History.Select(h => h.Path)).ToHashSet(StringComparer.Ordinal);
            heardAt = DateTime.UtcNow;
        }
        return heard.Contains(t.Path);
    }

    public IReadOnlyList<Track> RecommendNext(IReadOnlyList<string> session, int n) =>
        inputs() is { } i ? RecommendApi.Picks(i, session, n, DateTime.UtcNow, Environment.TickCount64).Select(p => p.Track).OfType<Track>().ToList() : [];
    public Vibe? VibeOf(Track t) => vibe(t);
    public Task RateAsync(Track t, int stars) => alexa.RateAsync(t, stars);

    /// <summary>How often they have played it, whether it is a favourite, and what the MusicBrainz cache already holds (never fetched for this).</summary>
    public string About(Track t)
    {
        var lines = new List<string>();
        var plays = MostPlayed(500).ToList().FindIndex(x => x.Path == t.Path);
        lines.Add(plays >= 0 ? $"Among their most played songs (number {plays + 1})" : Heard(t) ? "They have played it before" : "They have never played it before");
        if (s.Favorites.Any(f => f.Path == t.Path)) lines.Add("It is one of their favourites");
        try
        {
            if (DjFactsOf.Brainz(t, mb?.Peek(t)) is { } x)
            {
                if (x.ReleaseType.Length > 0) lines.Add("Released as: " + x.ReleaseType);
                if (x.OriginalYear > 0) lines.Add("First released: " + x.OriginalYear);
                if (x.Label.Length > 0) lines.Add("Label: " + x.Label);
            }
        }
        catch (Exception) { }
        return string.Join("\n", lines);
    }
    public Task<AlexaRequested?> RequestDownloadAsync(string words, CancellationToken ct) => alexa.RequestDownloadAsync(words, ct);
}

/// <summary>
/// "Talk to the DJ" for the server: the request reader for each signed-in person, the line in the DJ's one voice, and the doors:
/// POST /api/dj/ask { text, current, recent } → the answer (the web and Windows players ask in process), and GET /api/dj/ask/landed?artist=&amp;title= → { path }
/// once a song fetched for a request can be played. Only the recognised words come here, never sound; they are not kept (a short debug log line at most).
/// </summary>
public sealed class DjAskService(DjAskEngine engine, DjService dj, DjClips clips, JellyfinClient jf, DownloadManager downloads, WebCatalog catalog,
    ListenLogStore log, VibeIndex vibes, RelatedCache related, ILogger<DjAskService> logger, MusicBrainzClient? mb = null)
{
    readonly ConditionalWeakTable<UserSession, SessionAskUser> users = new();

    public SessionAskUser UserOf(UserSession s)
    {
        lock (users)
        {
            if (users.TryGetValue(s, out var have)) return have;
            var alexa = new SessionAlexaUser(s, jf, downloads, catalog, (_, _, _, _) => { });
            var u = new SessionAskUser(s, alexa, () => RecommendApi.InputsOf(s, log, vibes, related), vibes.Of, mb);
            users.Add(s, u);
            return u;
        }
    }

    public async Task<DjAskAnswer> AskAsync(UserSession s, string text, string? current, IReadOnlyList<string> recent, CancellationToken ct, int? localHour = null, DjSurface? surface = null)
    {
        if (surface is { } sf) engine.Contexts.For(s.Id).Seen(sf, DateTime.UtcNow);
        var a = await engine.AnswerAsync(UserOf(s), text, current, recent, ct, localHour);
        if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug("DJ ask: {Intent} ({Words})", a.Intent ?? "none", text.Length > 40 ? text[..40] + "…" : text);
        return Voice(s, a);
    }

    /// <summary>The line in the DJ's voice: a clip in the dashboard's one voice, or the device's voice when the person chose it; otherwise no clip (the player shows a toast).</summary>
    public DjAskAnswer Voice(UserSession s, DjAskAnswer a)
    {
        if (a.Say.Length == 0) return a;
        if (dj.Settings(s).DeviceVoice) return a with { DeviceVoice = true };
        try { return clips.Request(a.Say) is { } id ? a with { Clip = DjClips.UrlOf(id) } : a; } catch (Exception) { return a; }
    }

    public string? Landed(UserSession s, string artist, string title) => DjAskEngine.Landed(UserOf(s), artist, title);

    /// <summary>The answer for a player elsewhere (the phone): with [find], each path also comes with its file, title and artist, so a library that keeps
    /// its songs under other paths can find the same ones.</summary>
    public static JsonObject Json(DjAskAnswer a, Func<string, Track?> find)
    {
        var o = Json(a);
        if (a.Paths is { } p)
            o["songs"] = new JsonArray(p.Select(x => find(x) is { } t
                ? (JsonNode)new JsonObject { ["path"] = x, ["file"] = t.FilePath ?? "", ["title"] = t.Title, ["artist"] = t.Artist }
                : new JsonObject { ["path"] = x }).ToArray());
        return o;
    }

    public static JsonObject Json(DjAskAnswer a)
    {
        var o = new JsonObject { ["do"] = a.Do.ToString().ToLowerInvariant(), ["say"] = a.Say, ["clip"] = a.Clip, ["deviceVoice"] = a.DeviceVoice, ["intent"] = a.Intent };
        if (a.Paths is { } p) o["paths"] = new JsonArray(p.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        if (a.Title is { } t) o["title"] = t;
        if (a.Shuffle) o["shuffle"] = true;
        if (a.Replace) o["replace"] = true;
        if (a.Wait is { } w) o["wait"] = new JsonObject { ["artist"] = w.Artist, ["title"] = w.Title };
        return o;
    }
}

public static class DjAskEndpoints
{
    const int MaxBody = 16 * 1024;

    public static void AddDjAsk(this IServiceCollection services)
    {
        services.AddSingleton<IDjAskModel, CloudflareAskModel>();
        // one context per person, shared by the ask prompt and the lines between songs
        services.AddSingleton(sp => new DjAskEngine(sp.GetRequiredService<IDjAskModel>(), sp.GetRequiredService<DjContexts>()));
        services.AddSingleton<DjAskService>();
    }

    public static void MapDjAsk(this WebApplication app)
    {
        app.MapPost("/api/dj/ask", async (HttpContext ctx, SessionStore store, JellyfinClient jf, DjAskService ask) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (ctx.Request.ContentLength is > MaxBody) return Results.Json(new { error = "That request is too big." }, statusCode: 413);
            JsonObject? body = null;
            try { body = await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted) as JsonObject; } catch (System.Text.Json.JsonException) { }
            if (body?["text"] is not JsonValue tv || !tv.TryGetValue<string>(out var text)) return Results.BadRequest(new { error = "Send { text }." });
            var current = body["current"] is JsonValue cv && cv.TryGetValue<string>(out var c) ? c : null;
            // the phone names its song by title and artist (its paths are its own)
            if (current is null && body["currentSong"] is JsonObject cs && cs["title"] is JsonValue ctv && ctv.TryGetValue<string>(out var ct) && cs["artist"] is JsonValue cav && cav.TryGetValue<string>(out var ca))
                current = ask.UserOf(s).FindSong(ca, ct)?.Path;
            var recent = body["recent"] is JsonArray ra ? ra.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var x) ? x : null).OfType<string>().TakeLast(20).ToList() : [];
            // the search box's mic: words that are not a request are searched for on the phone (one parser, here)
            if (body["search"] is JsonValue sb && sb.TryGetValue<bool>(out var search) && search && DjAskRules.Parse(text) is null)
                return Results.Text("{\"do\":\"search\",\"say\":\"\"}", "application/json");
            int? hour = body["hour"] is JsonValue hv && hv.TryGetValue<int>(out var h) && h is >= 0 and <= 23 ? h : null;
            DjSurface? surface = body["surface"] is JsonValue sv && sv.TryGetValue<string>(out var sn) && Enum.TryParse<DjSurface>(sn, true, out var su) ? su : null;
            var a = await ask.AskAsync(s, text, current, recent, ctx.RequestAborted, hour, surface);
            var u = ask.UserOf(s);
            return Results.Text(DjAskService.Json(a, u.Find).ToJsonString(), "application/json");
        }).DisableAntiforgery();

        app.MapGet("/api/dj/ask/landed", async (HttpContext ctx, string? artist, string? title, SessionStore store, JellyfinClient jf, DjAskService ask) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title)) return Results.BadRequest(new { error = "Send artist and title." });
            return Results.Json(new { path = ask.Landed(s, artist, title) });
        });
    }
}
