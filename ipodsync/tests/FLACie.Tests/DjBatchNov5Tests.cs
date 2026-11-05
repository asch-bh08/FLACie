using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The owner's DJ report of 5 Nov (car and web): the first song announced, a line before every song on "Every song", the time of day said less
/// often and in different words, and no "from their playlist".</summary>
public class DjBatchNov5Tests
{
    static readonly DateTime T0 = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    static readonly string[] Genres = ["rock", "pop", "jazz", "soul", "disco"];
    static DjSong S(int i) => new($"Song {i}", $"Artist {(char)('A' + i % 26)}{i % 3}", $"Album {i % 40}", "", 1960 + i % 60, Genres[i % Genres.Length], 200_000, $"/m/{i}.flac");

    // ---- 1. announce the first song ----

    [Fact]
    public void A_tapped_song_gets_an_intro_with_announce_first_on_and_none_with_it_off()
    {
        var on = new DjSettings { Enabled = true, Chatty = DjChatty.Rare };
        // mid-session (the memory has just been asked), so no hello: still a line, on Rare too, and never "next"
        for (var seed = 0; seed < 50; seed++)
        {
            var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(3) };
            var l = DjPlanner.Plan(on, new DjRequest(DjSurface.Phone, S(seed), null, FirstSong: true, LocalHour: 9, Weekday: 3), mem, T0, new Random(seed));
            Assert.NotNull(l);
            Assert.True(l!.Slot is DjSlot.Open or DjSlot.Intro, l.Slot.ToString());
            Assert.DoesNotMatch(@"(?i)\bnext\b", l.Caption);
        }
        var off = new DjSettings { Enabled = true, Chatty = DjChatty.Every, AnnounceFirst = false };
        Assert.Null(DjPlanner.Plan(off, new DjRequest(DjSurface.Phone, S(1), null, FirstSong: true), new DjMemory(), T0, new Random(1)));
        // the song's own "never" still wins
        var never = new DjSettings { Enabled = true };
        DjRules.SetSong(never, "Song 1", S(1).Artist, false);
        Assert.Null(DjPlanner.Plan(never, new DjRequest(DjSurface.Phone, S(1), null, FirstSong: true), new DjMemory(), T0, new Random(1)));
    }

    [Fact]
    public void Announce_first_is_on_by_default_and_goes_through_the_json()
    {
        Assert.True(new DjSettings().AnnounceFirst);
        Assert.False(FLACie.Server.Dj.DjJson.Settings(new DjSettings { AnnounceFirst = false }, true)["announceFirst"]!.GetValue<bool>());
        var s = new DjSettings();
        Assert.Null(FLACie.Server.Dj.DjJson.Apply(new System.Text.Json.Nodes.JsonObject { ["announceFirst"] = false }, s));
        Assert.False(s.AnnounceFirst);
        Assert.Null(FLACie.Server.Dj.DjJson.Request(new System.Text.Json.Nodes.JsonObject
        {
            ["surface"] = "phone", ["next"] = new System.Text.Json.Nodes.JsonObject { ["title"] = "A", ["artist"] = "B" }, ["firstSong"] = true,
        }, out var req));
        Assert.True(req!.FirstSong);
    }

    // ---- 2. every song ----

    /// <summary>A long run on "Every song": albums played through, runs of one artist, short gaps, a quick skip now and then (the song after a skip still
    /// plays out); every song that is let play gets a line.</summary>
    [Theory]
    [InlineData(DjStyle.Classic, 1)]
    [InlineData(DjStyle.Chill, 2)]
    [InlineData(DjStyle.Hype, 3)]
    public void Every_song_gets_a_line_however_long_the_session(DjStyle style, int seed)
    {
        var st = new DjSettings { Enabled = true, Chatty = DjChatty.Every, Style = style };
        var mem = new DjMemory(); var rng = new Random(seed); var t = T0;
        DjSong? prev = null;
        var said = 0; var asked = 0;
        for (var i = 0; i < 120; i++)
        {
            // albums: songs 10-19 are one album in order; songs 40-49 one artist
            var next = i is >= 10 and < 20 ? new DjSong($"Track {i}", "Blur", "Parklife", "Blur", 1994, "rock", 200_000, $"/b/{i}.flac", null, i - 9, 1)
                : i is >= 40 and < 50 ? new DjSong($"Tune {i}", "Eminem", $"Album {i}", "", 2002, "hip-hop", 240_000, $"/e/{i}.flac")
                : S(i);
            var skippedBefore = i % 17 == 5;   // the song before was skipped quickly (it is not the one asked about)
            var req = new DjRequest(DjSurface.Auto, next, prev, PrevPlayedOut: !skippedBefore, SkippedRecently: skippedBefore, LocalHour: 9, Weekday: 3);
            var l = DjPlanner.Plan(st, req, mem, t, rng);
            asked++;
            if (l is not null) said++;
            else Assert.Fail($"no line before song {i} ({next.Title})");
            prev = next; t += TimeSpan.FromMinutes(i % 9 == 0 ? 1 : 3);
        }
        Assert.Equal(asked, said);
    }

    [Fact]
    public void Every_song_still_keeps_quiet_over_spoken_word_and_a_short_clip()
    {
        var st = new DjSettings { Enabled = true, Chatty = DjChatty.Every };
        var mem = new DjMemory();
        Assert.Null(DjPlanner.Plan(st, new DjRequest(DjSurface.Web, S(1) with { DurationMs = 30_000 }, S(0)), mem, T0, new Random(1)));
        Assert.Null(DjPlanner.Plan(st, new DjRequest(DjSurface.Web, S(2) with { Genre = "Audiobook" }, S(1)), mem, T0, new Random(1)));
    }

    // ---- 3. the time of day ----

    static List<DjLine> EveryLines(int n, int seed, int hour = 9, int weekday = 3)
    {
        var st = new DjSettings { Enabled = true, Chatty = DjChatty.Every };
        var mem = new DjMemory(); var rng = new Random(seed); var t = T0;
        DjSong? prev = null; var res = new List<DjLine>();
        for (var i = 0; i < n; i++)
        {
            var next = S(i);
            if (DjPlanner.Plan(st, new DjRequest(DjSurface.Web, next, prev, LocalHour: hour, Weekday: weekday), mem, t, rng) is { } l) res.Add(l);
            prev = next; t += TimeSpan.FromMinutes(3);
        }
        return res;
    }

    [Fact]
    public void The_time_of_day_is_offered_in_the_first_line_then_about_one_line_in_six_never_the_same_words_twice_running()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var lines = EveryLines(60, seed);
            Assert.NotNull(lines[0].TimePhrase);
            var offered = lines.Select((l, i) => (l.TimePhrase, i)).Where(x => x.TimePhrase is not null).ToList();
            // at most one in six after the first, and never two within five lines
            Assert.InRange(offered.Count, 2, 1 + 60 / DjPlanner.TimeEvery);
            for (var k = 1; k < offered.Count; k++)
            {
                Assert.True(offered[k].i - offered[k - 1].i >= DjPlanner.TimeEvery, $"seed {seed}: time at lines {offered[k - 1].i} and {offered[k].i}");
                Assert.NotEqual(offered[k - 1].TimePhrase, offered[k].TimePhrase);
            }
            // a line not offered the time says none: no weekday, no "morning"
            foreach (var l in lines.Where(l => l.TimePhrase is null))
                Assert.DoesNotMatch(@"(?i)\b(morning|afternoon|evening|tonight|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", l.Caption);
        }
    }

    [Fact]
    public void There_are_several_wordings_for_a_morning()
    {
        var w = DjPlanner.TimePhrases(9, 3);
        Assert.True(w.Count >= 4);
        Assert.Contains("this morning", w);
        Assert.Contains("on a Wednesday morning", w);
        Assert.Contains("early on", w);
        Assert.Contains("for your Wednesday", w);
        Assert.Empty(DjPlanner.TimePhrases(null, 3));
        Assert.DoesNotContain(DjPlanner.TimePhrases(9, null), x => x.Contains("day", StringComparison.Ordinal) && !x.Contains("today"));
    }

    [Fact]
    public void The_writer_is_given_the_time_only_when_the_planner_offers_it_and_is_told_not_to_reuse_it()
    {
        var req = new DjRequest(DjSurface.Web, S(2), S(1), LocalHour: 9, Weekday: 3);
        var plain = new DjLine(DjSlot.Intro, "intro1", "Alright, here's Song 2 by C2.", "Alright, here's Song 2 by C2.");
        var f = DjWriter.FactsOf(req, plain, T0);
        Assert.Equal("", f.TimeOfDay); Assert.Equal("", f.Day); Assert.Equal("", f.Colour);
        var (sys, user) = DjWriter.Prompt(f, ["Alright, here's Song 1 on this Wednesday morning."]);
        Assert.Contains("do not mention the time of day", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time", sys);
        var timed = DjWriter.FactsOf(req, plain with { TimePhrase = "early on" }, T0);
        Assert.Equal("early on", timed.Colour);
        var (_, user2) = DjWriter.Prompt(timed, []);
        Assert.Contains("early on", user2);
        // the model says the time when it was not offered: the template line instead
        Assert.Null(DjWriter.Check("Alright, on this Wednesday morning, Song 2 by C2", f, []));
        Assert.Equal("Alright, early on, here's Song 2 by C2", DjWriter.Check("Alright, early on, here's Song 2 by C2", timed, []));
        // the same time words as a recent line: refused
        Assert.Null(DjWriter.Check("So, early on, Song 2 by C2", timed, ["Okay, early on, Song 1 by B1"]));
    }

    // ---- 4. "from their playlist" ----

    [Fact]
    public void The_prompt_never_calls_the_listener_they_and_mid_queue_never_names_the_playlist()
    {
        var req = new DjRequest(DjSurface.Phone, Song("Without Me", "Eminem"), Song("Lose Yourself", "Eminem"), PlaylistId: "pl1");
        var line = new DjLine(DjSlot.Intro, "intro1", "Okay, now, Without Me.", "Okay, now, Without Me.");
        var f = DjWriter.FactsOf(req, line, T0);
        var (sys, user) = DjWriter.Prompt(f, []);
        Assert.DoesNotMatch(@"(?i)\b(their|they|them)\b", user);
        Assert.Contains("do not mention the playlist", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("your", sys);
    }

    static DjSong Song(string t, string a) => new(t, a, "", "", 0, "", 200_000, "/x/" + t + ".flac");

    [Theory]
    [InlineData("Next is Without Me, from their playlist")]
    [InlineData("Here's Without Me, from their own playlist")]
    [InlineData("Without Me, one they love")]
    [InlineData("Okay, Without Me, just what they asked for")]
    [InlineData("Here's Without Me, something for them")]
    [InlineData("Alright, Without Me, from your playlist")]
    public void A_line_about_the_listener_in_the_third_person_or_the_playlist_mid_queue_is_refused(string said)
    {
        var req = new DjRequest(DjSurface.Phone, Song("Without Me", "Eminem"), Song("Lose Yourself", "Eminem"), PlaylistId: "pl1");
        var f = DjWriter.FactsOf(req, new DjLine(DjSlot.Intro, "intro1", "Okay, now, Without Me.", "Okay, now, Without Me."), T0);
        Assert.Null(DjWriter.Check(said, f, []));
        Assert.Equal("Okay, here's Without Me, one you love", DjWriter.Check("Okay, here's Without Me, one you love", f, []));
    }

    [Fact]
    public void A_band_called_they_is_still_fine_and_the_queue_start_still_names_its_playlist()
    {
        var req = new DjRequest(DjSurface.Phone, Song("Kill You", "Eminem"), null, PlaylistId: "pl1", QueueStart: true, QueueName: "Rap Mix", Shuffled: true);
        var f = DjWriter.FactsOf(req, new DjLine(DjSlot.Queue, "queue1", "Shuffling Rap Mix... starting with Kill You, by Eminem.", "Shuffling Rap Mix... starting with Kill You, by Eminem."), T0);
        Assert.Equal("Shuffling your Rap Mix playlist, starting with Kill You, Eminem", DjWriter.Check("Shuffling your Rap Mix playlist, starting with Kill You, Eminem", f, []));
    }
}
