using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class DownloadDigestTests
{
    readonly List<(string Title, string Message)> sent = [];
    DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    DownloadDigest Make() => new((t, m) => sent.Add((t, m)), () => now);

    [Fact]
    public void A_burst_of_downloads_is_one_message_after_it_goes_quiet()
    {
        var d = Make();
        d.Done("u1", "Dad", "A - X"); now = now.AddSeconds(10);
        d.Done("u1", "Dad", "B - Y"); now = now.AddSeconds(10);
        d.Done("u1", "Dad", "C - Z"); now = now.AddSeconds(10);
        d.Done("u1", "Dad", "D - W"); now = now.AddSeconds(20);
        d.Sweep();
        Assert.Empty(sent);                       // still within 45 s of the last one
        now = now.AddSeconds(40); d.Sweep();
        var (title, msg) = Assert.Single(sent);
        Assert.Equal("🎵 FLACie · Dad downloaded", title);
        Assert.Equal("4 songs: A - X, B - Y, C - Z and 1 more", msg);
        d.Sweep(); Assert.Single(sent);           // never repeated
    }

    [Fact]
    public void Each_user_gets_their_own_message_and_a_repeat_is_counted_once()
    {
        var d = Make();
        d.Done("u1", "hms", "A - X"); d.Done("u1", "hms", "A - X"); d.Done("u2", "Dad", "B - Y");
        now = now.AddMinutes(1); d.Sweep();
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, s => s.Title.EndsWith("hms downloaded") && s.Message == "1 song: A - X");
        Assert.Contains(sent, s => s.Title.EndsWith("Dad downloaded"));
    }

    [Fact]
    public void A_long_stream_of_downloads_is_still_announced_within_five_minutes()
    {
        var d = Make();
        for (var i = 0; i < 40; i++) { d.Done("u1", "Dad", "S" + i); now = now.AddSeconds(10); d.Sweep(); }
        Assert.NotEmpty(sent);
    }
}
