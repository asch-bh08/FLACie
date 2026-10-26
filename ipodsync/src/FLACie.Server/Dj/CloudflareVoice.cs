using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FLACie.Core;
using Microsoft.AspNetCore.DataProtection;

namespace FLACie.Server.Dj;

/// <summary>The DJ's voice, server-wide (FLACie Web dashboard): the owner's Cloudflare account and Workers AI token (stored encrypted), the voice, and how much of
/// Cloudflare's free daily allowance the DJ may use. Kept in the server's data folder as dj.json.</summary>
public sealed class DjVoiceConfig
{
    public string AccountId { get; set; } = "";
    public string Token { get; set; } = "";       // protected; empty when none
    /// <summary>"melotts", or "aura-1:&lt;speaker&gt;". Aura-1 Helios (British, warm) by default: MeloTTS sounded cheap.</summary>
    public string Voice { get; set; } = CloudflareVoice.DefaultVoice;
    /// <summary>The lines are written by a Workers AI text model (the templates when it fails); off: the templates only.</summary>
    public bool AiLines { get; set; } = true;
    /// <summary>2 since the default voice became Aura-1 Helios. An older file still saying "melotts" kept the old default, so it moves to the new one once.</summary>
    public int Version { get; set; }
    /// <summary>The most of the free 10,000 neurons a day the DJ may use, in percent.</summary>
    public int CapPercent { get; set; } = 50;
}

/// <summary>How much of today's (UTC) free allowance the DJ has used, by its own estimate. Kept in dj-usage.json so a restart does not forget it.</summary>
public sealed class DjUsage
{
    public string Day { get; set; } = "";
    public double Neurons { get; set; }
    public int Lines { get; set; }
}

/// <summary>
/// Makes the DJ's clips with Cloudflare Workers AI: Deepgram Aura-1 with one of its speakers (Helios by default) or MeloTTS, and writes the lines with a small text model (<see cref="WriteAsync"/>). Nothing is sent until an admin has added the account
/// id and token. Each line's cost is estimated beforehand and a line that would go over the daily cap is refused without a call. The token is never logged or returned.
/// </summary>
public sealed partial class CloudflareVoice
{
    public const string MeloTts = "melotts";
    public const string DefaultVoice = "aura-1:helios";
    public const int ConfigVersion = 2;
    /// <summary>The text model that writes the lines: small, fast and cheap (about 15 neurons a line).</summary>
    public const string TextModel = "@cf/meta/llama-3.1-8b-instruct";
    /// <summary>Cloudflare's price of <see cref="TextModel"/>, neurons per million tokens.</summary>
    public const double TextInNeurons = 25_608, TextOutNeurons = 75_147;
    /// <summary>The most tokens the model may answer with (a line is about 35).</summary>
    public const int TextMaxTokens = 70;
    public const double FreeNeurons = 10_000;
    /// <summary>Aura-1's speakers (Deepgram says helios and athena are British, angus Irish).</summary>
    public static readonly string[] Speakers = ["angus", "asteria", "arcas", "orion", "orpheus", "athena", "luna", "zeus", "perseus", "helios", "hera", "stella"];
    const int MaxAudioBytes = 8 * 1024 * 1024;

    readonly string file, usageFile; readonly IHttpClientFactory hf; readonly IDataProtector protector; readonly ILogger<CloudflareVoice> log; readonly Func<DateTime> clock;
    readonly object gate = new();
    DjVoiceConfig cfg = new();
    DjUsage usage = new();
    public string LastResult { get; private set; } = "";
    public DateTime? LastAt { get; private set; }

    /// <summary>How long new lines go without a clip after Cloudflare failed to make one (a refused token, an outage); the device's own voice says them meanwhile.</summary>
    public static readonly TimeSpan FailureRest = TimeSpan.FromMinutes(10);
    long failedTicks;   // when Cloudflare last failed (DateTime ticks, 0: not since the last success or save); read and written from several threads
    /// <summary>Cloudflare failed to make a clip less than <see cref="FailureRest"/> ago, and nothing has been made or saved since: new lines get no clip.
    /// (Clips already made are still served.)</summary>
    public bool Failing => Interlocked.Read(ref failedTicks) is var t && t > 0 && clock() - new DateTime(t, DateTimeKind.Utc) < FailureRest;

    [GeneratedRegex(@"^[A-Za-z0-9]{1,64}$")] private static partial Regex AccountShape();

    public CloudflareVoice(DataPaths paths, IHttpClientFactory hf, IDataProtectionProvider dp, IConfiguration conf, ILogger<CloudflareVoice> log, Func<DateTime>? clock = null)
    {
        file = Path.Combine(paths.Root, "dj.json"); usageFile = Path.Combine(paths.Root, "dj-usage.json");
        this.hf = hf; protector = dp.CreateProtector("FLACie.Dj"); this.log = log; this.clock = clock ?? (() => DateTime.UtcNow);
        try { if (File.Exists(file)) cfg = JsonSerializer.Deserialize<DjVoiceConfig>(File.ReadAllText(file)) ?? new(); } catch (Exception) { }
        try { if (File.Exists(usageFile)) usage = JsonSerializer.Deserialize<DjUsage>(File.ReadAllText(usageFile)) ?? new(); } catch (Exception) { }
        // first start: take the account from the environment, so a Docker setup can hand it over
        if (cfg.AccountId.Length == 0 && conf["FLACIE_CLOUDFLARE_ACCOUNT"] is { Length: > 0 } a) cfg.AccountId = a.Trim();
        if (cfg.Token.Length == 0 && conf["FLACIE_CLOUDFLARE_TOKEN"] is { Length: > 0 } t) cfg.Token = Protect(t);
        if (cfg.Version < ConfigVersion && NormVoice(cfg.Voice) == MeloTts) cfg.Voice = DefaultVoice;
        cfg.Version = ConfigVersion;
        cfg.Voice = NormVoice(cfg.Voice); cfg.CapPercent = Math.Clamp(cfg.CapPercent, 0, 100);
    }

    // the token goes into an Authorization header: a line break in a pasted token would start another header line, so control characters are dropped
    string Protect(string s) { try { return protector.Protect(new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim()); } catch (Exception) { return ""; } }
    string Unprotect(string s) { try { return s.Length == 0 ? "" : protector.Unprotect(s); } catch (Exception) { return ""; } }

    /// <summary>"melotts", or "aura-1:&lt;speaker&gt;" with a known speaker; anything else is MeloTTS.</summary>
    public static string NormVoice(string? v)
    {
        var s = (v ?? "").Trim().ToLowerInvariant();
        return s.StartsWith("aura-1:", StringComparison.Ordinal) && Speakers.Contains(s[7..]) ? s : MeloTts;
    }

    public DjVoiceConfig Current { get { lock (gate) return JsonSerializer.Deserialize<DjVoiceConfig>(JsonSerializer.Serialize(cfg))!; } }
    public bool HasToken => Current.Token.Length > 0;
    /// <summary>An account id and a token are set, so clips can be asked for.</summary>
    public bool Ready { get { var c = Current; return AccountShape().IsMatch(c.AccountId) && Unprotect(c.Token).Length > 0; } }

    /// <summary>Saves the settings. A null [newToken] keeps the saved one; an empty one removes it.</summary>
    public void Save(DjVoiceConfig c, string? newToken)
    {
        lock (gate)
        {
            var token = newToken is null ? cfg.Token : newToken.Trim().Length == 0 ? "" : Protect(newToken.Trim());
            cfg = JsonSerializer.Deserialize<DjVoiceConfig>(JsonSerializer.Serialize(c))!;
            cfg.AccountId = new string(cfg.AccountId.Where(char.IsAsciiLetterOrDigit).ToArray());
            cfg.Voice = NormVoice(cfg.Voice); cfg.CapPercent = Math.Clamp(cfg.CapPercent, 0, 100); cfg.Token = token; cfg.Version = ConfigVersion;
            Interlocked.Exchange(ref failedTicks, 0);   // a new token or account is worth trying at once
            try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, JsonSerializer.Serialize(cfg)); } catch (Exception e) { log.LogWarning("Saving the DJ voice settings failed: {Kind}", e.GetType().Name); }
        }
    }

    /// <summary>Cloudflare's price of one line, in neurons: MeloTTS 18.63 per audio minute (the length guessed at 2.6 words a second), Aura-1 1,363.64 per 1,000 characters.</summary>
    public static double EstimateNeurons(string voice, string text) =>
        NormVoice(voice) == MeloTts ? DjText.WordCount(text) / 2.6 / 60.0 * 18.63 : text.Length / 1000.0 * 1363.64;

    string Today => clock().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    void Roll() { if (usage.Day != Today) usage = new DjUsage { Day = Today }; }

    /// <summary>Today's use (UTC day): lines made, neurons spent, and that as a percentage of the free allowance.</summary>
    public (int Lines, double Neurons, double Percent) Usage { get { lock (gate) { Roll(); return (usage.Lines, usage.Neurons, usage.Neurons / FreeNeurons * 100); } } }

    double Cap => FreeNeurons * Current.CapPercent / 100.0;

    /// <summary>This line fits under today's cap.</summary>
    public bool CanAfford(string voice, string text) { lock (gate) { Roll(); return usage.Neurons + EstimateNeurons(voice, text) <= Cap; } }

    /// <summary>Takes the line's estimate out of today's allowance up front (so lines made at the same time cannot go over the cap together); false when it does not fit.</summary>
    bool Reserve(double n) { lock (gate) { Roll(); if (usage.Neurons + n > Cap) return false; usage.Neurons += n; return true; } }
    void Settle(double n, bool made)
    {
        lock (gate)
        {
            Roll();
            if (made) usage.Lines++; else usage.Neurons = Math.Max(0, usage.Neurons - n);
            try { File.WriteAllText(usageFile, JsonSerializer.Serialize(usage)); } catch (Exception) { }
        }
    }

    /// <summary>The MP3 for [text] in [voice] (null: the saved voice), or null when it could not be made: not set up, over today's cap, or Cloudflare said no. Never throws.</summary>
    public async Task<byte[]?> SynthesizeAsync(string text, string? voice = null, CancellationToken ct = default)
    {
        var c = Current; var v = NormVoice(voice ?? c.Voice);
        var token = Unprotect(c.Token);
        if (!AccountShape().IsMatch(c.AccountId) || token.Length == 0) return Fail("Add the Cloudflare account id and API token first.");
        var cost = EstimateNeurons(v, text);
        if (!Reserve(cost)) return Fail("Today's DJ share of the free Cloudflare allowance is used up (the daily cap).");
        var made = false;
        try
        {
            var (model, body) = v == MeloTts
                ? ("@cf/myshell-ai/melotts", new JsonObject { ["prompt"] = text, ["lang"] = "en" })
                : ("@cf/deepgram/aura-1", new JsonObject { ["text"] = text, ["speaker"] = v[7..], ["encoding"] = "mp3" });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/accounts/{c.AccountId}/ai/run/{model}");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var http = hf.CreateClient("cloudflare");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (res.Content.Headers.ContentLength is > MaxAudioBytes) return Down("Cloudflare sent more than a clip");
            var bytes = await ReadAtMost(res.Content, ct);
            if (bytes is null) return Down("Cloudflare sent more than a clip");
            if (!res.IsSuccessStatusCode)
                return Down(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "Cloudflare refused the token (it needs Workers AI access)"
                    : (int)res.StatusCode == 429 ? "Cloudflare says the allowance is used up or too many requests (429)"
                    : $"Cloudflare answered {(int)res.StatusCode} {res.ReasonPhrase}" + ErrorOf(bytes));
            var audio = AudioOf(res.Content.Headers.ContentType?.MediaType ?? "", bytes, out var why);
            if (audio is null) return Down(why);
            made = true;
            LastResult = "Made a clip"; LastAt = clock(); Interlocked.Exchange(ref failedTicks, 0);
            return audio;
        }
        catch (Exception e)
        {
            var msg = e is TaskCanceledException or OperationCanceledException ? "Cloudflare didn't answer in time" : "Couldn't reach Cloudflare (" + e.GetType().Name + ")";
            return ct.IsCancellationRequested ? Fail(msg) : Down(msg);   // (the caller giving up is not Cloudflare failing)
        }
        finally { Settle(cost, made); }
    }

    /// <summary>The text model's price of one line: the prompt at about 4 characters a token, and the answer at its most.</summary>
    public static double EstimateTextNeurons(string system, string user, int maxTokens = TextMaxTokens) =>
        (system.Length + user.Length) / 4.0 * TextInNeurons / 1e6 + maxTokens * TextOutNeurons / 1e6;

    /// <summary>What happened to the last line asked of the text model (for the dashboard).</summary>
    public string LastWrite { get; private set; } = "";

    /// <summary>
    /// The text model's answer to [system] + [user], or null: not set up, the lines would leave no room under today's cap for [keepFor] more (the clip that says it),
    /// Cloudflare said no or did not answer before [ct] gave up. Its neurons count into today's use (Cloudflare's own token count when it gives one). Never throws,
    /// and a failure here never stops the clips (it is not <see cref="Failing"/>).
    /// </summary>
    public async Task<string?> WriteAsync(string system, string user, double keepFor, CancellationToken ct = default)
    {
        var c = Current;
        var token = Unprotect(c.Token);
        if (!AccountShape().IsMatch(c.AccountId) || token.Length == 0) { LastWrite = "Not set up"; return null; }
        var cost = EstimateTextNeurons(system, user);
        lock (gate) { Roll(); if (usage.Neurons + cost + keepFor > Cap) { LastWrite = "Near today's cap: a template line instead"; return null; } usage.Neurons += cost; }
        var spent = 0.0;
        try
        {
            var body = new JsonObject
            {
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = user }),
                ["max_tokens"] = TextMaxTokens, ["temperature"] = 0.8,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/accounts/{c.AccountId}/ai/run/{TextModel}");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var http = hf.CreateClient("cloudflare");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var bytes = await ReadAtMost(res.Content, ct);
            if (bytes is null || !res.IsSuccessStatusCode) { spent = 0; LastWrite = $"The text model answered {(int)res.StatusCode}" + (bytes is null ? "" : ErrorOf(bytes)); log.LogWarning("DJ text: {Message}", LastWrite); return null; }
            var j = JsonNode.Parse(bytes);
            var r = j?["result"];
            spent = cost;
            if (r?["usage"] is JsonObject u && u["prompt_tokens"] is JsonValue pi && pi.TryGetValue<int>(out var pin) && u["completion_tokens"] is JsonValue po && po.TryGetValue<int>(out var pout))
                spent = pin * TextInNeurons / 1e6 + pout * TextOutNeurons / 1e6;
            var text = r?["response"] is JsonValue rv && rv.TryGetValue<string>(out var s) ? s : null;
            LastWrite = text is null ? "The text model sent no line" : "Wrote a line";
            return text;
        }
        catch (Exception e)
        {
            // a timeout may still be charged: count the estimate
            spent = cost;
            LastWrite = e is TaskCanceledException or OperationCanceledException ? "The text model didn't answer in time" : "Couldn't reach the text model (" + e.GetType().Name + ")";
            log.LogWarning("DJ text: {Message}", LastWrite);
            return null;
        }
        finally
        {
            lock (gate)
            {
                Roll();
                usage.Neurons = Math.Max(0, usage.Neurons - cost + spent);
                try { File.WriteAllText(usageFile, JsonSerializer.Serialize(usage)); } catch (Exception) { }
            }
        }
    }

    byte[]? Fail(string msg)
    {
        LastResult = msg; LastAt = clock();
        log.LogWarning("DJ voice: {Message}", msg);
        return null;
    }

    /// <summary>Cloudflare was asked and no clip came back: new lines go without a clip for <see cref="FailureRest"/> (see <see cref="Failing"/>).</summary>
    byte[]? Down(string msg) { Interlocked.Exchange(ref failedTicks, clock().Ticks); return Fail(msg); }

    static async Task<byte[]?> ReadAtMost(HttpContent content, CancellationToken ct)
    {
        await using var s = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[16384]; int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0) { ms.Write(buf, 0, n); if (ms.Length > MaxAudioBytes) return null; }
        return ms.ToArray();
    }

    /// <summary>Cloudflare's own error text from a JSON answer (errors[0].message), short.</summary>
    static string ErrorOf(byte[] body)
    {
        try { if (JsonNode.Parse(body)?["errors"]?[0]?["message"]?.GetValue<string>() is { Length: > 0 } m) return ": " + (m.Length > 120 ? m[..120] : m); } catch (Exception) { }
        return "";
    }

    /// <summary>The MP3 in an answer: a binary audio body, or JSON <c>{ success, result: { audio: base64 } }</c>.</summary>
    public static byte[]? AudioOf(string mediaType, byte[] body, out string why)
    {
        why = "";
        var first = body.FirstOrDefault(b => b is not ((byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t'));
        var json = mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) || (!mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) && first == (byte)'{');
        if (!json)
        {
            if (body.Length == 0) { why = "Cloudflare sent an empty clip"; return null; }
            return body;
        }
        try
        {
            var j = JsonNode.Parse(body);
            if (j?["success"] is JsonValue sv && sv.TryGetValue<bool>(out var ok) && !ok) { why = "Cloudflare could not make the clip" + ErrorOf(body); return null; }
            if (j?["result"]?["audio"] is JsonValue av && av.TryGetValue<string>(out var b64) && b64.Length > 0)
            {
                var bytes = Convert.FromBase64String(b64);
                if (bytes.Length > 0) return bytes;
            }
            why = "Cloudflare's answer had no audio";
            return null;
        }
        catch (Exception) { why = "Cloudflare's answer could not be read"; return null; }
    }
}
