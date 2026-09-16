namespace IpodSync.Core.Artwork;

/// <summary>
/// Read-only: renders a device thumbnail (RGB565LE in an ithmb file) as a
/// <c>data:image/bmp</c> URL for display. A 16-bit BI_BITFIELDS bitmap holds RGB565
/// pixels as-is, so no image library is needed. An iPod stores each cover at several
/// sizes; callers say how big they need it, so a grid of covers doesn't pull the 240px
/// ones (a 60-cover grid at 240px is ~9 MB of data URLs, at 128px under 3 MB).
/// </summary>
public static class ArtworkPreview
{
    public sealed class Cache
    {
        internal readonly object Gate = new();
        internal string? Dir;
        internal DateTime DbStamp;
        internal Dictionary<int, List<ArtworkDb.Thumb>>? Thumbs;
        internal readonly Dictionary<(int Id, int Side), string> Urls = [];
        internal readonly LinkedList<(int Id, int Side)> Order = new();

        /// <summary>Roughly 24 MB of decoded covers.</summary>
        public int MaxEntries { get; init; } = 300;

        public void Clear() { lock (Gate) { Dir = null; Thumbs = null; Urls.Clear(); Order.Clear(); } }
    }

    public static string? DataUrl(string artworkDir, uint artworkId, Cache cache, int preferredSide = 240)
    {
        string dbPath = Path.Combine(artworkDir, "ArtworkDB");
        if (artworkId == 0 || !File.Exists(dbPath)) return null;
        lock (cache.Gate)
        {
            var stamp = File.GetLastWriteTimeUtc(dbPath);
            if (cache.Dir != artworkDir || cache.DbStamp != stamp || cache.Thumbs is null)
            {
                var root = ArtworkDb.Parse(File.ReadAllBytes(dbPath));
                cache.Thumbs = [];
                foreach (var img in ArtworkDb.Images(root))
                    cache.Thumbs[ArtworkDb.ImageId(img)] = [.. ArtworkDb.Thumbs(img)];
                cache.Dir = artworkDir;
                cache.DbStamp = stamp;
                cache.Urls.Clear();
                cache.Order.Clear();
            }

            var key = ((int)artworkId, preferredSide);
            if (cache.Urls.TryGetValue(key, out var hit))
            {
                cache.Order.Remove(key);
                cache.Order.AddLast(key);
                return hit;
            }
            if (!cache.Thumbs.TryGetValue((int)artworkId, out var thumbs) || thumbs.Count == 0) return null;

            // Square formats only, smallest that still covers the requested size.
            var square = thumbs.Select(t => (Thumb: t, Side: (int)Math.Round(Math.Sqrt(t.Size / 2.0))))
                .Where(x => x.Side > 0 && x.Side * x.Side * 2 == x.Thumb.Size).ToList();
            if (square.Count == 0) return null;
            var chosen = square.Where(x => x.Side >= preferredSide).OrderBy(x => x.Side).FirstOrDefault();
            if (chosen.Thumb is null) chosen = square.OrderByDescending(x => x.Side).First();

            string ithmb = Path.Combine(artworkDir, $"F{chosen.Thumb.Format}_1.ithmb");
            if (!File.Exists(ithmb)) return null;
            byte[] pixels = new byte[chosen.Thumb.Size];
            using (var fs = new FileStream(ithmb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (chosen.Thumb.Offset < 0 || chosen.Thumb.Offset + (long)chosen.Thumb.Size > fs.Length) return null;
                fs.Seek(chosen.Thumb.Offset, SeekOrigin.Begin);
                fs.ReadExactly(pixels);
            }
            string url = "data:image/bmp;base64," + Convert.ToBase64String(Bmp565(pixels, chosen.Side, chosen.Side));

            cache.Urls[key] = url;
            cache.Order.AddLast(key);
            while (cache.Order.Count > cache.MaxEntries && cache.Order.First is { } oldest)
            {
                cache.Urls.Remove(oldest.Value);
                cache.Order.RemoveFirst();
            }
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
