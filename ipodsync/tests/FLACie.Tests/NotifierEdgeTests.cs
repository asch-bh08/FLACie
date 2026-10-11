using System.Net;
using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FLACie.Tests;

/// <summary>Edge cases of the ntfy notifier: wording per status, toggles, a corrupt settings file, concurrency, and the token never leaving the Authorization header.</summary>
public class NotifierEdgeTests : IDisposable
{
    const string Token = "tk_edge_case_token_0123456789";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-notify-edge").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> f) : HttpMessageHandler
    {
        public int Calls;
        public readonly List<string> Auth = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            lock (Auth) Auth.Add(r.Headers.Authorization?.ToString() ?? "");
            return f(r, ct);
        }
    }
    sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }
    sealed class Lines : ILogger<Notifier>
    {
        public readonly List<string> All = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel l, EventId id, TState s, Exception? e, Func<TState, Exception?, string> f) { lock (All) All.Add(f(s, e) + (e?.ToString() ?? "")); }
    }

    (Notifier N, Handler H, Lines Log, ActivityLog Act) Make(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> f)
    {
        var h = new Handler(f); var log = new Lines(); var act = new ActivityLog();
        var n = new Notifier(new DataPaths(dir), new Factory(h), DataProtectionProvider.Create(new DirectoryInfo(dir)),
            new ConfigurationBuilder().AddInMemoryCollection().Build(), log, act);
        return (n, h, log, act);
    }

    static Task<HttpResponseMessage> Status(HttpStatusCode c) => Task.FromResult(new HttpResponseMessage(c));
    static NotifyConfig On(Action<NotifyConfig>? tweak = null) { var c = new NotifyConfig { Url = "https://ntfy.example", Topic = "t", Enabled = true }; tweak?.Invoke(c); return c; }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "ntfy rejected the token (needs write access to this topic)")]
    [InlineData(HttpStatusCode.Forbidden, "ntfy rejected the token (needs write access to this topic)")]
    [InlineData(HttpStatusCode.TooManyRequests, "ntfy answered 429 Too Many Requests")]
    [InlineData(HttpStatusCode.InternalServerError, "ntfy answered 500 Internal Server Error")]
    [InlineData(HttpStatusCode.NotFound, "ntfy answered 404 Not Found")]
    public async Task Each_status_has_its_own_plain_wording_and_never_the_token(HttpStatusCode code, string expected)
    {
        var (n, _, log, act) = Make((_, _) => Status(code));
        n.Save(On(), Token);
        var (ok, msg) = await n.SendAsync("x", "y");
        Assert.False(ok);
        Assert.Equal(expected, msg);
        Assert.DoesNotContain(Token, msg);
        Assert.DoesNotContain(log.All, l => l.Contains(Token));
        Assert.DoesNotContain(act.Recent().Select(a => a.Text), t => t.Contains(Token));
    }

    [Fact]
    public async Task A_server_that_never_answers_gives_up_in_ten_seconds_with_a_timeout_message()
    {
        var (n, _, _, _) = Make(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        n.Save(On(), Token);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (ok, msg) = await n.SendAsync("x", "y");
        Assert.False(ok);
        Assert.Equal("ntfy didn't answer in time", msg);
        Assert.InRange(sw.Elapsed.TotalSeconds, 9, 14);
    }

    [Fact]
    public async Task Switched_off_sends_nothing_and_each_kind_has_its_own_toggle()
    {
        var (n, h, _, _) = Make((_, _) => Status(HttpStatusCode.OK));
        n.Save(On(c => c.Enabled = false), null);
        await n.NotifyAsync("signin", "t", "m");
        Assert.Equal(0, h.Calls);
        n.Save(On(c => { c.SignIns = false; c.Plays = false; c.Downloads = false; c.Imports = false; c.Charts = false; c.LowSpace = false; c.Started = false; }), null);
        foreach (var k in new[] { "signin", "plays", "downloads", "imports", "charts", "space", "started" }) await n.NotifyAsync(k, "t", "m");
        Assert.Equal(0, h.Calls);
        await n.NotifyAsync("alert", "t", "m");            // a kind without a toggle always goes (when notifications are on)
        for (var i = 0; i < 50 && h.Calls == 0; i++) await Task.Delay(20);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task No_address_or_topic_says_so_without_a_request()
    {
        var (n, h, _, _) = Make((_, _) => Status(HttpStatusCode.OK));
        n.Save(new NotifyConfig { Enabled = true }, null);
        var (ok, msg) = await n.SendAsync("x", "y");
        Assert.False(ok);
        Assert.Equal("Add the ntfy address and a topic first.", msg);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task Without_a_token_no_Authorization_header_is_sent_and_an_empty_new_token_removes_the_old_one()
    {
        var (n, h, _, _) = Make((_, _) => Status(HttpStatusCode.OK));
        n.Save(On(), Token);
        Assert.True(n.HasToken);
        n.Save(On(), null);                  // keeps
        Assert.True(n.HasToken);
        n.Save(On(), "");                    // removes
        Assert.False(n.HasToken);
        await n.SendAsync("x", "y");
        Assert.Equal("", h.Auth[0]);
    }

    [Fact]
    public void A_corrupt_settings_file_starts_with_defaults_instead_of_throwing()
    {
        File.WriteAllText(Path.Combine(dir, "notify.json"), "{ this is not json");
        var (n, _, _, _) = Make((_, _) => Status(HttpStatusCode.OK));
        Assert.False(n.Current.Enabled);
        Assert.Equal("", n.Current.Url);
    }

    [Fact]
    public async Task A_token_that_the_data_keys_cannot_unprotect_is_treated_as_no_token()
    {
        // a restored backup with another key ring: the saved blob cannot be read
        File.WriteAllText(Path.Combine(dir, "notify.json"), JsonSerializer.Serialize(new NotifyConfig { Url = "https://ntfy.example", Topic = "t", Enabled = true, Token = "CfDJ8-not-a-real-blob" }));
        var (n, h, _, _) = Make((_, _) => Status(HttpStatusCode.OK));
        var r = await n.SendAsync("x", "y");
        Assert.True(r.Ok);
        Assert.Equal("", h.Auth[0]);
    }

    [Fact]
    public async Task Saving_and_sending_from_many_threads_at_once_is_safe()
    {
        var (n, _, _, _) = Make((_, _) => Status(HttpStatusCode.OK));
        n.Save(On(), Token);
        var errors = new List<Exception>();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(w => Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 100; i++)
                {
                    if (i % 10 == 0) n.Save(On(c => c.Topic = "t" + w), null);
                    _ = n.Current; _ = n.HasToken;
                    await n.NotifyAsync("plays", "t", "m");
                }
            }
            catch (Exception e) { lock (errors) errors.Add(e); }
        })));
        Assert.Empty(errors);
        Assert.True(n.HasToken);                // saves with a null token never dropped it
    }

    [Fact]
    public async Task A_token_with_line_breaks_cannot_add_headers_and_is_not_echoed()
    {
        // real HTTP stack: the header value is validated when the request is written
        var listener = new HttpListener();
        var port = Random.Shared.Next(20000, 30000);
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try { listener.Start(); } catch (Exception) { return; }   // no listener rights on this machine: nothing to check
        var seen = new List<string>();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try { var c = await listener.GetContextAsync(); lock (seen) seen.Add(string.Join("|", c.Request.Headers.AllKeys)); c.Response.StatusCode = 200; c.Response.Close(); }
                catch (Exception) { break; }
            }
        });
        var log = new Lines(); var act = new ActivityLog();
        var n = new Notifier(new DataPaths(dir), new RealFactory(), DataProtectionProvider.Create(new DirectoryInfo(dir)), new ConfigurationBuilder().AddInMemoryCollection().Build(), log, act);
        n.Save(new NotifyConfig { Url = $"http://127.0.0.1:{port}", Topic = "t", Enabled = true }, "abc\r\nX-Evil: 1");
        var (ok, msg) = await n.SendAsync("x", "y");
        listener.Stop();
        lock (seen) Assert.DoesNotContain(seen, s => s.Contains("X-Evil"));
        Assert.DoesNotContain("abc", msg);
        Assert.DoesNotContain(log.All, l => l.Contains("abc\r\nX-Evil"));
        _ = ok;
    }

    sealed class RealFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
