namespace IpodSync.Core.Artwork;

/// <summary>
/// In-memory edit of a device's artwork: the parsed ArtworkDB plus the bytes that
/// would be appended to each .ithmb file. Nothing touches the device here; the write
/// pipeline appends <see cref="Appends"/> and writes <see cref="Root"/> only after
/// every check passes.
///
/// New images clone the header and mhod layout of an existing image (so every field
/// we don't set is a real iTunes value) and occupy the next slot at the end of each
/// ithmb, exactly as the device's 529 images are laid out.
/// </summary>
public sealed class ArtworkSession
{
    public ArtChunk Root { get; }
    public string ArtworkDir { get; }
    /// <summary>Format id → ithmb length on the device when the session started.</summary>
    public Dictionary<int, long> OriginalLengths { get; } = new();
    /// <summary>Format id → bytes to append (concatenated slots, in id order).</summary>
    public Dictionary<int, MemoryStream> Appends { get; } = new();
    /// <summary>Fill = centre-crop to the square (iTunes never pads that format on the
    /// device), otherwise scale to fit and letterbox.</summary>
    public List<(int Format, int Width, int Height, bool Fill)> Formats { get; } = [];
    public bool Changed { get; private set; }

    private ArtworkSession(ArtChunk root, string dir)
    {
        Root = root;
        ArtworkDir = dir;
    }

    public static ArtworkSession? Open(string artworkDir)
    {
        string db = Path.Combine(artworkDir, "ArtworkDB");
        if (!File.Exists(db)) return null;
        var root = ArtworkDb.Parse(File.ReadAllBytes(db));
        var s = new ArtworkSession(root, artworkDir);
        var images = ArtworkDb.Images(root).ToList();
        foreach (var (fmt, size) in ArtworkDb.Formats(root))
        {
            // Every format on the nano 5G is square; take the dimensions from the
            // existing thumbnails rather than assuming them.
            var thumbs = images.SelectMany(ArtworkDb.Thumbs).Where(t => t.Format == fmt).ToList();
            int side = (int)Math.Round(Math.Sqrt(size / 2.0));
            if (side * side * 2 != size) throw new InvalidDataException($"format {fmt}: {size} bytes is not a square RGB565 slot");
            if (thumbs.Any(t => t.Size != size || t.Bottom > side || t.Right > side))
                throw new InvalidDataException($"format {fmt}: existing thumbnails don't fit a {side}x{side} slot");
            s.Formats.Add((fmt, side, side, false));
            string ithmb = Path.Combine(artworkDir, $"F{fmt}_1.ithmb");
            long len = File.Exists(ithmb) ? new FileInfo(ithmb).Length : 0;
            long end = thumbs.Count == 0 ? 0 : thumbs.Max(t => (long)t.Offset + t.Size);
            if (end != len) throw new InvalidDataException($"F{fmt}_1.ithmb is {len} bytes but thumbnails end at {end}; refusing to append");
            s.OriginalLengths[fmt] = len;
            s.Appends[fmt] = new MemoryStream();
        }
        // A format whose thumbnails are never padded while other formats of the same
        // images are (e.g. 1078, 80x80 on the nano 5G) is filled by cropping.
        var padded = images.SelectMany(ArtworkDb.Thumbs).Where(t => t.PadTop > 0 || t.PadLeft > 0).Select(t => t.Format).ToHashSet();
        if (padded.Count > 0)
            for (int i = 0; i < s.Formats.Count; i++)
                if (!padded.Contains(s.Formats[i].Format)) s.Formats[i] = s.Formats[i] with { Fill = true };
        return s;
    }

    /// <summary>Adds an image built from <paramref name="imagePath"/> referenced by
    /// <paramref name="refCount"/> tracks; returns its id.</summary>
    public int AddImage(string imagePath, ulong trackPid, int refCount)
    {
        var template = ArtworkDb.Images(Root).FirstOrDefault()
            ?? throw new InvalidOperationException("ArtworkDB has no existing image to base a new one on");
        int id = ArtworkDb.NextImageId(Root);
        long sourceSize = new FileInfo(imagePath).Length;

        var mhii = new ArtChunk { Magic = "mhii", Header = (byte[])template.Header.Clone(), Payload = (byte[])template.Payload.Clone() };
        ArtChunk.W(mhii.Header, 0x10, id);
        BitConverter.TryWriteBytes(mhii.Header.AsSpan(0x14, 8), trackPid);
        ArtChunk.W(mhii.Header, 0x30, (int)Math.Min(sourceSize + 1, int.MaxValue));
        ArtChunk.W(mhii.Header, 0x38, refCount);

        foreach (var mhod in template.Children)
        {
            var child = mhod.Children.FirstOrDefault();
            if (child?.Magic != "mhni")
            {
                mhii.Children.Add(DeepClone(mhod));   // e.g. type 6 / mhaf: copied as iTunes wrote it
                continue;
            }
            int fmt = ArtChunk.R(child.Header, 0x10);
            var (_, w, h, fill) = Formats.First(f => f.Format == fmt);
            var thumb = Thumbnailer.Make(imagePath, fmt, w, h, fill);
            var buffer = Appends[fmt];
            long offset = OriginalLengths[fmt] + buffer.Length;
            buffer.Write(thumb.Pixels);

            var newMhod = DeepClone(mhod);
            var mhni = newMhod.Children[0];
            ArtChunk.W(mhni.Header, 0x14, checked((int)offset));
            ArtChunk.W(mhni.Header, 0x18, thumb.Pixels.Length);
            BitConverter.TryWriteBytes(mhni.Header.AsSpan(0x1C, 2), (ushort)thumb.PadTop);
            BitConverter.TryWriteBytes(mhni.Header.AsSpan(0x1E, 2), (ushort)thumb.PadLeft);
            BitConverter.TryWriteBytes(mhni.Header.AsSpan(0x20, 2), (ushort)(thumb.PadTop + thumb.ContentHeight));
            BitConverter.TryWriteBytes(mhni.Header.AsSpan(0x22, 2), (ushort)(thumb.PadLeft + thumb.ContentWidth));
            if (mhni.Header.Length >= 0x2C) ArtChunk.W(mhni.Header, 0x28, thumb.Pixels.Length);
            mhii.Children.Add(newMhod);
        }

        ArtworkDb.ImageList(Root).Children.Add(mhii);
        ArtChunk.W(Root.Header, 0x1C, id + 1);
        Changed = true;
        return id;
    }

    /// <summary>Adjusts an image's reference count; removes the image entry when it
    /// reaches zero (its ithmb slots are left in place, unused -- appending never
    /// moves another image's pixels).</summary>
    public void AddReference(int imageId, int delta)
    {
        var mhii = ArtworkDb.Images(Root).FirstOrDefault(m => ArtworkDb.ImageId(m) == imageId);
        if (mhii is null) return;
        int refs = ArtworkDb.RefCount(mhii) + delta;
        if (refs <= 0) ArtworkDb.ImageList(Root).Children.Remove(mhii);
        else ArtChunk.W(mhii.Header, 0x38, refs);
        Changed = true;
    }

    public byte[] SerializeDb() => Root.Serialize();

    private static ArtChunk DeepClone(ArtChunk c)
    {
        var n = new ArtChunk { Magic = c.Magic, Header = (byte[])c.Header.Clone(), Payload = (byte[])c.Payload.Clone() };
        foreach (var k in c.Children) n.Children.Add(DeepClone(k));
        return n;
    }
}
