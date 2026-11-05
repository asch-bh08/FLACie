using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// A restart of FLACie Web cancelled the songs an import was fetching at that moment and they were kept as failed ("Cancelled"): the owner's
/// "80s - 90s rally" lost 3 songs to deploys (5 Nov). A song whose fetch was cancelled goes back in the queue, so the import picks it up again.
/// </summary>
public class ImportResultTests
{
    [Fact]
    public void A_cancelled_fetch_goes_back_in_the_queue_and_a_real_failure_stays_a_failure()
    {
        Assert.Equal("pending", ImportManager.StateAfter(new(false, [], "Cancelled")).State);
        Assert.Equal(("failed", "Waiting for a peer..."), ImportManager.StateAfter(new(false, [], "Waiting for a peer...")));
        Assert.Equal("requested", ImportManager.StateAfter(new(true, [], "")).State);
    }
}
