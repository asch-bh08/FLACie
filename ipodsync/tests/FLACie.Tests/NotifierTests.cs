using System.Net;
using System.Text.Json;
using FLACie.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FLACie.Tests;

public class NotifierTests : IDisposable
{
    const string Token = "tk_unit_test_token_value";
    readonly string dir = Directory.CreateTempSubdirectory("flacie-notify").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) : HttpMessageHandler
    {
        public readonly List<(string Auth, string Body)> Seen = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.Headers.Authorization?.ToString() ?? "", r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));
            return await f(r);
        }
    }
    sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }
    sealed class Lines : ILogger<Notifier>
    {
        public readonly List<string> All = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel l, EventId id, TState s, Exception? e, Func<TState, Exception?, string> f) { All.Add(f(s, e) + (e?.ToString() ?? "")); }
    }

    (Notifier N, Handler H, Lines Log) Make(Func<HttpRequestMessage, Task<HttpResponseMessage>> f, Dictionary<string, string?>? conf = null)
    {
        var h = new Handler(f); var log = new Lines();
        var n = new Notifier(new DataPaths(dir), new Factory(h), DataProtectionProvider.Create(new DirectoryInfo(dir)),
            new ConfigurationBuilder().AddInMemoryCollection(conf ?? []).Build(), log, new ActivityLog());
        return (n, h, log);
    }

    static Task<HttpResponseMessage> Status(HttpStatusCode c) => Task.FromResult(new HttpResponseMessage(c));

    [Fact]
    public async Task A_403_says_the_token_was_rejected_and_the_failure_is_kept_for_Settings()
    {
        var (n, _, _) = Make(_ => Status(HttpStatusCode.Forbidden));
        n.Save(new NotifyConfig { Url = "https://ntfy.example", Topic = "t", Enabled = true }, Token);
        var (ok, msg) = await n.SendAsync("x", "y");
        Assert.False(ok);
        Assert.Equal("ntfy rejected the token (needs write access to this topic)", msg);
        Assert.Equal(msg, n.LastResult);
    }

    [Fact]
    public async Task The_title_is_prefixed_and_the_token_goes_only_in_the_header()
    {
        var (n, h, log) = Make(_ => Status(HttpStatusCode.OK));
        n.Save(new NotifyConfig { Url = "https://ntfy.example", Topic = "t", Enabled = true }, Token);
        await n.SendAsync("Imports finished", "3 of 3");
        var (auth, body) = Assert.Single(h.Seen);
        Assert.Equal("Bearer " + Token, auth);
        Assert.Equal("🎵 FLACie · Imports finished", JsonDocument.Parse(body).RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain(Token, body);
        Assert.DoesNotContain(log.All, l => l.Contains(Token));
        Assert.DoesNotContain(Token, File.ReadAllText(Path.Combine(dir, "notify.json")));   // stored protected, not in the clear
    }

    [Fact]
    public async Task An_unreachable_server_never_blocks_the_caller_and_the_token_is_not_logged()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        var (n, _, log) = Make(_ => gate.Task);
        n.Save(new NotifyConfig { Url = "https://ntfy.example", Topic = "t", Enabled = true }, Token);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t = n.NotifyAsync("plays", "title", "msg", 2, "musical_note");   // what playback calls
        Assert.True(sw.ElapsedMilliseconds < 1000);                          // returned while the server had not answered
        Assert.False(t.IsCompleted);
        gate.SetException(new HttpRequestException("unreachable"));
        await t;
        Assert.Equal("unreachable", n.LastResult);
        Assert.DoesNotContain(log.All, l => l.Contains(Token));
    }

    [Fact]
    public void The_plays_toggle_is_on_by_default_and_saved()
    {
        var (n, _, _) = Make(_ => Status(HttpStatusCode.OK));
        Assert.True(n.Current.Plays);
        n.Save(new NotifyConfig { Url = "u", Topic = "t", Plays = false }, null);
        var (n2, _, _) = Make(_ => Status(HttpStatusCode.OK));
        Assert.False(n2.Current.Plays);
    }

    [Fact]
    public void The_env_topic_replaces_the_old_default_but_not_a_chosen_one()
    {
        File.WriteAllText(Path.Combine(dir, "notify.json"), "{\"Url\":\"https://ntfy.example\",\"Topic\":\"FLACie\",\"Enabled\":true}");
        var (n, _, _) = Make(_ => Status(HttpStatusCode.OK), new() { ["FLACIE_NTFY_TOPIC"] = "owner-topic" });
        Assert.Equal("owner-topic", n.Current.Topic);
        File.WriteAllText(Path.Combine(dir, "notify.json"), "{\"Url\":\"https://ntfy.example\",\"Topic\":\"mine\",\"Enabled\":true}");
        var (n2, _, _) = Make(_ => Status(HttpStatusCode.OK), new() { ["FLACIE_NTFY_TOPIC"] = "owner-topic" });
        Assert.Equal("mine", n2.Current.Topic);
    }
}
