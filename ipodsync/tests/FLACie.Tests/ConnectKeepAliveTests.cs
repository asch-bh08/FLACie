using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// Jellyfin answers every KeepAlive with a KeepAlive; answering those back made a ping-pong of 311,539 messages a minute that kept FLACie Web and
/// Jellyfin at 1.5 and 2 cores with nothing playing (owner, 5 Nov). Only ForceKeepAlive is acted on: it says how often to send one.
/// </summary>
public class ConnectKeepAliveTests
{
    [Fact] public void JellyfinsKeepAliveIsNeverAnswered() => Assert.Null(Connect.KeepAliveEvery("KeepAlive", null));

    [Fact] public void ForceKeepAliveSetsTheIntervalToHalfOfJellyfins()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), Connect.KeepAliveEvery("ForceKeepAlive", System.Text.Json.Nodes.JsonValue.Create(60)));
        Assert.Equal(TimeSpan.FromSeconds(15), Connect.KeepAliveEvery("ForceKeepAlive", null));   // no interval given: Jellyfin's default 30 s, halved
        Assert.Equal(TimeSpan.FromSeconds(5), Connect.KeepAliveEvery("ForceKeepAlive", System.Text.Json.Nodes.JsonValue.Create(2)));   // never faster than every 5 s
    }

    [Fact] public void OtherMessagesLeaveTheKeepAliveAlone() => Assert.Null(Connect.KeepAliveEvery("Sessions", null));
}
