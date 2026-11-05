using System.Diagnostics;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Item 13: a song's level envelope for the vibe pass and the DJ's intro, from any format: FLAC from its frame sizes (no decoding); MP3, M4A, AAC,
/// OGG, Opus, WMA and WebM decoded by ffmpeg (in the server image) to mono 8 kHz PCM and measured (<see cref="WaveShape.PcmLevels"/>). The file goes
/// through a temporary file (an M4A whose index is at the end cannot be read from a pipe), which is deleted at once.
/// </summary>
public static class AudioLevels
{
    public static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase) { "flac", "mp3", "m4a", "aac", "ogg", "oga", "opus", "wma", "webm", "mp4" };
    public const int Rate = 8000;
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);
    /// <summary>The ffmpeg to run (FLACIE_FFMPEG, else "ffmpeg" on the path).</summary>
    public static string Ffmpeg { get; set; } = Environment.GetEnvironmentVariable("FLACIE_FFMPEG") is { Length: > 0 } f ? f : "ffmpeg";

    public static bool Readable(Track t) => Formats.Contains(MediaInfo.FromExtension(t.FilePath ?? t.Path));

    public static async Task<(float[] Levels, double SecondsPerLevel)?> OfAsync(byte[] bytes, string extension, CancellationToken ct)
    {
        if (extension.Equals("flac", StringComparison.OrdinalIgnoreCase) || (bytes.Length > 4 && bytes[0] == 'f' && bytes[1] == 'L' && bytes[2] == 'a' && bytes[3] == 'C'))
            return WaveShape.FrameLevels(bytes);
        var tmp = Path.Combine(Path.GetTempPath(), "flacie-vibe-" + Guid.NewGuid().ToString("N") + "." + (Formats.Contains(extension) ? extension : "bin"));
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            var psi = new ProcessStartInfo(Ffmpeg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "-v", "error", "-nostdin", "-i", tmp, "-vn", "-f", "s16le", "-ac", "1", "-ar", Rate.ToString(), "pipe:1" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(Limit);
            using var pcm = new MemoryStream();
            var err = p.StandardError.ReadToEndAsync(limit.Token);
            try { await p.StandardOutput.BaseStream.CopyToAsync(pcm, limit.Token); await p.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch (Exception) { } throw; }
            _ = await err;
            return p.ExitCode == 0 ? WaveShape.PcmLevels(pcm.GetBuffer().AsSpan(0, (int)pcm.Length), Rate) : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }   // no ffmpeg on this machine
        finally { try { File.Delete(tmp); } catch (IOException) { } }
    }
}
