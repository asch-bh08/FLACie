using System.Buffers.Binary;
using System.Text;

namespace FLACie.Core;

// Apple Lossless (ALAC) playback without anyone else's transcoder. Browsers cannot decode ALAC and many phones have no decoder for it, so the server
// reads the .m4a itself (this file: the MP4 layout, the decoder), and sends plain PCM as a WAV, which every browser and player plays and seeks in.
// The decoder follows the published ALAC bitstream (Apple's open-source reference, Apache-2.0, and the layout every decoder implements); it is checked
// bit for bit against ffmpeg's output on real files (see ipodsync/tests/alac).

/// <summary>Random access to the bytes of a file, wherever it lives (Jellyfin over HTTP, a NAS share, a folder).</summary>
public interface IRangeSource
{
    long Length { get; }
    /// <summary>Reads up to [count] bytes at [offset]; fewer only at the end of the file.</summary>
    Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct);
}

/// <summary>A file read from a byte array (tests and small files).</summary>
public sealed class MemoryRangeSource(byte[] data) : IRangeSource
{
    public long Length => data.Length;
    public Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct)
    {
        if (offset >= data.Length) return Task.FromResult(Array.Empty<byte>());
        var n = (int)Math.Min(count, data.Length - offset);
        return Task.FromResult(data.AsSpan((int)offset, n).ToArray());
    }
}

/// <summary>A file served over HTTP with Range requests (Jellyfin's stream, the file mover): the caller makes each request (so it carries the right credentials).</summary>
public sealed class HttpRangeSource : IRangeSource
{
    readonly HttpClient http; readonly Func<HttpRequestMessage> make;
    public long Length { get; private set; }
    HttpRangeSource(HttpClient http, Func<HttpRequestMessage> make) { this.http = http; this.make = make; }

    public static async Task<HttpRangeSource> OpenAsync(HttpClient http, Func<HttpRequestMessage> make, CancellationToken ct)
    {
        var s = new HttpRangeSource(http, make);
        using var req = make(); req.Headers.TryAddWithoutValidation("Range", "bytes=0-15");
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        if (res.Content.Headers.ContentRange?.Length is { } len) s.Length = len;
        else if (res.Content.Headers.ContentLength is { } cl && (int)res.StatusCode == 200) s.Length = cl;
        else throw new IOException("the server does not say how long the file is");
        return s;
    }

    public async Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct)
    {
        if (offset >= Length) return [];
        var last = Math.Min(Length - 1, offset + count - 1);
        using var req = make(); req.Headers.TryAddWithoutValidation("Range", $"bytes={offset}-{last}");
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsByteArrayAsync(ct);
        // a server that ignores Range sends the whole file: cut the part asked for out of it
        if ((int)res.StatusCode == 200 && offset > 0) return body.Length > offset ? body.AsSpan((int)offset, (int)Math.Min(count, body.Length - offset)).ToArray() : [];
        return body;
    }
}

/// <summary>What the MP4 says about its Apple Lossless track: the decoder's settings and where each frame is.</summary>
public sealed class AlacInfo
{
    public int FrameLength, Channels, BitDepth, SampleRate, Pb, Mb, Kb;
    public long TotalSamples;
    public long[] FrameOffset = [];
    public int[] FrameSize = [];
    /// <summary>PCM samples in each frame (the last one is usually shorter).</summary>
    public int[] FrameSamples = [];
    /// <summary>Samples before each frame (one more entry than frames, the last is the total).</summary>
    public long[] FrameStart = [];
    public int BytesPerSample => BitDepth <= 16 ? 2 : 3;
    public int BlockAlign => Channels * BytesPerSample;
    public long DataBytes => TotalSamples * BlockAlign;
}

public static class Mp4Alac
{
    /// <summary>Reads the file's index. Null when the file has no Apple Lossless audio (it is AAC or something else: play it as it is).</summary>
    public static async Task<AlacInfo?> ReadAsync(IRangeSource src, CancellationToken ct)
    {
        byte[]? moov = null; long pos = 0;
        while (pos + 8 <= src.Length)
        {
            var h = await src.ReadAsync(pos, 16, ct);
            if (h.Length < 8) return null;
            long size = BinaryPrimitives.ReadUInt32BigEndian(h); var type = Encoding.ASCII.GetString(h, 4, 4); long hdr = 8;
            if (size == 1 && h.Length >= 16) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(h.AsSpan(8)); hdr = 16; }
            else if (size == 0) size = src.Length - pos;
            if (size < hdr) return null;
            if (type == "moov") { if (size > 64 << 20) return null; moov = await src.ReadAsync(pos + hdr, (int)(size - hdr), ct); break; }
            pos += size;
        }
        if (moov is null) return null;
        return ParseMoov(moov);
    }

    static IEnumerable<(string Type, int Start, int End)> Boxes(byte[] d, int start, int end)
    {
        var p = start;
        while (p + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p)); var type = Encoding.ASCII.GetString(d, p + 4, 4); var hdr = 8;
            if (size == 1 && p + 16 <= end) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(p + 8)); hdr = 16; }
            else if (size == 0) size = end - p;
            if (size < hdr || p + size > end) yield break;
            yield return (type, p + hdr, (int)(p + size));
            p += (int)size;
        }
    }
    static (int Start, int End)? Child(byte[] d, (int Start, int End) parent, string type)
    {
        foreach (var b in Boxes(d, parent.Start, parent.End)) if (b.Type == type) return (b.Start, b.End);
        return null;
    }
    static uint U32(byte[] d, int p) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p));

    static AlacInfo? ParseMoov(byte[] d)
    {
        foreach (var trak in Boxes(d, 0, d.Length).Where(b => b.Type == "trak"))
        {
            if (Child(d, (trak.Start, trak.End), "mdia") is not { } mdia || Child(d, mdia, "minf") is not { } minf || Child(d, minf, "stbl") is not { } stbl) continue;
            if (Child(d, stbl, "stsd") is not { } stsd) continue;
            // stsd: version/flags, entry count, then the sample entries
            var entry = Boxes(d, stsd.Start + 8, stsd.End).FirstOrDefault();
            if (entry.Type != "alac") continue;
            // the audio sample entry: 8 bytes (reserved, data reference), then version, revision, vendor, channels, sample size, ..., rate; QuickTime v1/v2 add more
            var version = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(entry.Start + 8));
            var inner = entry.Start + 8 + 20 + (version == 1 ? 16 : version == 2 ? 36 : 0);
            (int Start, int End)? cookieBox = null;
            foreach (var b in Boxes(d, inner, entry.End)) if (b.Type == "alac") { cookieBox = (b.Start, b.End); break; }
            if (cookieBox is not { } cb || cb.End - cb.Start < 28) return null;
            var c = cb.Start + 4;   // skip the box's own version / flags
            var info = new AlacInfo
            {
                FrameLength = (int)U32(d, c), BitDepth = d[c + 5], Pb = d[c + 6], Mb = d[c + 7], Kb = d[c + 8], Channels = d[c + 9], SampleRate = (int)U32(d, c + 20),
            };
            if (info.FrameLength <= 0 || info.FrameLength > 16384 || info.Channels is < 1 or > 2 || info.BitDepth is not (16 or 20 or 24)) return null;   // surround and 32-bit are not handled here

            // frame sizes
            if (Child(d, stbl, "stsz") is not { } stsz) return null;
            var fixedSize = (int)U32(d, stsz.Start + 4); var n = (int)U32(d, stsz.Start + 8);
            var sizes = new int[n];
            for (var i = 0; i < n; i++) sizes[i] = fixedSize != 0 ? fixedSize : (int)U32(d, stsz.Start + 12 + 4 * i);
            // where the chunks are, and how many frames each holds
            var co = Child(d, stbl, "stco"); var is64 = false; if (co is null) { co = Child(d, stbl, "co64"); is64 = true; }
            if (co is not { } stco || Child(d, stbl, "stsc") is not { } stsc) return null;
            var chunks = (int)U32(d, stco.Start + 4);
            var runs = (int)U32(d, stsc.Start + 4);
            var firstChunk = new int[runs]; var perChunk = new int[runs];
            for (var r = 0; r < runs; r++) { firstChunk[r] = (int)U32(d, stsc.Start + 8 + 12 * r); perChunk[r] = (int)U32(d, stsc.Start + 12 + 12 * r); }
            var offsets = new long[n]; var s = 0;
            for (var ch = 1; ch <= chunks && s < n; ch++)
            {
                long off = is64 ? (long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(stco.Start + 8 + 8 * (ch - 1))) : U32(d, stco.Start + 8 + 4 * (ch - 1));
                var run = 0; while (run + 1 < runs && firstChunk[run + 1] <= ch) run++;
                for (var k = 0; k < perChunk[run] && s < n; k++) { offsets[s] = off; off += sizes[s]; s++; }
            }
            if (s < n) return null;
            // how many PCM samples each frame holds
            var samples = new int[n];
            if (Child(d, stbl, "stts") is { } stts)
            {
                var ents = (int)U32(d, stts.Start + 4); var idx = 0;
                for (var e = 0; e < ents && idx < n; e++) { var cnt = (int)U32(d, stts.Start + 8 + 8 * e); var delta = (int)U32(d, stts.Start + 12 + 8 * e); for (var k = 0; k < cnt && idx < n; k++) samples[idx++] = delta; }
                for (; idx < n; idx++) samples[idx] = info.FrameLength;
            }
            else Array.Fill(samples, info.FrameLength);
            var start = new long[n + 1]; for (var i = 0; i < n; i++) start[i + 1] = start[i] + samples[i];
            info.FrameOffset = offsets; info.FrameSize = sizes; info.FrameSamples = samples; info.FrameStart = start; info.TotalSamples = start[n];
            return info;
        }
        return null;
    }
}

/// <summary>Decodes ALAC frames to PCM.</summary>
public sealed class AlacDecoder
{
    readonly AlacInfo cfg;
    readonly int[][] predictErr, output, extra;
    readonly int[] coefs = new int[32];
    public AlacDecoder(AlacInfo info)
    {
        cfg = info;
        predictErr = [new int[info.FrameLength], new int[info.FrameLength]];
        output = [new int[info.FrameLength], new int[info.FrameLength]];
        extra = [new int[info.FrameLength], new int[info.FrameLength]];
    }

    // ---- bits ----
    sealed class Bits(byte[] d, int offset, int length)
    {
        long pos; readonly long end = (long)(offset + length) * 8;
        public int Bit()
        {
            if (pos >= end) throw new InvalidDataException("the frame ended early");
            var b = (d[offset + (int)(pos >> 3)] >> (7 - (int)(pos & 7))) & 1; pos++; return b;
        }
        public uint Read(int n)
        {
            uint v = 0;
            for (var i = 0; i < n;) { var take = Math.Min(8 - (int)(pos & 7), n - i); if (pos + take > end) throw new InvalidDataException("the frame ended early"); var by = d[offset + (int)(pos >> 3)]; var chunk = (by >> (8 - (int)(pos & 7) - take)) & ((1 << take) - 1); v = (v << take) | (uint)chunk; pos += take; i += take; }
            return v;
        }
        public uint Peek(int n) { var p = pos; var v = Read(n); pos = p; return v; }
        public void Skip(int n) => pos += n;
        public int Signed(int n) { var v = (int)Read(n); return n == 32 ? v : (v << (32 - n)) >> (32 - n); }
    }

    static int Log2(uint v) => v == 0 ? 0 : 31 - System.Numerics.BitOperations.LeadingZeroCount(v);
    static int SignExtend(int v, int bits) => (v << (32 - bits)) >> (32 - bits);
    static int SignOnly(int v) => v < 0 ? -1 : v > 0 ? 1 : 0;

    static uint DecodeScalar(Bits b, int k, int bps)
    {
        var x = 0; while (x < 9 && b.Bit() == 1) x++;
        if (x > 8) return b.Read(bps);               // too long a run of ones: the value follows as it is
        if (k != 1)
        {
            var extra = b.Peek(k);
            x = (x << k) - x;                        // times 2^k - 1
            if (extra > 1) { x += (int)extra - 1; b.Skip(k); } else b.Skip(k - 1);
        }
        return (uint)x;
    }

    void RiceDecompress(Bits b, int[] outp, int n, int bps, int mult)
    {
        unchecked
        {
            uint history = (uint)cfg.Mb; var signMod = 0u;
            for (var i = 0; i < n; i++)
            {
                var k = Math.Min(Log2((history >> 9) + 3), cfg.Kb);
                var x = DecodeScalar(b, k, bps) + signMod; signMod = 0;
                outp[i] = (int)(x >> 1) ^ -(int)(x & 1);
                if (x > 0xffff) history = 0xffff; else history = history + x * (uint)mult - ((history * (uint)mult) >> 9);
                if (history < 128 && i + 1 < n)
                {
                    k = Math.Min(7 - Log2(history) + (int)((history + 16) >> 6), cfg.Kb);
                    var block = (int)DecodeScalar(b, k, 16);
                    if (block > 0)
                    {
                        if (block >= n - i) throw new InvalidDataException("a run of zeros runs past the frame");
                        Array.Clear(outp, i + 1, block); i += block;
                    }
                    if (block <= 0xffff) signMod = 1;
                    history = 0;
                }
            }
        }
    }

    // the adaptive predictor (the ffmpeg / Apple "unpc" step): [inp] holds the residuals, [outp] gets the samples
    static void LpcPrediction(int[] inp, int[] outp, int n, int bps, int[]? c, int order, int quant)
    {
        unchecked
        {
            outp[0] = inp[0];
            if (n <= 1) return;
            if (order == 0) { if (!ReferenceEquals(inp, outp)) Array.Copy(inp, 1, outp, 1, n - 1); return; }
            if (order == 31) { for (var i = 1; i < n; i++) outp[i] = SignExtend(outp[i - 1] + inp[i], bps); return; }
            var p = 0;   // index of the oldest sample the prediction looks at
            var j0 = 1;
            for (; j0 <= order && j0 < n; j0++) outp[j0] = SignExtend(outp[j0 - 1] + inp[j0], bps);
            for (var i = j0; i < n; i++)
            {
                var err = inp[i]; var d = outp[p]; p++;
                var val = 0;
                for (var j = 0; j < order; j++) val += (outp[p + j] - d) * c![j];
                val = (val + (1 << (quant - 1))) >> quant;
                val += d + err;
                outp[i] = SignExtend(val, bps);
                var sign = SignOnly(err);
                if (sign != 0)
                {
                    for (var j = 0; j < order && err * sign > 0; j++)
                    {
                        var v = d - outp[p + j];
                        var sg = SignOnly(v) * sign;
                        c![j] -= sg;
                        v *= sg;
                        err -= (v >> quant) * (j + 1);
                    }
                }
            }
        }
    }

    /// <summary>Decodes one frame into [pcm] (interleaved, the file's bit depth in the low bits of each int). Returns the samples per channel.</summary>
    public int DecodeFrame(byte[] data, int offset, int length, int[] pcm)
    {
        var b = new Bits(data, offset, length);
        var done = 0; int total = 0;
        while (true)
        {
            var element = (int)b.Read(3);
            if (element == 7) break;                    // end of the frame
            if (element is not (0 or 1 or 3)) throw new InvalidDataException("an element this decoder does not know");
            var ch = element == 1 ? 2 : 1;
            if (ch > cfg.Channels || done + ch > cfg.Channels) throw new InvalidDataException("more channels than the file says");
            b.Read(4);                                   // element instance tag
            b.Skip(12);
            var hasSize = b.Bit() == 1;
            var extraBits = (int)b.Read(2) << 3;
            var bps = cfg.BitDepth - extraBits + ch - 1;
            var compressed = b.Bit() == 0;
            var n = hasSize ? (int)b.Read(32) : cfg.FrameLength;
            if (n <= 0 || n > cfg.FrameLength) throw new InvalidDataException("a frame longer than the file allows");
            int shift = 0, weight = 0;
            if (compressed)
            {
                shift = (int)b.Read(8); weight = (int)b.Read(8);
                var ptype = new int[2]; var quant = new int[2]; var mult = new int[2]; var order = new int[2]; var cf = new int[2][] { new int[32], new int[32] };
                for (var c = 0; c < ch; c++)
                {
                    ptype[c] = (int)b.Read(4); quant[c] = (int)b.Read(4); mult[c] = (int)b.Read(3); order[c] = (int)b.Read(5);
                    for (var i = order[c] - 1; i >= 0; i--) cf[c][i] = b.Signed(16);     // stored oldest last
                }
                if (extraBits > 0) for (var i = 0; i < n; i++) for (var c = 0; c < ch; c++) extra[c][i] = (int)b.Read(extraBits);
                for (var c = 0; c < ch; c++)
                {
                    RiceDecompress(b, predictErr[c], n, bps, mult[c] * cfg.Pb / 4);
                    if (ptype[c] == 15) LpcPrediction(predictErr[c], predictErr[c], n, bps, null, 31, 0);
                    LpcPrediction(predictErr[c], output[c], n, bps, cf[c], order[c], quant[c]);
                }
            }
            else
            {
                extraBits = 0;
                for (var i = 0; i < n; i++) for (var c = 0; c < ch; c++) output[c][i] = b.Signed(cfg.BitDepth);
            }
            if (ch == 2 && weight != 0)
                for (var i = 0; i < n; i++) { var a = output[0][i]; var bb = output[1][i]; a -= (bb * weight) >> shift; bb += a; output[0][i] = bb; output[1][i] = a; }
            if (extraBits > 0) for (var c = 0; c < ch; c++) for (var i = 0; i < n; i++) output[c][i] = (output[c][i] << extraBits) | extra[c][i];
            for (var c = 0; c < ch; c++) for (var i = 0; i < n; i++) pcm[i * cfg.Channels + done + c] = output[c][i];
            done += ch; total = n;
        }
        if (done != cfg.Channels) throw new InvalidDataException("the frame has fewer channels than the file says");
        return total;
    }
}

/// <summary>Streams an Apple Lossless file as a WAV: the header is exact from the start, so a player can seek anywhere with Range requests.</summary>
public static class AlacWav
{
    public static byte[] Header(AlacInfo i)
    {
        var bits = i.BytesPerSample * 8; var data = i.DataBytes;
        var h = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(h, 0); BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), (uint)Math.Min(uint.MaxValue, 36 + data));
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(h, 8); BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(20), 1); BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(22), (ushort)i.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(24), (uint)i.SampleRate); BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(28), (uint)(i.SampleRate * i.BlockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(32), (ushort)i.BlockAlign); BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(34), (ushort)bits);
        Encoding.ASCII.GetBytes("data").CopyTo(h, 36); BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(40), (uint)Math.Min(uint.MaxValue, data));
        return h;
    }

    public static long TotalLength(AlacInfo i) => 44 + i.DataBytes;

    /// <summary>Writes bytes [from, to] (inclusive, of the whole WAV file) to [output].</summary>
    public static async Task WriteAsync(Stream output, IRangeSource src, AlacInfo info, long from, long to, CancellationToken ct)
    {
        var total = TotalLength(info); to = Math.Min(to, total - 1);
        if (from > to) return;
        if (from < 44) { var h = Header(info); var end = (int)Math.Min(to, 43); await output.WriteAsync(h.AsMemory((int)from, end - (int)from + 1), ct); }
        if (to < 44) return;
        var dataFrom = Math.Max(from, 44) - 44; var dataTo = to - 44;
        var firstSample = dataFrom / info.BlockAlign;
        // the frame holding that sample
        var f = Array.BinarySearch(info.FrameStart, 0, info.FrameStart.Length - 1, firstSample); if (f < 0) f = ~f - 1; f = Math.Max(0, f);
        var skipBytes = (int)((firstSample - info.FrameStart[f]) * info.BlockAlign) + (int)(dataFrom % info.BlockAlign);
        var dec = new AlacDecoder(info); var pcm = new int[info.FrameLength * info.Channels]; var outBuf = new byte[info.FrameLength * info.BlockAlign];
        var remaining = dataTo - dataFrom + 1;
        var frames = info.FrameOffset.Length;
        while (f < frames && remaining > 0)
        {
            // read a run of frames that sit next to each other in one request
            var j = f; long runBytes = 0;
            while (j < frames && runBytes < 512 * 1024 && (j == f || info.FrameOffset[j] == info.FrameOffset[j - 1] + info.FrameSize[j - 1])) { runBytes += info.FrameSize[j]; j++; }
            var blob = await src.ReadAsync(info.FrameOffset[f], (int)runBytes, ct);
            if (blob.Length < runBytes) throw new IOException("the file ended before its last frame");
            long at = 0;
            for (var k = f; k < j && remaining > 0; k++)
            {
                var n = dec.DecodeFrame(blob, (int)at, info.FrameSize[k], pcm); at += info.FrameSize[k];
                var bytes = Pack(pcm, n * info.Channels, info.BitDepth, outBuf);
                var skip = Math.Min(skipBytes, bytes); skipBytes -= skip;
                var take = (int)Math.Min(bytes - skip, remaining);
                if (take > 0) { await output.WriteAsync(outBuf.AsMemory(skip, take), ct); remaining -= take; }
            }
            f = j;
        }
        // the index promised more samples than the frames hold: pad with silence so the length is exactly what the header said
        while (remaining > 0) { var z = (int)Math.Min(remaining, 8192); await output.WriteAsync(new byte[z], ct); remaining -= z; }
    }

    static int Pack(int[] pcm, int count, int depth, byte[] outBuf)
    {
        var o = 0;
        if (depth == 16) for (var i = 0; i < count; i++) { var v = pcm[i]; outBuf[o++] = (byte)v; outBuf[o++] = (byte)(v >> 8); }
        else
        {
            var up = depth == 20 ? 4 : 0;   // 20-bit audio is written as 24-bit
            for (var i = 0; i < count; i++) { var v = pcm[i] << up; outBuf[o++] = (byte)v; outBuf[o++] = (byte)(v >> 8); outBuf[o++] = (byte)(v >> 16); }
        }
        return o;
    }
}
