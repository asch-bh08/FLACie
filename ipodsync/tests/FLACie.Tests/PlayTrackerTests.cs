using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class PlayTrackerTests
{
    sealed class Rig
    {
        public DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        public readonly List<(string Title, string Message)> Sent = [];
        public bool On = true;
        public readonly PlayTracker T;
        public Rig() { T = new PlayTracker((t, m) => Sent.Add((t, m)), () => Now, () => On); }

        /// <summary>Plays a song for [seconds] of listening, one report per second (the web player reports about that often).</summary>
        public void Listen(string user, string name, string key, string title, string artist, double seconds, string? via = "NAS", string device = "FLACie Web")
        {
            T.Served(user, key, via);
            T.Begin(user, name, key, title, artist, device);
            T.Tick(user, key, true);
            for (var i = 0; i < seconds; i++) { Now = Now.AddSeconds(1); T.Tick(user, key, true); }
        }
    }

    [Fact]
    public void Play_via_Jellyfin_publishes_nothing()
    {
        var r = new Rig();
        r.Listen("u1", "Dad", "a", "Song", "Artist", 200, via: null);
        r.Now = r.Now.AddMinutes(11); r.T.Sweep();
        Assert.Empty(r.Sent);
    }

    [Fact]
    public void Play_via_NAS_announces_the_start_once_and_a_summary_after_ten_idle_minutes()
    {
        var r = new Rig();
        r.Listen("u1", "Dad", "t0", "Song 0", "Artist", 240);
        Assert.Single(r.Sent);
        Assert.Equal("🎵 FLACie · Dad is listening", r.Sent[0].Title);
        Assert.Equal("Song 0 - Artist · via NAS · FLACie Web", r.Sent[0].Message);
        for (var i = 1; i < 7; i++) r.Listen("u1", "Dad", "t" + i, "Song " + i, "Artist", 240);
        Assert.Single(r.Sent);                       // 6 more tracks: nothing extra
        r.Now = r.Now.AddMinutes(9); r.T.Sweep();
        Assert.Single(r.Sent);                       // not idle long enough yet
        r.Now = r.Now.AddMinutes(2); r.T.Sweep();
        Assert.Equal(2, r.Sent.Count);
        Assert.Equal("🎵 FLACie · Dad finished", r.Sent[1].Title);
        Assert.Equal("7 tracks · 28 min · via NAS", r.Sent[1].Message);
        r.T.Sweep();                                 // a second sweep never repeats the summary
        Assert.Equal(2, r.Sent.Count);
    }

    [Fact]
    public void The_start_message_says_which_playlist_it_plays_from()
    {
        var r = new Rig();
        r.T.Served("u1", "a", "NAS"); r.T.Begin("u1", "hms", "a", "Song", "Artist", "FLACie Web", "Road trip");
        for (var i = 0; i < 25; i++) { r.Now = r.Now.AddSeconds(1); r.T.Tick("u1", "a", true); }
        Assert.Equal("Song - Artist · from Road trip · via NAS · FLACie Web", Assert.Single(r.Sent).Message);
    }

    [Fact]
    public void A_song_under_twenty_seconds_does_not_count()
    {
        var r = new Rig();
        r.Listen("u1", "Dad", "a", "Song", "Artist", 15);
        Assert.Empty(r.Sent);
    }

    [Fact]
    public void Skipping_through_songs_a_second_each_publishes_nothing()
    {
        var r = new Rig();
        for (var i = 0; i < 40; i++) r.Listen("u1", "Dad", "t" + i, "Song " + i, "Artist", 1);
        r.Now = r.Now.AddMinutes(11); r.T.Sweep();
        Assert.Empty(r.Sent);
        // and once one song is actually listened to, only that one counts
        r.Listen("u1", "Dad", "keep", "Keeper", "Artist", 30);
        Assert.Single(r.Sent);
        Assert.Contains("Keeper - Artist", r.Sent[0].Message);
    }

    [Fact]
    public void The_same_song_again_within_a_minute_is_the_same_play()
    {
        var r = new Rig();
        r.Listen("u1", "Dad", "a", "Song", "Artist", 25);
        Assert.Single(r.Sent);
        r.Listen("u1", "Dad", "a", "Song", "Artist", 25); // restarted straight away
        Assert.Single(r.Sent);
        r.Now = r.Now.AddMinutes(11); r.T.Sweep();
        Assert.Equal(2, r.Sent.Count);
        Assert.StartsWith("1 track ", r.Sent[1].Message);   // counted once
    }

    [Fact]
    public void Two_users_make_two_independent_runs()
    {
        var r = new Rig();
        r.Listen("u1", "Dad", "a", "Song A", "X", 30);
        r.Listen("u2", "hms", "b", "Song B", "Y", 30);
        Assert.Equal(2, r.Sent.Count);
        Assert.Contains("Dad is listening", r.Sent[0].Title);
        Assert.Contains("hms is listening", r.Sent[1].Title);
        r.Now = r.Now.AddMinutes(11); r.T.Sweep();
        Assert.Equal(4, r.Sent.Count);
        Assert.Contains(r.Sent, s => s.Title.Contains("Dad finished"));
        Assert.Contains(r.Sent, s => s.Title.Contains("hms finished"));
    }

    [Fact]
    public void Each_song_has_its_own_clock_a_long_one_after_a_short_one_does_not_inflate_it()
    {
        var r = new Rig();
        r.Listen("u1", "Dad", "a", "Short", "X", 180);
        r.Listen("u1", "Dad", "b", "Long", "X", 20 * 60);   // a 20 minute track right after
        r.Now = r.Now.AddMinutes(11); r.T.Sweep();
        // 3 min + 20 min = 23 min: never the long song's length credited to the short one
        Assert.Equal("2 tracks · 23 min · via NAS", r.Sent[^1].Message);

        var r2 = new Rig();
        r2.Listen("u1", "Dad", "a", "Short", "X", 180);
        r2.Now = r2.Now.AddMinutes(11); r2.T.Sweep();
        Assert.Equal("1 track · 3 min · via NAS", r2.Sent[^1].Message);
    }

    [Fact]
    public void A_pause_adds_no_listening_time()
    {
        var r = new Rig();
        r.T.Served("u1", "a", "NAS"); r.T.Begin("u1", "Dad", "a", "S", "A", "FLACie Web");
        r.T.Tick("u1", "a", true);
        for (var i = 0; i < 10; i++) { r.Now = r.Now.AddSeconds(1); r.T.Tick("u1", "a", true); }
        r.T.Tick("u1", "a", false);                              // paused
        r.Now = r.Now.AddMinutes(30); r.T.Tick("u1", "a", true); // resumed: the pause is not listening
        Assert.Empty(r.Sent);                                    // 10 s so far
        for (var i = 0; i < 12; i++) { r.Now = r.Now.AddSeconds(1); r.T.Tick("u1", "a", true); }
        Assert.Single(r.Sent);
    }

    [Fact]
    public void A_new_play_after_ten_idle_minutes_closes_the_old_run_and_opens_a_new_one()
    {
        var r = new Rig();
        r.Listen("u1", "Dad", "a", "One", "X", 30);
        r.Now = r.Now.AddMinutes(15);                            // no sweep in between
        r.Listen("u1", "Dad", "b", "Two", "X", 30);
        Assert.Equal(["is listening", "finished", "is listening"], r.Sent.Select(s => s.Title.Split("Dad ")[1]).ToArray());
    }

    [Fact]
    public void Switched_off_tracks_nothing()
    {
        var r = new Rig { On = false };
        r.Listen("u1", "Dad", "a", "Song", "X", 60);
        Assert.Empty(r.Sent);
    }
}
