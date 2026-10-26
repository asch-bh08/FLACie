using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class VibeTests
{
    const double Step = 0.01;   // seconds per level

    /// <summary>[seconds] of levels at [floor], with a hit of [hit] every [beat] seconds.</summary>
    static float[] Clicks(double seconds, double beat, float floor, float hit, double step = Step)
    {
        var l = new float[(int)(seconds / step)];
        for (var i = 0; i < l.Length; i++) l[i] = floor;
        for (var t = 0.0; t < seconds; t += beat) { var i = (int)Math.Round(t / step); if (i < l.Length) l[i] = hit; }
        return l;
    }

    /// <summary>A real CD-quality FLAC file gives a level per 4096-sample frame: about 0.093 s, so a beat is only 3 to 11 levels long.</summary>
    const double FrameStep = 4096.0 / 44100;

    // each hit lands on the nearest frame, up to half a frame (46 ms) early or late, as a real one does: 3 BPM either way is what the ear would call the same tempo
    [Theory]
    [InlineData(120)]
    [InlineData(90)]
    [InlineData(150)]
    public void The_tempo_is_heard_at_real_frame_resolution(double bpm) =>
        Assert.InRange(VibeMath.FromLevels(Clicks(60, 60 / bpm, 0.2f, 1f, FrameStep), FrameStep).Bpm, bpm - 3, bpm + 3);

    [Fact]
    public void Eighth_notes_between_stronger_beats_do_not_double_the_tempo()
    {
        // an 80 BPM beat with a slightly softer hit halfway between each pair (eighth notes): the beat is 80, not 160
        var l = Clicks(30, 60 / 80.0, 0.2f, 1f);
        for (var t = 60 / 160.0; t < 30; t += 60 / 80.0) { var i = (int)Math.Round(t / Step); if (i < l.Length) l[i] = 0.85f; }
        Assert.InRange(VibeMath.FromLevels(l, Step).Bpm, 78, 82);
    }

    [Fact]
    public void A_click_every_half_second_is_120_bpm()
    {
        var v = VibeMath.FromLevels(Clicks(30, 0.5, 0.2f, 1f), Step);
        Assert.InRange(v.Bpm, 118, 122);
        Assert.True(v.Dance > 0.5, $"dance {v.Dance}");
    }

    [Fact]
    public void Half_and_double_tempo_are_close() =>
        Assert.True(VibeMath.Distance(new Vibe(0.5, 0.6, 64, 0.5), new Vibe(0.5, 0.6, 128, 0.5)) < 0.15);

    [Fact]
    public void Quiet_sparse_audio_has_low_energy() =>
        Assert.True(VibeMath.FromLevels(Clicks(30, 0.85, 0.05f, 0.35f), Step).Energy < 0.25);

    [Fact]
    public void Loud_dense_audio_has_high_energy()
    {
        var v = VibeMath.FromLevels(Clicks(30, 60 / 128.0, 0.75f, 1f), Step);
        Assert.True(v.Energy > 0.6, $"energy {v.Energy}");
        Assert.True(v.Loudness > 0.7, $"loudness {v.Loudness}");
    }

    [Fact]
    public void Ballad_and_banger_are_far()
    {
        var ballad = VibeMath.FromLevels(Clicks(30, 0.85, 0.05f, 0.35f), Step);
        var banger = VibeMath.FromLevels(Clicks(30, 60 / 128.0, 0.75f, 1f), Step);
        Assert.True(VibeMath.Distance(ballad, banger) > 0.5, $"distance {VibeMath.Distance(ballad, banger)}");
    }

    [Fact]
    public void The_index_knows_nothing_of_an_unanalysed_song_and_keeps_what_it_learns()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var t = new Track("/m/a.flac", "T", "A", "Al", "A", 1, 1, 1000, 2000, null, 0, TrackSource.Nas);
        var index = new FLACie.Server.VibeIndex(new FLACie.Server.DataPaths(dir));
        Assert.Null(index.Of(t));
        index.Set(t, new Vibe(0.5, 0.6, 120, 0.7));
        index.Save();
        Assert.Equal(new Vibe(0.5, 0.6, 120, 0.7), new FLACie.Server.VibeIndex(new FLACie.Server.DataPaths(dir)).Of(t));
    }

    [Fact]
    public void Silence_has_no_tempo() => Assert.Equal(0, VibeMath.FromLevels(new float[3000], Step).Bpm);
}
