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

        if (!Transcoder.FfmpegAvailable()) return ExtractCoverWithTagLib(audioPath, dir, key);
        var video = Transcoder.ProbeVideo(audioPath);
        if (video is null) return null;
        string outPath = Path.Combine(dir, key + (video.Value.Codec == "png" ? ".png" : ".jpg"));
        var (code, err) = Transcoder.RunFfmpeg(["-hide_banner", "-loglevel", "error", "-y", "-i", audioPath,
            "-an", "-map", "0:v:0", "-c", "copy", "-f", "image2", outPath + ".partial"]);
        if (code != 0 || !File.Exists(outPath + ".partial")) return null;
        File.Move(outPath + ".partial", outPath, overwrite: true);
        return outPath;
    }

    /// <summary>Without ffmpeg (Android): the first embedded picture, via TagLib.</summary>
    private static string? ExtractCoverWithTagLib(string audioPath, string dir, string key)
    {
        try
        {
            using var tf = TagLib.File.Create(audioPath);
            var pic = tf.Tag.Pictures?.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? tf.Tag.Pictures?.FirstOrDefault();
            if (pic is null || pic.Data.Count == 0) return null;
            string outPath = Path.Combine(dir, key + (pic.MimeType?.Contains("png", StringComparison.OrdinalIgnoreCase) == true ? ".png" : ".jpg"));
            File.WriteAllBytes(outPath + ".partial", pic.Data.Data);
            File.Move(outPath + ".partial", outPath, overwrite: true);
            return outPath;
        }
        catch { return null; }
    }

    /// <summary>Turns an image file into RGB565LE pixels at a given size. The default uses
    /// ffmpeg; hosts without it (Android) register their own.</summary>
    public interface IRasterizer
    {
        bool Available { get; }
        (int Width, int Height)? Measure(string imagePath);
        /// <summary>Scale the image to <paramref name="scaledW"/>x<paramref name="scaledH"/>, place it
        /// with its top-left at (<paramref name="offsetX"/>, <paramref name="offsetY"/>) on a black
        /// <paramref name="width"/>x<paramref name="height"/> canvas (negative offsets crop), return
        /// width*height*2 bytes of RGB565LE.</summary>
        byte[] Render(string imagePath, int width, int height, int scaledW, int scaledH, int offsetX, int offsetY, bool fill);
    }

    public static IRasterizer Rasterizer { get; set; } = new FfmpegRasterizer();

    public static bool Available => Rasterizer.Available;

    public static Thumbnail Make(string imagePath, int format, int width, int height, bool fill = false)
    {
        var size = Rasterizer.Measure(imagePath) ?? throw new InvalidOperationException($"not an image: {imagePath}");
        if (size.Width <= 0 || size.Height <= 0) throw new InvalidOperationException($"image has no dimensions: {imagePath}");
        double scale = fill ? Math.Max((double)width / size.Width, (double)height / size.Height)
                            : Math.Min((double)width / size.Width, (double)height / size.Height);
        int sw = Math.Max(1, (int)Math.Round(size.Width * scale)), sh = Math.Max(1, (int)Math.Round(size.Height * scale));
        int cw = Math.Min(sw, width), ch = Math.Min(sh, height);
        int padLeft = (width - cw) / 2, padTop = (height - ch) / 2;
        int offX = fill ? -(Math.Max(sw, width) - width) / 2 : padLeft;
        int offY = fill ? -(Math.Max(sh, height) - height) / 2 : padTop;
        byte[] px = Rasterizer.Render(imagePath, width, height, fill ? Math.Max(sw, width) : cw, fill ? Math.Max(sh, height) : ch, offX, offY, fill);
        if (px.Length != width * height * 2)
            throw new InvalidOperationException($"thumbnail {format} is {px.Length} bytes, expected {width * height * 2}");
        return new Thumbnail(format, width, height, padTop, padLeft, ch, cw, px);
    }

    /// <summary>The original ffmpeg path (bit-exact scaler), unchanged in output.</summary>
    public sealed class FfmpegRasterizer : IRasterizer
    {
        public bool Available => Transcoder.FfmpegAvailable();

        public (int Width, int Height)? Measure(string imagePath) =>
            Transcoder.ProbeVideo(imagePath) is { } p ? (p.Width, p.Height) : null;

        public byte[] Render(string imagePath, int width, int height, int scaledW, int scaledH, int offsetX, int offsetY, bool fill)
        {
            string filter = fill
                ? $"scale={scaledW}:{scaledH},crop={width}:{height},format=rgb565le"
                : $"scale={scaledW}:{scaledH},pad={width}:{height}:{offsetX}:{offsetY}:black,format=rgb565le";
            string tmp = Path.Combine(Path.GetTempPath(), "ipodsync-thumb-" + Guid.NewGuid().ToString("N") + ".raw");
            try
            {
                var (code, err) = Transcoder.RunFfmpeg(["-hide_banner", "-loglevel", "error", "-y", "-i", imagePath,
                    "-frames:v", "1", "-sws_flags", "lanczos+accurate_rnd+full_chroma_int+bitexact",
                    "-vf", filter,
                    "-fflags", "+bitexact", "-f", "rawvideo", tmp]);
                if (code != 0 || !File.Exists(tmp)) throw new InvalidOperationException($"ffmpeg thumbnail failed: {err.Trim()}");
                return File.ReadAllBytes(tmp);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}
