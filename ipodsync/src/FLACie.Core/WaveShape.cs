namespace FLACie.Core;

/// <summary>
/// The seek bar's shape (200 bars, 0-255: how loud each part of a song is) worked out on the server from a FLAC file without decoding it. Every frame of a FLAC
/// file holds a fixed number of samples, and a loud, busy passage packs into bigger frames than a quiet one, so the size of the frames over time is the song's
/// shape. A browser that decodes the song sends an exact shape later, which takes its place; this one is for songs nobody has played on a PC yet (a phone
/// cannot decode a whole Hi-Res file, and Jellyfin here does not convert).
/// </summary>
public static class WaveShape
{
    public const int Bars = 200;

    /// <summary>The shape of a FLAC file, or null when it is not one (or holds no frames).</summary>
    public static int[]? FromFlac(ReadOnlySpan<byte> f)
    {
        if (f.Length < 42 || f[0] != 'f' || f[1] != 'L' || f[2] != 'a' || f[3] != 'C') return null;
        // the metadata blocks: each has a 4-byte header (last-block flag, type, 24-bit length); STREAMINFO gives the block size when it is fixed
        var at = 4; var fixedBlock = 0;
        while (at + 4 <= f.Length)
        {
            var last = (f[at] & 0x80) != 0; var type = f[at] & 0x7f; var len = (f[at + 1] << 16) | (f[at + 2] << 8) | f[at + 3];
            if (type == 0 && at + 4 + 4 <= f.Length) { var min = (f[at + 4] << 8) | f[at + 5]; var max = (f[at + 6] << 8) | f[at + 7]; if (min == max) fixedBlock = min; }
            at += 4 + len;
            if (last) break;
        }
        // the frames: a sync code and a header whose CRC-8 checks out (so a sync pattern inside the audio is not taken for a frame)
        var starts = new List<int>(); var samples = new List<int>();
        for (var i = at; i + 6 < f.Length; i++)
        {
            if (f[i] != 0xFF || (f[i + 1] & 0xFE) != 0xF8) continue;
            if (Header(f, i, fixedBlock) is not { } n) continue;
            starts.Add(i); samples.Add(n);
        }
        if (starts.Count < 2) return null;
        // each frame's bits per sample, laid out over time
        var total = 0L; foreach (var n in samples) total += n;
        if (total <= 0) return null;
        var sum = new double[Bars]; var weight = new double[Bars];
        var t = 0L;
        for (var k = 0; k < starts.Count; k++)
        {
            var size = (k + 1 < starts.Count ? starts[k + 1] : f.Length) - starts[k];
            var bar = (int)Math.Min(Bars - 1, t * Bars / total);
            sum[bar] += size * 8.0; weight[bar] += samples[k];
            t += samples[k];
        }
        var rate = new double[Bars];
        for (var b = 0; b < Bars; b++) rate[b] = weight[b] > 0 ? sum[b] / weight[b] : (b > 0 ? rate[b - 1] : 0);
        // a quiet stretch still costs a few bits a sample, so the floor and the top are set from the song itself (the 2nd and 98th percentiles)
        var sorted = rate.OrderBy(v => v).ToArray();
        double lo = sorted[(int)(Bars * .02)], hi = sorted[(int)(Bars * .98)];
        if (hi - lo < 1e-9) return Enumerable.Repeat(128, Bars).ToArray();
        return rate.Select(v => (int)Math.Round(255 * Math.Clamp((v - lo) / (hi - lo), 0, 1))).ToArray();
    }

    /// <summary>The number of samples in the frame whose header starts at [i], or null when the header is not a valid one.</summary>
    static int? Header(ReadOnlySpan<byte> f, int i, int fixedBlock)
    {
        var p = i + 2;
        var bsCode = f[p] >> 4; var srCode = f[p] & 0x0F; p++;
        if (srCode == 0x0F || bsCode == 0) return null;
        var chan = f[p] >> 4; if (chan > 10 || (f[p] & 1) != 0) return null; p++;
        // the frame or sample number, UTF-8 coded (1 to 7 bytes)
        var lead = f[p]; var extra = lead < 0x80 ? 0 : lead >= 0xFE ? 6 : lead >= 0xFC ? 5 : lead >= 0xF8 ? 4 : lead >= 0xF0 ? 3 : lead >= 0xE0 ? 2 : lead >= 0xC0 ? 1 : -1;
        if (extra < 0 || p + 1 + extra >= f.Length) return null;
        for (var k = 1; k <= extra; k++) if ((f[p + k] & 0xC0) != 0x80) return null;
        p += 1 + extra;
        int n;
        if (bsCode == 1) n = 192;
        else if (bsCode <= 5) n = 576 << (bsCode - 2);
        else if (bsCode == 6) { if (p >= f.Length) return null; n = f[p] + 1; p++; }
        else if (bsCode == 7) { if (p + 1 >= f.Length) return null; n = ((f[p] << 8) | f[p + 1]) + 1; p += 2; }
        else n = 256 << (bsCode - 8);
        if (srCode == 12) p++; else if (srCode is 13 or 14) p += 2;
        if (p >= f.Length) return null;
        // CRC-8 (polynomial 0x07) over the header, which must match the byte after it
        byte crc = 0;
        for (var k = i; k < p; k++) { crc ^= f[k]; for (var b = 0; b < 8; b++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1); }
        if (crc != f[p]) return null;
        // a fixed-block-size stream never has a frame larger than its block size (only the last one may be shorter)
        if (fixedBlock > 0 && n > fixedBlock) return null;
        return n;
    }
}
