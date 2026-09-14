namespace IpodSync.Core.Artwork;

/// <summary>
/// Read-only: renders a device thumbnail (RGB565LE in an ithmb file) as a
/// <c>data:image/bmp</c> URL for display. A 16-bit BI_BITFIELDS bitmap holds RGB565
/// pixels as-is, so no image library is needed.
/// </summary>
public static class ArtworkPreview
{
    public sealed class Cache
    {
        internal readonly object Gate = new();
        internal string? Dir;
        internal DateTime DbStamp;
        internal Dictionary<int, ArtworkDb.Thumb>? Largest;
        internal readonly Dictionary<int, string> Urls = [];

        public void Clear() { lock (Gate) { Dir = null; Largest = null; Urls.Clear(); } }
    }

    public static string? DataUrl(string artworkDir, uint artworkId, Cache cache)
    {
        string dbPath = Path.Combine(artworkDir, "ArtworkDB");
        if (artworkId == 0 || !File.Exists(dbPath)) return null;
        lock (cache.Gate)
        {
            var stamp = File.GetLastWriteTimeUtc(dbPath);
            if (cache.Dir != artworkDir || cache.DbStamp != stamp || cache.Largest is null)
            {
                var root = ArtworkDb.Parse(File.ReadAllBytes(dbPath));
                cache.Largest = [];
                foreach (var img in ArtworkDb.Images(root))
                {
                    var best = ArtworkDb.Thumbs(img).OrderByDescending(t => t.Size).FirstOrDefault();
                    if (best is not null) cache.Largest[ArtworkDb.ImageId(img)] = best;
                }
                cache.Dir = artworkDir;
                cache.DbStamp = stamp;
                cache.Urls.Clear();
            }
            if (cache.Urls.TryGetValue((int)artworkId, out var hit)) return hit;
            if (!cache.Largest.TryGetValue((int)artworkId, out var thumb)) return null;

            int side = (int)Math.Round(Math.Sqrt(thumb.Size / 2.0));
            if (side <= 0 || side * side * 2 != thumb.Size) return null;   // only square formats
            string ithmb = Path.Combine(artworkDir, $"F{thumb.Format}_1.ithmb");
            if (!File.Exists(ithmb)) return null;
            byte[] pixels = new byte[thumb.Size];
            using (var fs = new FileStream(ithmb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (thumb.Offset < 0 || thumb.Offset + (long)thumb.Size > fs.Length) return null;
                fs.Seek(thumb.Offset, SeekOrigin.Begin);
                fs.ReadExactly(pixels);
            }
            string url = "data:image/bmp;base64," + Convert.ToBase64String(Bmp565(pixels, side, side));
            cache.Urls[(int)artworkId] = url;
            return url;
        }
    }

    /// <summary>Top-down 16-bpp BI_BITFIELDS bitmap (masks F800/07E0/001F).</summary>
    public static byte[] Bmp565(byte[] rgb565le, int width, int height)
    {
        int stride = (width * 2 + 3) & ~3;
        const int headerSize = 14 + 40 + 12;
        byte[] bmp = new byte[headerSize + stride * height];
        void W16(int o, int v) => BitConverter.TryWriteBytes(bmp.AsSpan(o, 2), (ushort)v);
        void W32(int o, int v) => BitConverter.TryWriteBytes(bmp.AsSpan(o, 4), v);
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        W32(2, bmp.Length); W32(10, headerSize);
        W32(14, 40); W32(18, width); W32(22, -height); W16(26, 1); W16(28, 16); W32(30, 3);
        W32(34, stride * height); W32(38, 2835); W32(42, 2835);
        W32(54, 0xF800); W32(58, 0x07E0); W32(62, 0x001F);
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(rgb565le, y * width * 2, bmp, headerSize + y * stride, width * 2);
        return bmp;
    }
}
