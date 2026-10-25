using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The engine that picks what plays next: vibe decides the fit, taste and "goes together" lift it, and the rails keep it from repeating itself.</summary>
public class EngineTests
{
    static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    static long Ago(double days) => new DateTimeOffset(Now.AddDays(-days)).ToUnixTimeMilliseconds();

    static readonly Vibe Banger = new(0.9, 0.9, 128, 0.8), Club = new(0.85, 0.88, 130, 0.8), Ballad = new(0.2, 0.4, 70, 0.1);

    static Track Song(string title, string artist, string genre) =>
        new($"jf:{artist}:{title}", title, artist, "Album", artist, 1, 1, 200_000, 2001, null, 0, TrackSource.Jellyfin, Genre: genre);

    static ListenEvent Heard(Track t, double days = 1) => new(Guid.NewGuid().ToString(), ListenKind.Play, Ago(days), t.Path, t.Title, t.Artist, 200, 200, "autoplay");
    static ListenEvent Did(ListenKind k, Track t) => new(Guid.NewGuid().ToString(), k, Ago(1), t.Path, t.Title, t.Artist);

    static List<Pick> Next(IReadOnlyList<Track> songs, IReadOnlyList<ListenEvent> events, Dictionary<Track, Vibe> vibes, IReadOnlyList<Track> session,
        EngineOptions? o = null, IReadOnlyList<CatalogSong>? related = null, IReadOnlyList<Playlist>? playlists = null)
    {
        var lib = new Library(songs);
        var taste = new TasteProfile(lib, [], [], playlists ?? [], Now, events: events);
        return Engine.Next(lib, taste, t => vibes.GetValueOrDefault(t), session, related ?? [], Now, o ?? new EngineOptions());
    }

    static bool Has(List<Pick> picks, Track t) => picks.Any(p => p.Track == t);

    [Fact]
    public void Liked_songs_cross_genre_when_the_vibe_matches()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var guetta = Song("Sexy Bitch", "David Guetta", "Club");
        var ballad = Song("Slow One", "Crooner", "Pop");
        var picks = Next([space, guetta, ballad], [Did(ListenKind.Heart, guetta), Did(ListenKind.Heart, ballad)],
            new() { [space] = Banger, [guetta] = Club, [ballad] = Ballad }, [space]);
        Assert.True(Has(picks, guetta));
        var g = picks.FindIndex(p => p.Track == guetta); var b = picks.FindIndex(p => p.Track == ballad);
        Assert.True(b < 0 || g < b);
    }

    [Fact]
    public void A_never_heard_song_needs_a_genre_or_artist_link()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var metal = Song("Loud", "Metal Band", "Metal");
        var rave = Song("Charly", "Other Raver", "Rave");
        var picks = Next([space, metal, rave], [], new() { [space] = Banger, [metal] = Banger, [rave] = Banger }, [space]);
        Assert.False(Has(picks, metal));
        Assert.True(Has(picks, rave));
    }

    [Fact]
    public void No_ballad_after_a_banger()
    {
        var a = Song("A", "One", "Rave"); var b = Song("B", "Two", "Rave"); var c = Song("C", "Three", "Rave");
        var ballad = Song("Slow One", "Crooner", "Rave");
        var picks = Next([a, b, c, ballad], [Did(ListenKind.Heart, ballad), Heard(ballad), Heard(ballad, 2)],
            new() { [a] = Banger, [b] = Banger, [c] = Banger, [ballad] = Ballad }, [a, b, c]);
        Assert.False(Has(picks, ballad));
    }

    [Fact]
    public void Never_the_same_artist_twice_running()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var firestarter = Song("Firestarter", "The Prodigy", "Rave");
        var guetta = Song("Sexy Bitch", "David Guetta", "Club");
        var picks = Next([space, firestarter, guetta], [Did(ListenKind.Heart, firestarter), Did(ListenKind.Heart, guetta)],
            new() { [space] = Banger, [firestarter] = Banger, [guetta] = Club }, [space]);
        Assert.False(Has(picks, firestarter));
        Assert.True(Has(picks, guetta));
    }

    [Fact]
    public void Not_heard_within_4_hours()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var recent = Song("Recent", "Someone", "Rave");
        var guetta = Song("Sexy Bitch", "David Guetta", "Club");
        var picks = Next([space, recent, guetta], [Heard(recent, 0.05), Did(ListenKind.Heart, recent), Did(ListenKind.Heart, guetta)],
            new() { [space] = Banger, [recent] = Banger, [guetta] = Club }, [space]);
        Assert.False(Has(picks, recent));
        Assert.True(Has(picks, guetta));
    }

    [Fact]
    public void Thumbed_down_never_comes_back()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var hated = Song("Hated", "Someone", "Rave");
        var picks = Next([space, hated], [Did(ListenKind.Heart, hated), Did(ListenKind.ThumbDown, hated)],
            new() { [space] = Banger, [hated] = Banger }, [space]);
        Assert.Empty(picks);
    }

    [Fact]
    public void Explicit_off_drops_explicit_songs()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var rude = Song("Sexy Bitch (Explicit)", "David Guetta", "Club");
        var vibes = new Dictionary<Track, Vibe> { [space] = Banger, [rude] = Club };
        IReadOnlyList<ListenEvent> ev = [Did(ListenKind.Heart, rude)];
        Assert.True(Has(Next([space, rude], ev, vibes, [space]), rude));
        Assert.False(Has(Next([space, rude], ev, vibes, [space], new EngineOptions(Explicit: false)), rude));
    }

    [Fact]
    public void Half_familiar_half_new()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var known = Enumerable.Range(0, 10).Select(i => Song($"K{i}", $"Known {i}", "Rave")).ToList();
        var fresh = Enumerable.Range(0, 10).Select(i => Song($"N{i}", $"New {i}", "Rave")).ToList();
        var vibes = new Dictionary<Track, Vibe> { [space] = Banger };
        foreach (var t in known.Concat(fresh)) vibes[t] = Banger;
        var picks = Next([space, .. known, .. fresh], known.Select(t => Heard(t)).ToList(), vibes, [space]);
        Assert.Equal(10, picks.Count);
        Assert.Equal(5, picks.Count(p => p.Track is { } t && known.Contains(t)));
        Assert.Equal(5, picks.Count(p => p.Track is { } t && fresh.Contains(t)));
    }

    [Fact]
    public void Related_songs_not_in_the_library_count_as_new()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var picks = Next([space], [], new() { [space] = Banger }, [space], related: [new CatalogSong("Faithless", "Insomnia", "Reverence", null, 300_000, 1, 1)]);
        Assert.Contains(picks, p => p.New?.Title == "Insomnia");
    }

    [Fact]
    public void A_seed_without_vibe_data_falls_back_to_genre_and_artist()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var rave = Song("Charly", "Other Raver", "Rave");
        var metal = Song("Loud", "Metal Band", "Metal");
        var picks = Next([space, rave, metal], [], [], [space]);
        Assert.True(Has(picks, rave));
        Assert.False(Has(picks, metal));
    }

    [Fact]
    public void When_everything_is_filtered_the_rails_relax_in_order()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var same = Song("Firestarter", "The Prodigy", "Rave");
        var other = Song("Insomnia", "Faithless", "Rave");
        var hated = Song("Hated", "Someone", "Rave");
        var vibes = new Dictionary<Track, Vibe> { [space] = Banger, [same] = Banger, [other] = Banger, [hated] = Banger };
        var hour = 1 / 24.0;
        // everything heard in the last hour: the 4-hour rule gives way first, so another artist still comes before the same one
        var picks = Next([space, same, other, hated], [Heard(space, hour), Heard(same, hour), Heard(other, hour), Heard(hated, hour), Did(ListenKind.ThumbDown, hated)], vibes, [space]);
        Assert.True(Has(picks, other));
        Assert.False(Has(picks, same));
        Assert.False(Has(picks, hated));
        // with only the same artist left, the artist rule gives way too; the thumbs-down never does
        picks = Next([space, same, hated], [Heard(space, hour), Heard(same, hour), Heard(hated, hour), Did(ListenKind.ThumbDown, hated)], vibes, [space]);
        Assert.True(Has(picks, same));
        Assert.False(Has(picks, hated));
    }

    [Fact]
    public void Why_names_the_strongest_reason()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var guetta = Song("Sexy Bitch", "David Guetta", "Club");
        var picks = Next([space, guetta], [Heard(guetta)], new() { [space] = Banger, [guetta] = Club }, [space]);
        Assert.StartsWith("Same energy as Out of Space", picks.Single().Why);
    }

    [Fact]
    public void Songs_from_a_playlist_the_person_plays_from_go_together()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var mate = Song("Mate", "Someone", "Rave");
        var list = new Playlist("pl1", "Rave", 0, null, [new(space.Path, space.Title, space.Artist), new(mate.Path, mate.Title, mate.Artist)]);
        var vibes = new Dictionary<Track, Vibe> { [space] = Banger, [mate] = Banger };
        ListenEvent From(string ctx) => new(Guid.NewGuid().ToString(), ListenKind.Play, Ago(1), space.Path, space.Title, space.Artist, 200, 200, ctx);
        var plays = Next([space, mate], [Did(ListenKind.Heart, mate), From("pl1")], vibes, [space], playlists: [list]).Single();
        var never = Next([space, mate], [Did(ListenKind.Heart, mate), From("autoplay")], vibes, [space], playlists: [list]).Single();
        Assert.True(plays.Score - never.Score > 0.2, $"{plays.Score} vs {never.Score}");
    }

    [Fact]
    public void A_thumbed_down_related_song_never_appears()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var bad = new CatalogSong("Faithless", "Insomnia", "Reverence", null, 300_000, 1, 1);
        var down = new ListenEvent("x", ListenKind.ThumbDown, Ago(1), "cloud:insomnia", "Insomnia", "Faithless");
        var picks = Next([space], [down], new() { [space] = Banger }, [space], related: [bad]);
        Assert.DoesNotContain(picks, p => p.New == bad);
    }

    [Fact]
    public void Why_only_credits_the_artist_when_the_artist_is_what_scored()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var known = Song("Known", "Known Raver", "Rave");
        var charly = Song("Charly", "Other Raver", "Rave");
        var picks = Next([space, known, charly], [Heard(known), Heard(known, 2)], new() { [space] = Banger, [known] = Banger, [charly] = Banger }, [space]);
        Assert.DoesNotContain("you play Other Raver", picks.Single(p => p.Track == charly).Why);
    }

    [Fact]
    public void The_same_inputs_and_seed_give_the_same_list()
    {
        var space = Song("Out of Space", "The Prodigy", "Rave");
        var songs = Enumerable.Range(0, 20).Select(i => Song($"S{i}", $"Artist {i}", "Rave")).ToList();
        var vibes = new Dictionary<Track, Vibe> { [space] = Banger };
        foreach (var t in songs) vibes[t] = Banger;
        string Run() => string.Join(",", Next([space, .. songs], [], vibes, [space], new EngineOptions(Seed: 7)).Select(p => p.Track!.Title));
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void A_best_known_song_of_an_artist_you_play_comes_before_a_lesser_one()
    {
        var played = Song("Big One", "Ann", "Pop"); var hit = Song("Hit", "Ann", "Pop"); var other = Song("B-side", "Ann", "Pop");
        var lib = new Library([played, hit, other]);
        var taste = new TasteProfile(lib, [], [], [], Now, hits: new HashSet<string> { Matching.MatchKey(hit) }, events: [Heard(played, 2)]);
        for (var seed = 1; seed < 6; seed++)
        {
            var picks = Engine.Next(lib, taste, _ => null, [], [], Now, new EngineOptions(Take: 3, NewShare: 1, Seed: seed));
            Assert.True(picks.FindIndex(p => p.Track == hit) < picks.FindIndex(p => p.Track == other));
        }
    }
}
