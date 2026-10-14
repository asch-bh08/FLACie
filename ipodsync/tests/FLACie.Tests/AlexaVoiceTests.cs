using System.Diagnostics;
using FLACie.Core;
using FLACie.Server.Alexa;
using Xunit;

namespace FLACie.Tests;

public class AlexaVoiceTests
{
    // ---------- helpers ----------
    static Track T(string title, string artist, string album = "Some Album", int year = 2000, string genre = "") =>
        new($"/m/{artist}/{album}/{title}", title, artist, album, artist, 1, 1, 200000, year, null, 0, TrackSource.Jellyfin, Genre: genre);

    static Playlist P(string name, params (string t, string a)[] e) =>
        new("id-" + name, name, 0, null, e.Select(x => new PlaylistEntry("/m/" + x.t, x.t, x.a)).ToList());

    static readonly Lazy<VoiceMatcher> Shared = new(() => new VoiceMatcher(Library(), Playlists()));
    static VoiceMatcher M => Shared.Value;

    static List<Track> Library() =>
    [
        T("Making Plans for Nigel", "XTC", "Drums and Wires", 1979, "New Wave"),
        T("Back in Black", "AC/DC", "Back in Black", 1980, "Rock"),
        T("Highway to Hell", "AC/DC", "Highway to Hell", 1979, "Rock"),
        T("Get the Party Started", "P!nk", "Missundaztood", 2001, "Pop"),
        T("Scream & Shout", "will.i.am", "Willpower", 2012, "Pop"),
        T("Halo", "Beyoncé", "I Am... Sasha Fierce", 2008, "R&B"),
        T("Crazy in Love", "Beyoncé", "Dangerously in Love", 2003, "R&B"),
        T("Sweet Child o' Mine", "Guns N' Roses", "Appetite for Destruction", 1987, "Rock"),
        T("Hey Jude - Remastered 2015", "The Beatles", "1", 1968, "Rock"),
        T("Love Me Do (Mono)", "The Beatles", "Please Please Me", 1963, "Rock"),
        T("Yesterday", "The Beatles", "Help!", 1965, "Rock"),
        T("All the Small Things", "Blink-182", "Enema of the State", 1999, "Punk"),
        T("California Love", "2Pac", "All Eyez on Me", 1996, "Hip-Hop/Rap"),
        T("Don't Stop Believin'", "Journey", "Escape", 1981, "Rock"),
        T("Blinding Lights", "The Weeknd", "After Hours", 2020, "Pop"),
        T("Save Your Tears", "The Weeknd", "After Hours", 2020, "Pop"),
        T("(I Can't Get No) Satisfaction", "The Rolling Stones", "Out of Our Heads", 1965, "Rock"),
        T("Heroes", "David Bowie", "Heroes", 1977, "Rock"),
        T("Heroes (Live)", "David Bowie", "Stage", 1978, "Rock"),
        T("Heroes", "Peter Gabriel", "Scratch My Back", 2010, "Rock"),
        T("Creep", "Radiohead", "Pablo Honey", 1993, "Alternative"),
        T("Karma Police", "Radiohead", "OK Computer", 1997, "Alternative"),
        T("Hoppípolla", "Sigur Rós", "Takk...", 2005, "Post-Rock"),
        T("Kickstart My Heart", "Mötley Crüe", "Dr. Feelgood", 1989, "Metal"),
        T("It's Tricky", "Run-D.M.C.", "Raising Hell", 1986, "Hip-Hop/Rap"),
        T("Dog Days Are Over", "Florence + the Machine", "Lungs", 2009, "Indie"),
        T("Umbrella (feat. Jay-Z)", "Rihanna", "Good Girl Gone Bad", 2007, "Pop"),
        T("Queen", "Some Singer", "Odd Album", 2015, "Pop"),
        T("Bohemian Rhapsody", "Queen", "A Night at the Opera", 1975, "Rock"),
        T("Garbage", "Beck Wannabe", "Weird", 2010, "Alternative"),
        T("Stupid Girl", "Garbage", "Garbage", 1995, "Alternative"),
        T("Smells Like Teen Spirit", "Nirvana", "Nevermind", 1991, "Grunge"),
        T("So What", "Miles Davis", "Kind of Blue", 1959, "Jazz"),
        T("Take Five", "Dave Brubeck", "Time Out", 1959, "Jazz"),
        T("Paranoid Android", "Radiohead", "OK Computer", 1997, "Alternative"),
        T("Echoes", "Tom & Jerry", "Cartoon", 2001, "Pop"),
        T("Echoes", "Tim <3 Jim", "Hearts", 2002, "Pop"),
        T("Stand By Me", "Ben E. King", "Don't Play That Song", 1961, "Soul"),
        T("Mr. Brightside", "The Killers", "Hot Fuss", 2004, "Indie"),
        T("Wannabe", "Spice Girls", "Spice", 1996, "Pop"),
    ];

    static List<Playlist> Playlists() =>
    [
        P("Road Trip 2024", ("Highway to Hell", "AC/DC")),
        P("Gym Heroes Mix", ("Stronger", "Kanye West")),
    ];

    static MatchResult Say(string utterance) => M.Match(VoiceParser.FromRaw(utterance));
    static string TitleOf(VoiceMatch m) => ((Track)m.Item).Title;
    static string NameOf(VoiceMatch m) => m.Item switch { Track t => t.Title, Group g => g.Name, Playlist p => p.Name, _ => "" };

    // ---------- Phonetic ----------
    [Theory]
    [InlineData("x t c", "XTC")]
    [InlineData("a c d c", "AC/DC")]
    [InlineData("pink", "P!nk")]
    [InlineData("will i am", "will.i.am")]
    [InlineData("beyonce", "Beyoncé")]
    [InlineData("guns and roses", "Guns N' Roses")]
    [InlineData("the beetles", "The Beatles")]
    [InlineData("radio head", "Radiohead")]
    [InlineData("sigur ros", "Sigur Rós")]
    [InlineData("motley crue", "Mötley Crüe")]
    [InlineData("run d m c", "Run-D.M.C.")]
    [InlineData("florence and the machine", "Florence + the Machine")]
    [InlineData("blink 182", "Blink-182")]
    [InlineData("hey jude", "Hey Jude - Remastered 2015")]
    [InlineData("love me do", "Love Me Do (Mono)")]
    [InlineData("two pac", "2Pac")]
    [InlineData("to pac", "2Pac")]
    [InlineData("dont stop believing", "Don't Stop Believin'")]
    [InlineData("for you", "4 You")]
    [InlineData("umbrella", "Umbrella (feat. Jay-Z)")]
    [InlineData("umbrella", "Umbrella feat. Jay-Z")]
    [InlineData("song two", "Song 2")]
    [InlineData("heroes", "Heroes - 2011 Remaster")]
    [InlineData("satisfaction", "Satisfaction [Live]")]
    public void Key_folds_speech_variants_together(string spoken, string name) =>
        Assert.Equal(Phonetic.Key(spoken), Phonetic.Key(name));

    [Fact]
    public void Key_keeps_the_words_inside_a_non_tag_bracket() =>
        Assert.NotEqual(Phonetic.Key("Satisfaction"), Phonetic.Key("(I Can't Get No) Satisfaction"));

    [Fact]
    public void Key_separates_different_names() =>
        Assert.NotEqual(Phonetic.Key("Heroes"), Phonetic.Key("Zebra Crossing"));

    [Fact]
    public void Similarity_rates_misheard_names_high_and_garbage_low()
    {
        Assert.True(Phonetic.Similarity("the beetles", "The Beatles") >= 0.9);
        Assert.Equal(1.0, Phonetic.Similarity("Blinding Lights", "blinding lights"), 3);
        Assert.True(Phonetic.Similarity("flurbo zanzibar", "Radiohead") < 0.5);
        Assert.True(Phonetic.Similarity("flurbo zanzibar", "Blinding Lights") < 0.5);
        Assert.Equal(Phonetic.Similarity("abc def", "xyz"), Phonetic.Similarity("xyz", "abc def"), 6);
        Assert.Equal(0, Phonetic.Similarity("", "x"));
    }

    // ---------- VoiceParser ----------
    [Fact]
    public void Parser_plain_phrase_is_free_text()
    {
        var q = VoiceParser.FromRaw("play blinding lights");
        Assert.Equal("blinding lights", q.Raw);
        Assert.Null(q.Title); Assert.Null(q.Artist);
    }

    [Fact]
    public void Parser_title_by_artist()
    {
        var q = VoiceParser.FromRaw("Alexa, please play Blinding Lights by The Weeknd");
        Assert.Equal("blinding lights", q.Title);
        Assert.Equal("the weeknd", q.Artist);
    }

    [Fact]
    public void Parser_album_and_playlist_forms()
    {
        var a = VoiceParser.FromRaw("play the album abbey road by the beatles");
        Assert.Equal("abbey road", a.Album); Assert.Equal("the beatles", a.Artist);
        Assert.Equal("abbey road", VoiceParser.FromRaw("play the album abbey road").Album);
        Assert.Equal("road trip", VoiceParser.FromRaw("play my road trip playlist").Playlist);
        Assert.Equal("road trip", VoiceParser.FromRaw("play the playlist road trip").Playlist);
    }

    [Fact]
    public void Parser_something_like()
    {
        var q = VoiceParser.FromRaw("play something like radio head");
        Assert.Equal("radio head", q.SimilarTo);
        Assert.Null(q.Title);
    }

    [Theory]
    [InlineData("play songs from the 90s", "1990s")]
    [InlineData("play music from the nineties", "1990s")]
    [InlineData("play songs from the 1980s", "1980s")]
    [InlineData("play the 70s", "1970s")]
    [InlineData("play songs from the 00s", "2000s")]
    public void Parser_decades(string say, string decade) => Assert.Equal(decade, VoiceParser.FromRaw(say).Decade);

    [Fact]
    public void Parser_genre_and_decade_together()
    {
        var q = VoiceParser.FromRaw("play 80s rock");
        Assert.Equal("1980s", q.Decade); Assert.Equal("rock", q.Genre);
        Assert.Equal("jazz", VoiceParser.FromRaw("play some jazz").Genre);
        Assert.Equal("hip hop", VoiceParser.FromRaw("play hip hop").Genre);
    }

    [Fact]
    public void Parser_a_title_starting_like_a_decade_is_not_a_decade()
    {
        var q = VoiceParser.FromRaw("play 90s kid");
        Assert.Null(q.Decade); Assert.Equal("90s kid", q.Raw);
    }

    [Fact]
    public void Parser_song_intent_and_songs_by()
    {
        Assert.Equal("heroes", VoiceParser.FromRaw("play the song heroes").Title);
        Assert.Equal("queen", VoiceParser.FromRaw("play songs by queen").Artist);
    }

    [Fact]
    public void Parser_slots_normalise()
    {
        var q = VoiceParser.FromSlots(" Halo ", null, null, null, null, "nineties");
        Assert.Equal("Halo", q.Title); Assert.Equal("1990s", q.Decade);
    }

    // ---------- Matcher: tricky names ----------
    [Theory]
    [InlineData("play x t c", "XTC")]
    [InlineData("play xtc", "XTC")]
    [InlineData("play a c d c", "AC/DC")]
    [InlineData("play ac dc", "AC/DC")]
    [InlineData("play pink", "P!nk")]
    [InlineData("play will i am", "will.i.am")]
    [InlineData("play beyonce", "Beyoncé")]
    [InlineData("play guns and roses", "Guns N' Roses")]
    [InlineData("play guns n roses", "Guns N' Roses")]
    [InlineData("play the beetles", "The Beatles")]
    [InlineData("play beatles", "The Beatles")]
    [InlineData("play blink 182", "Blink-182")]
    [InlineData("play two pac", "2Pac")]
    [InlineData("play radio head", "Radiohead")]
    [InlineData("play radiohead", "Radiohead")]
    [InlineData("play sigur ros", "Sigur Rós")]
    [InlineData("play motley crue", "Mötley Crüe")]
    [InlineData("play run dmc", "Run-D.M.C.")]
    [InlineData("play run d m c", "Run-D.M.C.")]
    [InlineData("play florence and the machine", "Florence + the Machine")]
    [InlineData("play florence plus the machine", "Florence + the Machine")]
    public void Artists_with_awkward_names_are_found_confidently(string say, string artist)
    {
        var r = Say(say);
        Assert.Equal(Decision.Confident, r.Decision);
        Assert.Equal(MatchKind.Artist, r.Ranked[0].Kind);
        Assert.Equal(artist, NameOf(r.Ranked[0]));
    }

    [Theory]
    [InlineData("play blinding lights", "Blinding Lights")]
    [InlineData("play blinding lights by the weeknd", "Blinding Lights")]
    [InlineData("play dont stop believing", "Don't Stop Believin'")]
    [InlineData("play don't stop believin", "Don't Stop Believin'")]
    [InlineData("play satisfaction", "(I Can't Get No) Satisfaction")]
    [InlineData("play i can't get no satisfaction", "(I Can't Get No) Satisfaction")]
    [InlineData("play hey jude", "Hey Jude - Remastered 2015")]
    [InlineData("play love me do", "Love Me Do (Mono)")]
    [InlineData("play umbrella", "Umbrella (feat. Jay-Z)")]
    [InlineData("play umbrella by rihanna", "Umbrella (feat. Jay-Z)")]
    [InlineData("play beyonce halo", "Halo")]
    [InlineData("play halo by beyonce", "Halo")]
    [InlineData("play radiohead creep", "Creep")]
    [InlineData("play creep by radio head", "Creep")]
    [InlineData("play stand by me", "Stand By Me")]
    [InlineData("play scream and shout", "Scream & Shout")]
    [InlineData("play heroes by david bowie", "Heroes")]
    [InlineData("play sweet child of mine", "Sweet Child o' Mine")]
    [InlineData("play hopipolla", "Hoppípolla")]
    public void Songs_are_found_confidently(string say, string title)
    {
        var r = Say(say);
        Assert.Equal(Decision.Confident, r.Decision);
        Assert.Equal(MatchKind.Track, r.Ranked[0].Kind);
        Assert.Equal(title, TitleOf(r.Ranked[0]));
    }

    [Fact]
    public void Exact_title_with_artist_picks_the_right_heroes()
    {
        var r = Say("play heroes by peter gabriel");
        Assert.Equal(Decision.Confident, r.Decision);
        Assert.Equal("Peter Gabriel", ((Track)r.Ranked[0].Item).Artist);
    }

    [Fact]
    public void Live_version_of_the_same_song_is_not_a_second_choice()
    {
        var r = Say("play heroes by david bowie");
        Assert.DoesNotContain(r.Ranked.Skip(1), m => m.Item is Track t && t.Artist == "David Bowie" && t.Title == "Heroes (Live)");
        Assert.Equal("Heroes", TitleOf(r.Ranked[0]));
    }

    // ---------- ambiguity ----------
    [Fact]
    public void Heroes_by_two_artists_asks_one_short_question()
    {
        var r = Say("play heroes");
        Assert.Equal(Decision.Ambiguous, r.Decision);
        var q = M.DisambiguationQuestion(r);
        Assert.Contains("Heroes by David Bowie", q);
        Assert.Contains("Heroes by Peter Gabriel", q);
        Assert.StartsWith("Did you mean", q);
        Assert.EndsWith("?", q);
        Assert.True(q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 20, q);
    }

    [Fact]
    public void Disambiguation_question_is_speech_safe()
    {
        var r = Say("play echoes");
        Assert.Equal(Decision.Ambiguous, r.Decision);
        var q = M.DisambiguationQuestion(r);
        Assert.DoesNotContain("&", q);
        Assert.DoesNotContain("<", q);
        Assert.DoesNotContain(">", q);
        Assert.Contains("Tom and Jerry", q);
    }

    [Fact]
    public void A_weak_single_candidate_is_confirmed_not_played()
    {
        var r = Say("play karma poliss and friends");
        Assert.NotEqual(Decision.Confident, r.Decision);
    }

    [Fact]
    public void Confident_result_needs_no_question() =>
        Assert.Equal("", M.DisambiguationQuestion(Say("play blinding lights")));

    // ---------- artist vs song ----------
    [Fact]
    public void Artist_name_that_is_also_a_song_prefers_the_artist()
    {
        var r = Say("play queen");
        Assert.Equal(Decision.Confident, r.Decision);
        Assert.Equal(MatchKind.Artist, r.Ranked[0].Kind);
        r = Say("play garbage");
        Assert.Equal(MatchKind.Artist, r.Ranked[0].Kind);
        Assert.Equal(Decision.Confident, r.Decision);
    }

    [Fact]
    public void Saying_song_picks_the_track()
    {
        var r = Say("play the song garbage");
        Assert.Equal(MatchKind.Track, r.Ranked[0].Kind);
        Assert.Equal("Beck Wannabe", ((Track)r.Ranked[0].Item).Artist);
        Assert.Equal(Decision.Confident, r.Decision);
    }

    // ---------- albums, playlists, similar ----------
    [Fact]
    public void Album_by_name()
    {
        var r = Say("play the album ok computer");
        Assert.Equal(Decision.Confident, r.Decision);
        Assert.Equal(MatchKind.Album, r.Ranked[0].Kind);
        Assert.Equal("OK Computer", ((Group)r.Ranked[0].Item).Name);
        Assert.Equal(2, ((Group)r.Ranked[0].Item).Tracks.Count);
    }

    [Fact]
    public void Playlist_road_trip_matches_road_trip_2024()
    {
        var r = Say("play my road trip playlist");
        Assert.Equal(Decision.Confident, r.Decision);
        Assert.Equal(MatchKind.Playlist, r.Ranked[0].Kind);
        Assert.Equal("Road Trip 2024", NameOf(r.Ranked[0]));
    }

    [Fact]
    public void Missing_playlist_is_not_found_not_a_song()
    {
        var r = Say("play my gardening playlist");
        Assert.Equal(Decision.NotFound, r.Decision);
    }

    [Fact]
    public void Something_like_resolves_the_seed_artist()
    {
        var r = Say("play something like radio head");
        Assert.Equal(Decision.Confident, r.Decision);
        Assert.Equal(MatchKind.Artist, r.Ranked[0].Kind);
        Assert.Equal("Radiohead", NameOf(r.Ranked[0]));
    }

    // ---------- not found ----------
    [Theory]
    [InlineData("play flurbo zanzibar")]
    [InlineData("play qwxz plorf")]
    [InlineData("play")]
    [InlineData("play zzzzzzzz")]
    public void Garbage_is_not_found(string say)
    {
        var r = Say(say);
        Assert.Equal(Decision.NotFound, r.Decision);
        Assert.All(r.Ranked, m => Assert.True(m.Confidence < VoiceMatcher.ConfidentThreshold));
        Assert.False(string.IsNullOrWhiteSpace(M.DisambiguationQuestion(r)));
    }

    [Fact]
    public void Empty_library_is_not_found() =>
        Assert.Equal(Decision.NotFound, new VoiceMatcher([], []).Match(VoiceParser.FromRaw("play anything at all")).Decision);

    // ---------- decade and genre ----------
    [Fact]
    public void Decade_slot_collects_that_decades_tracks()
    {
        var r = Say("play songs from the 90s");
        Assert.Equal(Decision.Confident, r.Decision);
        var pl = Assert.IsType<Playlist>(r.Ranked[0].Item);
        Assert.Equal(MatchKind.Playlist, r.Ranked[0].Kind);
        Assert.All(pl.Entries, e => Assert.Contains(e.Title, new[] { "Stupid Girl", "Smells Like Teen Spirit", "Creep", "Karma Police", "Paranoid Android", "All the Small Things", "California Love", "Wannabe" }));
        Assert.Equal(8, pl.Entries.Count);
    }

    [Fact]
    public void Genre_slot_collects_that_genre()
    {
        var pl = Assert.IsType<Playlist>(Say("play some jazz").Ranked[0].Item);
        Assert.Equal(2, pl.Entries.Count);
        var hh = Assert.IsType<Playlist>(Say("play hip hop").Ranked[0].Item);
        Assert.Equal(2, hh.Entries.Count);
    }

    [Fact]
    public void Genre_and_decade_intersect()
    {
        var pl = Assert.IsType<Playlist>(Say("play 90s alternative").Ranked[0].Item);
        Assert.Equal(["Creep", "Karma Police", "Paranoid Android", "Stupid Girl"], pl.Entries.Select(e => e.Title).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Decade_with_no_tracks_is_not_found() =>
        Assert.Equal(Decision.NotFound, Say("play songs from the 1930s").Decision);

    // ---------- AliasGenerator ----------
    [Fact]
    public void Aliases_spoken_forms()
    {
        Assert.Contains("x t c", AliasGenerator.AliasesFor("XTC", MatchKind.Artist));
        var acdc = AliasGenerator.AliasesFor("AC/DC", MatchKind.Artist);
        Assert.Contains("a c d c", acdc); Assert.Contains("ac dc", acdc);
        Assert.Contains("pink", AliasGenerator.AliasesFor("P!nk", MatchKind.Artist));
        var will = AliasGenerator.AliasesFor("will.i.am", MatchKind.Artist);
        Assert.Contains("will i am", will);
        Assert.Contains("beyonce", AliasGenerator.AliasesFor("Beyoncé", MatchKind.Artist));
        Assert.Contains("guns and roses", AliasGenerator.AliasesFor("Guns N' Roses", MatchKind.Artist));
        Assert.Contains("beatles", AliasGenerator.AliasesFor("The Beatles", MatchKind.Artist));
        Assert.Contains("florence and the machine", AliasGenerator.AliasesFor("Florence + the Machine", MatchKind.Artist));
        Assert.Contains("run d m c", AliasGenerator.AliasesFor("Run-D.M.C.", MatchKind.Artist));
    }

    [Fact]
    public void Aliases_numbers_both_ways()
    {
        var blink = AliasGenerator.AliasesFor("Blink-182", MatchKind.Artist);
        Assert.Contains("blink 182", blink);
        Assert.Contains("blink one eight two", blink);
        Assert.Contains("2 door cinema club", AliasGenerator.AliasesFor("Two Door Cinema Club", MatchKind.Artist));
        Assert.Contains("two pac", AliasGenerator.AliasesFor("2Pac", MatchKind.Artist));
    }

    [Fact]
    public void Aliases_strip_tags_and_apostrophes()
    {
        Assert.Contains("hey jude", AliasGenerator.AliasesFor("Hey Jude - Remastered 2015", MatchKind.Track));
        Assert.Contains("umbrella", AliasGenerator.AliasesFor("Umbrella (feat. Jay-Z)", MatchKind.Track));
        Assert.Contains("dont stop believin", AliasGenerator.AliasesFor("Don't Stop Believin'", MatchKind.Track));
        Assert.Contains("dont stop believing", AliasGenerator.AliasesFor("Don't Stop Believin'", MatchKind.Track));
        var sat = AliasGenerator.AliasesFor("(I Can't Get No) Satisfaction", MatchKind.Track);
        Assert.Contains("satisfaction", sat); Assert.Contains("i cant get no satisfaction", sat);
        Assert.Contains("road trip", AliasGenerator.AliasesFor("Road Trip 2024", MatchKind.Playlist));
    }

    [Theory]
    [InlineData("XTC")] [InlineData("AC/DC")] [InlineData("Blinding Lights")] [InlineData("Guns N' Roses")]
    [InlineData("Hey Jude - Remastered 2015")] [InlineData("Blink-182")] [InlineData("")] [InlineData("The Beatles")]
    public void Aliases_never_include_the_original_and_are_unique_and_capped(string name)
    {
        var a = AliasGenerator.AliasesFor(name, MatchKind.Artist);
        Assert.True(a.Count <= 8);
        Assert.Equal(a.Count, a.Distinct().Count());
        Assert.DoesNotContain(a, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(a, string.IsNullOrWhiteSpace);
    }

    // ---------- performance, no IO ----------
    static List<Track> Generated(int n)
    {
        string[] w = ["love","night","fire","blue","river","dream","heart","gold","stone","wild","summer","ghost","angel","road","storm","silver","dance","shadow","moon","ocean","city","crazy","sweet","broken","electric","paper","velvet","thunder","midnight","echo"];
        var rnd = new Random(42);
        string Name(int k) => string.Join(' ', Enumerable.Range(0, k).Select(_ => w[rnd.Next(w.Length)] + (rnd.Next(5) == 0 ? rnd.Next(100).ToString() : "")));
        var list = new List<Track>(n);
        for (var i = 0; i < n; i++)
            list.Add(new Track($"\\\\nas.invalid\\share\\{i}.flac", Name(1 + rnd.Next(3)) + " " + i, "Band " + w[rnd.Next(w.Length)] + " " + rnd.Next(400), "Album " + rnd.Next(1500), "", 1, 1, 1000, 1960 + rnd.Next(60), null, 0, TrackSource.Nas,
                FilePath: $"\\\\nas.invalid\\share\\{i}.flac", Genre: i % 7 == 0 ? "Rock" : "Pop"));
        list.Add(T("Blinding Lights", "The Weeknd", "After Hours", 2020, "Pop"));
        list.Add(T("Radiohead Tribute", "Radiohead", "Pablo Honey", 1993));
        return list;
    }

    [Fact]
    public void Ten_thousand_songs_build_fast_and_query_fast()
    {
        var songs = Generated(10_000);
        var sw = Stopwatch.StartNew();
        var m = new VoiceMatcher(songs, Playlists());
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1500, $"build took {sw.ElapsedMilliseconds} ms");

        string[] says = ["play blinding lights", "play radio head", "play love", "play midnight echo 7", "play the beetles", "play fire by band moon 12", "play flurbo zanzibar", "play my road trip playlist", "play dream heart", "play songs from the 90s"];
        foreach (var s in says) m.Match(VoiceParser.FromRaw(s)); // warm up
        var times = new List<double>();
        for (var rep = 0; rep < 8; rep++)
            foreach (var s in says)
            {
                var q = VoiceParser.FromRaw(s);
                var t0 = Stopwatch.GetTimestamp();
                m.Match(q);
                times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            }
        times.Sort();
        var median = times[times.Count / 2];
        Assert.True(median < 25, $"median query {median:F2} ms (max {times[^1]:F2})");
        Assert.True(times[^1] < 250, $"slowest query {times[^1]:F2} ms");
        Assert.Equal("Blinding Lights", TitleOf(m.Match(VoiceParser.FromRaw("play blinding lights")).Ranked[0]));
        Assert.Equal(Decision.NotFound, m.Match(VoiceParser.FromRaw("play flurbo zanzibar")).Decision);
        Console.WriteLine($"PERF build={sw.ElapsedMilliseconds}ms median={median:F2}ms max={times[^1]:F2}ms");
    }

    [Fact]
    public void Matcher_touches_neither_the_network_nor_the_files_of_the_songs()
    {
        // the generated tracks point at an unreachable UNC share; a build or query that opened them would hang or throw
        var songs = Generated(300);
        var sw = Stopwatch.StartNew();
        var m = new VoiceMatcher(songs, []);
        m.Match(VoiceParser.FromRaw("play love night"));
        Assert.True(sw.ElapsedMilliseconds < 1500);
        Assert.DoesNotContain(typeof(VoiceMatcher).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
            f => f.FieldType.FullName!.StartsWith("System.Net") || f.FieldType.FullName.StartsWith("System.IO"));
    }
}
