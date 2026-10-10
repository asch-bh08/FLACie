using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class DownloadDigestTests
{
    readonly List<(string Title, string Message)> sent = [];
    DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    DownloadDigest Make() => new((t, m) => sent.Add((t, m)), () => now);

    [Fact]
    public void A_single_song_is_said_live_right_away()
    {
        var d = Make();
        d.Done("u1", "hms", "Song - Artist");
        var (title, msg) = Assert.Single(sent);
        Assert.Equal("🎵 FLACie · hms downloaded", title);
        Assert.Equal("Song - Artist", msg);
    }

    [Fact]
    public void The_first_three_are_live_and_a_big_burst_becomes_one_summary_when_it_goes_quiet()
    {
        var d = Make();
        for (var i = 0; i < 50; i++) { d.Done("u1", "Dad", $"S{i} - X"); now = now.AddSeconds(5); d.Sweep(); }
        Assert.Equal(3, sent.Count);                 // only the live ones so far (the burst keeps going: no quiet gap yet)
        now = now.AddSeconds(50); d.Sweep();
        Assert.Equal(4, sent.Count);
        Assert.Equal("🎵 FLACie · Dad downloaded 47 more", sent[3].Title);
        Assert.Equal("S3 - X, S4 - X, S5 - X and 44 more", sent[3].Message);
        now = now.AddMinutes(1); d.Sweep();
        Assert.Equal(4, sent.Count);                 // never repeated
    }

    [Fact]
    public void Each_user_has_their_own_burst_and_a_repeat_is_counted_once()
    {
        var d = Make();
        for (var i = 0; i < 6; i++) d.Done("u1", "hms", "A" + i);
        d.Done("u1", "hms", "A5");
        d.Done("u2", "Dad", "B");
        Assert.Equal(4, sent.Count);                 // 3 live for hms, 1 live for Dad
        now = now.AddMinutes(1); d.Sweep();
        Assert.Equal(5, sent.Count);
        Assert.Contains("hms downloaded 3 more", sent[4].Title);
    }

    [Fact]
    public void After_a_quiet_stretch_the_next_song_is_live_again()
    {
        var d = Make();
        d.Done("u1", "hms", "One"); now = now.AddMinutes(10); d.Sweep();
        d.Done("u1", "hms", "Two");
        Assert.Equal(2, sent.Count);
    }
}
