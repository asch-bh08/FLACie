using System.Globalization;
using System.Text;

namespace FLACie.Core;

/// <summary>A ready-made playlist: [Paths] are library songs, best fit for this listener first. [ArtKey] is the first song's picture.</summary>
public sealed record MadePlaylist(string Id, string Name, string Subtitle, string? ArtKey, IReadOnlyList<string> Paths);

/// <summary>
/// Ready-made playlists, like Spotify's made-for-you and community lists, so searching "2010s", "rock" or an artist finds a list to play or keep
/// (owner, 2 Nov). Built from the library alone, ordered by this listener's taste:
///   one per decade with at least 15 songs ("2010s"), one per genre with at least 15 ("Rock"), "Best of [artist]" for lead artists with at least 8, and "[artist] Radio" (their songs with others in their genres).
/// Each holds up to 50 songs (a Best of 25), one copy of each song, and at most 3 songs per artist in a decade or genre list so one artist cannot fill it.
/// The order is the taste score, then a fixed hash of the song: the same library and taste always give the same list (no shuffle), so a list someone
/// liked is still there when they come back. Ids are stable ("made:decade:2010", "made:genre:rock", "made:artist:prodigy").
/// Spoken word (audiobooks, podcasts) never counts: these are music lists.
/// </summary>
public static class MadePlaylists
{
    public const int MinDecade = 15, MinGenre = 15, MinArtist = 8, Take = 50, TakeArtist = 25, PerArtist = 3, MaxSearch = 6, RadioOwn = 10;

    /// <summary>Music, not spoken word (the same words the speed rules use to tell an audiobook or podcast).</summary>
    public static bool IsMusic(Track t) => !SpeedPolicy.IsSpoken(t.Genre);

    /// <summary>Every ready-made list for this library: decades (newest first), then genres and artists (biggest first).</summary>
    public static List<MadePlaylist> Build(Library lib, TasteProfile? taste)
    {
        // one copy of each song, scored once
        var songs = new List<(Track T, string Key, string Lead, double Score, uint Hash)>();
        var seen = new HashSet<string>();
        foreach (var t in lib.Songs)
        {
            if (!IsMusic(t)) continue;
            var key = Matching.MatchKey(t);
            if (!seen.Add(key)) continue;
            if (taste is not null && taste.Disliked(t)) continue;
            songs.Add((t, key, Matching.PrimaryArtist(t.Artist), Score(t, taste), Fnv(key)));
        }
        // best fit first; songs that score the same keep a fixed order of their own (a hash, so not the alphabet)
        songs.Sort((a, b) => b.Score.CompareTo(a.Score) is var c && c != 0 ? c : a.Hash.CompareTo(b.Hash) is var h && h != 0 ? h : string.CompareOrdinal(a.Key, b.Key));
        var ordered = songs.Select(s => (s.T, s.Lead)).ToList();

        var made = new List<MadePlaylist>();
        foreach (var g in ordered.Where(s => s.T.Year >= 1900 && s.T.Year <= DateTime.UtcNow.Year + 1).GroupBy(s => s.T.Year / 10 * 10)
                     .Where(g => g.Count() >= MinDecade).OrderByDescending(g => g.Key))
            made.Add(Make($"made:decade:{g.Key}", $"{g.Key}s", g, Take, PerArtist));

        var genres = new List<MadePlaylist>();
        foreach (var g in ordered.Where(s => GenreKey(s.T).Length > 0).GroupBy(s => GenreKey(s.T)).Where(g => g.Count() >= MinGenre).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
            genres.Add(Make($"made:genre:{g.Key}", GenreName(g.Key, g.Select(s => s.T.Genre)), g, Take, PerArtist));
        made.AddRange(genres);

        // each album's size by its key, worked out once (Best of prefers the album version: looking each song's album up among all the albums made the
        // Search page wait 13 s with a 20,000-song library)
        var albumSizes = new Dictionary<string, int>();
        foreach (var a in lib.Albums) albumSizes.TryAdd(a.Tracks[0].AlbumKey, a.Tracks.Count);
        foreach (var g in ordered.Where(s => s.Lead.Length > 0).GroupBy(s => s.Lead).Where(g => g.Count() >= MinArtist).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
            made.Add(BestOf(g.Key, g.ToList(), albumSizes));

        // an artist's radio: their best songs, each followed by two from other artists in their genres (each song's genre worked out once, and the other
        // artists' songs read in order only until the radio is full, not the whole library once per artist)
        var genreOf = ordered.Select(s => GenreKey(s.T)).ToArray();
        foreach (var g in ordered.Where(s => s.Lead.Length > 0).GroupBy(s => s.Lead).Where(g => g.Count() >= MinArtist).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var theirs = g.Take(RadioOwn).ToList();
            var kinds = g.Select(s => GenreKey(s.T)).Where(k => k.Length > 0).ToHashSet();
            var lead = g.Key;
            var like = ordered.Where((s, i) => s.Lead != lead && kinds.Contains(genreOf[i]));
            if (!like.Any()) continue;
            var others = Make("", "", like, Take - theirs.Count, PerArtist).Paths;
            var paths = new List<string>(Take);
            for (int i = 0, j = 0; i < theirs.Count || j < others.Count;)
            {
                if (i < theirs.Count) paths.Add(theirs[i++].T.Path);
                for (var k = 0; k < 2 && j < others.Count; k++) paths.Add(others[j++]);
            }
            made.Add(new MadePlaylist($"made:radio:{g.Key}", ArtistName(g.Select(s => s.T.Artist)) + " Radio", $"{paths.Count} songs · made for you", theirs[0].T.ArtKey, paths));
        }
        return made;
    }

    public static MadePlaylist? Find(IReadOnlyList<MadePlaylist> lists, string id) => lists.FirstOrDefault(m => m.Id == id);

    /// <summary>The lists for a signed-in person, ordered by their taste as Home works it out ([events] is their listening log, null before they have one).</summary>
    public static List<MadePlaylist> For(UserSession s, IReadOnlyList<ListenEvent>? events, DateTime utcNow) =>
        Build(s.Library, HomeBuilder.Taste(s.Library, s.History, s.Favorites, s.Playlists, utcNow, s.PlayStats, null, events));

    /// <summary>
    /// As <see cref="For"/>, kept for ten minutes while the library stays the same: Search asks on every keystroke, and working out the taste reads the whole
    /// listening log. A changed library (a sync, a download) builds them again at once.
    /// </summary>
    public static List<MadePlaylist> Cached(UserSession s, Func<IReadOnlyList<ListenEvent>?> events, DateTime utcNow)
    {
        lock (cache)
        {
            if (cache.TryGetValue(s, out var c) && ReferenceEquals(c.Lib, s.Library) && utcNow - c.At < TimeSpan.FromMinutes(10)) return c.Lists;
        }
        var lib = s.Library;
        var lists = For(s, events(), utcNow);
        lock (cache) cache.AddOrUpdate(s, new Built(lib, utcNow, lists));
        return lists;
    }
    sealed record Built(Library Lib, DateTime At, List<MadePlaylist> Lists);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UserSession, Built> cache = new();

    /// <summary>
    /// "Add to my playlists": a normal playlist of the account holding the list's songs as they are now, named like the list ("Rock", or "Rock 2" when
    /// "Rock" is taken, as saving the queue does). From then on it is the person's own and does not change when the made list does. The caller saves the profile.
    /// </summary>
    public static Playlist SaveTo(UserSession s, MadePlaylist m)
    {
        var name = m.Name;
        for (var n = 2; s.Playlists.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++) name = $"{m.Name} {n}";
        var pl = s.CreatePlaylist(name);
        s.ReplacePlaylistTracks(pl.Id, m.Paths.Select(s.Library.ByPath).OfType<Track>());
        return s.Playlists.First(p => p.Id == pl.Id);
    }

    /// <summary>
    /// The lists whose name matches every word of the query, closest name first, six at most. "2010s", "2010" and "2013" find the 2010s, "90s" the 1990s,
    /// "prodigy" and "best of prodigy" find Best of The Prodigy, then The Prodigy Radio and the decade and genre lists their songs are in; words like "playlist" or "mix" are ignored ("rock playlist" is "rock").
    /// </summary>
    public static List<MadePlaylist> Search(IReadOnlyList<MadePlaylist> lists, string q)
    {
        var words = Matching.SearchWords(q ?? "").Where(w => !Filler.Contains(w)).Select(DecadeWord).ToList();
        if (words.Count == 0) return [];
        var query = string.Concat(words);
        var found = lists.Where(m => Matching.SearchHit(words, Hay(m)))
            .Select(m => (m, rank: Rank(m, query)))
            .OrderBy(x => x.rank).ThenBy(x => Kind(x.m)).ThenByDescending(x => x.m.Paths.Count).ThenBy(x => x.m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.m).ToList();
        // an artist asked for by name also brings the decade and genre lists their songs are in, the ones with most of them first
        if (found.FirstOrDefault(m => Kind(m) == 0 && Rank(m, query) == 0) is { } artist)
        {
            var theirs = artist.Paths.ToHashSet();
            found.AddRange(lists.Where(m => Kind(m) == 2 && !found.Contains(m)).Select(m => (m, n: m.Paths.Count(theirs.Contains))).Where(x => x.n > 0)
                .OrderByDescending(x => x.n).ThenBy(x => x.m.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.m));
        }
        return found.Take(MaxSearch).ToList();
    }

    /// <summary>0: a Best of; 1: an artist's radio; 2: a decade or genre. A Best of comes before its radio when both are the name searched for.</summary>
    static int Kind(MadePlaylist m) => m.Id.Split(':', 3)[1] switch { "artist" => 0, "radio" => 1, _ => 2 };

    // ---- the parts ----

    static readonly HashSet<string> Filler = ["playlist", "playlists", "mix", "mixes", "songs", "song", "music", "list", "radio", "hits"];

    /// <summary>A year finds its decade ("2013" is the 2010s); "90s" and "90" stay as they are (the decade's search words hold them).</summary>
    static string DecadeWord(string w) => w.Length == 4 && w.All(char.IsDigit) && int.Parse(w) is >= 1900 and < 2100 and var y ? $"{y / 10 * 10}s" : w;

    /// <summary>What a list is found by: its name, and for a decade the short forms ("10s", "90s"), for an artist the plain lead name.</summary>
    static string[] Hay(MadePlaylist m)
    {
        var parts = m.Id.Split(':', 3);
        return parts[1] switch
        {
            "decade" => [m.Name, parts[2][2..] + "s", "decade"],
            "artist" or "radio" => [m.Name, parts[2]],
            _ => [m.Name, "genre"],
        };
    }

    /// <summary>0: the query is the name (or the artist's or decade's own name); 1: the name starts with it; 2: it is somewhere in the name.</summary>
    static int Rank(MadePlaylist m, string query)
    {
        var name = Squash(m.Name);
        var own = m.Id.Split(':', 3) is var p && p[1] == "artist" ? Squash(m.Name["Best of ".Length..]) : name; // a radio's "Radio" is a filler word already
        if (name == query || own == query || Squash(p[2]) == query || (p[1] == "decade" && p[2][2..] + "s" == query)) return 0;
        if (name.StartsWith(query) || own.StartsWith(query)) return 1;
        return 2;
    }

    static string Squash(string s) => string.Concat(Matching.SearchWords(s).Where(w => !Filler.Contains(w)));

    static readonly System.Text.RegularExpressions.Regex NotTheSong = new(@"\b(instrumental|karaoke|a ?cappella|acapella|backing track)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex Version = new(@"\s*(?:[(\[][^)\]]*\b(?:edit|version|mix|remaster(?:ed)?|mono|stereo|live|demo)\b[^)\]]*[)\]]|\s-\s.*\b(?:edit|version|remaster(?:ed)?)\b.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// F23.3: the artist's Best of: one version of each song (the album version before a radio edit, a remaster or a single; never an instrumental),
    /// hits first (the order the songs come in), sized to the artist: half their distinct songs, at least 10 and at most 50.
    /// </summary>
    static MadePlaylist BestOf(string lead, List<(Track T, string Lead)> theirs, Dictionary<string, int> albumSizes)
    {
        static string Song(Track t) => Matching.NormTitle(Version.Replace(WebCatalog.BaseTitle(t.Title), ""));
        int AlbumSize(Track t) => albumSizes.GetValueOrDefault(t.AlbumKey);
        var bySong = theirs.Where(s => !NotTheSong.IsMatch(s.T.Title) && !NotTheSong.IsMatch(s.T.Album)).GroupBy(s => Song(s.T)).ToList();
        // the song's place is where its best scored version came; the version kept is the plain album one
        var picked = bySong.Select(g => (First: theirs.IndexOf(g.First()), Pick: g.OrderBy(s => Version.IsMatch(s.T.Title) ? 1 : 0).ThenByDescending(s => AlbumSize(s.T)).First()))
            .OrderBy(x => x.First).Select(x => x.Pick.T).ToList();
        var size = Math.Clamp(picked.Count / 2, 10, 50);
        var list = picked.Take(size).ToList();
        return new MadePlaylist($"made:artist:{lead}", "Best of " + ArtistName(theirs.Select(s => s.T.Artist)), $"{list.Count} songs · made for you", list.Select(t => t.ArtKey).FirstOrDefault(), list.Select(t => t.Path).ToList());
    }

    static MadePlaylist Make(string id, string name, IEnumerable<(Track T, string Lead)> songs, int take, int perArtist)
    {
        var per = new Dictionary<string, int>();
        var picked = new List<Track>(take);
        foreach (var (t, lead) in songs)
        {
            if (picked.Count >= take) break;
            var n = per.GetValueOrDefault(lead);
            if (n >= perArtist) continue;
            per[lead] = n + 1; picked.Add(t);
        }
        return new MadePlaylist(id, name, $"{picked.Count} songs · made for you", picked.Select(t => t.ArtKey).FirstOrDefault(), picked.Select(t => t.Path).ToList());
    }

    /// <summary>0..1, how well the song fits this listener: mostly its artist, genre, decade and album, partly its own plays; a hit of an artist played counts a little more.</summary>
    static double Score(Track t, TasteProfile? taste)
    {
        if (taste is null || taste.Empty) return 0;
        var s = taste.Fit(t) * 0.6 + taste.Love(t) * 0.4 + (taste.IsHit(t) ? 0.1 : 0) - taste.Penalty(t) * 0.5;
        return Math.Clamp(s, 0, 1);
    }

    /// <summary>The recommender's genre: the tag trimmed and lower-case.</summary>
    static string GenreKey(Track t) => t.Genre.Trim().ToLowerInvariant();

    /// <summary>The library's own spelling when it has capitals ("R&amp;B", "EDM", "Hip-Hop"), else title case ("rock" is "Rock").</summary>
    static string GenreName(string key, IEnumerable<string> spellings)
    {
        var cased = spellings.Select(s => s.Trim()).Where(s => s.Any(char.IsUpper)).GroupBy(s => s).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault();
        return cased?.Key ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key);
    }

    /// <summary>The artist as the library spells them most often (shortest first, so "The Prodigy" rather than "The Prodigy feat. Keith").</summary>
    static string ArtistName(IEnumerable<string> spellings) =>
        spellings.GroupBy(s => s.Trim()).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Length).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;

    /// <summary>FNV-1a: a hash that is the same in every run (string.GetHashCode is not).</summary>
    static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619; }
        return h;
    }
}
