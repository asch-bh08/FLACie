using Android.Graphics;
using IpodSync.Core.Artwork;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// Cover-art thumbnails without ffmpeg: decode with Android's BitmapFactory, scale with
/// filtering, draw onto a black canvas (pad or centre-crop, geometry decided by
/// <see cref="Thumbnailer.Make"/>), and pack to RGB565 little-endian. Deterministic on a
/// given phone, so a dry run and the real write produce the same pixels.
/// </summary>
public sealed class AndroidRasterizer : Thumbnailer.IRasterizer
{
    public bool Available => true;

    public (int Width, int Height)? Measure(string imagePath)
    {
        using var o = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(imagePath, o);
        return o.OutWidth > 0 && o.OutHeight > 0 ? (o.OutWidth, o.OutHeight) : null;
    }

    public byte[] Render(string imagePath, int width, int height, int scaledW, int scaledH, int offsetX, int offsetY, bool fill)
    {
        using var src = BitmapFactory.DecodeFile(imagePath) ?? throw new InvalidOperationException($"Android can't decode {System.IO.Path.GetFileName(imagePath)}");
        using var scaled = Bitmap.CreateScaledBitmap(src, scaledW, scaledH, filter: true);
        using var canvasBitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        using (var canvas = new Canvas(canvasBitmap))
        {
            canvas.DrawColor(global::Android.Graphics.Color.Black);
            canvas.DrawBitmap(scaled, offsetX, offsetY, null);
        }
        int[] argb = new int[width * height];
        canvasBitmap.GetPixels(argb, 0, width, 0, 0, width, height);
        byte[] rgb565 = new byte[width * height * 2];
        for (int i = 0; i < argb.Length; i++)
        {
            int c = argb[i];
            int v = (((c >> 16) & 0xFF) >> 3) << 11 | (((c >> 8) & 0xFF) >> 2) << 5 | ((c & 0xFF) >> 3);
            rgb565[2 * i] = (byte)v;
            rgb565[2 * i + 1] = (byte)(v >> 8);
        }
        return rgb565;
    }
}
