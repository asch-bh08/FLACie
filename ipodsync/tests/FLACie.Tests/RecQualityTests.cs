using FLACie.Core;
using Xunit;
using Xunit.Abstractions;

namespace FLACie.Tests;

/// <summary>
/// The offline evaluation of what plays next (owner, 25 Nov: "I play Heads Will Roll and the next 10-20 songs are by Yeah Yeah Yeahs and not a good
/// recommendation"; "don't add the same song just remixed into the same queue unless it's a full remix queue"). Each test asks the engine as the web
/// player and the DJ do, on <see cref="RecFixture"/>, over several random seeds, and checks the picks by numbers: how many artists, how many by one
/// artist, whether they stay in the scene, whether a song comes back as another version. The numbers are written to the test output.
/// </summary>
public class RecQualityTests(ITestOutputHelper output)
{
    static readonly RecFixture F = new();
    static readonly long[] Seeds = [1, 2, 3, 4, 5, 6, 7, 8];

    static List<Track> Picks(IReadOnlyList<Track> session, long seed, int take = 10) =>
        Engine.Next(F.Lib, F.Taste, t => F.Vibes.GetValueOrDefault(t), session, session.Count > 0 ? F.RelatedOf(session[^1]) : [], RecFixture.Now,
            new EngineOptions(Take: take, Seed: seed)).Select(p => p.Track).OfType<Track>().ToList();

    static string A(Track t) => Matching.PrimaryArtist(t.Artist);
    static bool IsVersion(Track t) => Matching.Recording(t.Title).Length > 0;

    /// <summary>The numbers for one list of picks after [session].</summary>
    public sealed record Score(int Count, int Artists, int MaxPerArtist, int InARow, double InScene, int Versions, int OtherVersions);

    static Score Measure(IReadOnlyList<Track> session, List<Track> picks, HashSet<string> scene)
    {
        var keys = session.Select(t => Matching.SongKey(t.Title, t.Artist)).ToHashSet();
        var again = picks.Count(t => !keys.Add(Matching.SongKey(t.Title, t.Artist)));
        var inARow = picks.Zip(picks.Skip(1)).Count(p => A(p.First) == A(p.Second)) + (session.Count > 0 && picks.Count > 0 && A(session[^1]) == A(picks[0]) ? 1 : 0);
        return new(picks.Count, picks.Select(A).Distinct().Count(), picks.Count == 0 ? 0 : picks.GroupBy(A).Max(g => g.Count()), inARow,
            picks.Count == 0 ? 0 : (double)picks.Count(t => scene.Contains(A(t))) / picks.Count, picks.Count(IsVersion), again);
    }

    Score Average(string name, IReadOnlyList<Track> session, HashSet<string> scene)
    {
        var all = Seeds.Select(s => (Picks: Picks(session, s), Seed: s)).ToList();
        var scores = all.Select(x => Measure(session, x.Picks, scene)).ToList();
        var avg = new Score((int)Math.Round(scores.Average(s => s.Count)), scores.Min(s => s.Artists), scores.Max(s => s.MaxPerArtist), scores.Sum(s => s.InARow),
            scores.Min(s => s.InScene), scores.Max(s => s.Versions), scores.Sum(s => s.OtherVersions));
        output.WriteLine($"{name}: picks {avg.Count}, artists (fewest) {avg.Artists}, most by one artist {avg.MaxPerArtist}, same artist in a row {avg.InARow}, " +
            $"in scene (worst) {avg.InScene:0.00}, remixes/live (most) {avg.Versions}, another version of a song already there {avg.OtherVersions}");
        output.WriteLine("  e.g. " + string.Join(" | ", all[0].Picks.Select(t => $"{t.Title} ({t.Artist})")));
        return avg;
    }

    [Fact]
    public void After_heads_will_roll_the_next_ten_spread_over_the_scene()
    {
        var s = Average("Heads Will Roll", [F["Heads Will Roll"]], RecFixture.Indie);
        Assert.Equal(10, s.Count);
        Assert.True(s.Artists >= 7, $"{s.Artists} artists");
        Assert.True(s.MaxPerArtist <= 2, $"{s.MaxPerArtist} by one artist");
        Assert.Equal(0, s.InARow);
        Assert.True(s.InScene >= 0.8, $"in scene {s.InScene:0.00}");
        Assert.Equal(0, s.Versions);
        Assert.Equal(0, s.OtherVersions);
    }

    [Fact]
    public void After_a_90s_dance_playlist_the_picks_are_90s_dance()
    {
        var session = new[] { "The Rhythm of the Night", "What Is Love", "No Limit", "Mr. Vain", "Show Me Love" }.Select(t => F[t]).ToList();
        var s = Average("90s dance playlist", session, RecFixture.Nineties);
        Assert.Equal(10, s.Count);
        Assert.True(s.InScene >= 0.8, $"in scene {s.InScene:0.00}");
        Assert.True(s.Artists >= 7, $"{s.Artists} artists");
        Assert.True(s.MaxPerArtist <= 2);
        Assert.Equal(0, s.InARow);
    }

    [Fact]
    public void After_a_pop_banger_the_picks_stay_pop()
    {
        var s = Average("Teenage Dream", [F["Teenage Dream"]], RecFixture.PopScene);
        Assert.True(s.InScene >= 0.7, $"in scene {s.InScene:0.00}");
        Assert.True(s.MaxPerArtist <= 2);
        Assert.Equal(0, s.InARow);
    }

    [Fact]
    public void No_song_twice_by_version()
    {
        // owner, 12 Nov: "Dreamer", then its other vocal; Out of Space then its live take
        var session = new[] { "Out of Space", "Dreamer", "Maps", "Mr. Brightside", "Insomnia" }.Select(t => F[t]).ToList();
        var s = Average("versions", session, RecFixture.Nineties);
        Assert.Equal(0, s.OtherVersions);
        Assert.Equal(0, s.Versions);
    }

    [Fact]
    public void A_remix_queue_gets_remixes()
    {
        var session = new[] { "Heads Will Roll (A-Trak Remix)", "Titanium (Alesso Remix)", "Levels (Skrillex Remix)" }.Select(t => F[t]).ToList();
        var picks = Seeds.Select(seed => Picks(session, seed)).ToList();
        var most = picks.Max(p => p.Count(IsVersion));
        output.WriteLine($"remix queue: remixes in the picks (most) {most}; e.g. " + string.Join(" | ", picks[0].Select(t => t.Title)));
        Assert.True(most >= 1, "a remix queue carries on with remixes");
        Assert.All(picks, p => Assert.Equal(0, Measure(session, p, []).OtherVersions));
    }

    /// <summary>The web player's Autoplay run (PlayerState.UseEngine): the song the person chose stays the seed, the engine is asked for 30, and the ones not
    /// queued yet are played, ten a refill. Whatever the engine cannot supply the player fills from its own list, which is where the run of one artist came from.</summary>
    [Fact]
    public void An_autoplay_run_of_twenty_after_one_song_is_not_one_artists_catalogue()
    {
        var worst = 0; var worstWindow = 0; var fewest = int.MaxValue;
        foreach (var seed in Seeds)
        {
            var hwr = F["Heads Will Roll"];
            List<Track> queue = [hwr];
            for (var refill = 0; refill < 2; refill++)
            {
                var have = queue.Select(t => Matching.SongKey(t.Title, t.Artist)).ToHashSet();
                var fresh = Engine.Next(F.Lib, F.Taste, t => F.Vibes.GetValueOrDefault(t), [hwr], F.RelatedOf(hwr), RecFixture.Now, new EngineOptions(Take: 30, Seed: seed + refill))
                    .Select(p => p.Track).OfType<Track>().Where(t => have.Add(Matching.SongKey(t.Title, t.Artist))).Take(10).ToList();
                queue.AddRange(AutoplaySpread.Spread(fresh, t => t.Artist, hwr.Artist, already: queue.Skip(1).Count(t => A(t) == A(hwr))));
            }
            var run = queue.Skip(1).Take(20).ToList();
            fewest = Math.Min(fewest, run.Count);
            worst = Math.Max(worst, run.Count(t => A(t) == "yeah yeah yeahs"));
            for (var i = 0; i < queue.Count; i++) worstWindow = Math.Max(worstWindow, queue.Skip(i).Take(10).GroupBy(A).Max(g => g.Count()));
            if (seed == Seeds[0]) output.WriteLine("autoplay run: " + string.Join(" | ", run.Select(t => $"{t.Title} ({t.Artist})")));
        }
        output.WriteLine($"autoplay run of 20 after Heads Will Roll: songs the engine supplied (fewest) {fewest}, Yeah Yeah Yeahs songs (most) {worst}, most by one artist in any 10 in a row {worstWindow}");
        Assert.Equal(20, fewest);
        Assert.True(worst <= 3, $"{worst} Yeah Yeah Yeahs songs in 20");
        Assert.True(worstWindow <= 2, $"{worstWindow} by one artist in ten");
    }

    /// <summary>Replayed listening: how often the song the person really played next is among the ten picks (the person's own habits are a signal).</summary>
    [Fact]
    public void Hit_rate_on_the_persons_own_sessions()
    {
        var plays = F.Events.Where(e => e.Kind == ListenKind.Play && e.Heard > 100).Select(e => (F.Lib.ByPath(e.Path)!, e.At)).ToList();
        var cases = RecEval.Cases(plays).Select(c => (c.Session.TakeLast(3).ToList() as IReadOnlyList<Track>, c.Next)).ToList();
        // (the history is what the taste was built from, so this measures how well the engine follows habits, not how it generalises)
        var rate = RecEval.HitRateAt10(s => Engine.Next(F.Lib, F.Taste, t => F.Vibes.GetValueOrDefault(t), s, F.RelatedOf(s[^1]), RecFixture.Now.AddDays(1),
            new EngineOptions(Take: 10, Seed: 3)).Select(p => p.Track).OfType<Track>().ToList(), cases);
        output.WriteLine($"hit rate at 10 on the person's own sessions: {rate:0.00} over {cases.Count} cases");
        Assert.True(rate >= 0.35, $"hit rate {rate:0.00}");
    }
}
