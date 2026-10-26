namespace FLACie.Core;

/// <summary>One row on the home screen: songs (a "Play" row) or albums. [Rotates] says it changes on a schedule.</summary>
public sealed record Shelf(string Id, string Title, string Subtitle, IReadOnlyList<Track> Tracks, IReadOnlyList<Group> Albums, string? Rotates = null,
    IReadOnlyList<CatalogSong>? NewSongs = null)
{
    /// <summary>Songs on the row that are not in the library (Discover), shown after its library songs.</summary>
    public IReadOnlyList<CatalogSong> New => NewSongs ?? [];
}

/// <summary>A song the user played, newest first in the history.</summary>
public sealed record PlayedEntry(string Path, string Title, string Artist, long At);

/// <summary>
/// The home screen the way YouTube Music lays it out: Listen again, Quick picks, mood and genre rows, Albums for you and a Daily Mix that
/// rotates by the hour. Everything is built from the library plus what the account already knows (play history, favourites, playlists), so
/// it costs nothing to compute and needs no service. Randomness is seeded by the time slot, so a shelf is stable within its hour and a new
/// set turns up in the next one. The Android app builds the same shelves (library/Recommend.kt).
/// </summary>
public static class HomeBuilder
{
    public sealed record Mood(string Name, string Subtitle, string[] Genres);

    public static readonly Mood[] Moods =
    [
        new("Relax", "Slow it down", ["ambient", "chill", "lounge", "jazz", "acoustic", "folk", "classical", "soul", "easy", "lo-fi", "lofi", "downtempo", "new age", "bossa", "piano", "instrumental", "singer", "soft"]),
        new("Workout", "Keep moving", ["hip hop", "hip-hop", "rap", "trap", "dance", "electronic", "edm", "house", "techno", "drum", "metal", "punk", "hardcore", "workout", "rock", "hardstyle"]),
        new("Party", "Turn it up", ["dance", "pop", "disco", "funk", "reggaeton", "latin", "house", "club", "r&b", "party", "hip hop", "electro"]),
        new("Feel good", "Good vibes", ["pop", "funk", "soul", "reggae", "disco", "indie", "happy", "summer", "ska"]),
        new("Late night", "After dark", ["r&b", "rnb", "hip hop", "trap", "soul", "chill", "trip", "downtempo", "alternative", "indie"]),
        new("Focus", "Head down", ["instrumental", "classical", "ambient", "post-rock", "electronic", "lo-fi", "lofi", "piano", "study"]),
    ];

    static bool InMood(Track t, Mood m) => t.Genre.Length > 0 && m.Genres.Any(g => t.Genre.Contains(g, StringComparison.OrdinalIgnoreCase));

    public static long Slot(DateTime utc, int hours = 1) => (long)(utc - DateTime.UnixEpoch).TotalHours / Math.Max(1, hours);

    static List<T> Shuffled<T>(IEnumerable<T> items, long seed) { var r = new Random((int)(seed % int.MaxValue)); return items.OrderBy(_ => r.Next()).ToList(); }

    /// <summary>At most [per] songs from any one artist, keeping order.</summary>
    static List<Track> Spread(IEnumerable<Track> tracks, int per, int take)
    {
        var seen = new Dictionary<string, int>(); var res = new List<Track>();
        foreach (var t in tracks)
        {
            var a = Matching.PrimaryArtist(t.Artist);
            seen[a] = seen.GetValueOrDefault(a) + 1;
            if (seen[a] <= per) res.Add(t);
            if (res.Count >= take) break;
        }
        return res;
    }

    /// <summary>The moods that have enough songs in this library to be worth a chip.</summary>
    public static List<string> MoodsAvailable(Library lib) => Moods.Where(m => lib.Songs.Count(t => InMood(t, m)) >= 6).Select(m => m.Name).ToList();

    /// <summary>A mood's songs for today, spread across artists.</summary>
    public static List<Track> MoodTracks(Library lib, string mood, DateTime utcNow, int take = 40)
    {
        var m = Moods.FirstOrDefault(x => x.Name == mood);
        return m is null ? [] : Spread(Shuffled(lib.Songs.Where(t => InMood(t, m)), Slot(utcNow, 24) * 17 + mood.Length), 2, take);
    }

    /// <summary>The lead artists this account plays most (library names), to look their best-known songs up for the Quick picks.</summary>
    public static List<string> TopArtists(Library lib, IReadOnlyList<PlayedEntry> history, IReadOnlyList<PlaylistEntry> favourites, IReadOnlyList<Playlist> playlists, DateTime utcNow, IReadOnlyList<JfPlay>? jellyfin, int n = 24) =>
        new TasteProfile(lib, history, favourites, playlists, utcNow, jellyfin).TopArtists(lib, n);

    /// <summary>
    /// What this account likes. Once it has a listening log ([events] not null) the log alone says what was played and loved: the log was started
    /// from the history and favourites, so counting those lists as well would count every old play twice. Jellyfin's counts and the playlists count as before.
    /// </summary>
    public static TasteProfile Taste(Library lib, IReadOnlyList<PlayedEntry> history, IReadOnlyList<PlaylistEntry> favourites, IReadOnlyList<Playlist> playlists, DateTime utcNow,
        IReadOnlyList<JfPlay>? jellyfin, IReadOnlySet<string>? hits, IReadOnlyList<ListenEvent>? events) =>
        events is null ? new TasteProfile(lib, history, favourites, playlists, utcNow, jellyfin, hits)
            : new TasteProfile(lib, [], [], playlists, utcNow, jellyfin, hits, events);

    /// <summary>[events] is the account's listening log (null before it has one); [vibe] how each song feels, where known; [related] the songs that go
    /// with a song as far as they are already known (it must answer at once: Home never waits for a lookup).</summary>
    public static List<Shelf> Build(Library lib, IReadOnlyList<PlayedEntry> history, IReadOnlyList<PlaylistEntry> favourites, IReadOnlyList<Playlist> playlists, DateTime utcNow,
        IReadOnlyList<JfPlay>? jellyfin = null, IReadOnlySet<string>? hits = null, IReadOnlyList<ListenEvent>? events = null, Func<Track, Vibe?>? vibe = null,
        Func<Track, IReadOnlyList<CatalogSong>>? related = null)
    {
        var shelves = new List<Shelf>();
        if (lib.Songs.Count == 0) return shelves;
        var slot = Slot(utcNow); var day = Slot(utcNow, 24);

        // what each song means to this user
        var plays = new Dictionary<string, int>();
        var listen = new List<Track>();
        foreach (var h in history)
        {
            var t = lib.Resolve(h.Path, h.Title, h.Artist); if (t is null) continue;
            var key = Matching.MatchKey(t);
            plays[key] = plays.GetValueOrDefault(key) + 1;
            if (listen.All(x => Matching.MatchKey(x) != key)) listen.Add(t);
        }
        var favKeys = favourites.Select(f => lib.Resolve(f.Path, f.Title, f.Artist)).OfType<Track>().Select(Matching.MatchKey).ToHashSet();
        var listKeys = playlists.SelectMany(p => p.Entries).Select(e => lib.Resolve(e.Path, e.Title, e.Artist)).OfType<Track>().Select(Matching.MatchKey).ToHashSet();
        // Jellyfin's own count counts too: everything played in any app, not only here
        var jfWeight = new Dictionary<string, double>();
        foreach (var p in jellyfin ?? []) if (lib.ByJellyfinId(p.Id) is { } jt) { var k = Matching.MatchKey(jt); jfWeight[k] = jfWeight.GetValueOrDefault(k) + Math.Log(1 + Math.Max(0, p.Plays)) * 1.5 + (p.Favourite ? 3 : 0); }
        double Taste(Track t) { var k = Matching.MatchKey(t); return plays.GetValueOrDefault(k) * 2 + jfWeight.GetValueOrDefault(k) + (favKeys.Contains(k) ? 3 : 0) + (listKeys.Contains(k) ? 1 : 0); }

        if (listen.Count > 0)
            shelves.Add(new("listen-again", "Listen again", "Your recent plays", listen.Take(16).ToList(), []));
        else if (jellyfin is { Count: > 0 })
        {
            // nothing played through FLACie yet: the songs Jellyfin says were played most recently
            var lastPlayed = jellyfin.Where(p => p.LastMs > 0).OrderByDescending(p => p.LastMs).Select(p => lib.ByJellyfinId(p.Id)).OfType<Track>().DistinctBy(Matching.MatchKey).Take(16).ToList();
            foreach (var t in lastPlayed) listen.Add(t);
            if (lastPlayed.Count > 0) shelves.Add(new("listen-again", "Listen again", "Recently played in Jellyfin", lastPlayed, []));
        }

        // Quick picks and Discover come from the engine, worked out as at the start of the hour so they hold still while Home is shown
        var hour = DateTime.UnixEpoch.AddHours(slot);
        var taste = HomeBuilder.Taste(lib, history, favourites, playlists, hour, jellyfin, hits, events);
        var again = listen.Take(8).Select(Matching.MatchKey).ToHashSet();
        // one pass of the engine: the songs this person knows make Quick picks, the ones never played make Discover
        var all = Engine.Next(lib, taste, vibe ?? (_ => null), [], [], hour, new EngineOptions(Take: 60, NewShare: 0.5, Seed: slot))
            .Select(p => p.Track).OfType<Track>().Where(t => !again.Contains(Matching.MatchKey(t))).ToList();
        bool Fresh(Track t) => taste.Known(t) == 0 && !taste.Likes(t);
        var picks = all.Where(t => !Fresh(t)).Take(20).ToList();
        // an account with little to go on yet still gets a full row
        if (picks.Count < 10) picks = all.Take(20).ToList();
        if (picks.Count > 0) shelves.Add(new("quick-picks", "Quick picks", "Chosen for you from what you play, save and list", picks, [], "Refreshes every hour"));
        // Discover: about half songs the library lacks, that go with what was played lately, and the rest library songs never played
        var newSongs = new List<CatalogSong>();
        if (related is not null)
        {
            var seen = new HashSet<string>();
            foreach (var from in listen.Concat(picks).Take(3))
                foreach (var s in related(from))
                    if (lib.Find(s.Title, s.Artist) is null && !taste.Disliked(new Track("", s.Title, s.Artist, s.Album, s.Artist, 0, 0, 0, 0, null, 0, TrackSource.Cloud))
                        && seen.Add(Matching.MatchKey(s.Title, s.Artist))) newSongs.Add(s);
            newSongs = Shuffled(newSongs, slot).Take(8).ToList();
        }
        var discover = all.Where(t => Fresh(t) && !picks.Contains(t)).Take(16 - newSongs.Count).ToList();
        if (discover.Count + newSongs.Count >= 6)
            shelves.Add(new("discover", "Discover", newSongs.Count > 0 ? "New to you" : "In your library, not played yet", discover, [], "Refreshes every hour", newSongs));

        // the Daily Mix of the hour cycles through the moods, then the genres you own most of
        var genres = lib.Songs.Where(t => t.Genre.Length > 0).GroupBy(t => t.Genre, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(8).ToList();
        var mixes = new List<(string Name, string Sub, List<Track> Tracks)>();
        foreach (var m in Moods) { var l = lib.Songs.Where(t => InMood(t, m)).ToList(); if (l.Count >= 8) mixes.Add((m.Name, m.Subtitle, l)); }
        foreach (var g in genres) if (g.Count() >= 8 && mixes.All(x => !x.Name.Equals(g.Key, StringComparison.OrdinalIgnoreCase))) mixes.Add((g.Key, "More " + g.Key, g.ToList()));
        if (mixes.Count > 0)
        {
            var now = mixes[(int)(slot % mixes.Count)];
            var mix = Spread(Shuffled(now.Tracks, slot).OrderByDescending(t => Taste(t) > 0 ? 1 : 0), 3, 30);
            shelves.Add(new("daily-mix", "Daily Mix: " + now.Name, now.Sub, mix, [], "A new mix every hour, cycling through moods and genres"));
        }

        // "Similar to": the artist you favour most, then songs of the same genre by other artists
        var top = lib.Songs.Where(t => Taste(t) > 0).GroupBy(t => Matching.PrimaryArtist(t.Artist)).Where(g => g.Key.Length > 0).OrderByDescending(g => g.Sum(Taste)).FirstOrDefault();
        if (top is not null && top.FirstOrDefault(t => t.Genre.Length > 0) is { } seed)
        {
            var sim = Spread(Shuffled(lib.Songs.Where(t => t.Genre.Equals(seed.Genre, StringComparison.OrdinalIgnoreCase) && Matching.PrimaryArtist(t.Artist) != top.Key), day * 7 + 3), 2, 14);
            if (sim.Count >= 6) shelves.Add(new("similar", "Similar to " + top.First().Artist, "Same sound, other artists", sim, []));
        }

        // albums for you: the albums whose songs you like best, then a few you haven't heard in a while
        var albums = lib.Albums.Where(a => a.Tracks.Count >= 3).Select(a => (Album: a, Score: a.Tracks.Sum(Taste) / a.Tracks.Count)).ToList();
        var forYou = albums.Where(a => a.Score > 0).OrderByDescending(a => a.Score).Take(6).Select(a => a.Album)
            .Concat(Shuffled(albums.Where(a => a.Score == 0).Select(a => a.Album), day * 5 + 1).Take(10)).DistinctBy(a => a.Tracks[0].AlbumKey).Take(12).ToList();
        if (forYou.Count > 0) shelves.Add(new("albums-for-you", "Albums for you", "From your library", [], forYou));

        var recent = lib.RecentAlbums(14);
        if (recent.Count > 0) shelves.Add(new("recently-added", "Recently added", "New in your library", [], recent));
        return shelves;
    }
}
