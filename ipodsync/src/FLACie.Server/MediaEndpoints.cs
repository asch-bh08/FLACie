using System.Text;
using System.Text.Json.Nodes;
using FLACie.Core;
using Microsoft.Net.Http.Headers;

namespace FLACie.Server;

/// <summary>
/// Audio and covers go through the server: Jellyfin with the user's token, NAS files over SMB, so the browser never
/// holds a credential. Audio supports range requests, so seeking never restarts a song. Only songs in the signed-in
/// user's own library are served.
/// </summary>
public static class MediaEndpoints
{
    static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp3"] = "audio/mpeg", ["flac"] = "audio/flac", ["m4a"] = "audio/mp4", ["aac"] = "audio/aac", ["ogg"] = "audio/ogg", ["opus"] = "audio/ogg",
        ["wav"] = "audio/wav", ["aiff"] = "audio/aiff", ["aif"] = "audio/aiff", ["alac"] = "audio/mp4", ["wma"] = "audio/x-ms-wma",
    };

    public static void MapMedia(this WebApplication app, string dataDir)
    {
        _ = Directory.CreateDirectory(Path.Combine(dataDir, "art"));
        // a page of album tiles asks for dozens of covers at once; a NAS answers a few at a time reliably
        var nasSlots = new SemaphoreSlim(4);

        // pre=1 marks the page's early read of the NEXT song (a second audio element, so a skip starts at once): background work that does not count as a play, is
        // never ahead of a song someone waits for, waits out a slow Jellyfin, and asks only for the start of the file (a small Range), never the whole song.
        app.MapGet("/stream", async (HttpContext ctx, string p, string? pre, SessionStore store, JellyfinClient jf, IHttpClientFactory hf, AlacCache alac, PlayTracker plays, Notifier notify, PlaybackActivity activity) =>
        {
            var s = store.For(ctx.User);
            var t = s.FindByPath(p);
            if (t is null) return Results.NotFound();
            var early = pre is "1" or "true";
            using var busy = early ? null : activity.BeginStream();   // background jobs wait while a song is being served
            using var prio = JellyfinGate.Use(early ? GatePriority.Prefetch : GatePriority.Playing);   // and the song being played goes to the front of every queue
            // where this play really comes from, decided here where the stream is resolved: Jellyfin plays are announced by Jellyfin's own watcher, so only
            // NAS files and the file mover's direct fallback (a song Jellyfin has not scanned yet) are ever reported as "music played"
            if (!early) plays.Served(s.Id, t.Path, t.Source switch { TrackSource.Nas => "NAS", TrackSource.Cloud => "direct", TrackSource.Jellyfin when notify.Current.JellyfinPlays => "Jellyfin", _ => null });
            async Task<IResult> Proxy(HttpRequestMessage req, bool ranges = true)
            {
                if (ranges && ctx.Request.Headers.Range.Count > 0) req.Headers.TryAddWithoutValidation("Range", early ? PrefetchRange(ctx.Request.Headers.Range.ToString()) : ctx.Request.Headers.Range.ToString());
                else if (ranges && early) req.Headers.TryAddWithoutValidation("Range", PrefetchRange(""));
                // the browser gave up (a skip, a closed tab): RequestAborted cancels the call to Jellyfin too, so it stops reading the file at once
                try
                {
                    using var res = await hf.CreateClient("media").SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
                    ctx.Response.StatusCode = (int)res.StatusCode;
                    foreach (var h in new[] { "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges" })
                    {
                        if (res.Content.Headers.TryGetValues(h, out var v) || res.Headers.TryGetValues(h, out v)) ctx.Response.Headers[h] = v.ToArray();
                    }
                    await using var body = await res.Content.ReadAsStreamAsync(ctx.RequestAborted);
                    await body.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
                }
                catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
                // Jellyfin is not answering (the gate's breaker is open): say so at once and when to try again, instead of an error page
                catch (JellyfinUnavailableException e) when (!ctx.Response.HasStarted)
                {
                    ctx.Response.StatusCode = 503;
                    ctx.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(e.Wait.TotalSeconds)).ToString();
                }
                catch (HttpRequestException) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 502; }
                return Results.Empty;
            }
            // Apple Lossless: browsers cannot decode it, so the server decodes it itself and sends WAV (any other .m4a goes on as it is)
            if (await AlacServe.TryServeAsync(ctx, s, t, jf, hf, alac)) return Results.Empty;
            if (t.Source == TrackSource.Jellyfin && s.Jellyfin is { } a)
            {
                // formats a browser cannot decode (WMA, APE ...) are converted to MP3 by Jellyfin on the way (no seeking inside them, but they play)
                var transcode = MediaInfo.NotPlayableInBrowsers.Contains(MediaInfo.FromExtension(t.FilePath ?? ""));
                using var req = jf.Authorized(a, transcode ? $"{a.Server}/Audio/{t.JellyfinId}/stream.mp3?AudioCodec=mp3&AudioBitRate=256000&MaxAudioChannels=2" : jf.StreamUrl(a, t.JellyfinId!));
                return await Proxy(req, !transcode);
            }
            // a song just downloaded, playing from the file mover before Jellyfin has scanned it (only that service, with the key kept here)
            if (t.Source == TrackSource.Cloud && s.Services is { FileMoverUrl.Length: > 0 } svc && t.Path.StartsWith(svc.FileMoverUrl.TrimEnd('/') + "/file?", StringComparison.Ordinal))
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, t.Path);
                req.Headers.TryAddWithoutValidation("X-Api-Key", svc.FileMoverKey);
                return await Proxy(req);
            }
            if (t.Source == TrackSource.Nas && s.Nas is { } n)
            {
                var rel = NasClient.RelPath(n, t.Path);
                long size;
                try { size = await Task.Run(() => NasClient.Size(n, rel)); } catch (FileNotFoundException) { return Results.NotFound(); }
                var (satisfiable, partial, from, to) = ByteRanges.Resolve(ctx.Request.Headers.Range.ToString(), size);
                if (!satisfiable) { ctx.Response.Headers.ContentRange = $"bytes */{size}"; return Results.StatusCode(416); }
                if (early && from == 0 && to - from + 1 > PrefetchBytes) { to = PrefetchBytes - 1; partial = true; }
                ctx.Response.StatusCode = partial ? 206 : 200;
                ctx.Response.ContentType = Mime.GetValueOrDefault(Path.GetExtension(rel).TrimStart('.'), "application/octet-stream");
                ctx.Response.ContentLength = to - from + 1;
                ctx.Response.Headers.AcceptRanges = "bytes";
                if (partial) ctx.Response.Headers.ContentRange = $"bytes {from}-{to}/{size}";
                if (to >= from) try { await NasClient.CopyRangeAsync(n, rel, from, to - from + 1, ctx.Response.Body, ctx.RequestAborted); } catch (OperationCanceledException) { }
                return Results.Empty;
            }
            return Results.NotFound();
        }).RequireAuthorization();

        // covers, cached on disk by key (shared between users: a cover is the same picture for everyone). A Jellyfin cover's key carries Jellyfin's image tag
        // ("jf<item>~<tag>"), so the picture behind a key never changes: it is fetched once, kept for good and cached by the browser for good. Rows get one fixed
        // small size (240 px WebP, quality 80, a request Jellyfin's own image cache can answer); only the Now Playing cover asks for the original (?s=full).
        var log = app.Logger;
        var covers = app.Services.GetRequiredService<CoverStore>();
        app.MapGet("/art/{key}", async (HttpContext ctx, string key, SessionStore store, JellyfinClient jf, IHttpClientFactory hf) =>
        {
            if (key.Length > 400 || key.Contains('/') || key.Contains('\\') || key.Contains("..")) return Results.BadRequest();
            // a cover a person chose (a new key every time, so it is kept for good)
            if (key.StartsWith("ov") && app.Services.GetRequiredService<CoverOverrides>().Find(key) is { } chosen)
            {
                ctx.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
                return Results.File(chosen.File, chosen.Mime);
            }
            var isJf = key.StartsWith("jf");
            var tagged = isJf && key.Contains('~');
            var full = isJf && ctx.Request.Query["s"] == "full";
            var variant = !isJf ? CoverStore.Other : full ? CoverStore.Full : CoverStore.Row;
            var flightKey = key + "|" + variant;
            // tag-keyed and picture-URL-keyed covers never change; any other is kept a day by the browser (then a 304 from the saved copy). A missing one is remembered only briefly.
            var Kept = tagged || Art.ExternalUrl(key) is not null ? "private, max-age=31536000, immutable" : "private, max-age=86400, stale-while-revalidate=604800";
            const string Briefly = "private, max-age=30";
            // a saved cover is never asked for again. A Jellyfin key carries the picture's image tag (a changed picture is a new key), so there is nothing to re-check;
            // only a key without a tag (kept by an old library cache) is trusted for three days, then fetched again.
            if (covers.Find(key, variant) is { } have && !(isJf && !tagged && File.GetLastWriteTimeUtc(have) < DateTime.UtcNow.AddDays(-3))) { ctx.Response.Headers.CacheControl = Kept; return Cover(have); }
            ctx.Response.Headers.CacheControl = Briefly;
            // a cover that was just looked for and not found is not looked for again at once (a page of tiles would ask again and again)
            if (artMisses.TryGetValue(flightKey, out var missedAt) && DateTime.UtcNow - missedAt < TimeSpan.FromMinutes(3)) return Results.NotFound();
            var s = store.For(ctx.User);
            // one lookup per cover however many tiles ask at once (single flight): a tile that is scrolled away leaves the others their cover, and when EVERY tile that
            // wanted it is gone (the browser aborted its requests) the lookup is cancelled: still queued at the gate, it never reaches Jellyfin; already sent, it stops.
            // The big Now Playing cover is the one cover a person waits for (the gate's top priority); rows and tiles are "visible".
            using var prio = JellyfinGate.Use(full ? GatePriority.Playing : GatePriority.Visible);
            async Task<CoverResult> Fetch(CancellationToken flight)
            {
                byte[]? bytes = null; var type = "image/jpeg"; var transient = false;
                var http = hf.CreateClient("media");   // no client timeout (it carries audio): every cover lookup gets its own, so a stalled server never holds a slot for good
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(flight);
                limit.CancelAfter(TimeSpan.FromSeconds(15));
                var ct = limit.Token;
                try
                {
                    if (isJf && s.Jellyfin is { } a)
                    {
                        var id = key[2..]; string? tag = null;
                        if (id.IndexOf('~') is var tx and > 0) { tag = id[(tx + 1)..]; id = id[..tx]; }
                        using var req = jf.Authorized(a, jf.ImageUrl(a, id, full ? 0 : 240, tag, webp: !full));
                        using var res = await http.SendAsync(req, ct);
                        if (res.IsSuccessStatusCode) { bytes = await res.Content.ReadAsByteArrayAsync(ct); type = res.Content.Headers.ContentType?.MediaType ?? "image/jpeg"; }
                        else transient = (int)res.StatusCode >= 500 || res.StatusCode == System.Net.HttpStatusCode.TooManyRequests;   // Jellyfin is struggling: not "this cover does not exist"
                    }
                    else if (Art.ExternalUrl(key) is { } external)
                    {
                        using var res = await http.GetAsync(external, ct);
                        if (res.IsSuccessStatusCode) bytes = await res.Content.ReadAsByteArrayAsync(ct);
                    }
                    else if (Art.ParseLookupKey(key) is var (la, lb, lt))
                    {
                        await nasSlots.WaitAsync(ct);
                        try { bytes = await OnlineCover(http, la, lb, lt, ct); } finally { nasSlots.Release(); }
                    }
                    else if (key.StartsWith("nf") && s.Nas is { } n)
                    {
                        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(key[2..].Replace('-', '+').Replace('_', '/')))).Split('\n');
                        if (parts.Length >= 4)
                        {
                            await nasSlots.WaitAsync(ct);
                            try { bytes = await Task.Run(() => { try { return NasClient.FolderCover(n, parts[0]) ?? NasClient.EmbeddedCover(n, parts[0]); } catch (Exception e) { log.LogDebug(e, "NAS cover failed"); return null; } }, ct); }
                            finally { nasSlots.Release(); }
                            bytes ??= await OnlineCover(http, parts[1], parts[2], parts[3], ct);
                        }
                    }
                }
                catch (OperationCanceledException) when (flight.IsCancellationRequested) { throw; }   // nobody wants it any more
                catch (OperationCanceledException) { log.LogDebug("cover lookup timed out for {Key}", key); transient = true; }
                catch (JellyfinUnavailableException) { transient = true; }   // the gate's breaker is open: Jellyfin has been failing
                catch (HttpRequestException) { }
                catch (FormatException) { }   // a key that is not valid base64 is just a cover that does not exist
                if (bytes is null)
                {
                    // a cover Jellyfin could not give right now (a 5xx, a timeout, an open breaker) is not remembered as missing: the next view asks again
                    if (transient) return new CoverResult(null, true);
                    if (artMisses.Count > 2000) artMisses.Clear();
                    artMisses[flightKey] = DateTime.UtcNow;
                    return new CoverResult(null, false);
                }
                return new CoverResult(await covers.SaveAsync(key, variant, bytes, type), false);
            }
            CoverResult got;
            try { got = await coverFlights.RunAsync(flightKey, Fetch, ctx.RequestAborted); }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { return Results.Empty; }
            if (got.File is not { } saved) return got.Transient ? Results.StatusCode(503) : Results.NotFound();
            ctx.Response.Headers.CacheControl = Kept;
            return Cover(saved);
        }).RequireAuthorization();
    }

    /// <summary>How much of the next song the early read asks for: enough for it to start at once, nowhere near the whole file.</summary>
    public const int PrefetchBytes = 1 << 20;

    /// <summary>The Range an early read of the next song is held to: the first <see cref="PrefetchBytes"/> (an open-ended or larger request from the start is cut to it).</summary>
    public static string PrefetchRange(string asked)
    {
        var cap = "bytes=0-" + (PrefetchBytes - 1);
        if (asked.Length == 0) return cap;
        var m = System.Text.RegularExpressions.Regex.Match(asked.Trim(), @"^bytes=0-(\d*)$");
        if (!m.Success) return asked;   // a read from elsewhere in the file (a seek of the prefetch element) is left as it is
        return m.Groups[1].Value.Length > 0 && long.TryParse(m.Groups[1].Value, out var to) && to < PrefetchBytes - 1 ? asked : cap;
    }

    /// <summary>The saved cover with a validator (ETag and Last-Modified), so a browser that asks again is answered 304 without a body.</summary>
    static IResult Cover(string file)
    {
        var fi = new FileInfo(file);
        if (!fi.Exists) return Results.NotFound();   // (cleared or evicted between the lookup and now)
        var tag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\"" + fi.Length.ToString("x") + "-" + fi.LastWriteTimeUtc.Ticks.ToString("x") + "\"");
        return Results.File(file, CoverStore.TypeOf(file), lastModified: new DateTimeOffset(fi.LastWriteTimeUtc), entityTag: tag);
    }

    /// <summary>What a cover lookup came to: the saved file, or nothing (<see cref="Transient"/>: Jellyfin could not answer right now, so it is not a cover that does not exist).</summary>
    sealed record CoverResult(string? File, bool Transient);

    static readonly SingleFlight<CoverResult> coverFlights = new();

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> artMisses = new();

    static string Pad(string b) => b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');

    static readonly System.Text.RegularExpressions.Regex Compilation = new(@"\b(now|hits?|best of|greatest|various|top \d+|mix|party|summer|workout|anthems?|ministry|essentials?|chart|collection|playlist|radio|karaoke|tribute)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>An album (or the song's album) cover from Deezer's public catalog, matched by artist. A song search returns the song on every release
    /// that carries it, "Now That's What I Call Music 96" included, so the song's own album wins, and compilations only when nothing else exists.</summary>
    static async Task<byte[]?> OnlineCover(HttpClient http, string artist, string album, string title, CancellationToken ct)
    {
        var hit = await OnlineAlbum(http, artist, album, title, ct);
        if (hit?.Cover is not { Length: > 0 } url) return null;
        try { return await http.GetByteArrayAsync(url, ct); } catch { return null; }
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Album, string Cover)?> albumCache = new();

    /// <summary>The song's own album in the public catalog (title and cover), for songs whose file carries a compilation or a wrong album.</summary>
    public static async Task<(string Album, string Cover)?> OnlineAlbum(HttpClient http, string artist, string album, string title, CancellationToken ct)
    {
        var ck = Matching.MatchKey(title, artist) + "|" + Matching.AlbumNorm(album);
        if (albumCache.TryGetValue(ck, out var cached)) return cached;
        var found = await OnlineAlbumUncached(http, artist, album, title, ct);
        if (albumCache.Count > 5000) albumCache.Clear();
        albumCache[ck] = found;
        return found;
    }

    static async Task<(string Album, string Cover)?> OnlineAlbumUncached(HttpClient http, string artist, string album, string title, CancellationToken ct)
    {
        if (artist.Length == 0) return null;
        // "Me Against the World (1995)", "Album [Deluxe]": the bracketed part only hurts a catalog search
        album = System.Text.RegularExpressions.Regex.Replace(album, @"\s*[(\[][^)\]]*[)\]]", "").Trim();
        var want = Matching.PrimaryArtist(artist);
        foreach (var (ep, q) in new[] { ("search/album", $"{artist} {album}"), ("search", $"{artist} {title}") })
        {
            if (q.Trim() == artist) continue;
            try
            {
                var j = JsonNode.Parse(await http.GetStringAsync($"https://api.deezer.com/{ep}?q={Uri.EscapeDataString(q)}&limit=25", ct));
                var mine = (j?["data"]?.AsArray() ?? []).OfType<JsonObject>()
                    .Where(o => Matching.PrimaryArtist(o["artist"]?["name"]?.GetValue<string>() ?? "") is { Length: > 0 } a && (a.Contains(want) || want.Contains(a))).ToList();
                int Score(JsonObject o)
                {
                    var alTitle = ep == "search" ? o["album"]?["title"]?.GetValue<string>() ?? "" : o["title"]?.GetValue<string>() ?? "";
                    var s = 0;
                    if (album.Length > 0 && Matching.AlbumNorm(alTitle) == Matching.AlbumNorm(album)) s += 100;
                    if (Compilation.IsMatch(alTitle) && !Compilation.IsMatch(album)) s -= 50;
                    if (ep == "search" && Matching.NormTitle(o["title"]?.GetValue<string>() ?? "") == Matching.NormTitle(title)) s += 10;
                    return s;
                }
                // a result that matches neither the album nor the song is another release by the artist: no cover is better than a wrong one
                var hit = mine.Where(o => Score(o) >= 10 || (album.Length == 0 && ep == "search/album")).OrderByDescending(Score).FirstOrDefault();
                var url = hit?["cover_big"]?.GetValue<string>() ?? hit?["album"]?["cover_big"]?.GetValue<string>();
                var name = ep == "search" ? hit?["album"]?["title"]?.GetValue<string>() : hit?["title"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(url)) return (name ?? "", url);
            }
            catch { }
        }
        return null;
    }
}
