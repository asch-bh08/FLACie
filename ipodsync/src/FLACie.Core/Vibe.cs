namespace FLACie.Core;

/// <summary>
/// How a song feels to listen to, worked out from how loud it is over time: Energy (how full and busy it sounds, 0-1), Loudness (how loud its loud
/// parts are, 0-1), Bpm (its beat, 0 when it has no steady one) and Dance (how strong and steady that beat is, 0-1). Recommendations use it to pick
/// a next song that fits the one playing by feel, not by genre.
/// </summary>
public sealed record Vibe(double Energy, double Loudness, double Bpm, double Dance);

public static class VibeMath
{
    public const double MinBpm = 60, MaxBpm = 180;

    /// <summary>The vibe of a song from its level envelope (0-1 each), <paramref name="secondsPerLevel"/> apart.</summary>
    public static Vibe FromLevels(ReadOnlySpan<float> levels, double secondsPerLevel)
    {
        if (levels.Length == 0 || secondsPerLevel <= 0) return new Vibe(0, 0, 0, 0);
        var sum = 0.0; foreach (var l in levels) sum += Math.Clamp(l, 0, 1);
        var energy = sum / levels.Length;
        var sorted = levels.ToArray(); Array.Sort(sorted);
        var loudness = Math.Clamp(sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .95))], 0, 1);

        // the onsets: where the sound jumps up (a drum hit, a strummed chord), with their average taken off so a steady hum counts for nothing
        var onset = new double[levels.Length];
        for (var i = 1; i < levels.Length; i++) onset[i] = Math.Max(0, levels[i] - levels[i - 1]);
        var mean = onset.Average();
        for (var i = 0; i < onset.Length; i++) onset[i] -= mean;
        var r0 = Ac(onset, 0);
        if (r0 < 1e-9) return new Vibe(energy, loudness, 0, 0);

        // the beat: the gap (between 60 and 180 BPM) at which the onsets line up with themselves best. The plain sum has fewer terms at longer
        // gaps, so of two equally good gaps (a beat and every other beat) the quicker one wins.
        var minLag = Math.Max(1, (int)Math.Floor(60 / MaxBpm / secondsPerLevel));
        var maxLag = Math.Min(onset.Length - 2, (int)Math.Ceiling(60 / MinBpm / secondsPerLevel));
        if (maxLag <= minLag) return new Vibe(energy, loudness, 0, 0);
        int best = -1; var bestR = 0.0;
        for (var lag = minLag; lag <= maxLag; lag++) { var r = Ac(onset, lag); if (r > bestR) { bestR = r; best = lag; } }
        if (best < 0) return new Vibe(energy, loudness, 0, 0);
        // the true gap sits between whole levels: a parabola through the peak and its neighbours finds it
        double y0 = Ac(onset, best - 1), y2 = Ac(onset, best + 1), den = y0 - 2 * bestR + y2;
        var exact = best + (Math.Abs(den) > 1e-12 ? Math.Clamp(0.5 * (y0 - y2) / den, -0.5, 0.5) : 0);
        var bpm = Math.Clamp(60 / (exact * secondsPerLevel), MinBpm, MaxBpm);

        // a beat you can dance to is strong (the onsets line up well) and steady (they still line up two beats on)
        var strength = Math.Clamp(bestR / r0, 0, 1);
        var steady = 2 * best < onset.Length ? Math.Clamp(Ac(onset, 2 * best) / bestR, 0, 1) : 0;
        return new Vibe(energy, loudness, bpm, strength * steady);
    }

    static double Ac(double[] x, int lag)
    {
        var s = 0.0;
        for (var i = 0; i + lag < x.Length; i++) s += x[i] * x[i + lag];
        return s;
    }

    /// <summary>How differently two songs feel, 0 (the same) to 1. Tempo is compared up to doubling, so a 64 BPM song sits close to a 128 BPM one
    /// (you can dance half-time to either); a song with no known beat leaves tempo out.</summary>
    public static double Distance(Vibe a, Vibe b)
    {
        double wE = .4, wL = .3, wT = .2, wD = .1;
        var d = wE * Math.Abs(a.Energy - b.Energy) + wL * Math.Abs(a.Loudness - b.Loudness) + wD * Math.Abs(a.Dance - b.Dance);
        if (a.Bpm <= 0 || b.Bpm <= 0) return Math.Clamp(d / (1 - wT), 0, 1);
        var octaves = Math.Log2(a.Bpm / b.Bpm);
        var off = Math.Abs(octaves - Math.Round(octaves));   // 0 to half an octave
        return Math.Clamp(d + wT * off / 0.5, 0, 1);
    }
}
