using System.Security.Cryptography;
using IpodSync.Core.Transcode;

namespace IpodSync.Core.Artwork;

/// <summary>
/// Makes iPod thumbnails with ffmpeg, matching what iTunes wrote on the real device:
/// the picture is scaled to fit the format's square, centred, the rest black, and
/// stored as RGB565 little-endian. Deterministic (bit-exact scaler), so a dry run and
/// the real write produce identical pixels.
/// </summary>
public static class Thumbnailer
{
    public sealed record Thumbnail(int Format, int Width, int Height, int PadTop, int PadLeft, int ContentHeight, int ContentWidth, byte[] Pixels);

    /// <summary>Pulls the embedded cover (attached picture) out of an audio file,
    /// byte-for-byte. Returns null when the file has none.</summary>
    public static string? ExtractCover(string audioPath)
    {
        string key;
        using (var fs = File.OpenRead(audioPath)) key = Convert.ToHexString(SHA256.HashData(fs))[..32];
        string dir = Path.Combine(Transcoder.CacheDir, "covers");
        Directory.CreateDirectory(dir);
        foreach (var ext in new[] { ".jpg", ".png" })
            if (File.Exists(Path.Combine(dir, key + ext))) return Path.Combine(dir, key + ext);

        var video = Transcoder.ProbeVideo(audioPath);
        if (video is null) return null;
        string outPath = Path.Combine(dir, key + (video.Value.Codec == "png" ? ".png" : ".jpg"));
        var (code, err) = Transcoder.RunFfmpeg(["-hide_banner", "-loglevel", "error", "-y", "-i", audioPath,
            "-an", "-map", "0:v:0", "-c", "copy", "-f", "image2", outPath + ".partial"]);
        if (code != 0 || !File.Exists(outPath + ".partial")) return null;
        File.Move(outPath + ".partial", outPath, overwrite: true);
        return outPath;
    }

    public static Thumbnail Make(string imagePath, int format, int width, int height, bool fill = false)
    {
        var probe = Transcoder.ProbeVideo(imagePath) ?? throw new InvalidOperationException($"not an image: {imagePath}");
        if (probe.Width <= 0 || probe.Height <= 0) throw new InvalidOperationException($"image has no dimensions: {imagePath}");
        double scale = fill ? Math.Max((double)width / probe.Width, (double)height / probe.Height)
                            : Math.Min((double)width / probe.Width, (double)height / probe.Height);
        int sw = Math.Max(1, (int)Math.Round(probe.Width * scale)), sh = Math.Max(1, (int)Math.Round(probe.Height * scale));
        int cw = Math.Min(sw, width), ch = Math.Min(sh, height);
        int padLeft = (width - cw) / 2, padTop = (height - ch) / 2;
        string filter = fill
            ? $"scale={Math.Max(sw, width)}:{Math.Max(sh, height)},crop={width}:{height},format=rgb565le"
            : $"scale={cw}:{ch},pad={width}:{height}:{padLeft}:{padTop}:black,format=rgb565le";

        string tmp = Path.Combine(Path.GetTempPath(), "ipodsync-thumb-" + Guid.NewGuid().ToString("N") + ".raw");
        try
        {
            var (code, err) = Transcoder.RunFfmpeg(["-hide_banner", "-loglevel", "error", "-y", "-i", imagePath,
                "-frames:v", "1", "-sws_flags", "lanczos+accurate_rnd+full_chroma_int+bitexact",
                "-vf", filter,
                "-fflags", "+bitexact", "-f", "rawvideo", tmp]);
            if (code != 0 || !File.Exists(tmp)) throw new InvalidOperationException($"ffmpeg thumbnail failed: {err.Trim()}");
            byte[] px = File.ReadAllBytes(tmp);
            if (px.Length != width * height * 2)
                throw new InvalidOperationException($"thumbnail {format} is {px.Length} bytes, expected {width * height * 2}");
            return new Thumbnail(format, width, height, padTop, padLeft, ch, cw, px);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
