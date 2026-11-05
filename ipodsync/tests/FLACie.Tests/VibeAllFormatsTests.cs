using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>Item 13: the vibe pass reads MP3, M4A, AAC, OGG, Opus and WMA songs too (not only FLAC), so they get a vibe and a DJ intro.</summary>
public class VibeAllFormatsTests
{
    static string Fixture(string ext) => Path.Combine(AppContext.BaseDirectory, "TestAudio", "quiet-then-loud." + ext);

    [Theory]
    [InlineData("/m/a.flac", true)] [InlineData("/m/a.mp3", true)] [InlineData("/m/a.m4a", true)] [InlineData("/m/a.aac", true)]
    [InlineData("/m/a.ogg", true)] [InlineData("/m/a.opus", true)] [InlineData("/m/a.wma", true)] [InlineData("/m/a.txt", false)] [InlineData("/m/a.jpg", false)]
    public void Every_common_format_is_taken_by_the_pass(string path, bool taken) =>
        Assert.Equal(taken, AudioLevels.Readable(new Track(path, "T", "A", "B", "A", 1, 1, 200_000, 2000, null, 0, TrackSource.Jellyfin, path)));

    [Fact]
    public void Decoded_sound_gives_a_level_envelope_quiet_then_loud()
    {
        // 4 s quiet, then 4 s loud, at 8 kHz mono
        const int rate = 8000;
        var pcm = new byte[rate * 8 * 2];
        for (var i = 0; i < rate * 8; i++) { var amp = i < rate * 4 ? 300 : 20000; var v = (short)(amp * Math.Sin(i * 2 * Math.PI * 440 / rate)); pcm[2 * i] = (byte)v; pcm[2 * i + 1] = (byte)(v >> 8); }
        var (levels, step) = WaveShape.PcmLevels(pcm, rate)!.Value;
        Assert.Equal(0.1, step, 3);
        Assert.Equal(80, levels.Length);
        Assert.True(levels.Take(35).Average() < 0.2 && levels.Skip(45).Average() > 0.8);
        Assert.Null(WaveShape.PcmLevels(new byte[100], rate));   // under a second
    }

    static bool HasFfmpeg()
    {
        try { using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AudioLevels.Ffmpeg, "-version") { RedirectStandardOutput = true, UseShellExecute = false }); p!.WaitForExit(5000); return p.ExitCode == 0; }
        catch (Exception) { return false; }
    }

    [Theory]
    [InlineData("mp3")] [InlineData("m4a")] [InlineData("ogg")] [InlineData("opus")] [InlineData("wma")] [InlineData("flac")]
    public async Task Each_format_is_read_into_the_same_quiet_then_loud_shape(string ext)
    {
        var got = await AudioLevels.OfAsync(await File.ReadAllBytesAsync(Fixture(ext)), ext, default);
        if (ext != "flac" && !HasFfmpeg()) { Assert.Null(got); return; }   // a machine without ffmpeg: the song is left for later, nothing breaks
        var (levels, step) = got!.Value;
        Assert.InRange(levels.Length * step, 8, 11);                      // the file is 10 s long
        var half = levels.Length / 2;
        Assert.True(levels.Take(half - 5).Average() < levels.Skip(half + 5).Average(), $"{ext}: quiet first, loud after");
        var vibe = VibeFillService.Analyse(levels, step, null);
        Assert.True(vibe.Energy is >= 0 and <= 1);
    }
}
