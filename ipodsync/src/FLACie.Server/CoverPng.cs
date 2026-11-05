using System.IO.Compression;

namespace FLACie.Server;

/// <summary>
/// The made cover as a PNG (for the phone, which cannot draw SVG: /art/{key}?fmt=png): the same colours as <see cref="GeneratedCover.Svg"/>, a diagonal
/// fade, and the artist's initials large in the middle in a simple built-in letter shape (no font or image library on the server).
/// </summary>
public static class CoverPng
{
    // 5x7 letters and digits, one string per row, '#' is ink
    static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = [" ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"], ['B'] = ["#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### "],
        ['C'] = [" ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### "], ['D'] = ["#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### "],
        ['E'] = ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####"], ['F'] = ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    "],
        ['G'] = [" ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ####"], ['H'] = ["#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        ['I'] = ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "#####"], ['J'] = ["  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  "],
        ['K'] = ["#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #"], ['L'] = ["#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####"],
        ['M'] = ["#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #"], ['N'] = ["#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #"],
        ['O'] = [" ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "], ['P'] = ["#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    "],
        ['Q'] = [" ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #"], ['R'] = ["#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #"],
        ['S'] = [" ####", "#    ", "#    ", " ### ", "    #", "    #", "#### "], ['T'] = ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  "],
        ['U'] = ["#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "], ['V'] = ["#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  "],
        ['W'] = ["#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #"], ['X'] = ["#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #"],
        ['Y'] = ["#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  "], ['Z'] = ["#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####"],
        ['0'] = [" ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### "], ['1'] = ["  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### "],
        ['2'] = [" ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####"], ['3'] = ["#### ", "    #", "    #", " ### ", "    #", "    #", "#### "],
        ['4'] = ["   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # "], ['5'] = ["#####", "#    ", "#### ", "    #", "    #", "#   #", " ### "],
        ['6'] = [" ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### "], ['7'] = ["#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   "],
        ['8'] = [" ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### "], ['9'] = [" ### ", "#   #", "#   #", " ####", "    #", "    #", " ### "],
        // a note, for a song with no name at all
        ['♪'] = ["  ###", "  # #", "  #  ", "  #  ", "###  ", "###  ", " #   "],
    };

    public static byte[] Make(string artist, string title, int size)
    {
        size = Math.Clamp(size, 32, 1200);
        var (from, to) = GeneratedCover.ColoursFor(artist, title);
        var (r1, g1, b1) = Rgb(from); var (r2, g2, b2) = Rgb(to);
        var px = new byte[size * size * 3];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var t = (x + y) / (2.0 * (size - 1)); var i = (y * size + x) * 3;
                px[i] = (byte)(r1 + (r2 - r1) * t); px[i + 1] = (byte)(g1 + (g2 - g1) * t); px[i + 2] = (byte)(b1 + (b2 - b1) * t);
            }
        var letters = GeneratedCover.Initials(artist, title).ToUpperInvariant().Where(Glyphs.ContainsKey).Take(2).ToList();
        if (letters.Count == 0) letters = ['♪'];
        // each letter 5 cells wide with one between, the pair about 45 % of the cover wide
        var cell = Math.Max(1, (int)(size * 0.45 / (letters.Count * 6 - 1)));
        var w = (letters.Count * 6 - 1) * cell; var h = 7 * cell;
        int x0 = (size - w) / 2, y0 = (size - h) / 2 - size / 20;
        for (var n = 0; n < letters.Count; n++)
        {
            var g = Glyphs[letters[n]];
            for (var row = 0; row < 7; row++)
                for (var col = 0; col < 5; col++)
                    if (g[row][col] == '#')
                        for (var dy = 0; dy < cell; dy++)
                            for (var dx = 0; dx < cell; dx++)
                            {
                                int x = x0 + (n * 6 + col) * cell + dx, y = y0 + row * cell + dy;
                                if (x < 0 || y < 0 || x >= size || y >= size) continue;
                                var i = (y * size + x) * 3;
                                for (var c = 0; c < 3; c++) px[i + c] = (byte)(px[i + c] + (255 - px[i + c]) * 0.9);   // white at 90 %
                            }
        }
        return Encode(px, size, size);
    }

    static (int, int, int) Rgb(string hex) => (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

    /// <summary>An 8-bit RGB PNG of [rgb] (no filter, zlib).</summary>
    public static byte[] Encode(byte[] rgb, int width, int height)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (var y = 0; y < height; y++) { z.WriteByte(0); z.Write(rgb, y * width * 3, width * 3); }
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)width); BE(ihdr, 4, (uint)height); ihdr[8] = 8; ihdr[9] = 2;   // 8-bit, RGB
        Chunk(png, "IHDR", ihdr); Chunk(png, "IDAT", raw.ToArray()); Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static void BE(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var head = new byte[8]; BE(head, 0, (uint)data.Length); for (var i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        s.Write(head); s.Write(data);
        var crc = Crc(head.AsSpan(4), 0xFFFFFFFFu); crc = Crc(data, crc) ^ 0xFFFFFFFFu;
        var c = new byte[4]; BE(c, 0, crc); s.Write(c);
    }

    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n => { var c = (uint)n; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; return c; }).ToArray();
    static uint Crc(ReadOnlySpan<byte> d, uint c) { foreach (var b in d) c = Table[(c ^ b) & 0xFF] ^ (c >> 8); return c; }
}
