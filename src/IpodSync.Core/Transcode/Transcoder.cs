using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IpodSync.Core.Transcode;

/// <summary>
/// Transcode-on-add: turns a file the iPod can't play (FLAC, Ogg, Opus, WMA, APE,
/// WavPack, ...) into one it can, using a local ffmpeg.
///
/// - Lossless sources → Apple Lossless (.m4a), 16-bit, stereo, 44.1 or 48 kHz
///   (whichever family the source is in). The nano 5G already holds iTunes-synced
///   ALAC at both rates.
/// - Lossy sources → AAC 256 kbps (.m4a), same rate rule.
/// - Tags (title/artist/album/album artist/genre/composer/date/track/disc) are
///   carried into MP4 atoms; embedded pictures are not (artwork is its own feature).
///
/// Output lands in a content-addressed cache keyed by the source file's SHA-256 and
/// the settings, and ffmpeg runs in bit-exact mode, so a dry run and the following
/// real write use the identical file and a re-sync never re-transcodes.
/// Every output is probed and refused unless codec, channel count, sample rate and
/// duration are what was asked for.
/// </summary>
public static class Transcoder
{
    public sealed record Result(string OutputPath, string SourcePath, string Codec, int SampleRate, int Channels,
        double DurationSeconds, bool FromCache, string Summary);

    private static readonly HashSet<string> NativeExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".m4b", ".aac", ".mp4", ".wav", ".aif", ".aiff" };

    private static readonly HashSet<string> LosslessCodecs =
        new(StringComparer.OrdinalIgnoreCase) { "flac", "alac", "ape", "wavpack", "tta", "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_s16be", "pcm_s24be", "pcm_f32le", "mlp", "truehd", "dsd_lsbf", "dsd_msbf", "dsd_lsbf_planar", "dsd_msbf_planar" };

    /// <summary>MP4 audio is only native when it's AAC or ALAC.</summary>
    public static bool NeedsTranscode(string path, out string reason)
    {
        string ext = Path.GetExtension(path);
        if (!NativeExtensions.Contains(ext)) { reason = $"{ext} is not an iPod-playable container"; return true; }
        if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var probe = Probe(path);
                if (probe.Codec is not ("aac" or "alac")) { reason = $"MP4 audio codec '{probe.Codec}' is not AAC/ALAC"; return true; }
            }
            catch { /* no ffprobe: trust the container, as before transcode existed */ }
        }
        reason = "";
        return false;
    }

    public static string CacheDir => Environment.GetEnvironmentVariable("IPODSYNC_TRANSCODE_CACHE")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ipodsync", "transcode");

    public static Result Transcode(string source, string mode = "auto")
    {
        var probe = Probe(source);
        bool lossless = mode switch
        {
            "alac" => true,
            "aac" => false,
            _ => LosslessCodecs.Contains(probe.Codec),
        };
        // 44.1 kHz family (22.05/88.2/176.4) -> 44100; 48 kHz family (32/96/192) -> 48000.
        int rate = probe.SampleRate > 0 && probe.SampleRate % 11025 != 0 && probe.SampleRate % 8000 == 0 ? 48000 : 44100;

        string settings = lossless ? $"alac-s16-{rate}-2ch-v2" : $"aac-256k-{rate}-2ch-v2";
        string key;
        using (var fs = File.OpenRead(source)) key = Convert.ToHexString(SHA256.HashData(fs))[..32];
        Directory.CreateDirectory(CacheDir);
        string output = Path.Combine(CacheDir, $"{key}-{settings}.m4a");

        bool fromCache = File.Exists(output);
        if (!fromCache)
        {
            string tmp = output + ".partial";
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", source,
                // Ogg/Opus carry tags on the audio stream, most containers on the format.
                "-map", "0:a:0", "-vn", "-map_metadata", probe.TagsOnStream ? "0:s:a:0" : "0",
                "-ac", "2", "-ar", rate.ToString(CultureInfo.InvariantCulture),
                "-fflags", "+bitexact", "-flags:a", "+bitexact" };
            if (lossless) args.AddRange(["-c:a", "alac", "-sample_fmt", "s16p"]);
            else args.AddRange(["-c:a", "aac", "-b:a", "256k"]);
            // Vorbis "totaltracks"/"totaldiscs" aren't mapped by ffmpeg; fold them into N/M.
            if (probe.Tags.TryGetValue("track", out var tr) && !tr.Contains('/') && probe.Tags.TryGetValue("totaltracks", out var tt))
                args.AddRange(["-metadata", $"track={tr}/{tt}"]);
            if (probe.Tags.TryGetValue("disc", out var dn) && !dn.Contains('/') && probe.Tags.TryGetValue("totaldiscs", out var td))
                args.AddRange(["-metadata", $"disc={dn}/{td}"]);
            args.AddRange(["-movflags", "+faststart", "-f", "ipod", tmp]);

            var (code, err) = Run(Tool("ffmpeg"), args);
            if (code != 0 || !File.Exists(tmp)) throw new InvalidOperationException($"ffmpeg failed ({code}): {err.Trim()}");
            File.Move(tmp, output, overwrite: true);
        }

        var outProbe = Probe(output);
        string wantCodec = lossless ? "alac" : "aac";
        var problems = new List<string>();
        if (outProbe.Codec != wantCodec) problems.Add($"codec {outProbe.Codec} != {wantCodec}");
        if (outProbe.Channels != 2) problems.Add($"{outProbe.Channels} channels");
        if (outProbe.SampleRate != rate) problems.Add($"sample rate {outProbe.SampleRate} != {rate}");
        if (Math.Abs(outProbe.Duration - probe.Duration) > 0.25) problems.Add($"duration {outProbe.Duration:F2}s vs source {probe.Duration:F2}s");
        if (problems.Count > 0)
        {
            try { File.Delete(output); } catch { }
            throw new InvalidOperationException("transcode output rejected: " + string.Join("; ", problems));
        }

        string summary = $"{probe.Codec} {probe.SampleRate} Hz/{probe.Channels}ch -> {wantCodec} {rate} Hz/2ch{(lossless ? " 16-bit" : " 256k")} " +
                         $"({outProbe.Duration:F2}s{(fromCache ? ", cached" : "")})";
        return new Result(output, source, wantCodec, rate, 2, outProbe.Duration, fromCache, summary);
    }

    // ------------------------------------------------------------------ ffprobe / process

    public sealed record ProbeResult(string Codec, int SampleRate, int Channels, double Duration, Dictionary<string, string> Tags, bool TagsOnStream);

    public static ProbeResult Probe(string path)
    {
        var (code, output) = Run(Tool("ffprobe"),
            ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_name,sample_rate,channels:stream_tags:format=duration:format_tags", "-of", "json", path],
            captureStdout: true);
        if (code != 0) throw new InvalidOperationException($"ffprobe failed on {path}: {output.Trim()}");
        using var doc = JsonDocument.Parse(output);
        var stream = doc.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault();
        if (stream.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"no audio stream in {path}");
        var format = doc.RootElement.GetProperty("format");
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool onStream = false;
        if (format.TryGetProperty("tags", out var t))
            foreach (var p in t.EnumerateObject()) tags[p.Name] = p.Value.GetString() ?? "";
        if (!tags.ContainsKey("title") && stream.TryGetProperty("tags", out var st))
        {
            foreach (var p in st.EnumerateObject()) tags[p.Name] = p.Value.GetString() ?? "";
            onStream = tags.ContainsKey("title") || tags.ContainsKey("artist");
        }
        return new ProbeResult(
            stream.GetProperty("codec_name").GetString() ?? "",
            int.TryParse(stream.TryGetProperty("sample_rate", out var sr) ? sr.GetString() : "0", out var r) ? r : 0,
            stream.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0,
            double.TryParse(format.TryGetProperty("duration", out var d) ? d.GetString() : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var dur) ? dur : 0,
            tags, onStream);
    }

    public static (int Code, string Error) RunFfmpeg(IEnumerable<string> args) => Run(Tool("ffmpeg"), args);

    /// <summary>First video stream (an image file, or an audio file's attached cover).</summary>
    public static (string Codec, int Width, int Height)? ProbeVideo(string path)
    {
        var (code, output) = Run(Tool("ffprobe"),
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,width,height", "-of", "json", path],
            captureStdout: true);
        if (code != 0) return null;
        using var doc = JsonDocument.Parse(output);
        var stream = doc.RootElement.TryGetProperty("streams", out var ss) ? ss.EnumerateArray().FirstOrDefault() : default;
        if (stream.ValueKind != JsonValueKind.Object) return null;
        return (stream.GetProperty("codec_name").GetString() ?? "",
                stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0);
    }

    private static bool? _available;

    /// <summary>True when ffmpeg and ffprobe can be started (PATH or IPODSYNC_FFMPEG/IPODSYNC_FFPROBE).
    /// Without them, adds are limited to iPod-native files and covers can't be converted.</summary>
    public static bool FfmpegAvailable()
    {
        if (_available is bool known) return known;
        try { _available = Run(Tool("ffmpeg"), ["-version"]).Code == 0 && Run(Tool("ffprobe"), ["-version"]).Code == 0; }
        catch { _available = false; }
        return _available.Value;
    }

    private static string Tool(string name)
    {
        string? env = Environment.GetEnvironmentVariable(name == "ffmpeg" ? "IPODSYNC_FFMPEG" : "IPODSYNC_FFPROBE");
        return string.IsNullOrEmpty(env) ? name : env;
    }

    private static (int Code, string Output) Run(string exe, IEnumerable<string> args, bool captureStdout = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"could not start {exe}");
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(10))) { try { p.Kill(true); } catch { } throw new TimeoutException($"{exe} timed out"); }
        return (p.ExitCode, captureStdout ? stdout.Result : stderr.Result);
    }
}
