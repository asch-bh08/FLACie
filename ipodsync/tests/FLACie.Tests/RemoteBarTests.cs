using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 5 Nov: pausing the other device from the "Listening on" strip must leave the strip there, with Play.</summary>
public class RemoteBarTests
{
    static ConnectSession S(string id, bool paused, string? item = "x", bool self = false) =>
        new(id, id, "c", "u", self, true, item, "T", "A", 0, 1000, paused, [], null);

    [Fact] public void A_playing_player_is_shown() => Assert.Equal("a", RemoteBar.Pick([S("a", false), S("b", true)], null)!.Id);
    [Fact] public void The_one_just_paused_stays_for_Play() => Assert.Equal("a", RemoteBar.Pick([S("a", true)], "a")!.Id);
    [Fact] public void A_paused_player_never_shown_is_not_offered() => Assert.Null(RemoteBar.Pick([S("a", true)], null));
    [Fact] public void It_goes_when_the_player_stops() => Assert.Null(RemoteBar.Pick([S("a", true, item: null)], "a"));
    [Fact] public void Another_player_taking_over_replaces_it() => Assert.Equal("b", RemoteBar.Pick([S("a", true), S("b", false)], "a")!.Id);
    [Fact] public void This_browser_is_never_listed() => Assert.Null(RemoteBar.Pick([S("a", false, self: true)], "a"));
}
