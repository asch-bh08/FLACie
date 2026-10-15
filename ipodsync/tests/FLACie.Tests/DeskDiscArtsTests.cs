using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class DeskDiscArtsTests
{
    [Fact] public async Task LooksUpOncePerAlbumAndKeepsTheAnswer()
    {
        var arts = new DeskDiscArts(); int lookups = 0, found = 0;
        var answer = new TaskCompletionSource<string?>();
        Func<Task<string?>> lookup = () => { lookups++; return answer.Task; };
        Func<Task> onFound = () => { found++; return Task.CompletedTask; };

        Assert.Null(arts.Get("a", lookup, onFound));
        Assert.Null(arts.Get("a", lookup, onFound));   // asked again while the answer is on the way: no second lookup
        answer.SetResult("https://coverartarchive.org/release/x/1-250.jpg");
        await arts.Settled();
        Assert.Equal("https://coverartarchive.org/release/x/1-250.jpg", arts.Get("a", lookup, onFound));
        Assert.Equal(1, lookups); Assert.Equal(1, found);
    }

    [Fact] public async Task NoPictureOrAFailedLookupLeavesThePrint()
    {
        var arts = new DeskDiscArts(); int found = 0;
        Func<Task> onFound = () => { found++; return Task.CompletedTask; };
        Assert.Null(arts.Get("none", () => Task.FromResult<string?>(null), onFound));
        Assert.Null(arts.Get("boom", () => throw new HttpRequestException("down"), onFound));
        Assert.Null(arts.Get("late", async () => { await Task.Yield(); throw new HttpRequestException("down"); }, onFound));
        await arts.Settled();
        Assert.Null(arts.Get("none", () => Task.FromResult<string?>("https://x/never.jpg"), onFound));   // looked up once: no second try
        Assert.Null(arts.Get("late", () => Task.FromResult<string?>("https://x/never.jpg"), onFound));
        Assert.Equal(0, found);
    }

    [Fact] public async Task OnlyAPlainWebPictureGoesIntoTheStyle()
    {
        var arts = new DeskDiscArts();
        string?[] bad = ["javascript:alert(1)", "https://x/a'b.jpg", "https://x/a)b.jpg", "https://x/a\\b.jpg", "http://x/a b.jpg", "not a url", "data:image/png;base64,AA"];
        for (var i = 0; i < bad.Length; i++) { var u = bad[i]; arts.Get("k" + i, () => Task.FromResult(u), () => Task.CompletedTask); }
        await arts.Settled();
        for (var i = 0; i < bad.Length; i++) Assert.Null(arts.Get("k" + i, () => Task.FromResult<string?>(null), () => Task.CompletedTask));
    }

    [Fact] public async Task AnswersArrivingOnThePoolWhileThePageReadsAreSafe()
    {
        // the page reads on its render thread while lookups finish on the thread pool
        var arts = new DeskDiscArts(); var keys = Enumerable.Range(0, 4000).Select(i => "album" + i).ToArray();
        var reads = Task.Run(() => { for (var r = 0; r < 20; r++) foreach (var k in keys) arts.Get(k, () => Task.FromResult<string?>(null), () => Task.CompletedTask); });
        Parallel.ForEach(keys, k => arts.Get(k, async () => { await Task.Yield(); return $"https://x/{k}.jpg"; }, () => Task.CompletedTask));
        await reads; await arts.Settled();
        Assert.All(keys, k => Assert.True(arts.Get(k, () => Task.FromResult<string?>(null), () => Task.CompletedTask) is null or { Length: > 0 }));
    }
}
