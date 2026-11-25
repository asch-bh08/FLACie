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

    /// <summary>Answers with the statuses given, one per request (the last one again after that), counting the requests.</summary>
    sealed class Answers(params HttpStatusCode[] codes) : HttpMessageHandler
    {
        public int Asked;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var code = codes[Math.Min(Interlocked.Increment(ref Asked) - 1, codes.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    /// <summary>Refuses the connection the first <paramref name="times"/> times, then answers 200.</summary>
    sealed class RefusesFirst(int times) : HttpMessageHandler
    {
        public int Asked;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Interlocked.Increment(ref Asked) <= times ? throw new HttpRequestException("refused") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    static readonly TimeSpan[] Quick = [TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5)];

    // owner, 25 Nov: "sometimes about 10 s to play, sometimes not at all". One 503 from Jellyfin reached the browser as it was; the browser waited 1.5 s,
    // and four in a row skipped the song without a word. A song someone is waiting for is asked again at once, a couple of times, before anything is sent.
    [Fact]
    public async Task A_song_Jellyfin_answers_with_a_passing_error_is_asked_again_at_once()
    {
        var jf = new Answers(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(jf) { Timeout = Timeout.InfiniteTimeSpan };
        using var res = await JellyfinFirstReply.SendSongAsync(http, Song, TimeSpan.FromSeconds(5), CancellationToken.None, Quick);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, jf.Asked);
    }

    [Fact]
    public async Task A_refused_connection_is_tried_again_too()
    {
        var jf = new RefusesFirst(2);
        using var http = new HttpClient(jf) { Timeout = Timeout.InfiniteTimeSpan };
        using var res = await JellyfinFirstReply.SendSongAsync(http, Song, TimeSpan.FromSeconds(5), CancellationToken.None, Quick);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3, jf.Asked);
    }

    [Fact]
    public async Task Tries_are_limited_and_the_last_answer_is_passed_on()
    {
        var jf = new Answers(HttpStatusCode.BadGateway);
        using var http = new HttpClient(jf) { Timeout = Timeout.InfiniteTimeSpan };
        using var res = await JellyfinFirstReply.SendSongAsync(http, Song, TimeSpan.FromSeconds(5), CancellationToken.None, Quick);
        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Equal(3, jf.Asked);   // the first try and the two again
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task An_answer_that_asking_again_cannot_change_is_passed_on_at_once(HttpStatusCode code)
    {
        var jf = new Answers(code);
        using var http = new HttpClient(jf) { Timeout = Timeout.InfiniteTimeSpan };
        using var res = await JellyfinFirstReply.SendSongAsync(http, Song, TimeSpan.FromSeconds(5), CancellationToken.None, Quick);
        Assert.Equal(code, res.StatusCode);
        Assert.Equal(1, jf.Asked);
    }
}
