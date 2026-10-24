using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>The start of the next song, kept from the page's early read so Next does not wait for Jellyfin's first byte again (the owner heard 3-4 s
/// of nothing between songs).</summary>
public class SongHeadCacheTests
{
    static byte[] Bytes(int n) => Enumerable.Range(0, n).Select(i => (byte)i).ToArray();
    long now = 1_000_000;
    SongHeadCache Cache(int entries = 4, long bytes = 1 << 20) => new(entries, bytes, TimeSpan.FromMinutes(10), () => now);

    [Fact]
    public void A_kept_start_is_found_for_the_same_account_and_song_only()
    {
        var c = Cache();
        c.Put("me", "song1", Bytes(100), 1000, "audio/flac");
        Assert.NotNull(c.Get("me", "song1"));
        Assert.Null(c.Get("someone else", "song1"));   // never another account's bytes
        Assert.Null(c.Get("me", "song2"));
    }

    [Fact]
    public void The_oldest_go_when_too_many_or_too_big()
    {
        var c = Cache(entries: 2, bytes: 250);
        c.Put("me", "a", Bytes(100), 1000, "x"); now++;
        c.Put("me", "b", Bytes(100), 1000, "x"); now++;
        c.Put("me", "c", Bytes(100), 1000, "x");
        Assert.Null(c.Get("me", "a")); Assert.NotNull(c.Get("me", "b")); Assert.NotNull(c.Get("me", "c"));
        c.Put("me", "d", Bytes(200), 1000, "x");   // 300 bytes would be over 250
        Assert.Null(c.Get("me", "b")); Assert.Null(c.Get("me", "c")); Assert.NotNull(c.Get("me", "d"));
    }

    [Fact]
    public void A_start_kept_too_long_ago_is_dropped()
    {
        var c = Cache();
        c.Put("me", "a", Bytes(10), 100, "x");
        now += (long)TimeSpan.FromMinutes(11).TotalMilliseconds;
        Assert.Null(c.Get("me", "a"));
    }

    [Theory]
    [InlineData("", 100, 1000, true, false, 0, 99, 100, 999)]            // the whole song: the kept start, then the rest from Jellyfin
    [InlineData("bytes=0-", 100, 1000, true, true, 0, 99, 100, 999)]
    [InlineData("bytes=0-49", 100, 1000, true, true, 0, 49, -1, -1)]      // all of it is in the kept start
    [InlineData("bytes=20-500", 100, 1000, true, true, 20, 99, 100, 500)]
    [InlineData("bytes=100-", 100, 1000, false, true, 0, 0, 0, 0)]       // starts after the kept part: Jellyfin as usual
    [InlineData("bytes=-10", 100, 1000, false, true, 0, 0, 0, 0)]
    public void How_a_request_is_served(string range, int head, long total, bool use, bool partial, long hFrom, long hTo, long upFrom, long upTo)
    {
        var p = SongHeadCache.Plan(range, head, total);
        Assert.Equal(use, p is not null);
        if (p is null) return;
        Assert.Equal((partial, hFrom, hTo, upFrom, upTo), (p.Value.Partial, p.Value.HeadFrom, p.Value.HeadTo, p.Value.UpFrom, p.Value.UpTo));
    }
}
