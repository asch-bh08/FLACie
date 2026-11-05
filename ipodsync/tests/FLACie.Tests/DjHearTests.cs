using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The web mic when the browser cannot recognise speech itself (Brave turns off the service behind it: the mic stopped at once and the music came
/// back, owner 5 Nov): the page records the person and frank asks Cloudflare's Whisper what was said.
/// </summary>
public class DjHearTests
{
    [Fact]
    public void A_clip_is_priced_by_its_length_a_second_at_least()
    {
        Assert.Equal(CloudflareVoice.HearNeuronsPerMinute / 6, CloudflareVoice.EstimateHearNeuronsFor(10), 3);
        Assert.Equal(CloudflareVoice.HearNeuronsPerMinute / 60, CloudflareVoice.EstimateHearNeuronsFor(0.2), 3);
    }

    [Fact]
    public void Only_a_short_clip_is_taken()
    {
        Assert.Null(DjHear.Refuse(40_000, 4));
        Assert.NotNull(DjHear.Refuse(0, 4));                            // nothing recorded
        Assert.NotNull(DjHear.Refuse(DjHear.MaxBytes + 1, 4));          // too big
        Assert.NotNull(DjHear.Refuse(40_000, DjHear.MaxSeconds + 1));   // too long
        Assert.NotNull(DjHear.Refuse(null, 4));                         // size not given
    }
}
