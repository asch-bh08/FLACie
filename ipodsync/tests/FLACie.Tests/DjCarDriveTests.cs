using System.Text.RegularExpressions;
using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>The owner's complaints from a car drive (30 Oct): "Eminem, Without Me by Eminem", "because you skipped this song" in a playlist, "slowing down" before a
/// faster song, and no word when a playlist starts shuffled. Pure: the planner, the writer's prompt and checks, and the templates.</summary>
public class DjCarDriveTests
{
    static readonly DateTime T0 = new(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc);
    static DjSettings On(DjStyle st = DjStyle.Classic, DjChatty c = DjChatty.Every) => new() { Enabled = true, Chatty = c, Style = st };
    static DjSong Song(string title, string artist, string album = "", int year = 0, string genre = "", int track = 0) =>
        new(title, artist, album, "", year, genre, 200_000, "/m/song-" + Math.Abs(title.GetHashCode()) + ".flac", null, track);
    static readonly DjSong Prev = Song("Lose Yourself", "Eminem", "8 Mile", 2002, "Hip-Hop", 1);
    static readonly DjSong Next = Song("Without Me", "Eminem", "Curtain Call", 2002, "Hip-Hop", 9);
    static readonly DjLine Template = new(DjSlot.Intro, "intro1", "Alright, here's Without Me by Eminem.", "Alright, here's Without Me by Eminem.");
    static int Count(string text, string word) => Regex.Matches(text, Regex.Escape(word), RegexOptions.IgnoreCase).Count;

    // ---- 1. the same artist twice in a row ----

    [Fact]
    public void A_run_by_one_artist_tells_the_model_not_to_repeat_the_name()
    {
        var run = DjWriter.FactsOf(new DjRequest(DjSurface.Phone, Next, Prev, PlaylistId: "pl"), Template, T0);
        Assert.True(run.SameArtistRun);
        var (sys, user) = DjWriter.Prompt(run, []);
        Assert.Contains("same artist", user);
        Assert.Contains("Previous song: Lose Yourself", user);
        Assert.DoesNotContain("Lose Yourself by Eminem", user);
        Assert.Contains("twice", sys);
        var other = DjWriter.FactsOf(new DjRequest(DjSurface.Phone, Next, Song("Hey Ya!", "Outkast"), PlaylistId: "pl"), Template, T0);
        Assert.False(other.SameArtistRun);
        Assert.DoesNotContain("same artist", DjWriter.Prompt(other, []).User);
    }

    [Fact]
    public void A_line_naming_the_lead_artist_twice_is_refused_once_is_fine()
    {
        var f = DjWriter.FactsOf(new DjRequest(DjSurface.Phone, Next, Prev, PlaylistId: "pl"), Template, T0);
        Assert.Null(DjWriter.Check("Now Eminem, Without Me by Eminem.", f, []));
        Assert.Null(DjWriter.Check("Eminem again... Without Me, by Eminem.", f, []));
        Assert.Equal("Now, Without Me, from Eminem.", DjWriter.Check("Now, Without Me, from Eminem.", f, []));
        Assert.Equal("Alright... Without Me.", DjWriter.Check("Alright... Without Me.", f, []));
        Assert.Equal("More from Eminem.", DjWriter.Check("More from Eminem.", f, []));
        // a title that holds the artist's name is not the artist named twice
        var self = DjWriter.FactsOf(new DjRequest(DjSurface.Phone, Song("Eminem Show", "Eminem")), Template, T0);
        Assert.Equal("Here's Eminem Show, Eminem.", DjWriter.Check("Here's Eminem Show, Eminem.", self, []));
    }

    [Theory]
    [InlineData(DjStyle.Classic)] [InlineData(DjStyle.Chill)] [InlineData(DjStyle.Hype)]
    public void Templates_in_a_run_by_one_artist_name_them_at_most_once(DjStyle style)
    {
        var ids = new HashSet<string>();
        var seen = 0;
        for (var seed = 0; seed < 600; seed++)
        {
            var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(1) };
            var line = DjPlanner.Plan(On(style), new DjRequest(DjSurface.Phone, Next, Prev, PlaylistId: "pl", LocalHour: 14, Weekday: 3), mem, T0, new Random(seed));
            if (line is null) continue;
            seen++;
            Assert.True(Count(line.Caption, "Eminem") <= 1, line.Caption);
            ids.Add(line.TemplateId);
            // the artist is not worth saying again after their own song: only "More Eminem..." style lines name them
            if (line.Slot is DjSlot.Intro or DjSlot.Why or DjSlot.Year or DjSlot.Album or DjSlot.Genre or DjSlot.Feel or DjSlot.Time or DjSlot.Long)
                Assert.Equal(0, Count(line.Caption, "Eminem"));
        }
        Assert.True(seen > 100);
        Assert.True(ids.Count > 3);
    }

    // ---- 2. no skips in a playlist ----

    [Fact]
    public void In_their_own_playlist_neither_the_prompt_nor_the_context_talks_about_skips()
    {
        var req = new DjRequest(DjSurface.Auto, Next, Song("Poker Face", "Lady Gaga"), PrevPlayedOut: false, SkippedRecently: true, PlaylistId: "pl");
        var reset = new DjLine(DjSlot.Reset, "reset1", "x", "x");
        var f = DjWriter.FactsOf(req, reset, T0);
        var (sys, user) = DjWriter.Prompt(f, []);
        Assert.DoesNotContain("skip", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("direction", user, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("settled in", user);
        // the same when the phone sent no playlist (a queue)
        var queue = DjWriter.Prompt(DjWriter.FactsOf(req with { PlaylistId = null }, reset, T0), []).User;
        Assert.DoesNotContain("skip", queue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skip", sys);   // the character forbids it
        // the engine's own pick keeps the reset wording
        var picked = DjWriter.Prompt(DjWriter.FactsOf(req with { Why = "Because you play Eminem" }, reset, T0), []).User;
        Assert.Contains("change of direction", picked);

        // the shared context tags a skipped song "(skipped)" and gives the energy trend; for their own songs neither goes to the writer
        var ctx = new DjContext();
        ctx.Played("/m/a", "Poker Face", "Lady Gaga", true, 0.8, T0);
        ctx.Played("/m/b", "Bad Romance", "Lady Gaga", true, 0.7, T0.AddMinutes(1));
        ctx.Played("/m/c", "Paparazzi", "Lady Gaga", false, 0.3, T0.AddMinutes(2));
        Assert.Contains("(skipped)", ctx.Summary(T0.AddMinutes(3)));
        var own = ctx.Summary(T0.AddMinutes(3), engineChose: false);
        Assert.DoesNotContain("skipped", own);
        Assert.DoesNotContain("Energy", own);
        Assert.Contains("Poker Face", own);
    }

    [Fact]
    public void A_line_that_mentions_skips_or_a_change_of_direction_is_refused_unless_the_engine_picked_the_song()
    {
        var req = new DjRequest(DjSurface.Phone, Song("Valerie", "Amy Winehouse"), Song("Wonderwall", "Oasis"), PlaylistId: "pl");
        var f = DjWriter.FactsOf(req, Template, T0);
        foreach (var bad in new[] { "Since you skipped that one, here's Valerie, Amy Winehouse.", "Okay, a change of direction... Valerie, Amy Winehouse.",
                                    "Let's try something different... Valerie, Amy Winehouse.", "I picked Valerie, Amy Winehouse, for you.", "Slowing it down now... Valerie, Amy Winehouse." })
            Assert.Null(DjWriter.Check(bad, f, []));
        Assert.NotNull(DjWriter.Check("Here's Valerie, Amy Winehouse.", f, []));
        var picked = DjWriter.FactsOf(req with { PlaylistId = null, Why = "Because you play Oasis" }, Template, T0);
        Assert.NotNull(DjWriter.Check("Okay, a change of direction... Valerie, Amy Winehouse.", picked, []));
    }

    static readonly Regex Skippy = new(@"skip|different|direction|chang(e|ing) (it|things) up", RegexOptions.IgnoreCase);

    [Theory]
    [InlineData(DjStyle.Classic)] [InlineData(DjStyle.Chill)] [InlineData(DjStyle.Hype)]
    public void No_template_line_in_a_playlist_or_queue_talks_about_skips_or_steers(DjStyle style)
    {
        var steer = new Regex(@"not slowing|let's lift it|pick it up|slowing|nice and (slow|easy)|keep it easy|gently|drifting", RegexOptions.IgnoreCase);
        foreach (var playlist in new string?[] { "pl-1", null })
            for (var seed = 0; seed < 500; seed++)
            {
                var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(1) };
                foreach (var x in new[] { true, true, true, false, true }) mem.Skipped(x);
                var req = new DjRequest(DjSurface.Auto, Song("Valerie", "Amy Winehouse", "Back to Black", 2006, "Soul"), Song("Wonderwall", "Oasis"),
                    PrevPlayedOut: true, SkippedRecently: false, PlaylistId: playlist, LocalHour: 23, Weekday: 5);
                var extra = new DjExtra(Vibe: new Vibe(0.3, 0.4, 90, 0.4), PrevVibe: new Vibe(0.9, 0.9, 130, 0.8));
                var line = DjPlanner.Plan(On(style), req, mem, T0, new Random(seed), extra: extra);
                if (line is null) continue;
                Assert.DoesNotMatch(Skippy, line.Caption);
                Assert.DoesNotMatch(steer, line.Caption);
            }
    }

    // ---- 3. direction claims need strong evidence ----

    static Vibe V(double energy, double bpm, double loud = 0.5) => new(energy, loud, bpm, 0.5);

    [Fact]
    public void Poker_Face_to_Where_Them_Girls_At_is_not_slowing_down()
    {
        // stored Poker Face tempo 170 is a doubled-up estimate (really about 119); energy 0.75 -> 0.62 is not a clear drop
        Assert.Equal("", DjFeel.Of(V(0.62, 129), V(0.75, 170)));
        Assert.Equal("", DjFeel.Of(V(0.62, 129), V(0.75, 170), picked: true));
    }

    [Fact]
    public void A_direction_needs_a_clear_energy_move_and_a_tempo_that_does_not_disagree()
    {
        // energy alone, 0.4 down, tempo unknown: a calmer one
        Assert.Equal("a calmer one", DjFeel.Of(V(0.40, 0), V(0.80, 0)));
        Assert.Equal("a livelier one", DjFeel.Of(V(0.80, 0), V(0.45, 0)));
        // tempo agrees
        Assert.Equal("a calmer one", DjFeel.Of(V(0.40, 80), V(0.80, 120)));
        // tempo disagrees: clearly faster while the energy drops
        Assert.Equal("", DjFeel.Of(V(0.40, 140), V(0.80, 100)));
        // a ratio of 2, 0.5, 1.5 or 0.67 is a tempo error, so it is unknown: the energy decides alone
        Assert.Equal("a calmer one", DjFeel.Of(V(0.40, 160), V(0.80, 80)));
        Assert.Equal("a calmer one", DjFeel.Of(V(0.40, 120), V(0.80, 80)));
        Assert.Equal("a livelier one", DjFeel.Of(V(0.80, 60), V(0.45, 120)));
        // a big tempo drop with no energy move says nothing: bpm alone never decides
        Assert.Equal("", DjFeel.Of(V(0.60, 80), V(0.60, 130)));
        Assert.Equal("", DjFeel.Of(V(0.60, 140), V(0.55, 100)));
        // just under the threshold
        Assert.Equal("", DjFeel.Of(V(0.56, 0), V(0.80, 0)));
    }

    [Fact]
    public void The_engine_steering_wording_is_only_for_songs_it_picked()
    {
        Assert.Equal("slowing it down", DjFeel.Of(V(0.40, 0), V(0.80, 0), picked: true));
        Assert.Equal("picking up the pace", DjFeel.Of(V(0.80, 0), V(0.45, 0), picked: true));
        var req = new DjRequest(DjSurface.Phone, Next, Prev, PlaylistId: "pl");
        var extra = new DjExtra(Vibe: V(0.4, 0), PrevVibe: V(0.8, 0));
        Assert.Equal("a calmer one", DjWriter.FactsOf(req, Template, T0, extra).Feel);
        Assert.Equal("slowing it down", DjWriter.FactsOf(req with { PlaylistId = null, Why = "because you play Eminem" }, Template, T0, extra).Feel);
    }

    [Fact]
    public void Steering_templates_are_never_used_for_a_song_the_listener_chose()
    {
        var steer = new Regex(@"not slowing|let's lift it|pick it up a bit|nice and slow|nice and easy|keep it easy|gently|drifting on", RegexOptions.IgnoreCase);
        foreach (var t in DjTemplates.All.Where(t => steer.IsMatch(t.Text)))
            Assert.True(t.Need.HasFlag(DjNeed.Picked), t.Id);
        // and the lines over many songs: no steering for a playlist, some for the engine's picks
        var hype = 0;
        for (var seed = 0; seed < 600; seed++)
            foreach (var why in new string?[] { null, "because you play Oasis" })
            {
                var mem = new DjMemory { LastRequest = T0 - TimeSpan.FromMinutes(1) };
                var req = new DjRequest(DjSurface.Phone, Song("Valerie", "Amy Winehouse"), Song("Wonderwall", "Oasis"), PlaylistId: why is null ? "pl" : null, Why: why, LocalHour: 23, Weekday: 5);
                var line = DjPlanner.Plan(On(DjStyle.Hype), req, mem, T0, new Random(seed));
                if (line is null) continue;
                if (why is null) Assert.DoesNotMatch(steer, line.Caption);
                else if (steer.IsMatch(line.Caption)) hype++;
            }
        Assert.True(hype > 0);
    }

    // ---- 5. the character ----

    [Fact]
    public void The_character_does_not_claim_the_DJ_picked_songs_or_say_the_artist_twice()
    {
        Assert.Contains("never say the artist's name twice", DjWriter.Character, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unless the facts say you picked it", DjWriter.Character);
        Assert.Contains("Never mention a skipped song", DjWriter.Character);
        Assert.Contains("steering", DjWriter.Character);
    }

    // ---- 4. a playlist started on shuffle ----

    static readonly DjSong Firework = Song("Firework", "Katy Perry", "Teenage Dream", 2010, "Pop");
    static DjRequest Start(string name = "Pop", bool shuffled = true, DjSong? first = null, string? playlist = "pl-pop") =>
        new(DjSurface.Phone, first ?? Firework, null, PlaylistId: playlist, LocalHour: 14, Weekday: 3, QueueStart: true, QueueName: name, Shuffled: shuffled);

    [Fact]
    public void Shuffling_a_playlist_for_the_first_time_gets_a_line_that_says_so_and_names_the_first_song()
    {
        foreach (var style in new[] { DjStyle.Classic, DjStyle.Chill, DjStyle.Hype })
            for (var seed = 0; seed < 100; seed++)
            {
                var line = DjPlanner.Plan(On(style, DjChatty.Often), Start(), new DjMemory(), T0, new Random(seed));
                Assert.NotNull(line);
                Assert.Equal(DjSlot.Queue, line!.Slot);
                Assert.Contains("Pop", line.Caption); Assert.Contains("huffl", line.Caption);
                Assert.Contains("Firework", line.Caption);
                Assert.Contains("Katy Perry", line.Caption);
            }
        // not shuffled: said as playing it, never as shuffling
        var plain = DjPlanner.Plan(On(), Start(shuffled: false), new DjMemory(), T0, new Random(1))!;
        Assert.Equal(DjSlot.Queue, plain.Slot);
        Assert.DoesNotContain("huffl", plain.Caption);
        Assert.Contains("Pop", plain.Caption);
        // the line is the speaker's: spoken form too
        Assert.Contains("Pop", plain.Text);
    }

    [Fact]
    public void A_queue_start_line_follows_the_switches_the_budget_and_the_song_choices()
    {
        Assert.Null(DjPlanner.Plan(new DjSettings { Enabled = false }, Start(), new DjMemory(), T0, new Random(1)));                     // DJ off
        Assert.Null(DjPlanner.Plan(On(), Start() with { QueueDj = false }, new DjMemory(), T0, new Random(1)));                         // "DJ for this queue" off
        Assert.Null(DjPlanner.Plan(new DjSettings { Enabled = true, Playlists = { ["pl-pop"] = false } }, Start(), new DjMemory(), T0, new Random(1)));   // this playlist off
        Assert.NotNull(DjPlanner.Plan(new DjSettings { Enabled = false, Playlists = { ["pl-pop"] = true } }, Start(), new DjMemory(), T0, new Random(1)));
        var never = On(); DjRules.SetSong(never, "Firework", "Katy Perry", false);
        Assert.Null(DjPlanner.Plan(never, Start(), new DjMemory(), T0, new Random(1)));                                                  // "never talk over this song"
        Assert.Null(DjPlanner.Plan(On(), Start(first: Song("Intro", "Katy Perry") with { DurationMs = 30_000 }), new DjMemory(), T0, new Random(1)));   // a clip of a song
        Assert.Null(DjPlanner.Plan(On(), Start(first: Song("Chapter 1", "Reader", genre: "Audiobook")), new DjMemory(), T0, new Random(1)));          // spoken word
        // no name for the list: a normal opening, not a "Shuffling ..."
        Assert.NotEqual(DjSlot.Queue, DjPlanner.Plan(On(), Start(name: ""), new DjMemory(), T0, new Random(1))?.Slot);
        // Rare keeps to its half chance, like the hello at the start of a session
        var said = Enumerable.Range(0, 400).Count(seed => DjPlanner.Plan(On(DjStyle.Classic, DjChatty.Rare), Start(), new DjMemory(), T0, new Random(seed)) is not null);
        Assert.InRange(said, 120, 280);
        // Often and Every: always
        Assert.All(Enumerable.Range(0, 50), seed => Assert.NotNull(DjPlanner.Plan(On(DjStyle.Classic, DjChatty.Often), Start(), new DjMemory(), T0, new Random(seed))));
    }

    [Fact]
    public void The_line_for_a_new_queue_is_said_once_and_the_song_after_it_is_left_alone()
    {
        var mem = new DjMemory(); var st = On(DjStyle.Classic, DjChatty.Often);
        Assert.NotNull(DjPlanner.Plan(st, Start(), mem, T0, new Random(3)));
        // the next song change (asked as the first song plays) stays quiet: forced silence after a line
        var next = new DjRequest(DjSurface.Phone, Song("Roar", "Katy Perry"), Firework, PlaylistId: "pl-pop", LocalHour: 14, Weekday: 3);
        Assert.Null(DjPlanner.Plan(st, next, mem, T0.AddMinutes(4), new Random(3)));
        // a second start of the same queue in a session has no hello, but still says the line
        var again = DjPlanner.Plan(st, Start(), mem, T0.AddMinutes(6), new Random(4));
        Assert.Equal(DjSlot.Queue, again?.Slot);
        Assert.DoesNotContain("Good", again!.Caption);
    }

    [Fact]
    public void A_playlist_named_after_the_artist_does_not_say_the_artist_twice()
    {
        var req = Start(name: "Eminem", first: Next);
        for (var seed = 0; seed < 200; seed++)
        {
            var line = DjPlanner.Plan(On(), req, new DjMemory(), T0, new Random(seed))!;
            Assert.Equal(DjSlot.Queue, line.Slot);
            Assert.Equal(1, Count(line.Caption, "Eminem"));
            Assert.Contains("Without Me", line.Caption);
        }
    }

    [Fact]
    public void The_model_is_told_a_queue_has_started_and_may_use_the_list_name()
    {
        var f = DjWriter.FactsOf(Start(), new DjLine(DjSlot.Queue, "queue1", "Shuffling Pop... starting with Firework, by Katy Perry.", "Shuffling Pop... starting with Firework, by Katy Perry."), T0);
        Assert.Equal("Pop", f.QueueName);
        var (_, user) = DjWriter.Prompt(f, []);
        Assert.Contains("on shuffle", user);
        Assert.Contains("Pop", user);
        Assert.Contains("their own playlist", user);
        Assert.Equal("Shuffling Pop Hits... starting with Firework, Katy Perry.",
            DjWriter.Check("Shuffling Pop Hits... starting with Firework, Katy Perry.", f with { QueueName = "Pop Hits" }, []));
        // a list name that is not given is not made up
        Assert.Null(DjWriter.Check("Shuffling Road Trip... starting with Firework, Katy Perry.", f, []));
        // and a mid-queue line says nothing of queues
        var mid = DjWriter.Prompt(DjWriter.FactsOf(new DjRequest(DjSurface.Phone, Firework, Song("Roar", "Katy Perry")), Template, T0), []).User;
        Assert.DoesNotContain("shuffle", mid);
    }

    [Fact]
    public async Task The_server_answers_a_queue_start_with_the_line_for_the_phone_to_say()
    {
        var dir = Directory.CreateTempSubdirectory("flacie-dj-queue").FullName;
        try
        {
            var paths = new DataPaths(dir); var dp = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
            var h = new DjVoiceTests.Handler(_ => Task.FromResult(DjVoiceTests.Audio()));
            var v = new CloudflareVoice(paths, new DjVoiceTests.Factory(h), dp, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), new DjVoiceTests.Lines<CloudflareVoice>(), () => T0);
            var clips = new DjClips(paths, v, new DjVoiceTests.Lines<DjClips>(), () => T0);
            var dj = new DjService(new UserStateStore(paths, dp), v, clips, paths, new DjVoiceTests.Lines<DjService>(), () => T0);
            var me = new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" };
            var st = dj.Settings(me); st.Enabled = true; st.DeviceVoice = true; dj.Save(me, st);
            var cue = await dj.PrepareAsync(me, Start());
            Assert.NotNull(cue);
            Assert.Contains("Pop", cue!.Caption); Assert.Contains("Firework", cue.Caption);
            // asked again about the same start: the same line; the same song mid-queue is a different question
            Assert.Equal(cue, await dj.PrepareAsync(me, Start()));
            // the switch for this queue silences it
            Assert.Null(await dj.PrepareAsync(me, Start() with { QueueDj = false }));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact]
    public void The_queue_start_fields_go_over_the_wire_and_default_to_off()
    {
        Assert.Null(DjJson.Request(DjJson.RequestBody(Start()), out var back));
        Assert.True(back!.QueueStart); Assert.Equal("Pop", back.QueueName); Assert.True(back.Shuffled);
        Assert.Null(DjJson.Request(DjJson.RequestBody(Start(shuffled: false)), out var plain));
        Assert.False(plain!.Shuffled);
        Assert.Null(DjJson.Request(System.Text.Json.Nodes.JsonNode.Parse("""{ "surface": "phone", "next": { "title": "Yellow" } }""")!.AsObject(), out var none));
        Assert.False(none!.QueueStart); Assert.Null(none.QueueName); Assert.False(none.Shuffled);
        Assert.NotNull(DjJson.Request(System.Text.Json.Nodes.JsonNode.Parse("""{ "surface": "phone", "next": { "title": "Yellow" }, "queueStart": "yes" }""")!.AsObject(), out _));
    }
}
