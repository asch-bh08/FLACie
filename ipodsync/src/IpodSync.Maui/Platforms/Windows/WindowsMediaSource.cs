using IpodSync.Core.ItunesDb;
using IpodSync.Core.Transcode;
using IpodSync.Shared.Playback;
using Microsoft.Web.WebView2.Core;

namespace IpodSync.Maui.Platforms.Windows;

/// <summary>
/// Playback URLs for the Windows app. WebView2 can't load file:// URLs from a page, so each
/// iPod root (and the conversion cache) is mapped to a virtual host name and the &lt;audio&gt;
/// element loads https://ipod0.local/iPod_Control/Music/... The mapping is read-only.
/// Apple Lossless is converted to FLAC first — WebView2 can't decode ALAC.
/// </summary>
public sealed class WindowsMediaSource : IMediaSource
{
    private const string CacheHost = "ipodsync-cache.local";
    private static CoreWebView2? _webView;
    private static readonly Dictionary<string, string> Hosts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Called once the BlazorWebView is up (see MainPage).</summary>
    public static void Attach(CoreWebView2 webView)
    {
        _webView = webView;
        Hosts.Clear();
        try
        {
            Directory.CreateDirectory(PlaybackMedia.CacheDir);
            webView.SetVirtualHostNameToFolderMapping(CacheHost, PlaybackMedia.CacheDir, CoreWebView2HostResourceAccessKind.Allow);
        }
        catch { /* playback will report it can't find the file */ }
    }

    public string? Unavailable => _webView is null ? "Playback isn't ready yet — reopen the app if this persists." : null;

    public async Task<string?> UrlAsync(string deviceRoot, Track track, bool forceConversion = false, CancellationToken ct = default)
    {
        if (_webView is null || track.RelativePath is not { } rel) return null;
        string file = Path.Combine(deviceRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return null;

        if (forceConversion || PlaybackMedia.NeedsConversion(rel, track.FileTypeDescription, track.Bitrate))
        {
            string? converted = await Task.Run(() => PlaybackMedia.ConvertForPlayback(file), ct);
            if (converted is null) throw new InvalidOperationException("Apple Lossless tracks need ffmpeg to play here. Install it with: winget install Gyan.FFmpeg");
            return $"https://{CacheHost}/{Uri.EscapeDataString(Path.GetFileName(converted))}";
        }

        string host = await HostForAsync(deviceRoot);
        return $"https://{host}/{string.Join('/', rel.Split('/').Select(Uri.EscapeDataString))}";
    }

    private static async Task<string> HostForAsync(string deviceRoot)
    {
        await Gate.WaitAsync();
        try
        {
            if (Hosts.TryGetValue(deviceRoot, out var existing)) return existing;
            string host = $"ipod{Hosts.Count}.local";
            await MainThread.InvokeOnMainThreadAsync(() =>
                _webView!.SetVirtualHostNameToFolderMapping(host, deviceRoot, CoreWebView2HostResourceAccessKind.Allow));
            Hosts[deviceRoot] = host;
            return host;
        }
        finally { Gate.Release(); }
    }
}
