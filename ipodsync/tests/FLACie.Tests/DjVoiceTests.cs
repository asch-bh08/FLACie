using System.Net;
using System.Text;
using System.Text.Json;
using FLACie.Server;
using FLACie.Server.Dj;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FLACie.Tests;

/// <summary>The DJ's Cloudflare voice: both answer shapes, failures, the daily cap, and the token kept encrypted and out of the logs. No real Cloudflare is called.</summary>
public class DjVoiceTests : IDisposable
{
    const string Token = "cf_unit_test_token_value";   // a fake: never a real key
    const string Account = "0123456789abcdef0123456789abcdef";
    static readonly byte[] Mp3 = [0x49, 0x44, 0x33, 0x04, 0, 0, 0, 0, 0, 1, 0xFF, 0xFB, 0x90, 0x64];
    readonly string dir = Directory.CreateTempSubdirectory("flacie-dj-voice").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) : HttpMessageHandler
    {
        public readonly List<(string Url, string Auth, string Body)> Seen = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            lock (Seen) Seen.Add((r.RequestUri!.ToString(), r.Headers.Authorization?.ToString() ?? "", body));
            return await f(r);
        }
    }
    internal sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }
    internal sealed class Lines<T> : ILogger<T>
    {
        public readonly List<string> All = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel l, EventId id, TState s, Exception? e, Func<TState, Exception?, string> f) { lock (All) All.Add(f(s, e) + (e?.ToString() ?? "")); }
    }

    internal static HttpResponseMessage Audio() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Mp3) { Headers = { ContentType = new("audio/mpeg") } } };
    internal static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    DateTime now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    (CloudflareVoice V, Handler H, Lines<CloudflareVoice> Log) Make(Func<HttpRequestMessage, Task<HttpResponseMessage>> f, Dictionary<string, string?>? conf = null)
    {
        var h = new Handler(f); var log = new Lines<CloudflareVoice>();
        var v = new CloudflareVoice(new DataPaths(dir), new Factory(h), DataProtectionProvider.Create(new DirectoryInfo(dir)),
            new ConfigurationBuilder().AddInMemoryCollection(conf ?? []).Build(), log, () => now);
        return (v, h, log);
    }

    [Fact]
    public async Task MeloTts_answers_as_json_with_base64_audio()
    {
        var (v, h, log) = Make(_ => Task.FromResult(Json(JsonSerializer.Serialize(new { result = new { audio = Convert.ToBase64String(Mp3) }, success = true, errors = Array.Empty<object>() }))));
        v.Save(new DjVoiceConfig { AccountId = Account, Voice = CloudflareVoice.MeloTts }, Token);
        var mp3 = await v.SynthesizeAsync("Here's Yellow by Coldplay.");
        Assert.Equal(Mp3, mp3);
        var (url, auth, body) = Assert.Single(h.Seen);
        Assert.Equal($"https://api.cloudflare.com/client/v4/accounts/{Account}/ai/run/@cf/myshell-ai/melotts", url);
        Assert.Equal("Bearer " + Token, auth);
        var j = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Here's Yellow by Coldplay.", j.GetProperty("prompt").GetString());
        Assert.Equal("en", j.GetProperty("lang").GetString());
        Assert.DoesNotContain(Token, body);
        Assert.DoesNotContain(log.All, l => l.Contains(Token));
        Assert.Equal(1, v.Usage.Lines);
    }

    [Fact]
    public async Task Aura_answers_with_a_binary_mp3_and_gets_the_speaker()
    {
        var (v, h, _) = Make(_ => Task.FromResult(Audio()));
        v.Save(new DjVoiceConfig { AccountId = Account, Voice = "aura-1:helios" }, Token);
        Assert.Equal(Mp3, await v.SynthesizeAsync("Here's Yellow by Coldplay."));
        var (url, _, body) = Assert.Single(h.Seen);
        Assert.EndsWith("/ai/run/@cf/deepgram/aura-1", url);
        var j = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Here's Yellow by Coldplay.", j.GetProperty("text").GetString());
        Assert.Equal("helios", j.GetProperty("speaker").GetString());
        Assert.Equal("mp3", j.GetProperty("encoding").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"success\":false}", "refused the token")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"success\":false,\"errors\":[{\"message\":\"boom\"}]}", "500")]
    [InlineData(HttpStatusCode.OK, "{\"success\":false,\"errors\":[{\"message\":\"bad input\"}]}", "bad input")]
    [InlineData(HttpStatusCode.OK, "{\"success\":true,\"result\":{}}", "no audio")]
    public async Task A_failure_gives_no_clip_and_says_why(HttpStatusCode code, string json, string why)
    {
        var (v, _, log) = Make(_ => Task.FromResult(Json(json, code)));
        v.Save(new DjVoiceConfig { AccountId = Account }, Token);
        Assert.Null(await v.SynthesizeAsync("Here's Yellow by Coldplay."));
        Assert.Contains(why, v.LastResult);
        Assert.Equal(0, v.Usage.Lines);
        Assert.Equal(0, v.Usage.Neurons);   // a failed line is not counted against the allowance
        Assert.DoesNotContain(log.All, l => l.Contains(Token));
    }

    [Fact]
    public async Task An_unreachable_cloudflare_gives_no_clip_and_never_throws()
    {
        var (v, _, log) = Make(_ => throw new HttpRequestException("no route to host " + Token));
        v.Save(new DjVoiceConfig { AccountId = Account }, Token);
        Assert.Null(await v.SynthesizeAsync("Here's Yellow by Coldplay."));
        Assert.DoesNotContain(Token, v.LastResult);
        Assert.DoesNotContain(log.All, l => l.Contains(Token));
    }

    [Fact]
    public async Task Not_set_up_or_over_the_cap_means_no_call_at_all()
    {
        var (v, h, _) = Make(_ => Task.FromResult(Audio()));
        Assert.False(v.Ready);
        Assert.Null(await v.SynthesizeAsync("Here's Yellow by Coldplay."));
        v.Save(new DjVoiceConfig { AccountId = Account, CapPercent = 0 }, Token);
        Assert.True(v.Ready);
        Assert.False(v.CanAfford(CloudflareVoice.MeloTts, "Here's Yellow by Coldplay."));
        Assert.Null(await v.SynthesizeAsync("Here's Yellow by Coldplay."));
        Assert.Contains("cap", v.LastResult);
        Assert.Empty(h.Seen);
    }

    [Fact]
    public async Task The_daily_cap_stops_lines_once_the_estimate_reaches_it_and_resets_the_next_UTC_day()
    {
        var (v, h, _) = Make(_ => Task.FromResult(Audio()));
        v.Save(new DjVoiceConfig { AccountId = Account, Voice = "aura-1:luna", CapPercent = 1 }, Token);   // 100 neurons
        var line = new string('a', 60);                                                                        // 81.8 neurons on Aura-1
        Assert.NotNull(await v.SynthesizeAsync(line));
        Assert.Null(await v.SynthesizeAsync(line));
        Assert.Single(h.Seen);
        Assert.Equal(1, v.Usage.Lines);
        Assert.InRange(v.Usage.Percent, 0.81, 0.82);
        // the count survives a restart
        var (v2, _, _) = Make(_ => Task.FromResult(Audio()));
        Assert.Equal(1, v2.Usage.Lines);
        now = now.AddDays(1).Date;
        Assert.Equal(0, v.Usage.Lines);
        Assert.NotNull(await v.SynthesizeAsync(line));
    }

    // Aura-2 (owner, 27 Oct: a warmer, more casual voice, "like Spotify's DJ"): the newer Deepgram voices, at twice Aura-1's price
    [Fact]
    public async Task Aura2_voices_go_to_the_aura2_model_with_the_bare_speaker()
    {
        var (v, h, _) = Make(_ => Task.FromResult(Audio()));
        v.Save(new DjVoiceConfig { AccountId = Account, Voice = "aura-2:aries" }, Token);
        Assert.Equal("aura-2:aries", v.Current.Voice);
        Assert.Equal(Mp3, await v.SynthesizeAsync("Alright, here's Yellow."));
        var (url, _, body) = Assert.Single(h.Seen);
        Assert.EndsWith("/ai/run/@cf/deepgram/aura-2-en", url);
        var j = JsonDocument.Parse(body).RootElement;
        Assert.Equal("aries", j.GetProperty("speaker").GetString());
        Assert.Equal("mp3", j.GetProperty("encoding").GetString());
    }

    [Fact]
    public void Voice_names_are_checked_per_family()
    {
        Assert.Equal("aura-2:phoebe", CloudflareVoice.NormVoice(" Aura-2:Phoebe "));
        Assert.Equal("aura-1:helios", CloudflareVoice.NormVoice("aura-1:helios"));
        Assert.Equal(CloudflareVoice.MeloTts, CloudflareVoice.NormVoice("aura-2:nobody"));
        Assert.Equal(CloudflareVoice.MeloTts, CloudflareVoice.NormVoice("aura-1:aries"));   // an Aura-2 voice is not an Aura-1 one
        Assert.Contains("aries", CloudflareVoice.Speakers2);
        Assert.Equal(1363.64 * 2, CloudflareVoice.EstimateNeurons("aura-2:aries", new string('x', 1000)), 1);
    }

    [Fact]
    public void Neurons_are_estimated_from_words_for_MeloTts_and_characters_for_Aura()
    {
        var twentySix = string.Join(' ', Enumerable.Repeat("word", 26));   // 10 s of speech
        Assert.Equal(18.63 / 6, CloudflareVoice.EstimateNeurons("melotts", twentySix), 3);
        Assert.Equal(1363.64, CloudflareVoice.EstimateNeurons("aura-1:zeus", new string('x', 1000)), 3);
    }

    [Fact]
    public void The_token_is_stored_encrypted_kept_when_left_empty_and_removed_when_cleared()
    {
        var (v, _, _) = Make(_ => Task.FromResult(Audio()));
        v.Save(new DjVoiceConfig { AccountId = " " + Account + " ", Voice = "aura-1:nobody", CapPercent = 250 }, Token);
        var json = File.ReadAllText(Path.Combine(dir, "dj.json"));
        Assert.DoesNotContain(Token, json);
        Assert.True(v.HasToken);
        Assert.Equal(Account, v.Current.AccountId);
        Assert.Equal("melotts", v.Current.Voice);     // an unknown speaker falls back to MeloTTS
        Assert.Equal(100, v.Current.CapPercent);
        v.Save(new DjVoiceConfig { AccountId = Account, Voice = "aura-1:athena" }, null);
        Assert.True(v.HasToken);
        var (again, _, _) = Make(_ => Task.FromResult(Audio()));
        Assert.True(again.Ready);
        Assert.Equal("aura-1:athena", again.Current.Voice);
        v.Save(v.Current, "");
        Assert.False(v.HasToken);
        Assert.False(v.Ready);
    }

    [Fact]
    public void The_environment_can_hand_over_the_account_and_token()
    {
        var (v, _, _) = Make(_ => Task.FromResult(Audio()), new() { ["FLACIE_CLOUDFLARE_ACCOUNT"] = Account, ["FLACIE_CLOUDFLARE_TOKEN"] = Token });
        Assert.True(v.Ready);
        Assert.Equal(Account, v.Current.AccountId);
        Assert.DoesNotContain(Token, v.Current.Token);
    }

    [Fact]
    public async Task The_test_seam_redirects_cloudflare_to_a_loopback_stand_in()
    {
        var inner = new Handler(_ => Task.FromResult(Audio()));
        using var http = new HttpClient(new CatalogRedirectHandler(new Uri("http://127.0.0.1:9999")) { InnerHandler = inner });
        await http.PostAsync($"https://api.cloudflare.com/client/v4/accounts/{Account}/ai/run/@cf/myshell-ai/melotts", new StringContent("{}"));
        Assert.Equal($"http://127.0.0.1:9999/api.cloudflare.com/client/v4/accounts/{Account}/ai/run/@cf/myshell-ai/melotts", Assert.Single(inner.Seen).Url);
    }
}
