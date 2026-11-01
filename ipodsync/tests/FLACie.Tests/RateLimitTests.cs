using System.Net;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The one rate limiter every door uses (sign-in, Quick Connect, shares, covers, streams, the DJ, Alexa): so many a minute per key, fixed one-minute windows,
/// a Retry-After that says when the window opens again, and a bounded number of keys so a flood of addresses can never grow the server's memory.
/// </summary>
public class RateLimitTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Allows_up_to_the_limit_then_refuses_with_when_to_try_again()
    {
        var t = Now; var l = new RateLimiter(30, clock: () => t);
        for (var i = 0; i < 30; i++) Assert.True(l.TryTake(RateKey.Of("a"), out _));
        t = t.AddSeconds(20);
        Assert.False(l.TryTake(RateKey.Of("a"), out var wait));
        Assert.Equal(TimeSpan.FromSeconds(40), wait);
        Assert.True(l.TryTake(RateKey.Of("b"), out _));   // another key has its own minute
    }

    [Fact]
    public void The_window_opens_again_after_a_minute()
    {
        var t = Now; var l = new RateLimiter(2, clock: () => t);
        Assert.True(l.TryTake("a")); Assert.True(l.TryTake("a")); Assert.False(l.TryTake("a"));
        t = t.AddSeconds(59); Assert.False(l.TryTake("a"));
        t = t.AddSeconds(2); Assert.True(l.TryTake("a"));
    }

    [Fact]
    public void Memory_is_bounded_however_many_keys_come()
    {
        var t = Now; var l = new RateLimiter(30, maxKeys: 5000, clock: () => t);
        for (var i = 0; i < 6000; i++) Assert.True(l.TryTake("k" + i));
        Assert.True(l.KeyCount <= 5000, l.KeyCount.ToString());
        t = t.AddMinutes(2);
        Assert.True(l.TryTake("fresh"));
        Assert.Equal(1, l.KeyCount);
    }

    [Fact]
    public void A_flood_of_new_keys_does_not_forget_an_address_that_is_being_refused_just_now()
    {
        // the oldest windows go first when the table is full: one that started a moment ago (the address hammering right now) stays refused
        var t = Now; var l = new RateLimiter(1, maxKeys: 100, clock: () => t);
        for (var i = 0; i < 100; i++) l.TryTake("old" + i);
        t = t.AddSeconds(30);
        Assert.True(l.TryTake("hammer")); Assert.False(l.TryTake("hammer"));
        for (var i = 0; i < 50; i++) l.TryTake("new" + i);
        Assert.False(l.TryTake("hammer"));
    }

    [Fact]
    public void A_null_or_empty_name_shares_one_bucket()
    {
        var l = new RateLimiter(1, clock: () => Now);
        Assert.True(l.TryTake(null!)); Assert.False(l.TryTake(""));
    }

    [Fact]
    public void An_IPv6_address_counts_by_its_network_so_one_machine_cannot_rotate_through_its_addresses()
    {
        var a = RateKey.Of(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:1"));
        var b = RateKey.Of(IPAddress.Parse("2001:db8:1:2:ffff:0:0:2"));
        var other = RateKey.Of(IPAddress.Parse("2001:db8:1:3::1"));
        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void An_IPv4_address_is_the_same_key_however_it_arrives_and_never_meets_a_name()
    {
        Assert.Equal(RateKey.Of(IPAddress.Parse("203.0.113.7")), RateKey.Of(IPAddress.Parse("::ffff:203.0.113.7")));
        Assert.NotEqual(RateKey.Of(IPAddress.Parse("203.0.113.7")), RateKey.Of(IPAddress.Parse("203.0.113.8")));
        Assert.NotEqual(RateKey.Of(IPAddress.Parse("0.0.0.0")), RateKey.Of(""));
        Assert.NotEqual(RateKey.Of((IPAddress?)null), RateKey.Of(""));
    }

    [Fact]
    public void A_device_token_is_kept_only_as_a_hash()
    {
        var k = RateKey.OfToken("abcdef0123456789");
        Assert.Equal(k, RateKey.OfToken("abcdef0123456789"));
        Assert.NotEqual(k, RateKey.OfToken("abcdef0123456780"));
        Assert.DoesNotContain("abcdef", k.ToString());
    }

    // ---- the named limits and their settings ----

    [Fact]
    public void Every_door_has_a_default_and_a_setting_changes_it()
    {
        var limits = new RateLimits(n => n == "FLACIE_RATE_SIGNIN" ? "3" : null, () => Now);
        Assert.Equal(3, limits.PerMinute("signin"));
        Assert.Equal(RateLimits.Defaults["art"], limits.PerMinute("art"));
        var ip = RateKey.Of(IPAddress.Parse("198.51.100.1"));
        for (var i = 0; i < 3; i++) Assert.True(limits.TryTake("signin", ip, out _));
        Assert.False(limits.TryTake("signin", ip, out var wait));
        Assert.True(wait > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    public void A_limit_set_to_0_or_off_is_switched_off(string value)
    {
        var limits = new RateLimits(n => n == "FLACIE_RATE_SIGNIN" ? value : null, () => Now);
        for (var i = 0; i < 1000; i++) Assert.True(limits.TryTake("signin", RateKey.Of("x"), out _));
        Assert.Null(limits.PerMinute("signin"));
    }

    [Fact]
    public void All_limits_can_be_switched_off_at_once_and_a_nonsense_value_keeps_the_default()
    {
        var off = new RateLimits(n => n == "FLACIE_RATE_LIMITS" ? "off" : null, () => Now);
        for (var i = 0; i < 100; i++) Assert.True(off.TryTake("signin", RateKey.Of("x"), out _));
        var odd = new RateLimits(n => n == "FLACIE_RATE_STREAM" ? "lots" : null, () => Now);
        Assert.Equal(RateLimits.Defaults["stream"], odd.PerMinute("stream"));
    }

    [Fact]
    public void A_scale_setting_multiplies_every_default_for_a_test_server()
    {
        var limits = new RateLimits(n => n == "FLACIE_RATE_SCALE" ? "10" : null, () => Now);
        Assert.Equal(RateLimits.Defaults["signin"] * 10, limits.PerMinute("signin"));
    }

    [Fact]
    public void Over_tells_whether_a_key_has_used_up_its_minute_without_counting()
    {
        var t = Now; var limits = new RateLimits(n => n == "FLACIE_RATE_SHARE_MISS" ? "2" : null, () => t);
        var me = RateKey.Of("u1");
        Assert.False(limits.Over("share-miss", me));
        limits.TryTake("share-miss", me, out _); limits.TryTake("share-miss", me, out _);
        Assert.True(limits.Over("share-miss", me));
        Assert.True(limits.Over("share-miss", me));   // asking does not count
        Assert.False(limits.Over("share-miss", RateKey.Of("u2")));
        t = t.AddMinutes(1);
        Assert.False(limits.Over("share-miss", me));
        Assert.False(new RateLimits(_ => "off").Over("share-miss", me));
    }

    [Fact]
    public void An_unknown_limit_never_refuses() => Assert.True(new RateLimits(_ => null).TryTake("no-such-door", RateKey.Of("x"), out _));

    /// <summary>
    /// What a person can do at full speed must stay far below every limit. Measured with ipodsync/tools/loadtest/run-web.mjs against the 10,003-song mock
    /// (browser to FLACie): flinging through the whole Songs list sent at most 1,932 cover requests in 10 seconds (about 11,600 a minute); a burst of 40 skips
    /// 60 ms apart sent 16 stream opens, 82 seek-bar shapes and 60 covers in about 10 seconds; the sign-in page's Quick Connect polls 24 times a minute.
    /// </summary>
    [Fact]
    public void Defaults_stay_well_above_what_fast_normal_use_sends()
    {
        var d = RateLimits.Defaults;
        Assert.True(d["art"] >= 2 * 11_600, "a fling through 10,000 covers");
        Assert.True(d["stream"] >= 10 * 16 * 6, "40 fast skips every ten seconds for a minute, with room to spare");
        Assert.True(d["wave"] >= 6 * 82, "the seek-bar shapes of 40 skips every ten seconds");
        Assert.True(d["qc-poll"] >= 2 * 24, "two people signing in with Quick Connect from one address");
        Assert.True(d["search"] >= 5 * 60, "typing five characters a second into Search for a whole minute (one online search per keystroke)");
        Assert.True(d["public"] >= 8 * 30, "eight tabs behind one address asking /healthz every 2 s while the server restarts");
        Assert.True(d["signin"] <= 20, "a password guesser gets few tries");
    }
}
