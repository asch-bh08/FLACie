using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>A song started at 1.75x for no reason (owner, 1 Nov): music always starts at 1x; only spoken word and long items keep a speed.</summary>
public class SpeedStartTests
{
    [Fact]
    public void Music_always_starts_at_normal_speed()
    {
        Assert.Equal(1, SpeedPolicy.StartSpeed(1.75, "Pop", 200_000, 1.5));
        Assert.Equal(1, SpeedPolicy.StartSpeed(null, "Pop", 200_000, 1.5));
    }

    [Fact]
    public void Spoken_and_long_items_keep_their_speed()
    {
        Assert.Equal(1.75, SpeedPolicy.StartSpeed(1.75, "Audiobook", 200_000, 1.5));
        Assert.Equal(1.5, SpeedPolicy.StartSpeed(null, "Podcast", 200_000, 1.5));
        Assert.Equal(1.25, SpeedPolicy.StartSpeed(1.25, "", 40 * 60_000L, 1.5));
    }
}
