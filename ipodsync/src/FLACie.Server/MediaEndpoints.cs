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
        // catalog covers wait for their own slots, not the NAS's four: a page of catalog covers queued behind four at a time and ran out of time (owner, 5 Nov:
        // covers "take ages to load", many stayed blank)
        var catalogSlots = new SemaphoreSlim(8);

        // pre=1 marks the page's early read of the NEXT song (a second audio element, so a skip starts at once): background work that does not count as a play, is
        // never ahead of a song someone waits for, waits out a slow Jellyfin, and asks only for the start of the file (a small Range), never the whole song.
        app.MapGet("/stream", async (HttpContext ctx, string p, string? pre, string? wave, SessionStore store, JellyfinClient jf, IHttpClientFactory hf, AlacCache alac, PlayTracker plays, Notifier notify, PlaybackActivity activity, SongHeadCache heads) =>
        {
            var s = store.For(ctx.User);
            var t = s.FindByPath(p);
            if (t is null) return Results.NotFound();
            var early = pre is "1" or "true";
            var quiet = wave is "1";   // the seek bar's shape being worked out: the whole file, but not a play and behind any song someone is listening to
            // the start of this song kept by an early read, waiting (up to 4 s) for one still on its way: before this request counts as a song being served,
            // which would hold that early read back
            var kept = early || quiet ? null : await heads.GetAsync(s.Id, t.Path, TimeSpan.FromSeconds(4));
            using var busy = early || quiet ? null : activity.BeginStream();   // background jobs wait while a song is being served
            using var prio = JellyfinGate.Use(early || quiet ? GatePriority.Prefetch : GatePriority.Playing);   // and the song being played goes to the front of every queue
            // where this play really comes from, decided here where the stream is resolved: Jellyfin plays are announced by Jellyfin's own watcher, so only
            // NAS files and the file mover's direct fallback (a song Jellyfin has not scanned yet) are ever reported as "music played"
            if (!early && !quiet && !notify.PlaysQuietFor(s.DisplayName)) plays.Served(s.Id, t.Path, t.Source switch { TrackSource.Nas => "NAS", TrackSource.Cloud => "direct", TrackSource.Jellyfin when notify.Current.JellyfinPlays => "Jellyfin", _ => null });
            async Task<IResult> Proxy(HttpRequestMessage req, bool ranges = true, bool keepHead = false)
            {
                if (ranges && ctx.Request.Headers.Range.Count > 0) req.Headers.TryAddWithoutValidation("Range", early ? PrefetchRange(ctx.Request.Headers.Range.ToString(), PrefetchBytesFor(t)) : ctx.Request.Headers.Range.ToString());
                else if (ranges && early) req.Headers.TryAddWithoutValidation("Range", PrefetchRange("", PrefetchBytesFor(t)));
                // the browser gave up (a skip, a closed tab): RequestAborted cancels the call to Jellyfin too, so it stops reading the file at once. Not for the early
                // read of the next song: the browser's hidden player often stops reading once it has enough, and the start must still be kept (its own 30 s limit)
                using var own = keepHead ? new CancellationTokenSource(TimeSpan.FromSeconds(30)) : null;
                var upstream = own?.Token ?? ctx.RequestAborted;
                try
                {
                    using var res = await hf.CreateClient("media").SendAsync(req, HttpCompletionOption.ResponseHeadersRead, upstream);
                    ctx.Response.StatusCode = (int)res.StatusCode;
                    foreach (var h in new[] { "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges" })
                    {
                        if (res.Content.Headers.TryGetValues(h, out var v) || res.Headers.TryGetValues(h, out v)) ctx.Response.Headers[h] = v.ToArray();
                    }
                    await using var body = await res.Content.ReadAsStreamAsync(upstream);
                    if (keepHead && TotalOf(res) is long total && res.Content.Headers.ContentRange?.From is null or 0)
                    {
                        // the early read of the next song: its start is kept, so the real request sends it at once (SongHeadCache); read to the end whether or
                        // not the browser is still taking it
                        using var kept = new MemoryStream();
                        var buf = new byte[81920]; int n; var browser = true;
                        while ((n = await body.ReadAsync(buf, upstream)) > 0)
                        {
                            kept.Write(buf, 0, n);
                            if (browser) try { await ctx.Response.Body.WriteAsync(buf.AsMemory(0, n), ctx.RequestAborted); } catch (Exception) { browser = false; }
                        }
                        heads.Put(s.Id, t.Path, kept.ToArray(), total, res.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
                    }
                    else await body.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
                }
                catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested || own?.IsCancellationRequested == true) { }
                // Jellyfin is not answering (the gate's breaker is open): say so at once and when to try again, instead of an error page
                catch (JellyfinUnavailableException e) when (!ctx.Response.HasStarted)
                {
                    ctx.Response.StatusCode = 503;
                    ctx.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(e.Wait.TotalSeconds)).ToString();
                }
                catch (HttpRequestException) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 502; }
                return Results.Empty;
            }
            async Task<IResult> FromHead(SongHeadCache.Head head, SongHeadCache.Served plan, Func<HttpRequestMessage> rest)
            {
                ctx.Response.StatusCode = plan.Partial ? 206 : 200;
                ctx.Response.ContentType = head.ContentType;
                ctx.Response.ContentLength = plan.To - plan.From + 1;
                ctx.Response.Headers.AcceptRanges = "bytes";
                if (plan.Partial) ctx.Response.Headers.ContentRange = $"bytes {plan.From}-{plan.To}/{head.Total}";
                try
                {
                    await ctx.Response.Body.WriteAsync(head.Bytes.AsMemory((int)plan.HeadFrom, (int)(plan.HeadTo - plan.HeadFrom + 1)), ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                    if (plan.UpFrom < 0) return Results.Empty;
                    using var req = rest();
                    req.Headers.TryAddWithoutValidation("Range", $"bytes={plan.UpFrom}-{plan.UpTo}");
                    using var res = await hf.CreateClient("media").SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
                    // Jellyfin must send exactly the rest; anything else (a whole file, an error) can't be joined on: the connection is dropped and the browser asks again
                    if ((int)res.StatusCode != 206 || res.Content.Headers.ContentRange?.From != plan.UpFrom) { ctx.Abort(); return Results.Empty; }
                    await using var body = await res.Content.ReadAsStreamAsync(ctx.RequestAborted);
                    await body.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
                }
                catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
                catch (Exception) { ctx.Abort(); }
                return Results.Empty;
            }
            // Apple Lossless: browsers cannot decode it, so the server decodes it itself and sends WAV (any other .m4a goes on as it is)
            if (await AlacServe.TryServeAsync(ctx, s, t, jf, hf, alac)) return Results.Empty;
            if (t.Source == TrackSource.Jellyfin && s.Jellyfin is { } a)
            {
                // formats a browser cannot decode (WMA, APE ...) are converted to MP3 by Jellyfin on the way (no seeking inside them, but they play)
                var transcode = MediaInfo.NotPlayableInBrowsers.Contains(MediaInfo.FromExtension(t.FilePath ?? ""));
                // the start of this song was kept by the early read: sent at once, the rest asked of Jellyfin from where it ends
                if (!transcode && kept is { } head && SongHeadCache.Plan(ctx.Request.Headers.Range.ToString(), head.Bytes.Length, head.Total) is { } plan)
                    return await FromHead(head, plan, () => jf.Authorized(a, jf.StreamUrl(a, t.JellyfinId!)));
                using var req = jf.Authorized(a, transcode ? $"{a.Server}/Audio/{t.JellyfinId}/stream.mp3?AudioCodec=mp3&AudioBitRate=256000&MaxAudioChannels=2" : jf.StreamUrl(a, t.JellyfinId!));
                var keep = early && !transcode;
                var ended = keep ? heads.Begin(s.Id, t.Path) : null;
                try { return await Proxy(req, !transcode, keepHead: keep); } finally { ended?.Invoke(); }
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
                if (early && from == 0 && to - from + 1 > PrefetchBytesFor(t)) { to = PrefetchBytesFor(t) - 1; partial = true; }
                ctx.Response.StatusCode = partial ? 206 : 200;
                ctx.Response.ContentType = Mime.GetValueOrDefault(Path.GetExtension(rel).TrimStart('.'), "application/octet-stream");
                ctx.Response.ContentLength = to - from + 1;
                ctx.Response.Headers.AcceptRanges = "bytes";
                if (partial) ctx.Response.Headers.ContentRange = $"bytes {from}-{to}/{size}";
                if (to >= from) try { await NasClient.CopyRangeAsync(n, rel, from, to - from + 1, ctx.Response.Body, ctx.RequestAborted); } catch (OperationCanceledException) { }
                return Results.Empty;
            }
            return Results.NotFound();
        }).RequireAuthorization().RateLimited("stream");

        // the Living Sleeve seek bar's shape: how loud each part of a song is (200 bars, 0-255). Jellyfin servers can't always convert a song (transcoding off,
        // or no ffmpeg), so the first browser to play a song works the shape out from the audio it already decodes and sends it here; it is kept on disk
        // for everyone, keyed by the Jellyfin item (or a hash of the file's address for other sources). GET: the shape, or 404 when nobody has sent it yet.
        var waveDir = Directory.CreateDirectory(Path.Combine(app.Configuration["FLACIE_DATA"] ?? Path.Combine(AppContext.BaseDirectory, "data"), "waves")).FullName;
        // the Android app sends its own address for a Jellyfin song, which can name the server differently (a LAN address, a public one) or carry other
        // query parts, so FindByPath misses it: a ".../Audio/{32 hex}/stream" path is keyed by its Jellyfin item all the same
        static string? JellyfinItem(string p) => System.Text.RegularExpressions.Regex.Match(p, "/Audio/([0-9a-fA-F]{32})/stream") is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : null;
        string? WaveFile(UserSession s, string p) => s.FindByPath(p) is { } t
            ? Path.Combine(waveDir, (t.JellyfinId is { Length: 32 } id && id.All(Uri.IsHexDigit) ? id : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(t.Path)))) + ".bin")
            : JellyfinItem(p) is { } item ? Path.Combine(waveDir, item + ".bin") : null;
        // the song a shape is asked for, from the web's path or the app's Jellyfin address
        Track? WaveTrack(UserSession s, string p) => s.FindByPath(p) ?? (JellyfinItem(p) is { } item ? s.Library.ByJellyfinId(item) : null);
        // known=1: only a shape already worked out (the page looking ahead to the next songs): never read a whole file for it, or a burst of skips read dozens
        app.MapGet("/api/wave", async (HttpContext ctx, string p, string? known, SessionStore store, JellyfinClient jf, IHttpClientFactory hf) =>
        {
            // a signed-in browser, or the phone app by its token (it asks for the shape of every song it plays)
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (WaveFile(s, p) is not { } file) return known is "1" ? Results.NoContent() : Results.NotFound();
            if (File.Exists(file))
            {
                ctx.Response.Headers.CacheControl = "private, max-age=86400";
                return Results.Json((await File.ReadAllBytesAsync(file)).Select(b => (int)b));
            }
            // nobody has sent the exact shape yet: for a FLAC file the server works out a rough one from the file's frames (WaveShape) and keeps it, so a phone,
            // which cannot decode a whole Hi-Res file, still shows the song's shape. It is marked rough; a browser that can decode the song sends the exact one.
            var rough = file + ".rough";
            if (known is "1" && !File.Exists(rough)) return Results.NoContent();   // "not yet": no 404, which a browser logs as an error
            if (!File.Exists(rough) && WaveTrack(s, p) is { } t && MediaInfo.FromExtension(t.FilePath ?? t.Path) == "flac"
                && await ReadWhole(s, t, jf, hf, ctx.RequestAborted) is { } bytes && WaveShape.FromFlac(bytes) is { } bars)
                await File.WriteAllBytesAsync(rough, bars.Select(b => (byte)b).ToArray());
            if (!File.Exists(rough)) return Results.NotFound();
            ctx.Response.Headers["X-Wave"] = "rough";
            return Results.Json((await File.ReadAllBytesAsync(rough)).Select(b => (int)b));
        }).RateLimited("wave");
        app.MapPost("/api/wave", async (HttpContext ctx, string p, SessionStore store, JellyfinClient jf) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } caller) return Results.Unauthorized();
            if (WaveFile(caller, p) is not { } file) return Results.NotFound();
            int[]? bars;
            try { bars = await System.Text.Json.JsonSerializer.DeserializeAsync<int[]>(ctx.Request.Body); } catch (System.Text.Json.JsonException) { bars = null; }
            if (bars is not { Length: 200 } || bars.Any(b => b is < 0 or > 255)) return Results.BadRequest();
            if (!File.Exists(file)) await File.WriteAllBytesAsync(file, bars.Select(b => (byte)b).ToArray());   // the first shape sent is kept
            return Results.NoContent();
        }).DisableAntiforgery().RateLimited("wave");

        // sharing a song or a playlist (the phone app asks here; the web makes shares itself): its songs by title and artist, kept under a random id, and
        // the link to send (/s/{id}); whoever opens it signs in as themselves and plays the songs from their own library
        app.MapPost("/api/share", async (HttpContext ctx, ShareStore shares, SessionStore store, JellyfinClient jf) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            ShareRequest? r;
            try { r = await System.Text.Json.JsonSerializer.DeserializeAsync<ShareRequest>(ctx.Request.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (System.Text.Json.JsonException) { r = null; }
            var entries = (r?.Entries ?? []).Where(e => !string.IsNullOrWhiteSpace(e.T)).Select(e => new PlaylistEntry(e.P ?? "", e.T!.Trim(), (e.A ?? "").Trim())).ToList();
            if (r is null || entries.Count == 0) return Results.BadRequest(new { error = "Nothing to share." });
            var id = shares.Create(r.Kind ?? "playlist", string.IsNullOrWhiteSpace(r.Name) ? entries[0].Title : r.Name, s.Jellyfin?.UserName ?? ctx.User.Identity?.Name ?? "Someone", entries);
            return Results.Json(new { id, url = $"{ctx.Request.Scheme}://{ctx.Request.Host}/s/{id}" });
        }).DisableAntiforgery().RateLimited("share");

        // the iPod desk's disc pictures: a release's own "Medium" picture from the Cover Art Archive, fetched here (the browser only ever asks FLACie) and kept for
        // everyone; only for a release someone matched on MusicBrainz. No picture: 404, and the desk prints a disc from the cover.
        app.MapGet("/desk/disc/{release}", async (HttpContext ctx, string release, MbOverlay choices, DeskDiscPictures discs) =>
        {
            if (!DeskDiscPictures.ValidId(release) || choices.ByRelease(release) is not { } choice) return Results.NotFound();
            DiscPicture? pic;
            try { pic = await discs.GetAsync(release, DeskDiscPictures.ValidId(choice.ReleaseGroupMbid) ? choice.ReleaseGroupMbid : null, ctx.RequestAborted); }
            catch (OperationCanceledException) { return Results.Empty; }
            ctx.Response.Headers.CacheControl = pic is null ? "private, max-age=600" : "private, max-age=604800";
            return pic is null ? Results.NotFound() : Results.Bytes(pic.Bytes, pic.Mime);
        }).RequireAuthorization().RateLimited("art");

        // covers, cached on disk by key (shared between users: a cover is the same picture for everyone). A Jellyfin cover's key carries Jellyfin's image tag
        // ("jf<item>~<tag>"), so the picture behind a key never changes: it is fetched once, kept for good and cached by the browser for good. Rows get one fixed
        // small size (240 px WebP, quality 80, a request Jellyfin's own image cache can answer); only the Now Playing cover asks for the original (?s=full).
        var log = app.Logger;
        var covers = app.Services.GetRequiredService<CoverStore>();
        var artistCatalog = app.Services.GetService<WebCatalog>();
        Task<(string? Url, bool Unasked)> ArtistPicture(string artist, CancellationToken c) => artistCatalog?.ArtistPictureAsync(artist, c) ?? Task.FromResult<(string?, bool)>((null, false));
        app.MapGet("/art/{key}", async (HttpContext ctx, string key, SessionStore store, JellyfinClient jf, IHttpClientFactory hf) =>
        {
            if (key.Length > 400 || key.Contains('/') || key.Contains('\\') || key.Contains("..")) return Results.BadRequest();
            // the signed-in browser or the phone by its Jellyfin token (the phone was sent to the sign-in page and took that page for its cover)
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            key = FLACie.Core.CoverOverrides.Unversioned(key);   // a catalog cover's address carries the lookup rules ("~r2"): the key itself does not
            // a catalog key may carry a Jellyfin picture for when the catalog has nothing (a hoodtrap remix in no catalog shows its folder's picture; owner, 5 Nov).
            // The lookup and the saved cover use the key without it.
            // F1: the chain (catalog, folder picture, artist photo) is planned from the key with its fallbacks; it is saved under the key without them
            var chain = CoverChain.Plan(key);
            string? fallback = chain.Count > 0 ? null : Art.LookupFallback(key);
            if (Art.LookupFallbacks(key).Length > 0) key = Art.LookupBase(key);
            IResult Missing(bool transient)
            {
                var (status, cache) = CoverAnswers.Missing(transient);
                ctx.Response.Headers.CacheControl = cache;
                // a made cover only for a key that names a song's cover (a catalog, Jellyfin or NAS key); any other address that came to nothing is a 404
                if (!transient && fallback is null && !GeneratedCover.ForKey(key)) return Results.NotFound();
                switch (GeneratedCover.Answer(transient, fallback is not null))
                {
                    case CoverMiss.Fallback: return Results.Redirect("/art/" + Uri.EscapeDataString(fallback!) + "?v=3");
                    case CoverMiss.Generated:
                        // F1: every source failed: a cover made from the artist and title, never a blank or a 404
                        ctx.Response.Headers.CacheControl = GeneratedCover.CacheControl;
                        var (ga, gt) = GeneratedCover.Names(key);
                        // the phone asks ?fmt=png (it cannot draw SVG): the same cover as a PNG, at the row size or the player's (?s=full)
                        if (ctx.Request.Query["fmt"] == "png") return Results.File(CoverPng.Make(ga, gt, ctx.Request.Query["s"] == "full" ? GeneratedCover.FullPx : GeneratedCover.RowPx), "image/png");
                        return Results.Text(GeneratedCover.Svg(ga, gt), "image/svg+xml");
                    default: return Results.StatusCode(status);
                }
            }
            // a cover a person chose (a new key every time, so it is kept for good)
            if (key.StartsWith("ov") && app.Services.GetRequiredService<CoverOverrides>().Find(key) is { } chosen)
            {
                ctx.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
                return Results.File(chosen.File, chosen.Mime);
            }
            var isJf = key.StartsWith("jf");
            // a song downloaded from search: the cover it was found with, not the picture inside its file (the phone asks by the Jellyfin key; owner, 6 Nov)
            if (isJf && DownloadCovers.Current is { } dc && JellyfinSongOf(s, key) is { } song && dc.For(song) is { } chosenKey)
                return Results.Redirect("/art/" + Uri.EscapeDataString(chosenKey) + ctx.Request.QueryString.Value);
            var tagged = isJf && key.Contains('~');
            var external = Art.ExternalUrl(key);
            // a catalogue cover comes in two sizes too: 250 px for rows and cards, the big one only for the player
            var full = (isJf || external is not null) && ctx.Request.Query["s"] == "full";
            var variant = !isJf && external is null ? CoverStore.Other : full ? CoverStore.Full : CoverStore.Row;
            var flightKey = key + "|" + variant;
            // tag-keyed and picture-URL-keyed covers never change; any other is kept a day by the browser (then a 304 from the saved copy). A missing one is remembered only briefly.
            var Kept = tagged || Art.ExternalUrl(key) is not null ? "private, max-age=31536000, immutable" : "private, max-age=86400, stale-while-revalidate=604800";
            const string Briefly = "private, max-age=30";
            // a saved cover is never asked for again. A Jellyfin key carries the picture's image tag (a changed picture is a new key), so there is nothing to re-check;
            // only a key without a tag (kept by an old library cache) is trusted for three days, then fetched again.
            if (covers.Find(key, variant) is { } have && !(isJf && !tagged && File.GetLastWriteTimeUtc(have) < DateTime.UtcNow.AddDays(-3))) { ctx.Response.Headers.CacheControl = Kept; return Cover(have); }
            ctx.Response.Headers.CacheControl = Briefly;
            // a cover that was just looked for and not found is not looked for again at once (a page of tiles would ask again and again)
            if (artMisses.TryGet(flightKey, out _)) return Missing(false);
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
                    else if (external is not null)
                    {
                        using var res = await http.GetAsync(full ? external : Art.SmallExternal(external), ct);
                        if (res.IsSuccessStatusCode) bytes = await res.Content.ReadAsByteArrayAsync(ct);
                    }
                    else if (chain.Count > 0)
                    {
                        // F1: the catalog, the folder's picture, the artist's photo on Jellyfin, then the catalog's photo of the artist
                        async Task<(byte[]?, bool)> JfImage(string id, string? tag, CancellationToken c)
                        {
                            if (s.Jellyfin is not { } acc) return (null, false);
                            using var req = jf.Authorized(acc, jf.ImageUrl(acc, id, full ? 0 : 240, tag, webp: !full));
                            using var res = await http.SendAsync(req, c);
                            if (res.IsSuccessStatusCode) return (await res.Content.ReadAsByteArrayAsync(c), false);
                            return (null, (int)res.StatusCode >= 500 || res.StatusCode == System.Net.HttpStatusCode.TooManyRequests);
                        }
                        async Task<(byte[]?, bool)> Step(CoverStep st, CancellationToken c)
                        {
                            switch (st.Kind)
                            {
                                case CoverStep.Kinds.Catalog:
                                    await catalogSlots.WaitAsync(c);
                                    try { return await OnlineCover(http, st.Value, st.Album, st.Title, c); } finally { catalogSlots.Release(); }
                                case CoverStep.Kinds.JellyfinPicture:
                                    var pid = st.Value[2..]; string? ptag = null;
                                    if (pid.IndexOf('~') is var px and > 0) { ptag = pid[(px + 1)..]; pid = pid[..px]; }
                                    return await JfImage(pid, ptag, c);
                                case CoverStep.Kinds.JellyfinArtist:
                                    return await JfImage(st.Value, null, c);
                                default:
                                    await catalogSlots.WaitAsync(c);
                                    try
                                    {
                                        var (url, unasked) = await ArtistPicture(st.Value, c);
                                        if (url is null) return (null, unasked);
                                        using var res = await http.GetAsync(url, c);
                                        return res.IsSuccessStatusCode ? (await res.Content.ReadAsByteArrayAsync(c), false) : (null, true);
                                    }
                                    finally { catalogSlots.Release(); }
                            }
                        }
                        (bytes, _, transient) = await CoverChain.RunAsync(chain, Step, ct);
                    }
                    else if (key.StartsWith("nf") && s.Nas is { } n)
                    {
                        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(key[2..].Replace('-', '+').Replace('_', '/')))).Split('\n');
                        if (parts.Length >= 4)
                        {
                            await nasSlots.WaitAsync(ct);
                            try { bytes = await Task.Run(() => { try { return NasClient.FolderCover(n, parts[0]) ?? NasClient.EmbeddedCover(n, parts[0]); } catch (Exception e) { log.LogDebug(e, "NAS cover failed"); return null; } }, ct); }
                            finally { nasSlots.Release(); }
                            if (bytes is null)
                            {
                                await catalogSlots.WaitAsync(ct);
                                try { (bytes, transient) = await OnlineCover(http, parts[1], parts[2], parts[3], ct); } finally { catalogSlots.Release(); }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (flight.IsCancellationRequested) { throw; }   // nobody wants it any more
                catch (OperationCanceledException) { log.LogDebug("cover lookup timed out for {Key}", key); transient = true; }
                catch (JellyfinUnavailableException) { transient = true; }   // the gate's breaker is open: Jellyfin has been failing
                catch (HttpRequestException) { transient = true; }   // a connection that failed says nothing about whether the cover exists
                catch (FormatException) { }   // a key that is not valid base64 is just a cover that does not exist
                if (bytes is null)
                {
                    // a cover Jellyfin could not give right now (a 5xx, a timeout, an open breaker) is not remembered as missing: the next view asks again
                    // nor is a catalog lookup that was cut short (owner, 5 Nov): only a real "nothing" is remembered, for an hour
                    if (transient) return new CoverResult(null, true);
                    artMisses.Set(flightKey, true, CoverAnswers.MissKept);
                    return new CoverResult(null, false);
                }
                return new CoverResult(await covers.SaveAsync(key, variant, bytes, type), false);
            }
            CoverResult got;
            try { got = await coverFlights.RunAsync(flightKey, Fetch, ctx.RequestAborted); }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { return Results.Empty; }
            if (got.File is not { } saved) return Missing(got.Transient);
            ctx.Response.Headers.CacheControl = Kept;
            return Cover(saved);
        }).RateLimited("art");
    }

    /// <summary>The song whose own picture a Jellyfin cover key ("jf" + item id [+ "~" + tag]) is, or null (an album's picture, or not in the library).</summary>
    static Track? JellyfinSongOf(UserSession s, string key) => key.Length > 2 ? s.Library.ByJellyfinId(key[2..].Split('~')[0]) : null;

    /// <summary>How much of the next song the early read asks for: enough for it to start at once, nowhere near the whole file.</summary>
    public const int PrefetchBytes = 1 << 20;
    /// <summary>F4.2: how much of the next song is read ahead: its first <see cref="AheadSeconds"/> (from its size and length), at least
    /// <see cref="PrefetchBytes"/> and at most <see cref="MaxAheadBytes"/>; 4 MB (about 30 s of CD-quality FLAC) when its size is not known.</summary>
    public const int AheadSeconds = 30, MaxAheadBytes = 8 << 20, UnknownAheadBytes = 4 << 20;
    public static int PrefetchBytesFor(Track t) =>
        t.Size > 0 && t.DurationMs > 0 ? (int)Math.Clamp(t.Size * (AheadSeconds * 1000L) / t.DurationMs, PrefetchBytes, MaxAheadBytes) : UnknownAheadBytes;

    /// <summary>A song's full size from Jellyfin's answer: the total of a 206's Content-Range, or a 200's length.</summary>
    static long? TotalOf(HttpResponseMessage res) =>
        res.Content.Headers.ContentRange?.Length ?? ((int)res.StatusCode == 200 ? res.Content.Headers.ContentLength : null);

    /// <summary>The Range an early read of the next song is held to: its first [bytes] (an open-ended or larger request from the start is cut to it).</summary>
    public static string PrefetchRange(string asked) => PrefetchRange(asked, PrefetchBytes);

    public static string PrefetchRange(string asked, int bytes)
    {
        var cap = "bytes=0-" + (bytes - 1);
        if (asked.Length == 0) return cap;
        var m = System.Text.RegularExpressions.Regex.Match(asked.Trim(), @"^bytes=0-(\d*)$");
        if (!m.Success) return asked;   // a read from elsewhere in the file (a seek of the prefetch element) is left as it is
        return m.Groups[1].Value.Length > 0 && long.TryParse(m.Groups[1].Value, out var to) && to < bytes - 1 ? asked : cap;
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

    static readonly TimedLru<string, bool> artMisses = new(4000);

    static string Pad(string b) => b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');

    static readonly System.Text.RegularExpressions.Regex Compilation = new(@"\b(now|hits?|best of|greatest|various|top \d+|mix|party|summer|workout|anthems?|ministry|essentials?|chart|collection|playlist|radio|karaoke|tribute)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>An album (or the song's album) cover from Deezer's public catalog, matched by artist. A song search returns the song on every release
    /// that carries it, "Now That's What I Call Music 96" included, so the song's own album wins, and compilations only when nothing else exists.</summary>
    /// <summary>A whole song file read on the server (for its seek-bar shape): from Jellyfin (behind any song being played) or the NAS; null over 300 MB or on any failure.</summary>
    internal static async Task<byte[]?> ReadWhole(UserSession s, Track t, JellyfinClient jf, IHttpClientFactory hf, CancellationToken ct)
    {
        const long Max = 300L << 20;
        try
        {
            if (t.Source == TrackSource.Jellyfin && s.Jellyfin is { } a && t.JellyfinId is { Length: > 0 })
            {
                using var prio = JellyfinGate.Use(GatePriority.Prefetch);
                using var req = jf.Authorized(a, jf.StreamUrl(a, t.JellyfinId));
                using var res = await hf.CreateClient("media").SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!res.IsSuccessStatusCode || res.Content.Headers.ContentLength > Max) return null;
                return await res.Content.ReadAsByteArrayAsync(ct);
            }
            if (t.Source == TrackSource.Nas && s.Nas is { } n)
            {
                var rel = NasClient.RelPath(n, t.Path);
                var size = await Task.Run(() => NasClient.Size(n, rel), ct);
                if (size > Max) return null;
                using var ms = new MemoryStream((int)size);
                await NasClient.CopyRangeAsync(n, rel, 0, size, ms, ct);
                return ms.ToArray();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { }
        return null;
    }

    /// <summary>A catalog cover's picture, and whether it could not be had right now (<c>Transient</c>: then it is not a cover that does not exist).</summary>
    static async Task<(byte[]? Bytes, bool Transient)> OnlineCover(HttpClient http, string artist, string album, string title, CancellationToken ct)
    {
        var got = await CatalogCover(http, artist, album, title, ct);
        return (got.Bytes, got.Answer == CatalogAnswer.Unasked);
    }

    /// <summary>A catalog cover by artist, album and title: the picture (Found), a real "the catalog has nothing" (Nothing), or Unasked when the catalog could not
    /// be asked or the lookup was cut short, including a found cover whose picture would not download (owner, 5 Nov: those were remembered as "no cover").</summary>
    public static async Task<(byte[]? Bytes, CatalogAnswer Answer)> CatalogCover(HttpClient http, string artist, string album, string title, CancellationToken ct, RetryPolicy? retry = null)
    {
        var (hit, unasked) = await OnlineAlbumAsk(http, artist, album, title, ct, retry ?? RetryPolicy.Quick);
        if (hit?.Cover is not { Length: > 0 } url) return (null, unasked ? CatalogAnswer.Unasked : CatalogAnswer.Nothing);
        try
        {
            using var res = await HttpRetry.SendAsync(http, () => new HttpRequestMessage(HttpMethod.Get, url), retry ?? RetryPolicy.Quick, ct);
            if (res.IsSuccessStatusCode) return (await res.Content.ReadAsByteArrayAsync(ct), CatalogAnswer.Found);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested) { }
        return (null, CatalogAnswer.Unasked);
    }

    /// <summary>Catalog answers by song: a found album (its cover's address) is kept a day, so a cover seen again is not searched for again; a real "nothing" an hour
    /// (owner, 5 Nov: it used to be kept until the server restarted); one that could not be asked is not kept at all.</summary>
    static readonly TimedLru<string, (string Album, string Cover)?> albumCache = new(4000);

    /// <summary>How long one catalog question may take: the "media" client has no time limit of its own (it carries audio), and a caller may pass none.</summary>
    static readonly TimeSpan OnlineAsk = TimeSpan.FromSeconds(12);

    /// <summary>The song's own album in the public catalog (title and cover), for songs whose file carries a compilation or a wrong album. A catalog that
    /// hiccups is asked again (HttpRetry); one that could not be asked at all is not remembered as "no such album", so the next view asks again.</summary>
    public static async Task<(string Album, string Cover)?> OnlineAlbum(HttpClient http, string artist, string album, string title, CancellationToken ct, RetryPolicy? retry = null) =>
        (await OnlineAlbumAsk(http, artist, album, title, ct, retry ?? RetryPolicy.Quick)).Found;

    static async Task<((string Album, string Cover)? Found, bool Unasked)> OnlineAlbumAsk(HttpClient http, string artist, string album, string title, CancellationToken ct, RetryPolicy retry)
    {
        var ck = Matching.MatchKey(title, artist) + "|" + Matching.AlbumNorm(album);
        if (albumCache.TryGet(ck, out var cached)) return (cached, false);
        var (found, unasked) = await OnlineAlbumUncached(http, artist, album, title, ct, retry);
        if (found is null && unasked) return (null, true);
        albumCache.Set(ck, found, found is null ? CoverAnswers.MissKept : TimeSpan.FromDays(1));
        return (found, false);
    }

    /// <summary>The album, and whether a catalog question could not be answered at all (then a "none" is not to be remembered).</summary>
    static async Task<((string Album, string Cover)? Found, bool Unasked)> OnlineAlbumUncached(HttpClient http, string artist, string album, string title, CancellationToken ct, RetryPolicy retry)
    {
        if (artist.Length == 0) return (null, false);
        var unasked = false;
        // "Me Against the World (1995)", "Album [Deluxe]": the bracketed part only hurts a catalog search
        album = System.Text.RegularExpressions.Regex.Replace(album, @"\s*[(\[][^)\]]*[)\]]", "").Trim();
        var want = Matching.PrimaryArtist(artist);
        foreach (var (ep, q) in new[] { ("search/album", $"{artist} {album}"), ("search", $"{artist} {title}") })
        {
            if (q.Trim() == artist) continue;
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(OnlineAsk);
                var j = JsonNode.Parse(await HttpRetry.GetStringAsync(http, $"https://api.deezer.com/{ep}?q={Uri.EscapeDataString(q)}&limit=25", limit.Token, retry));
                // Deezer says "too many requests" with a 200 and an error inside: not "no such album", so it is asked again later (owner, 2 Nov: covers went missing)
                if (j?["error"] is not null) { unasked = true; continue; }
                var mine = (j?["data"]?.AsArray() ?? []).OfType<JsonObject>()
                    .Where(o => Matching.PrimaryArtist(o["artist"]?["name"]?.GetValue<string>() ?? "") is { Length: > 0 } a && (a.Contains(want) || want.Contains(a))).ToList();
                int Score(JsonObject o)
                {
                    var alTitle = ep == "search" ? o["album"]?["title"]?.GetValue<string>() ?? "" : o["title"]?.GetValue<string>() ?? "";
                    var s = 0;
                    if (album.Length > 0 && Matching.AlbumNorm(alTitle) == Matching.AlbumNorm(album)) s += 100;
                    if (Compilation.IsMatch(alTitle) && !Compilation.IsMatch(album)) s -= 50;
                    if (ep == "search" && Matching.NormTitle(o["title"]?.GetValue<string>() ?? "") == Matching.NormTitle(title)) s += 10;
                    // looked up by the song itself: its own release (named after it) before a compilation it is also on, as the phone does (George Michael's
                    // Careless Whisper came up on Wham!'s The Final first; owner, 2 Nov)
                    if (ep == "search" && album.Length == 0 && Matching.NormTitle(title) is { Length: > 0 } nt && Matching.NormTitle(alTitle).StartsWith(nt)) s += 20;
                    return s;
                }
                // a result that matches neither the album nor the song is another release by the artist: no cover is better than a wrong one
                var hit = mine.Where(o => Score(o) >= 10 || (album.Length == 0 && ep == "search/album")).OrderByDescending(Score).FirstOrDefault();
                var url = hit?["cover_big"]?.GetValue<string>() ?? hit?["album"]?["cover_big"]?.GetValue<string>();
                var name = ep == "search" ? hit?["album"]?["title"]?.GetValue<string>() : hit?["title"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(url)) return ((name ?? "", url), false);
            }
            // not a page that was read and had nothing: Deezer could not be asked (down, timed out, still busy after asking again, or the caller gave up)
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException) { unasked = true; }
            catch { unasked = true; }   // a page that could not be read (half a body) is not "nothing there" either (owner, 5 Nov)
        }
        return (null, unasked);
    }
}

/// <summary>What the phone app sends to share: song or playlist, its name, and the songs (path, title, artist).</summary>
public sealed record ShareRequest(string? Kind, string? Name, List<ShareSong>? Entries);
public sealed record ShareSong(string? P, string? T, string? A);
