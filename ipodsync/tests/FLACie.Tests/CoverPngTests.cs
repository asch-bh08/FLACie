using System.IO.Compression;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>The made cover as a PNG for the phone (/art/{key}?fmt=png), which cannot draw SVG.</summary>
public class CoverPngTests
{
    static (int W, int H, byte[] Pixels) Decode(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        int at = 8, w = 0, h = 0; using var idat = new MemoryStream();
        static uint U(byte[] b, int i) => (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]);
        while (at < png.Length)
        {
            var len = (int)U(png, at); var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            // the CRC covers the type and the data
            var crc = Crc32(png.AsSpan(at + 4, 4 + len));
            Assert.Equal(crc, U(png, at + 8 + len));
            if (type == "IHDR") { w = (int)U(png, at + 8); h = (int)U(png, at + 12); Assert.Equal(8, png[at + 16]); Assert.Equal(2, png[at + 17]); }
            if (type == "IDAT") idat.Write(png, at + 8, len);
            at += 12 + len;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress); using var raw = new MemoryStream(); z.CopyTo(raw);
        return (w, h, raw.ToArray());
    }

    /// <summary>The PNG standard's CRC-32, worked out bit by bit (independent of the encoder's table).</summary>
    static uint Crc32(ReadOnlySpan<byte> d)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in d) { c ^= b; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; }
        return c ^ 0xFFFFFFFFu;
    }

    [Theory]
    [InlineData(240)]
    [InlineData(1000)]
    public void A_valid_png_of_the_asked_size(int side)
    {
        var (w, h, px) = Decode(CoverPng.Make("Eminem", "Lose Yourself", side));
        Assert.Equal((side, side), (w, h));
        Assert.Equal(side * (1 + side * 3), px.Length);
    }

    [Fact]
    public void The_same_song_makes_the_same_cover_in_its_colours_with_light_letters_in_the_middle()
    {
        var a = CoverPng.Make("Eminem", "Lose Yourself", 240);
        Assert.Equal(a, CoverPng.Make("Eminem", "Lose Yourself", 240));
        var (_, _, px) = Decode(a);
        byte At(int x, int y, int c) => px[y * (1 + 240 * 3) + 1 + x * 3 + c];
        var (from, _) = GeneratedCover.ColoursFor("Eminem", "Lose Yourself");
        Assert.Equal(Convert.ToInt32(from[1..3], 16), At(0, 0, 0));          // the fade starts from the song's colour, as the SVG does
        // somewhere in the middle band the letters are near white
        var bright = 0;
        for (var x = 40; x < 200; x++) for (var y = 80; y < 150; y++) if (At(x, y, 0) > 220 && At(x, y, 1) > 220 && At(x, y, 2) > 220) bright++;
        Assert.True(bright > 500, $"{bright} light pixels");
    }

    [Fact]
    public void A_song_with_no_name_gets_a_note()
    {
        var (w, _, _) = Decode(CoverPng.Make("", "", 240));
        Assert.Equal(240, w);
    }
}
