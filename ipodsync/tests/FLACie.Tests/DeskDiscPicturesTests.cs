using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The iPod desk's disc pictures, fetched by the server from the Cover Art Archive and kept for everyone (the browser only ever asks FLACie).</summary>
public class DeskDiscPicturesTests
{
    sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    static CaaImage Img(string id, params string[] types) => new(id, $"https://caa/{id}-250.jpg", $"https://caa/{id}-1200.jpg", types.Contains("Front"), false, types);
    const string R1 = "6b3f1d2e-0000-4000-8000-000000000001", R2 = "6b3f1d2e-0000-4000-8000-000000000002", R3 = "6b3f1d2e-0000-4000-8000-000000000003";

    [Fact] public async Task FindsTheReleasesDiscPictureOnceForEveryone()
    {
        int lists = 0, fetches = 0; string? fetched = null;
        var gate = new TaskCompletionSource();
        var discs = new DeskDiscPictures(async (r, g, ct) => { lists++; await gate.Task; return [Img("front", "Front"), Img("disc", "Medium")]; },
                                         (url, ct) => { fetches++; fetched = url; return Task.FromResult<byte[]?>(Jpeg); });
        var a = discs.GetAsync(R1, null); var b = discs.GetAsync(R1, null);   // two viewers at once: one lookup
        gate.SetResult();
        var (pa, pb) = (await a, await b);
        Assert.NotNull(pa); Assert.Same(pa, pb); Assert.Equal("image/jpeg", pa!.Mime); Assert.Equal(Jpeg, pa.Bytes);
        Assert.Same(pa, await discs.GetAsync(R1, null));                      // a later visit: kept
        Assert.Equal(1, lists); Assert.Equal(1, fetches); Assert.Equal("https://caa/disc-1200.jpg", fetched);
    }

    [Fact] public async Task NoDiscPictureIsRememberedForADay()
    {
        var clock = new Clock(); int lists = 0;
        var discs = new DeskDiscPictures((r, g, ct) => { lists++; return Task.FromResult<IReadOnlyList<CaaImage>>([Img("front", "Front")]); }, (u, ct) => Task.FromResult<byte[]?>(Jpeg), clock);
        Assert.Null(await discs.GetAsync(R1, null));
        clock.Now += TimeSpan.FromHours(23); Assert.Null(await discs.GetAsync(R1, null)); Assert.Equal(1, lists);
        clock.Now += TimeSpan.FromHours(2); Assert.Null(await discs.GetAsync(R1, null)); Assert.Equal(2, lists);   // someone may have added one since
    }

    [Fact] public async Task AFailedLookupIsTriedAgainAfterAWhile()
    {
        var clock = new Clock(); int lists = 0;
        var discs = new DeskDiscPictures((r, g, ct) => { lists++; return lists == 1 ? throw new HttpRequestException("down") : Task.FromResult<IReadOnlyList<CaaImage>>([Img("disc", "Medium")]); },
                                         (u, ct) => Task.FromResult<byte[]?>(Jpeg), clock);
        Assert.Null(await discs.GetAsync(R1, null));
        Assert.Null(await discs.GetAsync(R1, null)); Assert.Equal(1, lists);    // not again at once (every render would ask)
        clock.Now += TimeSpan.FromMinutes(11);
        Assert.NotNull(await discs.GetAsync(R1, null)); Assert.Equal(2, lists);
    }

    [Fact] public async Task OnlyARealPictureIsServed()
    {
        var discs = new DeskDiscPictures((r, g, ct) => Task.FromResult<IReadOnlyList<CaaImage>>([Img("disc", "Medium")]), (u, ct) => Task.FromResult<byte[]?>("<html>"u8.ToArray()));
        Assert.Null(await discs.GetAsync(R1, null));
    }

    [Fact] public async Task KeepsAFewPicturesAtMost()
    {
        int lists = 0;
        var discs = new DeskDiscPictures((r, g, ct) => { lists++; return Task.FromResult<IReadOnlyList<CaaImage>>([Img(r, "Medium")]); }, (u, ct) => Task.FromResult<byte[]?>(Jpeg), keep: 2);
        await discs.GetAsync(R1, null); await discs.GetAsync(R2, null); await discs.GetAsync(R3, null);
        Assert.Equal(3, lists);
        await discs.GetAsync(R3, null); Assert.Equal(3, lists);   // the newest are kept
        await discs.GetAsync(R1, null); Assert.Equal(4, lists);   // the oldest made room
    }

    [Fact] public async Task AViewerWhoLeavesDoesNotStopTheLookupForTheOthers()
    {
        var gate = new TaskCompletionSource(); int lists = 0;
        var discs = new DeskDiscPictures(async (r, g, ct) => { lists++; await gate.Task; ct.ThrowIfCancellationRequested(); return [Img("disc", "Medium")]; }, (u, ct) => Task.FromResult<byte[]?>(Jpeg));
        using var gone = new CancellationTokenSource();
        var left = discs.GetAsync(R1, null, gone.Token); gone.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => left);
        var stays = discs.GetAsync(R1, null); gate.SetResult();
        Assert.NotNull(await stays); Assert.Equal(1, lists);
    }

    [Fact] public void OnlyMusicBrainzIdsAreLookedUp()
    {
        Assert.True(DeskDiscPictures.ValidId(R1));
        Assert.False(DeskDiscPictures.ValidId("../etc")); Assert.False(DeskDiscPictures.ValidId("")); Assert.False(DeskDiscPictures.ValidId(null));
    }
}
