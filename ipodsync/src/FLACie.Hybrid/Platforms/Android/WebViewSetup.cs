using System.Net;
using Android.Webkit;
using FLACie.Core;
using AWebView = Android.Webkit.WebView;

namespace FLACie.Hybrid.Platforms.Android;

/// <summary>
/// The page asks for <c>/stream?p=...</c> (a song) and <c>/art/{key}</c> (a cover), the same addresses as on the web. Here there is no server: those requests
/// are answered in-process, fetched from Jellyfin with the account's token and handed back on the page's own origin (https://0.0.0.1). Being same-origin
/// is what lets Web Audio read the song (flacie.js's analyser, the visualisers, the EQ) without CORS and without the microphone.
/// Everything else goes on to BlazorWebView's own client (the app's files, Blazor's start-up).
/// </summary>
public static class WebViewSetup
{
    public static void Apply(AWebView view, IServiceProvider services)
    {
        var s = view.Settings;
        s.MediaPlaybackRequiresUserGesture = false;   // flacie.load starts a song after an await, outside the tap that asked for it
        s.DomStorageEnabled = true;
        if (view.WebViewClient is { } inner) view.SetWebViewClient(new ProxyClient(inner, services.GetRequiredService<HybridAccount>(), services.GetRequiredService<JellyfinClient>()));
    }

    sealed class ProxyClient(WebViewClient inner, HybridAccount account, JellyfinClient jf) : WebViewClient
    {
        static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };

        public override bool ShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request) => inner.ShouldOverrideUrlLoading(view, request);
        public override void OnPageStarted(AWebView? view, string? url, global::Android.Graphics.Bitmap? favicon) => inner.OnPageStarted(view, url, favicon);
        public override void OnPageFinished(AWebView? view, string? url) => inner.OnPageFinished(view, url);

        public override WebResourceResponse? ShouldInterceptRequest(AWebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url;
            if (url is not null && url.Host == "0.0.0.1")
            {
                var path = url.Path ?? "";
                try
                {
                    if (path == "/stream") return Stream(url.GetQueryParameter("p") ?? "", request!.RequestHeaders);
                    if (path.StartsWith("/art/", StringComparison.Ordinal)) return Cover(Uri.UnescapeDataString(path[5..]), url.GetQueryParameter("s") == "full");
                    if (path.StartsWith("/desk/disc/", StringComparison.Ordinal) || path.StartsWith("/api/", StringComparison.Ordinal) || path == "/healthz") return Empty(404);
                }
                catch (Exception e)
                {
                    global::Android.Util.Log.Warn("FLACie", $"{path}: {e.Message}");
                    return Empty(502);
                }
            }
            return inner.ShouldInterceptRequest(view, request);
        }

        WebResourceResponse Stream(string p, IDictionary<string, string>? headers)
        {
            if (account.Session is not { Jellyfin: { } a } s || s.FindByPath(p) is not { JellyfinId: { } id } t) return Empty(404);
            var transcode = MediaInfo.NotPlayableInBrowsers.Contains(MediaInfo.FromExtension(t.FilePath ?? ""));
            var req = jf.Authorized(a, transcode ? $"{a.Server}/Audio/{id}/stream.mp3?AudioCodec=mp3&AudioBitRate=256000&MaxAudioChannels=2" : jf.StreamUrl(a, id));
            if (!transcode && headers is not null && headers.FirstOrDefault(h => h.Key.Equals("Range", StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } range)
                req.Headers.TryAddWithoutValidation("Range", range);
            return Forward(req);
        }

        WebResourceResponse Cover(string key, bool full)
        {
            if (account.Session is not { Jellyfin: { } a } || key.Length > 400 || key.Contains('/')) return Empty(404);
            HttpRequestMessage req;
            if (key.StartsWith("jf", StringComparison.Ordinal))
            {
                var id = key[2..]; string? tag = null;
                if (id.IndexOf('~') is var tx and > 0) { tag = id[(tx + 1)..]; id = id[..tx]; }
                req = jf.Authorized(a, jf.ImageUrl(a, id, full ? 0 : 240, tag, webp: !full));
            }
            else if (FLACie.Core.Art.ExternalUrl(key) is { } ext) req = new HttpRequestMessage(HttpMethod.Get, ext);
            else return Empty(404);
            return Forward(req, "private, max-age=31536000, immutable");
        }

        /// <summary>Sends the request and streams the answer back to the page as it arrives (status, type, length and ranges kept, so seeking works).</summary>
        static WebResourceResponse Forward(HttpRequestMessage req, string? cache = null)
        {
            var res = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            var status = (int)res.StatusCode;
            var type = res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var h = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" };
            if (res.Content.Headers.ContentLength is { } len) h["Content-Length"] = len.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (res.Content.Headers.ContentRange is { } cr) h["Content-Range"] = cr.ToString();
            if (res.Headers.AcceptRanges.Count > 0) h["Accept-Ranges"] = string.Join(",", res.Headers.AcceptRanges);
            else if (status is 200 or 206) h["Accept-Ranges"] = "bytes";
            if (cache is not null && res.IsSuccessStatusCode) h["Cache-Control"] = cache;
            var body = new OwnedStream(res.Content.ReadAsStream(), res);
            return new WebResourceResponse(type, null, status, Reason(status), h, body);
        }

        static WebResourceResponse Empty(int status) => new("text/plain", "utf-8", status, Reason(status), new Dictionary<string, string>(), new MemoryStream([]));
        static string Reason(int status) => Enum.IsDefined(typeof(HttpStatusCode), status) ? ((HttpStatusCode)status).ToString() : "Status";
    }

    /// <summary>The response body, which keeps its response alive until the WebView has read it and closes it.</summary>
    sealed class OwnedStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); owner.Dispose(); } base.Dispose(disposing); }
    }
}
