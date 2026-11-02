using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 2 Nov: a bad drive should explain itself. The phone's problems are kept on the server, bounded.</summary>
public class DiagLogStoreTests
{
    static (DiagLogStore Store, string Root) Make()
    {
        var root = Path.Combine(Path.GetTempPath(), "diag-" + Guid.NewGuid().ToString("N"));
        return (new DiagLogStore(new DataPaths(root)), root);
    }

    [Fact]
    public void Events_are_kept_per_account_and_bounded()
    {
        var (s, _) = Make();
        var n = s.Add("acct", Enumerable.Range(0, 80).Select(i => new DiagEvent(i, "stall", new string('x', 500))));
        Assert.Equal(DiagLogStore.MaxEvents, n);
        var lines = File.ReadAllLines(s.FileOf("acct"));
        Assert.Equal(50, lines.Length);
        Assert.Contains(new string('x', DiagLogStore.MaxText) + "\"", lines[0]);
        Assert.DoesNotContain(new string('x', DiagLogStore.MaxText + 1), lines[0]);
    }

    [Fact]
    public void An_event_without_a_kind_is_dropped() => Assert.Equal(0, Make().Store.Add("a", [new DiagEvent(1, "", "x")]));

    [Fact]
    public void A_full_file_starts_again_and_keeps_the_old_one()
    {
        var (s, _) = Make();
        File.WriteAllText(s.FileOf("a"), new string('y', (int)DiagLogStore.MaxBytes + 10));
        s.Add("a", [new DiagEvent(1, "error", "http 404")]);
        Assert.Single(File.ReadAllLines(s.FileOf("a")));
        Assert.True(File.Exists(s.FileOf("a") + ".old"));
    }
}
