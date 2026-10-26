using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The DJ's line planner (FLACie.Core/Dj): which switch wins, how often it speaks, when it stays quiet, variety, and the spoken and written forms.</summary>
public class DjPlannerTests
{
    static readonly DateTime T0 = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    static readonly string[] Genres = ["rock", "pop", "jazz", "soul", "disco"];

    static DjSettings On(DjChatty c = DjChatty.Often) => new() { Enabled = true, Chatty = c };

    static DjSong S(int i) => new($"Song {i}", $"Artist {(char)('A' + i % 26)}{i % 3}", $"Album {i % 40}", "", 1960 + i % 60, Genres[i % Genres.Length], 200_000, $"/m/{i}.flac");

    static DjSong Song(string title, string artist, string album = "", int year = 0, string genre = "", long ms = 200_000, string path = "", int track = 0, int disc = 0, string albumArtist = "") =>
        new(title, artist, album, albumArtist, year, genre, ms, path, null, track, disc);

    /// <summary>A long queue played straight through, a song every 3 minutes: the line (or null) before each song after the first.</summary>
    static List<DjLine?> Run(DjChatty c, int seed, int n, string? why = null)
    {
        var mem = new DjMemory(); var rng = new Random(seed); var t = T0; var st = On(c);
        var res = new List<DjLine?>();
        var prev = S(0);
        for (var i = 1; i <= n; i++)
        {
            var next = S(i);
            res.Add(DjPlanner.Plan(st, new DjRequest(DjSurface.Web, next, prev, Why: why), mem, t, rng));
            prev = next; t += TimeSpan.FromMinutes(3);
        }
        return res;
    }

    /// <summary>The first line of [slot] over many seeds, for a song change in the middle of a session.</summary>
    static DjLine Find(DjSlot slot, DjRequest req, DjPronounce? say = null)
    {
        for (var seed = 0; seed < 3000; seed++)
        {
            var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(3) };
            if (DjPlanner.Plan(On(), req, mem, T0, new Random(seed), say) is { } l && l.Slot == slot) return l;
        }
        throw new Xunit.Sdk.XunitException("The planner never picked " + slot);
    }

    /// <summary>The opening line of a session (Often always speaks there).</summary>
    static DjLine? Open(DjSong next, DjPronounce? say = null, int? hour = null, int? weekday = null, int seed = 1) =>
        DjPlanner.Plan(On(), new DjRequest(DjSurface.Web, next, LocalHour: hour, Weekday: weekday), new DjMemory(), T0, new Random(seed), say);

    // ---- which switch wins ----

    [Fact]
    public void The_queue_switch_wins_then_the_playlist_then_on_off_with_the_device_tick()
    {
        var off = new DjSettings { Enabled = false, Playlists = { ["bed"] = false, ["party"] = true } };
        var on = new DjSettings { Enabled = true, Web = false, Playlists = { ["bed"] = false, ["party"] = true } };
        Assert.True(DjRules.EffectiveOn(off, DjSurface.Web, "bed", true));      // this queue: on, whatever else says
        Assert.False(DjRules.EffectiveOn(on, DjSurface.Phone, "party", false)); // this queue: off
        Assert.True(DjRules.EffectiveOn(off, DjSurface.Web, "party", null));    // the playlist's own switch
        Assert.False(DjRules.EffectiveOn(on, DjSurface.Phone, "bed", null));
        Assert.True(DjRules.EffectiveOn(on, DjSurface.Phone, "other", null));   // a playlist with no switch follows the rest
        Assert.False(DjRules.EffectiveOn(on, DjSurface.Web, null, null));       // DJ on, but not ticked for web
        Assert.False(DjRules.EffectiveOn(off, DjSurface.Phone, null, null));
    }

    [Fact]
    public void When_the_switches_say_off_it_never_speaks_and_remembers_nothing()
    {
        var mem = new DjMemory();
        Assert.Null(DjPlanner.Plan(new DjSettings(), new DjRequest(DjSurface.Phone, S(1)), mem, T0, new Random(1)));
        Assert.Equal(0, mem.Songs);
        Assert.Null(mem.LastRequest);
        Assert.Null(DjPlanner.Plan(On(), new DjRequest(DjSurface.Phone, S(1), QueueDj: false), mem, T0, new Random(1)));
        Assert.Equal(0, mem.Songs);
    }

    // ---- how often ----

    [Theory]
    [InlineData(DjChatty.Often, 0.26, 0.37)]   // about 1 in 3.2
    [InlineData(DjChatty.Rare, 0.10, 0.17)]    // about 1 in 7.3
    public void It_speaks_about_as_often_as_the_setting_says(DjChatty c, double min, double max)
    {
        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            var lines = Run(c, seed, 2000);
            var rate = lines.Count(l => l is not null) / (double)lines.Count;
            Assert.InRange(rate, min, max);
        }
    }

    [Theory]
    [InlineData(DjChatty.Often, 1)]
    [InlineData(DjChatty.Rare, 4)]
    public void After_a_line_the_forced_silent_songs_pass_and_it_never_speaks_twice_running(DjChatty c, int forced)
    {
        var lines = Run(c, 7, 1500);
        var at = lines.Select((l, i) => (l, i)).Where(x => x.l is not null).Select(x => x.i).ToList();
        Assert.True(at.Count > 50);
        for (var k = 1; k < at.Count; k++) Assert.True(at[k] - at[k - 1] > forced, $"lines at {at[k - 1]} and {at[k]}");
    }

    [Fact]
    public void Often_always_opens_a_session_and_Rare_about_half_the_time()
    {
        for (var seed = 0; seed < 50; seed++) Assert.Equal(DjSlot.Open, Open(S(3), seed: seed)!.Slot);
        var rare = Enumerable.Range(0, 400).Count(seed => DjPlanner.Plan(On(DjChatty.Rare), new DjRequest(DjSurface.Web, S(3)), new DjMemory(), T0, new Random(seed)) is not null);
        Assert.InRange(rare, 150, 250);
    }

    [Fact]
    public void Thirty_minutes_without_a_question_starts_a_new_session_with_an_opening_line()
    {
        var mem = new DjMemory(); var rng = new Random(3);
        var t = T0; var prev = S(0);
        for (var i = 1; i < 20; i++) { DjPlanner.Plan(On(), new DjRequest(DjSurface.Web, S(i), prev), mem, t, rng); prev = S(i); t += TimeSpan.FromMinutes(3); }
        var after = DjPlanner.Plan(On(), new DjRequest(DjSurface.Web, S(50), prev), mem, t + TimeSpan.FromMinutes(31), rng);
        Assert.Equal(DjSlot.Open, after!.Slot);
    }

    [Theory]
    [InlineData(9, 6, "Saturday morning. ")]
    [InlineData(9, 0, "Sunday morning. ")]
    [InlineData(9, 3, "Good morning. ")]
    [InlineData(14, 3, "Good afternoon. ")]
    [InlineData(20, 6, "Good evening. ")]
    [InlineData(1, 6, "Good evening. ")]
    public void The_opening_line_greets_by_the_devices_time(int hour, int weekday, string start)
    {
        var l = Open(Song("Mr. Blue Sky", "Electric Light Orchestra"), hour: hour, weekday: weekday)!;
        Assert.StartsWith(start, l.Text);
        Assert.StartsWith(start, l.Caption);
    }

    [Fact]
    public void No_greeting_at_night_or_when_the_time_is_unknown()
    {
        Assert.DoesNotContain("Good", Open(Song("Mr. Blue Sky", "Electric Light Orchestra"), hour: 4)!.Text);
        Assert.DoesNotContain("Good", Open(Song("Mr. Blue Sky", "Electric Light Orchestra"))!.Text);
    }

    // ---- silent without a draw ----

    [Fact]
    public void It_stays_quiet_for_short_songs_skipping_albums_in_order_and_spoken_word()
    {
        var song = Song("Yellow", "Coldplay", "Parachutes", 2000, "rock", track: 5, disc: 1);
        Assert.NotNull(Open(song));   // the same song speaks when nothing says otherwise
        DjLine? Ask(DjRequest r) => DjPlanner.Plan(On(), r, new DjMemory(), T0, new Random(1));
        Assert.Null(Ask(new DjRequest(DjSurface.Web, song with { DurationMs = 45_000 })));
        Assert.Null(Ask(new DjRequest(DjSurface.Web, song, SkippedRecently: true)));
        Assert.Null(Ask(new DjRequest(DjSurface.Web, song, song with { Title = "Shiver", TrackNo = 4 })));                 // the next track, in order
        Assert.Null(Ask(new DjRequest(DjSurface.Web, song with { TrackNo = 1, DiscNo = 2 }, song with { Title = "End", TrackNo = 10 })));   // on to disc 2
        Assert.NotNull(Ask(new DjRequest(DjSurface.Web, song, song with { Title = "Shiver", TrackNo = 2 })));             // the same album, shuffled
        Assert.NotNull(Ask(new DjRequest(DjSurface.Web, song, song with { Title = "Shiver", TrackNo = 4, Album = "A Rush of Blood" })));
        Assert.Null(Ask(new DjRequest(DjSurface.Web, song with { Genre = "Spoken Word" })));
        Assert.Null(Ask(new DjRequest(DjSurface.Web, song with { Genre = "Audiobook" })));
    }

    [Fact]
    public void An_unknown_artist_with_a_file_name_title_is_never_spoken()
    {
        var path = "/music/Unsorted/06 - Everything Is Sound.flac";
        Assert.Null(Open(Song("06 - Everything Is Sound", "", path: path)));
        Assert.Null(Open(Song("06 - Everything Is Sound", "Unknown Artist", path: path)));
    }

    // ---- variety ----

    [Fact]
    public void No_template_again_within_12_lines_and_no_slot_twice_running()
    {
        foreach (var seed in new[] { 11, 12, 13 })
        {
            var lines = Run(DjChatty.Often, seed, 1500).OfType<DjLine>().ToList();
            for (var k = 1; k < lines.Count; k++)
            {
                Assert.DoesNotContain(lines[k].TemplateId, lines.Skip(Math.Max(0, k - 12)).Take(Math.Min(k, 12)).Select(l => l.TemplateId));
                Assert.NotEqual(lines[k - 1].Slot, lines[k].Slot);
                Assert.True(DjText.WordCount(lines[k].Text) <= DjPlanner.MaxWords, lines[k].Text);
                Assert.DoesNotContain("!", lines[k].Text);
            }
        }
    }

    [Fact]
    public void The_why_is_used_at_most_3_lines_in_6()
    {
        var lines = Run(DjChatty.Often, 21, 1500, "Same energy as Out of Space · you play The Prodigy a lot").OfType<DjLine>().ToList();
        Assert.Contains(lines, l => l.Slot == DjSlot.Why);
        for (var k = 0; k + 6 <= lines.Count; k++) Assert.True(lines.Skip(k).Take(6).Count(l => l.Slot == DjSlot.Why) <= 3);
    }

    [Fact]
    public void A_why_line_joins_two_parts_with_and_and_stays_under_18_words()
    {
        var req = new DjRequest(DjSurface.Web, Song("Breathe", "The Prodigy"), Song("Firestarter", "Daft Punk"), Why: "Same energy as Out of Space · you play The Prodigy a lot");
        var l = Find(DjSlot.Why, req);
        Assert.Contains("Breathe", l.Text);
        Assert.True(DjText.WordCount(l.Text) <= 18, l.Text);
        Assert.Contains("ame energy as Out of Space", l.Caption);
    }

    [Fact]
    public void The_why_is_split_cleaned_and_kept_to_two_parts()
    {
        Assert.Equal(new[] { "Same energy as Out of Space", "You play The Prodigy a lot" }, DjWhy.Parts("Same energy as Out of Space · you play The Prodigy a lot"));
        Assert.Equal(new[] { "Something new for you" }, DjWhy.Parts("New to you · played 12 times this week"));   // digits drop a part
        Assert.Equal(new[] { "Close to Florence + the Machine" }, DjWhy.Parts("An absolute banger · close to Florence + the Machine"));
        Assert.Empty(DjWhy.Parts("I think you will like this one"));
        Assert.Empty(DjWhy.Parts("this part is far too long to be said by the dj at all"));   // 9+ words
        Assert.Equal(2, DjWhy.Parts("a · b · c").Count);
        Assert.Empty(DjWhy.Parts(null));
    }

    // ---- spoken and written forms ----

    [Theory]
    [InlineData(1997, "nineteen ninety-seven")]
    [InlineData(1905, "nineteen oh five")]
    [InlineData(1900, "nineteen hundred")]
    [InlineData(2000, "two thousand")]
    [InlineData(2005, "two thousand and five")]
    [InlineData(2010, "twenty ten")]
    [InlineData(2012, "twenty twelve")]
    [InlineData(2026, "twenty twenty-six")]
    public void Years_are_said_the_way_people_say_them(int year, string said) => Assert.Equal(said, DjNumbers.Year(year));

    [Theory]
    [InlineData(7, "seven")]
    [InlineData(12, "twelve")]
    [InlineData(21, "twenty-one")]
    [InlineData(105, "one hundred and five")]
    public void Minutes_are_said_in_words(int n, string said) => Assert.Equal(said, DjNumbers.Words(n));

    [Fact]
    public void A_year_line_says_the_year_in_words_and_writes_it_in_digits()
    {
        var l = Find(DjSlot.Year, new DjRequest(DjSurface.Web, Song("Bitter Sweet Symphony", "The Verve", year: 1997), Song("Wonderwall", "Oasis")));
        Assert.Contains(l.TemplateId is "year5" or "year6" ? "nineties" : "nineteen ninety-seven", l.Text);
        Assert.DoesNotContain("1997", l.Text);
        Assert.Contains(l.TemplateId is "year5" or "year6" ? "nineties" : "1997", l.Caption);
    }

    [Fact]
    public void No_year_for_compilations_or_remasters()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(3) };
            var l = DjPlanner.Plan(On(), new DjRequest(DjSurface.Web, Song("Bohemian Rhapsody", "Queen", "Greatest Hits", 1981), Song("Wonderwall", "Oasis")), mem, T0, new Random(seed));
            Assert.NotEqual(DjSlot.Year, l?.Slot);
        }
    }

    [Fact]
    public void A_long_song_says_its_minutes_in_words()
    {
        var l = Find(DjSlot.Long, new DjRequest(DjSurface.Web, Song("I'd Do Anything for Love", "Meat Loaf", ms: 12 * 60_000 + 20_000), Song("Wonderwall", "Oasis")));
        Assert.Contains("about twelve minutes", l.Text);
        Assert.Contains("about 12 minutes", l.Caption);
    }

    [Fact]
    public void A_soundtrack_album_is_named_without_its_tag()
    {
        var l = Find(DjSlot.Album, new DjRequest(DjSurface.Web, Song("Let It Go", "Idina Menzel", "Frozen (Original Motion Picture Soundtrack)"), Song("Wonderwall", "Oasis")));
        Assert.Contains("Frozen", l.Caption);
        Assert.DoesNotContain("Original", l.Caption);
        Assert.Equal("Frozen", DjText.Album("Frozen (Original Motion Picture Soundtrack)", out var st));
        Assert.True(st);
        Assert.Equal("Parachutes", DjText.Album("Parachutes (Deluxe Edition)", out st));
        Assert.False(st);
        Assert.Equal("", DjText.Album("Unknown Album", out _));
    }

    [Theory]
    [InlineData("Hey Jude - Remastered 2015", "Hey Jude")]
    [InlineData("Yellow (Remastered 2011)", "Yellow")]
    [InlineData("Valerie (feat. Amy Winehouse)", "Valerie")]
    [InlineData("Song [Live]", "Song")]
    [InlineData("Song (Radio Edit)", "Song")]
    [InlineData("Song [Deluxe]", "Song")]
    [InlineData("1979", "1979")]
    [InlineData("(Don't Fear) The Reaper", "(Don't Fear) The Reaper")]
    public void Titles_lose_their_version_tags(string raw, string written) => Assert.Equal(written, DjText.Title(raw));

    [Fact]
    public void A_guest_from_feat_becomes_with_and_an_ampersand_never_splits_an_act()
    {
        Assert.Equal(new DjText.Credit("Mark Ronson", "Amy Winehouse"), DjText.CreditOf("Mark Ronson", "Valerie (feat. Amy Winehouse)"));
        Assert.Equal(new DjText.Credit("Mark Ronson", "Amy Winehouse"), DjText.CreditOf("Mark Ronson feat. Amy Winehouse", "Valerie"));
        Assert.Equal(new DjText.Credit("Simon & Garfunkel", null), DjText.CreditOf("Simon & Garfunkel", "The Boxer"));
        var l = Open(Song("Valerie (feat. Amy Winehouse)", "Mark Ronson"))!;
        Assert.Contains("Mark Ronson with Amy Winehouse", l.Text);
        Assert.DoesNotContain("feat", l.Text);
        Assert.DoesNotContain("feat", l.Caption);
        var sg = Open(Song("The Boxer", "Simon & Garfunkel"))!;
        Assert.Contains("Simon and Garfunkel", sg.Text);
        Assert.Contains("Simon & Garfunkel", sg.Caption);
    }

    [Fact]
    public void Long_credits_say_the_lead_act_only()
    {
        var c = DjText.CreditOf("Kool Keith, Dan the Automator, Del the Funky Homosapien", "Song");
        Assert.Equal("Kool Keith", c.Lead);
    }

    [Theory]
    [InlineData("ABBA", "Abba")]
    [InlineData("SOS", "S O S")]
    [InlineData("HUMBLE.", "Humble")]
    [InlineData("Mr. Blue Sky", "Mr. Blue Sky")]
    [InlineData("Wait...", "Wait...")]
    [InlineData("ALL CAPS TITLE", "All Caps Title")]
    [InlineData("DON'T STOP", "Don't Stop")]
    [InlineData("AC/DC", "A C D C")]
    [InlineData("Rocky II", "Rocky II")]
    [InlineData("P!nk", "Pink")]
    [InlineData("Florence + the Machine", "Florence and the Machine")]
    [InlineData("Don't Stop Me Now 🎸", "Don't Stop Me Now")]
    [InlineData("\"Heroes\"", "Heroes")]
    [InlineData("Is This It?", "Is This It")]
    public void Spoken_names_lose_shouting_symbols_and_emoji(string written, string said) => Assert.Equal(said, DjText.Speak(written));

    [Fact]
    public void Captions_keep_the_real_spelling_while_the_voice_gets_the_respelling()
    {
        var l = Open(Song("SOS", "ABBA"))!;
        Assert.Contains("S O S", l.Text); Assert.Contains("Abba", l.Text);
        Assert.Contains("SOS", l.Caption); Assert.Contains("ABBA", l.Caption);
        var h = Open(Song("HUMBLE.", "Kendrick Lamar"))!;
        Assert.Contains("Humble", h.Text);
        Assert.Contains("HUMBLE.", h.Caption);
        Assert.DoesNotContain("..", h.Caption);
    }

    [Fact]
    public void A_file_name_title_is_not_spoken_but_the_artist_still_is()
    {
        var l = Open(Song("06 - Everything Is Sound", "Jason Mraz", path: "/music/Jason Mraz/06 - Everything Is Sound.flac"))!;
        Assert.Equal(DjSlot.ArtistOnly, l.Slot);
        Assert.Contains("Jason Mraz", l.Text);
        Assert.DoesNotContain("Everything", l.Text);
    }

    [Fact]
    public void An_unknown_artist_gives_a_title_only_line()
    {
        foreach (var who in new[] { "Unknown Artist", "", "Various Artists" })
        {
            var l = Open(Song("The Wheels on the Bus", who))!;
            Assert.Equal(DjSlot.TitleOnly, l.Slot);
            Assert.Contains("The Wheels on the Bus", l.Text);
            Assert.DoesNotContain("Unknown", l.Text); Assert.DoesNotContain("Various", l.Text);
        }
    }

    [Fact]
    public void A_very_long_title_gives_an_artist_only_line()
    {
        var l = Open(Song("The Boy With the Arab Strap and a Very Long Name Indeed", "Belle and Sebastian"))!;
        Assert.Equal(DjSlot.ArtistOnly, l.Slot);
    }

    // ---- pronunciation ----

    [Fact]
    public void Pronunciation_overrides_change_the_spoken_line_only()
    {
        var l = Open(Song("Halo", "Beyoncé"))!;
        Assert.Contains("Bee-yon-say", l.Text);
        Assert.Contains("Beyoncé", l.Caption);
        Assert.DoesNotContain("Bee-yon-say", l.Caption);
        Assert.Contains("Churches", Open(Song("The Mother We Share", "CHVRCHES"))!.Text);
        Assert.Contains("Dvor-zhak", Open(Song("Symphony No. 9", "Antonín Dvořák"))!.Text);
    }

    [Fact]
    public void The_servers_pronounce_file_adds_and_replaces_entries()
    {
        var dir = Directory.CreateTempSubdirectory("flacie-dj-say").FullName;
        try
        {
            var f = Path.Combine(dir, "dj-pronounce.tsv");
            File.WriteAllText(f, "# written\tkind\tsay\n" + "Bon Iver\tartist\tBon Ee-vair\n" + "Sia  artist  Sigh-a\n" + "nonsense line\n" + "x\tsong\ty\n");
            var say = DjPronounce.Load(f);
            Assert.Equal("Bon Ee-vair", say.Artist("Bon Iver"));
            Assert.Equal("Sigh-a", say.Artist("Sia"));                // two or more spaces also separate columns; the file wins
            Assert.Equal("Bee-yon-say", say.Artist("Beyoncé"));       // the starter list stays
            var l = Open(Song("Holocene", "Bon Iver"), say)!;
            Assert.Contains("Bon Ee-vair", l.Text);
            Assert.Contains("Bon Iver", l.Caption);
            Assert.Same(DjPronounce.Default, DjPronounce.Load(Path.Combine(dir, "missing.tsv")));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    // ---- other slots ----

    [Fact]
    public void An_artist_heard_a_while_ago_gets_a_back_to_line()
    {
        var mem = new DjMemory(); var t = T0; var prev = Song("Do I Wanna Know", "Arctic Monkeys");
        DjPlanner.Plan(On(), new DjRequest(DjSurface.Web, prev), mem, t, new Random(1));
        for (var i = 1; i <= 8; i++) { t += TimeSpan.FromMinutes(4); var n = S(i); DjPlanner.Plan(On(), new DjRequest(DjSurface.Web, n, prev), mem, t, new Random(i)); prev = n; }
        var back = Song("R U Mine?", "Arctic Monkeys");
        var seen = false;
        for (var seed = 0; seed < 400 && !seen; seed++)
        {
            var copy = new DjMemory { LastRequest = mem.LastRequest, Songs = mem.Songs };
            foreach (var (k, v) in mem.Artists) copy.Artists[k] = v;
            seen = DjPlanner.Plan(On(), new DjRequest(DjSurface.Web, back, prev), copy, t + TimeSpan.FromMinutes(4), new Random(seed))?.Slot == DjSlot.Return;
        }
        Assert.True(seen);
    }

    [Fact]
    public void The_same_artist_twice_gets_a_more_from_line_and_never_a_that_was_by_the_same_act()
    {
        var req = new DjRequest(DjSurface.Web, Song("Shake It Off", "Taylor Swift"), Song("Blank Space", "Taylor Swift"));
        var l = Find(DjSlot.SameArtist, req);
        Assert.Contains("Taylor Swift", l.Text);
        for (var seed = 0; seed < 300; seed++)
            Assert.NotEqual(DjSlot.Bridge, DjPlanner.Plan(On(), req, new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(3) }, T0, new Random(seed))?.Slot);
    }

    [Fact]
    public void After_a_run_of_skips_the_first_full_play_can_reset()
    {
        var req = new DjRequest(DjSurface.Web, Song("Valerie", "Amy Winehouse"), Song("Wonderwall", "Oasis"));
        var seen = false;
        for (var seed = 0; seed < 400 && !seen; seed++)
        {
            var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(1) };
            foreach (var x in new[] { true, true, true, false, true }) mem.Skipped(x);
            seen = DjPlanner.Plan(On(), req, mem, T0, new Random(seed))?.Slot == DjSlot.Reset;
        }
        Assert.True(seen);
    }

    [Fact]
    public void The_same_song_change_with_the_same_seed_gives_the_same_line()
    {
        DjLine? Once() => DjPlanner.Plan(On(), new DjRequest(DjSurface.Web, S(5), S(4)), new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(3) }, T0, new Random(42));
        Assert.Equal(Once(), Once());
    }

    [Fact]
    public void There_are_60_templates_with_unique_ids_and_no_exclamation_marks()
    {
        Assert.Equal(60, DjTemplates.All.Count);
        Assert.Equal(60, DjTemplates.All.Select(t => t.Id).Distinct().Count());
        Assert.DoesNotContain(DjTemplates.All, t => t.Text.Contains('!'));
    }
}
