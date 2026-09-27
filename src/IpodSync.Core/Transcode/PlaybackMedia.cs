using System.Security.Cryptography;
using System.Text;

namespace IpodSync.Core.Transcode;

/// <summary>
/// Which of the iPod's files a browser engine can play, and on-the-fly conversion for the
/// ones it can't. Chromium (WebView2, Edge, Chrome) plays MP3, AAC-in-MP4, WAV and FLAC,
/// but not Apple Lossless — a big part of a real iPod library — so ALAC is decoded to FLAC
/// (fast, lossless, cached by content) and served instead. Android plays everything
/// natively and doesn't come through here.
/// </summary>
public static class PlaybackMedia
{
    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".m4a" or ".m4b" or ".mp4" or ".aac" => "audio/mp4",
        ".wav" => "audio/wav",
        ".flac" => "audio/flac",
        ".aif" or ".aiff" => "audio/aiff",
        ".ogg" or ".oga" or ".opus" => "audio/ogg",
        _ => "application/octet-stream",
    };

    /// <summary>True when a browser engine needs a converted copy. Decided from the library's
    /// own metadata (no probing): Apple Lossless and AIFF are the cases that matter.</summary>
    public static bool NeedsConversion(string path, string? fileTypeDescription, int bitrate)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".aif" or ".aiff" or ".ogg" or ".oga" or ".opus" or ".wma") return true;
        if (ext is not (".m4a" or ".m4b" or ".mp4")) return false;
        if (fileTypeDescription?.Contains("Lossless", StringComparison.OrdinalIgnoreCase) == true) return true;
        return bitrate >= 600;   // AAC never gets near this; ALAC is ~700-1400 kbps
    }

    public static string CacheDir => Path.Combine(Transcoder.CacheDir, "playback");

    /// <summary>Converts to FLAC for playback, cached by path+size+timestamp so the same track
    /// is converted once. Returns the cached file, or null when ffmpeg isn't available.</summary>
    public static string? ConvertForPlayback(string source)
    {
        if (!Transcoder.FfmpegAvailable()) return null;
        var info = new FileInfo(source);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..24];
        Directory.CreateDirectory(CacheDir);
        string outPath = Path.Combine(CacheDir, key + ".flac");
        if (File.Exists(outPath) && new FileInfo(outPath).Length > 0) return outPath;

        string partial = outPath + ".partial";
        var (code, err) = Transcoder.RunFfmpeg(["-hide_banner", "-loglevel", "error", "-y", "-i", source,
            "-map", "0:a:0", "-c:a", "flac", "-compression_level", "0", "-f", "flac", partial]);
        if (code != 0 || !File.Exists(partial)) { try { File.Delete(partial); } catch { } throw new InvalidOperationException($"could not prepare this track for playback: {err.Trim()}"); }
        File.Move(partial, outPath, overwrite: true);
        Prune();
        return outPath;
    }

    /// <summary>Keeps the playback cache under ~2 GB, oldest first.</summary>
    private static void Prune(long limitBytes = 2L * 1024 * 1024 * 1024)
    {
        try
        {
            var files = new DirectoryInfo(CacheDir).GetFiles("*.flac").OrderBy(f => f.LastAccessTimeUtc).ToList();
            long total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= limitBytes) break;
                total -= f.Length;
                try { f.Delete(); } catch { }
            }
        }
        catch { /* best effort */ }
    }
}
