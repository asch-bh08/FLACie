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

        // how well the onsets line up with themselves at each gap, up to 8 seconds. A real file has a level only every tenth of a second, so each hit
        // lands up to half a level early or late: the curve is smoothed over neighbouring gaps so a beat that falls 5, 5, 6, 5, 6 levels apart still adds up.
        var span = Math.Min(onset.Length - 2, (int)(8 / secondsPerLevel));
        if (span < 60 / MinBpm / secondsPerLevel) return new Vibe(energy, loudness, 0, 0);
        var raw = new double[span + 2];
        for (var lag = 0; lag < raw.Length; lag++) raw[lag] = Ac(onset, lag) / r0;
        var ac = new double[raw.Length];
        for (var lag = 1; lag < raw.Length - 1; lag++) ac[lag] = 0.25 * raw[lag - 1] + 0.5 * raw[lag] + 0.25 * raw[lag + 1];
        double At(double lag) { var i = (int)lag; var f = lag - i; return i + 1 >= ac.Length ? 0 : ac[i] * (1 - f) + ac[i + 1] * f; }

        // the beat: the tempo (60 to 180 BPM, in quarter steps) whose every multiple, out to 8 seconds, lines up best. Summing the multiples pins the
        // tempo far finer than one level, and a quicker tempo has more multiples, so of a beat and every other beat the beat wins.
        double bestBpm = 0, bestScore = 0;
        for (var b = MinBpm; b <= MaxBpm; b += 0.25)
        {
            var period = 60 / b / secondsPerLevel; var score = 0.0;
            for (var m = 1; m * period <= span; m++) score += At(m * period);
            if (score > bestScore) { bestScore = score; bestBpm = b; }
        }
        if (bestBpm == 0) return new Vibe(energy, loudness, 0, 0);
        var p = 60 / bestBpm / secondsPerLevel;

        // eighth notes between the beats look like a beat twice as fast: when every other hit is clearly stronger, those are the beat
        if (bestBpm / 2 >= MinBpm && Alternation(onset, p) < 0.9) { bestBpm /= 2; p *= 2; }

        // a beat you can dance to is strong (the onsets line up well) and steady (they still line up two beats on)
        double Peak(double lag) { var i = (int)Math.Round(lag); return i + 1 >= raw.Length ? 0 : Math.Max(raw[i - 1], Math.Max(raw[i], raw[i + 1])); }
        var strength = Math.Clamp(Peak(p), 0, 1);
        var steady = strength > 1e-9 ? Math.Clamp(Peak(2 * p) / strength, 0, 1) : 0;
        return new Vibe(energy, loudness, bestBpm, strength * steady);
    }

    /// <summary>The weaker of the two halves of the hits [period] apart (every other hit against the rest) over the stronger: 1 when they are even.</summary>
    static double Alternation(double[] onset, double period)
    {
        // the phase the hits fall on: the offset whose hits add up the most
        double Hits(double phase, int parity)
        {
            var s = 0.0;
            for (var k = parity; phase + k * period < onset.Length - 1; k += 2)
            {
                var i = (int)Math.Round(phase + k * period);
                s += Math.Max(0, Math.Max(onset[Math.Max(0, i - 1)], Math.Max(onset[i], onset[Math.Min(onset.Length - 1, i + 1)])));
            }
            return s;
        }
        double bestPhase = 0, best = -1;
        for (var ph = 0; ph < period; ph++) { var s = Hits(ph, 0) + Hits(ph, 1); if (s > best) { best = s; bestPhase = ph; } }
        double even = Hits(bestPhase, 0), odd = Hits(bestPhase, 1);
        return Math.Max(even, odd) < 1e-9 ? 1 : Math.Min(even, odd) / Math.Max(even, odd);
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
