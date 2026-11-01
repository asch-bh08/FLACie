using System.Text.Json.Nodes;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The NAS copy of the profile is a file named after the NAS login. The family all had the same NAS connection with no username, so every account
/// wrote ".flacie/profile-guest.json" and a sign-in took whichever copy was newest: hms's profile picked up Dad's play history (2026-10-25).
/// A copy that says it belongs to another Jellyfin account is never taken.
/// </summary>
public class ProfileOwnerTests
{
    static JsonObject Copy(string? userId, long updated, params long[] plays) => new()
    {
        ["v"] = 2, ["updated"] = updated,
        ["account"] = userId is null ? null : new JsonObject { ["server"] = "http://jf.test", ["userId"] = userId, ["user"] = "x", ["token"] = "t" },
        ["history"] = new JsonArray(plays.Select(w => (JsonNode)new JsonObject { ["p"] = "p" + w, ["t"] = "T", ["a"] = "A", ["w"] = w }).ToArray()),
    };

    [Fact]
    public void A_newer_NAS_copy_from_another_account_is_not_taken()
    {
        var mine = Copy("1111aaaa2222bbbb3333cccc4444dddd", 10, 1);
        var dads = Copy("5555eeee6666ffff7777aaaa8888bbbb", 20, 2);
        Assert.Same(mine, UserSession.ChooseProfile(mine, dads, "1111aaaa2222bbbb3333cccc4444dddd"));
    }

    [Fact]
    public void The_account_id_matches_with_or_without_dashes_and_in_any_case()
    {
        var mine = Copy("1111aaaa2222bbbb3333cccc4444dddd", 10);
        var nas = Copy("1111AAAA-2222-BBBB-3333-CCCC4444DDDD", 20);
        Assert.Same(nas, UserSession.ChooseProfile(mine, nas, "1111aaaa2222bbbb3333cccc4444dddd"));
    }

    [Fact]
    public void A_NAS_copy_without_an_account_is_not_taken_by_a_Jellyfin_sign_in()
    {
        var mine = Copy("u1", 10);
        Assert.Same(mine, UserSession.ChooseProfile(mine, Copy(null, 20), "u1"));
        Assert.Null(UserSession.ChooseProfile(null, Copy(null, 20), "u1"));
    }

    [Fact]
    public void A_NAS_sign_in_takes_the_NAS_copy()
    {
        var nas = Copy("u1", 20);
        Assert.Same(nas, UserSession.ChooseProfile(null, nas, null));
    }
}
