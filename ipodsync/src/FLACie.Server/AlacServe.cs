using System.Collections.Concurrent;
using FLACie.Core;
using Microsoft.Net.Http.Headers;

namespace FLACie.Server;

/// <summary>What the server has learned about each .m4a (is it Apple Lossless, and where its frames are), kept so a player's many Range requests cost one look.</summary>
public sealed class AlacCache
{
    readonly ConcurrentDictionary<string, Task<AlacInfo?>> cache = new();
    public Task<AlacInfo?> Get(string key, Func<Task<AlacInfo?>> make)
    {
        if (cache.Count > 400) cache.Clear();
        var t = cache.GetOrAdd(key, _ => make());
        // a failed look (the file was busy, the network blinked) is not remembered
        _ = t.ContinueWith(x => { if (x.IsFaulted) cache.TryRemove(key, out _); }, TaskScheduler.Default);
        return t;
    }
}

/// <summary>A NAS file as byte ranges.</summary>
sealed class NasRangeSource(NasAccount n, string rel, long length) : IRangeSource
{
    public long Length => length;
    public async Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct)
    {
        if (offset >= length) return [];
        var ms = new MemoryStream();
        await NasClient.CopyRangeAsync(n, rel, offset, Math.Min(count, length - offset), ms, ct);
        return ms.ToArray();
    }
}

/// <summary>
/// Apple Lossless (ALAC, the .m4a of an iTunes or "(LAC)" folder) cannot be played by browsers and many phones have no decoder for it. The server decodes it itself
/// (FLACie.Core/Alac.cs, no ffmpeg and no Jellyfin conversion) and sends a WAV, which everything plays and can seek in.
/// </summary>
public static class AlacServe
{
    static readonly HashSet<string> Ext = new(StringComparer.OrdinalIgnoreCase) { "m4a", "alac", "mp4", "m4b" };

    /// <summary>The file could be Apple Lossless (by its name): the file itself says for sure.</summary>
    public static bool MayBeAlac(Track t)
    {
        var name = t.FilePath ?? t.Path; var q = name.IndexOf('?'); if (q >= 0) name = name[..q];
        return Ext.Contains(Path.GetExtension(name).TrimStart('.'));
    }

    public static async Task<(IRangeSource Src, AlacInfo Info)?> OpenAsync(UserSession s, Track t, JellyfinClient jf, IHttpClientFactory hf, AlacCache cache, CancellationToken ct)
    {
        IRangeSource? src = null;
        string key;
        if (t.Source == TrackSource.Jellyfin && s.Jellyfin is { } a && t.JellyfinId is { Length: > 0 } id)
        {
            key = "jf|" + a.Server + "|" + id;
            var http = hf.CreateClient("media");
            var info = await cache.Get(key, async () => { var r = await HttpRangeSource.OpenAsync(http, () => jf.Authorized(a, jf.StreamUrl(a, id)), ct); return await Mp4Alac.ReadAsync(r, ct); });
            if (info is null) return null;
            src = await HttpRangeSource.OpenAsync(http, () => jf.Authorized(a, jf.StreamUrl(a, id)), ct);
            return (src, info);
        }
        if (t.Source == TrackSource.Nas && s.Nas is { } n)
        {
            var rel = NasClient.RelPath(n, t.Path);
            long size; try { size = await Task.Run(() => NasClient.Size(n, rel), ct); } catch (FileNotFoundException) { return null; }
            src = new NasRangeSource(n, rel, size);
            key = "nas|" + n.Host + "|" + n.Share + "|" + rel + "|" + size;
            var info = await cache.Get(key, () => Mp4Alac.ReadAsync(src, ct));
            return info is null ? null : (src, info);
        }
        return null;
    }

    /// <summary>Sends the song as WAV (all of it, or the Range asked for). False when it is not Apple Lossless: the caller plays the file as it is.</summary>
    public static async Task<bool> TryServeAsync(HttpContext ctx, UserSession s, Track t, JellyfinClient jf, IHttpClientFactory hf, AlacCache cache)
    {
        if (!MayBeAlac(t)) return false;
        (IRangeSource Src, AlacInfo Info)? opened;
        try { opened = await OpenAsync(s, t, jf, hf, cache, ctx.RequestAborted); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
        if (opened is not var (src, info)) return false;
        var total = AlacWav.TotalLength(info);
        long from = 0, to = total - 1; var partial = false;
        if (RangeHeaderValue.TryParse(ctx.Request.Headers.Range.ToString(), out var range) && range.Ranges.FirstOrDefault() is { } r)
        {
            from = r.From ?? Math.Max(0, total - (r.To ?? 0)); to = r.From is null ? total - 1 : Math.Min(r.To ?? total - 1, total - 1); partial = true;
        }
        if (from > to || from >= total) { ctx.Response.Headers.ContentRange = $"bytes */{total}"; ctx.Response.StatusCode = 416; return true; }
        ctx.Response.StatusCode = partial ? 206 : 200;
        ctx.Response.ContentType = "audio/wav";
        ctx.Response.ContentLength = to - from + 1;
        ctx.Response.Headers.AcceptRanges = "bytes";
        if (partial) ctx.Response.Headers.ContentRange = $"bytes {from}-{to}/{total}";
        try { await AlacWav.WriteAsync(ctx.Response.Body, src, info, from, to, ctx.RequestAborted); }
        catch (OperationCanceledException) { }
        return true;
    }
}
