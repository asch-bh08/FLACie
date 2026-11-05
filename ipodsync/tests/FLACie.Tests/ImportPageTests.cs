using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F25.2: what the Import page folds away and counts.</summary>
public class ImportPageTests
{
    static ImportJob Job(string state, int daysAgo) => new() { State = state, Created = DateTime.UtcNow.AddDays(-daysAgo), Lists = [new() { Name = "L", Items = [new() { State = "owned" }, new() { State = "done" }, new() { State = "failed" }, new() { State = "pending" }] }] };

    [Fact]
    public void Finished_imports_older_than_7_days_fold_under_older_and_running_ones_never_do()
    {
        Assert.True(Job("done", 8).IsOld(DateTime.UtcNow));
        Assert.True(Job("cancelled", 30).IsOld(DateTime.UtcNow));
        Assert.False(Job("done", 6).IsOld(DateTime.UtcNow));
        Assert.False(Job("running", 30).IsOld(DateTime.UtcNow));
        Assert.Equal(2, Job("running", 0).Added);   // in the library already, or downloaded
        Assert.Equal(4, Job("running", 0).Total);
    }
}
