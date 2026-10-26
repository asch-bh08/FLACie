using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The offline gate: replayed listening, how often the song really played next is in the top ten. The new engine has to beat the old ranker.</summary>
public class RecommendEvalTests
{
    static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    static readonly Vibe Banger = new(0.9, 0.9, 128, 0.8), Ballad = new(0.2, 0.4, 70, 0.1);

    static Track Song(string title, string artist) =>
        new($"jf:{artist}:{title}", title, artist, "Album", artist, 1, 1, 200_000, 2001, null, 0, TrackSource.Jellyfin, Genre: "Pop");

    [Fact]
    public void Hit_rate_counts_the_cases_whose_next_song_is_in_the_top_ten()
    {
        var a = Song("A", "One"); var b = Song("B", "Two"); var c = Song("C", "Three");
        var rate = RecEval.HitRateAt10(_ => [a, b], [([c], a), ([c], b), ([a], c), ([b], c)]);
        Assert.Equal(0.5, rate, 3);
        Assert.Equal(0, RecEval.HitRateAt10(_ => [], []));
    }

    [Fact]
    public void Plays_split_into_sessions_and_each_next_song_is_a_case()
    {
        var a = Song("A", "One"); var b = Song("B", "Two"); var c = Song("C", "Three");
        var at = new DateTimeOffset(Now).ToUnixTimeMilliseconds();
        // b twice within a minute is one play; the hour's gap starts a new session
        var cases = RecEval.Cases([(a, at), (b, at + 200_000), (b, at + 230_000), (c, at + 400_000), (a, at + 4_000_000), (b, at + 4_200_000)]);
        Assert.Equal(3, cases.Count);
        Assert.Equal([a], cases[0].Session); Assert.Equal(b, cases[0].Next);
        Assert.Equal([a, b], cases[1].Session); Assert.Equal(c, cases[1].Next);
        Assert.Equal([a], cases[2].Session); Assert.Equal(b, cases[2].Next);
    }

    [Fact]
    public void New_engine_beats_the_old_on_a_synthetic_history()
    {
        // two moods, every song played as much as the others, sessions that keep to one mood
        var loud = Enumerable.Range(0, 15).Select(i => Song($"Loud {i}", $"Loud Artist {i}")).ToList();
        var soft = Enumerable.Range(0, 15).Select(i => Song($"Soft {i}", $"Soft Artist {i}")).ToList();
        var vibes = loud.ToDictionary(t => t, _ => Banger);
        foreach (var t in soft) vibes[t] = Ballad;
        var lib = new Library([.. loud, .. soft]);
        var rnd = new Random(7);
        var events = new List<ListenEvent>();
        for (var day = 1; day <= 20; day++)
            foreach (var t in (day % 2 == 0 ? loud : soft).OrderBy(_ => rnd.Next()).Take(5))
                events.Add(new(Guid.NewGuid().ToString(), ListenKind.Play, new DateTimeOffset(Now.AddDays(-day)).ToUnixTimeMilliseconds(), t.Path, t.Title, t.Artist, 200, 200, "autoplay"));
        var taste = new TasteProfile(lib, [], [], [], Now, events: events);
        var cases = Enumerable.Range(0, 40).Select(i =>
        {
            var four = (i % 2 == 0 ? loud : soft).OrderBy(_ => rnd.Next()).Take(4).ToList();
            return ((IReadOnlyList<Track>)four.Take(3).ToList(), four[3]);
        }).ToList();

        var old = RecEval.HitRateAt10(s => Recommender.Recommend(lib, taste, Now, 10, 0, 2, s.Select(Matching.MatchKey)), cases);
        var neu = RecEval.HitRateAt10(s => Engine.Next(lib, taste, t => vibes.GetValueOrDefault(t), s, [], Now, new EngineOptions()).Select(p => p.Track).OfType<Track>().ToList(), cases);
        Assert.True(old > 0, "the old ranker must hit sometimes, or the comparison proves nothing");
        Assert.True(neu >= 1.5 * old, $"new {neu:0.00} vs old {old:0.00}");
    }
}
