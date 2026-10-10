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
        var artDir = Directory.CreateDirectory(Path.Combine(dataDir, "art")).FullName;
        // a page of album tiles asks for dozens of covers at once; a NAS answers a few at a time reliably
        var nasSlots = new SemaphoreSlim(4);

        app.MapGet("/stream", async (HttpContext ctx, string p, SessionStore store, JellyfinClient jf, IHttpClientFactory hf, AlacCache alac) =>
        {
            var s = store.For(ctx.User);
            var t = s.FindByPath(p);
            if (t is null) return Results.NotFound();
            async Task<IResult> Proxy(HttpRequestMessage req, bool ranges = true)
            {
                if (ranges && ctx.Request.Headers.Range.Count > 0) req.Headers.TryAddWithoutValidation("Range", ctx.Request.Headers.Range.ToString());
                using var res = await hf.CreateClient("media").SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
                ctx.Response.StatusCode = (int)res.StatusCode;
                foreach (var h in new[] { "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges" })
                {
                    if (res.Content.Headers.TryGetValues(h, out var v) || res.Headers.TryGetValues(h, out v)) ctx.Response.Headers[h] = v.ToArray();
                }
                await using var body = await res.Content.ReadAsStreamAsync(ctx.RequestAborted);
                await body.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
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
                var (from, to) = (0L, size - 1);
                var partial = false;
                if (RangeHeaderValue.TryParse(ctx.Request.Headers.Range.ToString(), out var range) && range.Ranges.FirstOrDefault() is { } r)
                {
                    from = r.From ?? Math.Max(0, size - (r.To ?? 0)); to = r.From is null ? size - 1 : Math.Min(r.To ?? size - 1, size - 1); partial = true;
                }
                if (from > to || from >= size) { ctx.Response.Headers.ContentRange = $"bytes */{size}"; return Results.StatusCode(416); }
                ctx.Response.StatusCode = partial ? 206 : 200;
                ctx.Response.ContentType = Mime.GetValueOrDefault(Path.GetExtension(rel).TrimStart('.'), "application/octet-stream");
                ctx.Response.ContentLength = to - from + 1;
                ctx.Response.Headers.AcceptRanges = "bytes";
                if (partial) ctx.Response.Headers.ContentRange = $"bytes {from}-{to}/{size}";
                try { await NasClient.CopyRangeAsync(n, rel, from, to - from + 1, ctx.Response.Body, ctx.RequestAborted); } catch (OperationCanceledException) { }
                return Results.Empty;
            }
            return Results.NotFound();
        }).RequireAuthorization();

        // covers, cached on disk by key (shared between users: a cover is the same picture for everyone)
        var log = app.Logger;
        app.MapGet("/art/{key}", async (HttpContext ctx, string key, SessionStore store, JellyfinClient jf, IHttpClientFactory hf) =>
        {
            if (key.Length > 400 || key.Contains('/') || key.Contains('\\') || key.Contains("..")) return Results.BadRequest();
            var file = Path.Combine(artDir, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(key))) + ".v3.img");
            ctx.Response.Headers.CacheControl = "private, max-age=604800";
            // a Jellyfin cover can be changed (fixed in Jellyfin): the saved copy is trusted for three days, then fetched again
            if (File.Exists(file) && !(key.StartsWith("jf") && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-3))) return Results.File(file, "image/jpeg");
            var s = store.For(ctx.User);
            byte[]? bytes = null;
            var http = hf.CreateClient("media");
            if (key.StartsWith("jf") && s.Jellyfin is { } a)
            {
                using var req = jf.Authorized(a, jf.ImageUrl(a, key[2..], 500));
                using var res = await http.SendAsync(req, ctx.RequestAborted);
                if (res.IsSuccessStatusCode) bytes = await res.Content.ReadAsByteArrayAsync(ctx.RequestAborted);
            }
            else if (Art.ExternalUrl(key) is { } external)
            {
                using var res = await http.GetAsync(external, ctx.RequestAborted);
                if (res.IsSuccessStatusCode) bytes = await res.Content.ReadAsByteArrayAsync(ctx.RequestAborted);
            }
            else if (Art.ParseLookupKey(key) is var (la, lb, lt))
            {
                await nasSlots.WaitAsync(ctx.RequestAborted);
                try { bytes = await OnlineCover(http, la, lb, lt, ctx.RequestAborted); } finally { nasSlots.Release(); }
            }
            else if (key.StartsWith("nf") && s.Nas is { } n)
            {
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(key[2..].Replace('-', '+').Replace('_', '/')))).Split('\n');
                if (parts.Length >= 4)
                {
                    await nasSlots.WaitAsync(ctx.RequestAborted);
                    try { bytes = await Task.Run(() => { try { return NasClient.FolderCover(n, parts[0]) ?? NasClient.EmbeddedCover(n, parts[0]); } catch (Exception e) { log.LogDebug(e, "NAS cover failed"); return null; } }); }
                    finally { nasSlots.Release(); }
                    bytes ??= await OnlineCover(http, parts[1], parts[2], parts[3], ctx.RequestAborted);
                }
            }
            if (bytes is null) return Results.NotFound();
            await File.WriteAllBytesAsync(file, bytes);
            return Results.File(bytes, "image/jpeg");
        }).RequireAuthorization();
    }

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
                var hit = mine.OrderByDescending(Score).FirstOrDefault();
                var url = hit?["cover_big"]?.GetValue<string>() ?? hit?["album"]?["cover_big"]?.GetValue<string>();
                var name = ep == "search" ? hit?["album"]?["title"]?.GetValue<string>() : hit?["title"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(url)) return (name ?? "", url);
            }
            catch { }
        }
        return null;
    }
}
