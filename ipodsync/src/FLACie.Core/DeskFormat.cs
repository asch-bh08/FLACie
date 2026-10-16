using System.Globalization;

namespace FLACie.Core;

/// <summary>The iPod desk's Now Playing card: one line saying what the file is ("FLAC · 24-bit / 96 kHz · 2,304 kbps"). From what FLACie read from the file
/// when it has, else the extension and a bit rate worked out from the file's size and length (marked "~"). Pure and unit-tested.</summary>
public static class DeskFormat
{
    public static string Line(MediaInfo? info, Track t)
    {
        if (info is { Known: true } i)
        {
            var parts = new List<string> { Name(i.Format) };
            if (i.Sampling.Length > 0) parts.Add(i.Sampling);
            if (i.BitrateKbps > 0) parts.Add($"{i.BitrateKbps.ToString("N0", CultureInfo.InvariantCulture)} kbps");
            return string.Join(" · ", parts.Where(p => p.Length > 0));
        }
        var ext = Name(MediaInfo.FromExtension(t.FilePath ?? t.Path).ToUpperInvariant());
        if (ext.Length == 0) return "";
        if (t.Size > 0 && t.DurationMs > 0)
        {
            var kbps = (int)Math.Round(t.Size * 8.0 / t.DurationMs);   // (bits per millisecond = kbit/s)
            return $"{ext} · ~{kbps.ToString("N0", CultureInfo.InvariantCulture)} kbps";
        }
        return ext;
    }

    /// <summary>The same for a small display (a car stereo's or a MiniDisc's dot matrix, a Discman remote's LCD): "FLAC 24/96 2304K", "MP3 44.1 320K"; not
    /// read yet, the extension and "~1000K" from the file's size and length.</summary>
    public static string Short(MediaInfo? info, Track t)
    {
        if (info is { Known: true } i)
        {
            var parts = new List<string> { Name(i.Format) };
            if (i.SampleRateHz > 0) parts.Add((i.BitDepth > 0 ? $"{i.BitDepth}/" : "") + (i.SampleRateHz % 1000 == 0 ? $"{i.SampleRateHz / 1000}" : (i.SampleRateHz / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)));
            if (i.BitrateKbps > 0) parts.Add($"{i.BitrateKbps.ToString(CultureInfo.InvariantCulture)}K");
            return string.Join(" ", parts.Where(p => p.Length > 0));
        }
        var ext = Name(MediaInfo.FromExtension(t.FilePath ?? t.Path).ToUpperInvariant());
        if (ext.Length == 0) return "";
        return t.Size > 0 && t.DurationMs > 0 ? $"{ext} ~{((int)Math.Round(t.Size * 8.0 / t.DurationMs)).ToString(CultureInfo.InvariantCulture)}K" : ext;
    }

    static string Name(string f) => f switch { "MPEG" or "MPEGAUDIO" => "MP3", "PCM_S16LE" or "PCM_S24LE" or "PCM_S32LE" or "PCM" => "WAV", _ => f };
}
