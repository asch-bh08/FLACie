using System.Net;
using FLACie.Server;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// Password guessing at the sign-in page. One address guessing at one name is stopped for a short while; one address trying name after name is stopped
/// for longer; guesses at one name from many addresses (credential stuffing) only make each further try at that name wait longer, never refuse it, so a
/// stranger can never lock the real person out; and a browser that has signed in as that name before (its known-device cookie) is not held up by
/// anyone else's guessing at all.
/// </summary>
public class LoginThrottleTests
{
    DateTime now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    LoginThrottle New() => new(() => now);
    static IPAddress Ip(int i) => IPAddress.Parse("198.51.100." + i);

    [Fact]
    public void Eight_wrong_passwords_for_a_name_from_one_address_stop_that_name_there_for_a_short_while()
    {
        var t = New();
        for (var i = 0; i < 8; i++) { Assert.Null(t.Blocked(Ip(1), "ashley")); t.Fail(Ip(1), "ashley"); }
        Assert.NotNull(t.Blocked(Ip(1), "ashley"));
        Assert.Null(t.Blocked(Ip(1), "kim"));       // another name from the same address
        Assert.Null(t.Blocked(Ip(2), "ashley"));    // the same name from anywhere else
        now = now.AddMinutes(10).AddSeconds(1);
        Assert.Null(t.Blocked(Ip(1), "ashley"));    // short: ten minutes after the guessing, never longer
    }

    [Fact]
    public void The_refusal_says_when_it_ends()
    {
        var t = New();
        for (var i = 0; i < 8; i++) { t.Fail(Ip(1), "ashley"); now = now.AddSeconds(30); }
        // the first of the eight was 4 minutes ago: the name opens again at ten minutes
        Assert.Equal(TimeSpan.FromMinutes(6), t.Blocked(Ip(1), "ashley"));
    }

    [Fact]
    public void One_address_trying_name_after_name_is_stopped()
    {
        var t = New();
        for (var i = 0; i < LoginThrottle.MaxPerAddress; i++) t.Fail(Ip(3), "name" + i);
        Assert.NotNull(t.Blocked(Ip(3), "someone-new"));
        Assert.Null(t.Blocked(Ip(4), "someone-new"));
        now = now.AddMinutes(15).AddSeconds(1);
        Assert.Null(t.Blocked(Ip(3), "someone-new"));
    }

    [Fact]
    public void Guessing_at_a_name_from_many_addresses_never_refuses_it_only_slows_each_try()
    {
        var t = New();
        var waits = new List<double>();
        for (var i = 0; i < 60; i++) { waits.Add(t.Delay("ashley").TotalSeconds); t.Fail(Ip(i % 250), "ashley"); }
        Assert.Null(t.Blocked(Ip(251), "ashley"));
        Assert.Equal([0, 0, 0, 0, 0, 1, 2, 4, 8, 16, 16], waits.Take(11));
        Assert.Equal(16, waits.Max());
        Assert.Equal(0, t.Delay("kim").TotalSeconds);
    }

    [Fact]
    public void A_browser_that_signed_in_as_that_name_before_is_not_held_up_by_anyone_elses_guessing()
    {
        var t = New();
        for (var i = 0; i < 40; i++) t.Fail(Ip(5), "ashley");     // someone at the same address (or a misconfigured proxy that makes everyone one address)
        for (var i = 0; i < 40; i++) t.Fail(Ip(100 + i), "ashley");
        Assert.NotNull(t.Blocked(Ip(5), "ashley"));
        Assert.Null(t.Blocked(Ip(5), "ashley", device: "dev-1"));
        Assert.Equal(TimeSpan.Zero, t.Delay("ashley", device: "dev-1"));
    }

    [Fact]
    public void A_known_device_has_its_own_short_limit_of_wrong_passwords()
    {
        var t = New();
        for (var i = 0; i < 8; i++) { Assert.Null(t.Blocked(Ip(6), "ashley", "dev-1")); t.Fail(Ip(6), "ashley", "dev-1"); }
        Assert.NotNull(t.Blocked(Ip(6), "ashley", "dev-1"));
        Assert.Equal(0, t.Delay("ashley").TotalSeconds);   // the owner mistyping on their own device does not slow the name for anyone
    }

    [Fact]
    public void A_good_sign_in_clears_the_names_counters()
    {
        var t = New();
        for (var i = 0; i < 7; i++) t.Fail(Ip(7), "ashley");
        t.Succeed(Ip(7), "ashley");
        Assert.Equal(TimeSpan.Zero, t.Delay("ashley"));
        t.Fail(Ip(7), "ashley");
        Assert.Null(t.Blocked(Ip(7), "ashley"));
    }

    [Fact]
    public void Names_are_compared_without_case_or_spaces()
    {
        var t = New();
        for (var i = 0; i < 8; i++) t.Fail(Ip(8), i % 2 == 0 ? "Ashley " : "ASHLEY");
        Assert.NotNull(t.Blocked(Ip(8), "ashley"));
    }

    [Fact]
    public void Memory_is_bounded_however_many_addresses_and_names_fail()
    {
        var t = New();
        for (var i = 0; i < 30_000; i++) t.Fail(IPAddress.Parse($"10.{i / 65536 % 256}.{i / 256 % 256}.{i % 256}"), "n" + i);
        Assert.True(t.KeyCount <= LoginThrottle.MaxKeys, t.KeyCount.ToString());
    }

    // ---- the known-device cookie ----

    [Fact]
    public void A_device_cookie_names_one_account_and_cannot_be_forged_or_moved_to_another_name()
    {
        var dp = DataProtectionProvider.Create(new DirectoryInfo(Directory.CreateTempSubdirectory("flacie-dev").FullName));
        var devices = new SignInDevices(dp);
        var cookie = devices.Issue("Ashley");
        var id = devices.Read(cookie, " ashley");
        Assert.NotNull(id);
        Assert.Equal(id, devices.Read(cookie, "ASHLEY"));
        Assert.Null(devices.Read(cookie, "kim"));
        Assert.Null(devices.Read(cookie[..^2] + "xx", "ashley"));
        Assert.Null(devices.Read("", "ashley"));
        Assert.Null(devices.Read(null, "ashley"));
        Assert.NotEqual(id, devices.Read(devices.Issue("ashley"), "ashley"));   // each browser its own id
        Assert.DoesNotContain("ashley", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
