using System.Diagnostics;
using System.Net;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// A song asked of Jellyfin gets a time limit on Jellyfin's first reply (the headers) only: a Jellyfin that never answers no longer leaves the song
/// buffering for good, and a long lossless stream that is already flowing is never cut. No real Jellyfin: fake handlers only.
/// </summary>
public class JellyfinFirstReplyTests
{
    /// <summary>A Jellyfin that takes the request and never answers; it notes when the caller lets go of it.</summary>
    sealed class NeverAnswers : HttpMessageHandler
    {
        public readonly TaskCompletionSource LetGo = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { LetGo.TrySetResult(); throw; }
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>Answers at once, then sends the body slowly: <paramref name="chunks"/> pieces, <paramref name="gap"/> apart.</summary>
    sealed class Trickles(int chunks, TimeSpan gap) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(chunks, gap)) });
    }

    sealed class SlowStream(int chunks, TimeSpan gap) : Stream
    {
        int left = chunks;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (left == 0) return 0;
            await Task.Delay(gap, ct);
            left--;
            buffer.Span[0] = 0x66;
            return 1;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    static HttpRequestMessage Song() => new(HttpMethod.Get, "http://192.0.2.10:8096/Audio/0123456789abcdef0123456789abcdef/stream?static=true");

    [Fact]
    public async Task A_Jellyfin_that_never_answers_is_given_up_on_after_the_first_reply_limit()
    {
        var jf = new NeverAnswers();
        using var http = new HttpClient(jf) { Timeout = Timeout.InfiniteTimeSpan };   // as the "media" client: no limit of its own
        using var req = Song();
        var sw = Stopwatch.StartNew();
        var send = JellyfinFirstReply.SendAsync(http, req, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        // a hard guard so a missing limit fails the test instead of hanging it
        Assert.Same(send, await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(10))));
        var e = await Assert.ThrowsAsync<JellyfinNoReplyException>(() => send);
        Assert.InRange(sw.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromMilliseconds(300), e.Waited);
        // and the request to Jellyfin is let go of, not left open
        Assert.Same(jf.LetGo.Task, await Task.WhenAny(jf.LetGo.Task, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task A_stream_that_is_flowing_is_never_cut_however_long_it_takes()
    {
        // the headers come at once; the body takes about 1.2 s, four times the limit
        using var http = new HttpClient(new Trickles(6, TimeSpan.FromMilliseconds(200))) { Timeout = Timeout.InfiniteTimeSpan };
        using var req = Song();
        using var res = await JellyfinFirstReply.SendAsync(http, req, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        await using var body = await res.Content.ReadAsStreamAsync();
        using var all = new MemoryStream();
        await body.CopyToAsync(all);
        Assert.Equal(6, all.Length);
    }

    [Fact]
    public async Task The_browser_letting_go_first_is_a_cancel_not_a_Jellyfin_failure()
    {
        using var http = new HttpClient(new NeverAnswers()) { Timeout = Timeout.InfiniteTimeSpan };
        using var req = Song();
        using var browser = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var e = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => JellyfinFirstReply.SendAsync(http, req, TimeSpan.FromSeconds(5), browser.Token));
        Assert.IsNotType<JellyfinNoReplyException>(e);
    }

    [Fact]
    public void The_limit_for_a_song_is_about_fifteen_seconds() => Assert.Equal(TimeSpan.FromSeconds(15), JellyfinFirstReply.Limit);

    [Fact]
    public void A_song_Jellyfin_did_not_answer_for_is_remembered_briefly_for_the_player_to_ask_about()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var stalls = new StreamStalls(() => now);
        Assert.False(stalls.Recent("u1", "/music/a.flac"));
        stalls.Mark("u1", "/music/a.flac");
        Assert.True(stalls.Recent("u1", "/music/a.flac"));
        Assert.False(stalls.Recent("u2", "/music/a.flac"));      // another account's player
        Assert.False(stalls.Recent("u1", "/music/b.flac"));      // another song
        now = now.AddMinutes(3);
        Assert.False(stalls.Recent("u1", "/music/a.flac"));      // long ago: some other error now
        stalls.Mark("u1", "/music/a.flac");
        stalls.Clear("u1", "/music/a.flac");                     // the song came through after all
        Assert.False(stalls.Recent("u1", "/music/a.flac"));
    }
}
