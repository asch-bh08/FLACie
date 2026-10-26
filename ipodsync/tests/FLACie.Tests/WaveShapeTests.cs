using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The seek bar's shape worked out by the server for a FLAC file nobody has sent a shape for yet (a phone cannot decode a whole Hi-Res file to make one):
/// every FLAC frame holds the same length of sound, and a loud passage packs into bigger frames than a quiet one, so the frame sizes give the song's shape
/// without decoding it.
/// </summary>
public class WaveShapeTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAudio", name));

    [Fact]
    public void A_quiet_first_half_and_a_loud_second_half_show_as_low_then_high_bars()
    {
        var bars = WaveShape.FromFlac(Fixture("quiet-then-loud.flac"));
        Assert.NotNull(bars);
        Assert.Equal(200, bars!.Length);
        Assert.All(bars, b => Assert.InRange(b, 0, 255));
        var first = bars.Take(90).Average(b => (double)b); var second = bars.Skip(110).Average(b => (double)b);
        Assert.True(second > first + 100, $"quiet half {first:0}, loud half {second:0}");
    }

    [Fact]
    public void Frame_levels_are_fine_enough_to_hear_a_beat_and_follow_the_loudness()
    {
        var (levels, step) = WaveShape.FrameLevels(Fixture("quiet-then-loud.flac"))!.Value;
        Assert.InRange(step, 0.001, 0.2);   // a beat at 180 BPM is a third of a second
        var half = levels.Length / 2;
        Assert.True(levels.Skip(half + half / 5).Average() > levels.Take(half - half / 5).Average() + 0.4);
        Assert.Null(WaveShape.FrameLevels([]));
    }

    [Fact]
    public void Anything_that_is_not_flac_has_no_shape_here()
    {
        Assert.Null(WaveShape.FromFlac(new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0 }));
        Assert.Null(WaveShape.FromFlac([]));
    }
}
