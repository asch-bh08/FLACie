using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>The DJ timed like a radio presenter (docs/dj-timing-report.md): a song's intro from its synced lyrics or its level envelope, the rule that places the
/// clip to end before the singing (or leads the song in under a long line), and the writer's word target. The Android app has the same timing tests (DjTimingTest.kt).</summary>
public class DjTimingTests
{
    static Lyrics Lrc(string text) => LyricsService.Parse(text, "test")!;

    [Fact]
    public void The_intro_is_the_first_sung_line_of_synced_lyrics_a_touch_early()
    {
        var i = DjIntro.FromLyrics(Lrc("[00:00.00]\n[00:08.50]First words here\n[00:12.00]Second line"));
        Assert.NotNull(i);
        Assert.Equal(8_300, i.Ms); Assert.Equal("lyrics", i.Source); Assert.True(i.Confidence >= DjIntro.MinConfidence);
        Assert.Equal(8_300, DjIntro.Usable(i));
    }

    [Fact]
    public void Unsynced_or_odd_lyrics_say_nothing()
    {
        Assert.Null(DjIntro.FromLyrics(Lrc("Just words\nNo times")));
        Assert.Null(DjIntro.FromLyrics(Lrc("[01:30.00]Far too late for a first line")));
        Assert.Null(DjIntro.FromLyrics(null));
    }

    /// <summary>A level envelope a tenth of a second apart: [quietS] seconds at [quiet], then the rest at [loud].</summary>
    static float[] Envelope(int quietS, float quiet, float loud, int totalS = 120)
    {
        var l = new float[totalS * 10];
        for (var i = 0; i < l.Length; i++) l[i] = i < quietS * 10 ? quiet : loud;
        return l;
    }

    [Fact]
    public void A_quiet_intro_then_a_lift_gives_the_intro_from_the_levels()
    {
        var i = DjIntro.FromLevels(Envelope(10, 0.2f, 0.8f), 0.1);
        Assert.NotNull(i);
        Assert.Equal("levels", i.Source);
        Assert.InRange(i.Ms, 9_000, 10_000);                 // the lift at 10 s, placed a little early
        Assert.True(i.Confidence >= 0.9);                    // a quarter of the level: clear
        Assert.Equal(i.Ms, DjIntro.Usable(i));
    }

    [Fact]
    public void Lyrics_win_over_the_levels()
    {
        var i = DjIntro.Best(Lrc("[00:15.00]Hello"), Envelope(10, 0.2f, 0.8f), 0.1);
        Assert.Equal("lyrics", i.Source); Assert.Equal(14_800, i.Ms);
    }

    [Fact]
    public void No_lift_or_a_weak_one_is_not_used_and_the_DJ_talks_as_before()
    {
        Assert.Null(DjIntro.FromLevels(Envelope(0, 0.7f, 0.7f), 0.1));          // loud from the start
        Assert.Null(DjIntro.FromLevels(Envelope(10, 0.6f, 0.8f), 0.1));         // the intro is nearly as loud: not clear enough
        Assert.Null(DjIntro.FromLevels(Envelope(60, 0.2f, 0.8f), 0.1));         // a lift after 40 s is not an intro
        var none = DjIntro.Best(null, Envelope(0, 0.7f, 0.7f), 0.1);
        Assert.Same(DjIntro.None, none);
        Assert.Null(DjIntro.Usable(none));
        Assert.Null(DjIntro.Usable(new SongIntro(8_000, 0.3, "levels")));       // low confidence
        Assert.Null(DjIntro.Usable(null));
        Assert.Equal(DjTimingPlan.Now, DjTiming.Plan(null, 4_000));
        Assert.Equal(DjTimingPlan.Now, DjTiming.Plan(8_000, null));
    }

    [Fact]
    public void The_vibe_pass_keeps_the_intro_with_the_vibe()
    {
        var v = VibeFillService.Analyse(Envelope(10, 0.2f, 0.8f), 0.1, null);
        Assert.Equal("levels", v.Intro?.Source);
        var w = VibeFillService.Analyse(Envelope(0, 0.7f, 0.7f), 0.1, null);
        Assert.Same(DjIntro.None, w.Intro);                  // looked at: not looked at again
    }

    [Fact]
    public void A_clip_that_fits_the_intro_ends_half_a_second_before_the_singing()
    {
        var p = DjTiming.Plan(introMs: 12_000, clipMs: 4_000);
        Assert.Equal(new DjTimingPlan(7_500, 0), p);
        Assert.Equal(12_000 - DjTiming.MarginMs, p.ClipAtMs + 4_000);
        Assert.Equal(new DjTimingPlan(0, 0), DjTiming.Plan(4_500, 4_000));     // just fits: at once, no hold
    }

    [Fact]
    public void A_clip_longer_than_the_intro_leads_the_song_in_never_more_than_one_and_a_half_seconds()
    {
        Assert.Equal(new DjTimingPlan(0, 1_000), DjTiming.Plan(4_000, 4_500));   // the line ends half a second before the singing
        Assert.Equal(new DjTimingPlan(0, 1_500), DjTiming.Plan(0, 6_000));       // straight into the vocals: held 1.5 s at most
        foreach (var intro in new[] { 0, 1_000, 3_000, 6_000, 20_000 })
            foreach (var clip in new[] { 500, 2_000, 5_000, 9_000, 15_000 })
            {
                var p = DjTiming.Plan(intro, clip);
                Assert.InRange(p.SongDelayMs, 0, DjTiming.MaxSongDelayMs);
                Assert.True(p.ClipAtMs >= 0);
                // fitting or not, the clip never starts after the point where it would still end before the singing
                if (p.ClipAtMs > 0) Assert.True(p.ClipAtMs + clip <= intro - DjTiming.MarginMs);
            }
    }

    [Fact]
    public void The_word_target_follows_the_intro_at_the_speaking_rate()
    {
        Assert.Equal(13, DjTiming.TargetWords(6_000));       // (6 - 0.5 - 0.3) s at 2.6 words a second
        Assert.Equal(0, DjTiming.TargetWords(2_000));        // too short for a line: it leads in instead
        Assert.Equal(0, DjTiming.TargetWords(30_000));       // room for any line
        Assert.Equal(0, DjTiming.TargetWords(null));
        Assert.Equal(DjTiming.ClipPadMs + 5_000, DjTiming.EstimateMs("one two three four five six seven eight nine ten eleven twelve thirteen"));
    }

    static readonly DjSong Smack = new("Smack That", "Akon, Eminem", "Konvicted", "Akon", 2006, "Hip-Hop", 212_000, "/m/smack.flac");
    static readonly DjLine Template = new(DjSlot.Intro, "intro1", "Next, Smack That by Akon with Em-in-em.", "Next, Smack That by Akon with Eminem.");

    [Fact]
    public void The_writer_is_asked_for_a_line_that_fits_the_intro()
    {
        var now = new DateTime(2026, 10, 25, 19, 0, 0, DateTimeKind.Utc);
        var extra = new DjExtra(Vibe: new Vibe(0.5, 0.5, 100, 0.5) { Intro = new SongIntro(6_000, 0.9, "lyrics") });
        var f = DjWriter.FactsOf(new DjRequest(DjSurface.Web, Smack), Template, now, extra);
        Assert.Equal(6_000, f.IntroMs); Assert.Equal(13, f.TargetWords);
        var (_, user) = DjWriter.Prompt(f, []);
        Assert.Contains("about 6 seconds, so keep the line to about 13 words", user);
        Assert.Equal(16, DjWriter.WordLimit(f));
        Assert.Null(DjWriter.Check("Here is Smack That by Akon, a song that goes on and on and on for far too long to fit before the singing", f, []));
        // no intro known: no target, and the usual limit
        var plain = DjWriter.FactsOf(new DjRequest(DjSurface.Web, Smack), Template, now);
        Assert.Equal(0, plain.TargetWords); Assert.DoesNotContain("Length", DjWriter.Prompt(plain, []).User);
        Assert.Equal(DjWriter.MaxWords, DjWriter.WordLimit(plain));
        Assert.NotNull(DjWriter.Check("Here is Smack That by Akon, a song that goes on and on and on for far too long to fit before the singing", plain, []));
    }

    [Fact]
    public void The_intro_travels_in_the_answer_to_the_phone_and_back()
    {
        var o = DjJson.Cue(new DjCue("Hi", "Hi", "/api/dj/clip/x.mp3", true, 7_200));
        Assert.Equal(7_200, (int)o["introMs"]!);
        var back = DjJson.ReadCue(System.Text.Json.Nodes.JsonNode.Parse(o.ToJsonString())!.AsObject());
        Assert.Equal(7_200, back?.IntroMs);
        Assert.Null(DjJson.ReadCue(System.Text.Json.Nodes.JsonNode.Parse(DjJson.Cue(new DjCue("Hi", "Hi", null, true)).ToJsonString())!.AsObject())?.IntroMs);
    }

    [Fact]
    public void Before_the_song_holds_it_back_for_the_whole_clip_however_long()
    {
        Assert.Equal(new DjTimingPlan(0, 4_000 + DjTiming.MarginMs), DjTiming.Before(4_000));
        Assert.Equal(new DjTimingPlan(0, 9_000 + DjTiming.MarginMs), DjTiming.Before(9_000));   // no 1.5 s cap: the person asked for it
    }
}
