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
    /// <summary>"melotts", "aura-1:&lt;speaker&gt;" or "aura-2:&lt;speaker&gt;". Aura-1 Helios (British, warm) by default: MeloTTS sounded cheap.</summary>
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
    /// <summary>Clips made (a pooled line's phrase and name clip count one each).</summary>
    public int Lines { get; set; }
    /// <summary>Lines handed out with a DJ clip, for the whole family.</summary>
    public int Said { get; set; }
    /// <summary>Neurons the pronunciation check (<see cref="DjPronounceCheck"/>) has used today; they are in <see cref="Neurons"/> too.</summary>
    public double Checks { get; set; }
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
    /// <summary>The text model that writes the lines: Llama 3.3 70B (about 22 neurons a line, see <see cref="DjModels"/>); <see cref="FallbackModel"/> when it can't answer.</summary>
    public static string TextModel => DjModels.Primary.Id;
    public static string FallbackModel => DjModels.Fallback.Id;
    /// <summary>Cloudflare's price of <see cref="TextModel"/>, neurons per million tokens.</summary>
    public static double TextInNeurons => DjModels.Primary.InNeurons;
    public static double TextOutNeurons => DjModels.Primary.OutNeurons;
    /// <summary>The most tokens the model may answer with (a line is about 35).</summary>
    public const int TextMaxTokens = DjBudget.MaxTokens;
    public const double FreeNeurons = 10_000;
    /// <summary>How much of the free allowance the Dashboard's voice samples may reach: past the DJ's own cap, so voices can still be heard once the DJ
    /// has used its share for the day (the owner could not hear any at 50 %), but never past the free allowance.</summary>
    public const double PreviewShare = 0.95;
    /// <summary>Aura-1's speakers (Deepgram says helios and athena are British, angus Irish).</summary>
    public static readonly string[] Speakers = ["angus", "asteria", "arcas", "orion", "orpheus", "athena", "luna", "zeus", "perseus", "helios", "hera", "stella"];
    /// <summary>Kokoro's English voices (FLACie's own voice on frank, ipodsync/tools/kokoro): no daily limit and no Cloudflare account. am_ / af_ American men /
    /// women, bm_ / bf_ British. The owner chose Liam (27 Oct).</summary>
    public static readonly string[] KokoroVoices = ["am_liam", "am_michael", "am_puck", "am_fenrir", "am_echo", "am_eric", "am_adam", "am_onyx", "bm_george", "bm_fable",
        "bm_lewis", "bm_daniel", "af_heart", "af_bella", "af_nicole", "af_sarah", "bf_emma", "bf_isabella"];
    public const string DefaultKokoroUrl = "http://127.0.0.1:8092";
    readonly string kokoroUrl;
    /// <summary>A Kokoro voice: made on frank, free.</summary>
    public static bool IsKokoro(string voice) => voice.StartsWith("kokoro:", StringComparison.Ordinal);

    /// <summary>Aura-2's English speakers (Cloudflare's @cf/deepgram/aura-2-en, docs/dj-voice-report.md): warmer and more natural than Aura-1, at twice the price.</summary>
    public static readonly string[] Speakers2 = ["amalthea", "andromeda", "apollo", "arcas", "aries", "asteria", "athena", "atlas", "aurora", "callista", "cora", "cordelia",
        "delia", "draco", "electra", "harmonia", "helena", "hera", "hermes", "hyperion", "iris", "janus", "juno", "jupiter", "luna", "mars", "minerva", "neptune", "odysseus",
        "ophelia", "orion", "orpheus", "pandora", "phoebe", "pluto", "saturn", "thalia", "theia", "vesta", "zeus"];
    const int MaxAudioBytes = 8 * 1024 * 1024;

    readonly string file, usageFile; readonly IHttpClientFactory hf; readonly IDataProtector protector; readonly ILogger<CloudflareVoice> log; readonly Func<DateTime> clock;
    readonly object gate = new();
    DjVoiceConfig cfg = new();
    DjUsage usage = new();
    public string LastResult { get; private set; } = "";
    public DateTime? LastAt { get; private set; }

    /// <summary>How long new lines go without a clip after <see cref="FailuresInARow"/> lines in a row could not be made (a refused token, an outage). One failure
    /// only costs that line: the next line is tried as usual (on 2026-10-26 a single 500 used to silence the DJ for ten minutes).</summary>
    public static readonly TimeSpan FailureRest = TimeSpan.FromSeconds(30);
    public const int FailuresInARow = 3;
    /// <summary>How long to wait before asking Cloudflare once more for the same line after a 5xx, a timeout or a dropped connection (shorter in tests).</summary>
    public TimeSpan RetryPause { get; set; } = TimeSpan.FromSeconds(1);
    long failedTicks;   // when Cloudflare last failed (DateTime ticks, 0: not since the last success or save); read and written from several threads
    int failedRun;      // lines in a row that could not be made
    /// <summary><see cref="FailuresInARow"/> lines in a row failed, the last less than <see cref="FailureRest"/> ago, and nothing has been made or saved since:
    /// new lines get no clip for that short while. (Clips already made are still served.)</summary>
    public bool Failing => Volatile.Read(ref failedRun) >= FailuresInARow && Interlocked.Read(ref failedTicks) is var t && t > 0 && clock() - new DateTime(t, DateTimeKind.Utc) < FailureRest;

    [GeneratedRegex(@"^[A-Za-z0-9]{1,64}$")] private static partial Regex AccountShape();

    public CloudflareVoice(DataPaths paths, IHttpClientFactory hf, IDataProtectionProvider dp, IConfiguration conf, ILogger<CloudflareVoice> log, Func<DateTime>? clock = null)
    {
        file = Path.Combine(paths.Root, "dj.json"); usageFile = Path.Combine(paths.Root, "dj-usage.json");
        this.hf = hf; protector = dp.CreateProtector("FLACie.Dj"); this.log = log; this.clock = clock ?? (() => DateTime.UtcNow);
        kokoroUrl = (conf["FLACIE_KOKORO_URL"] is { Length: > 0 } ku ? ku : DefaultKokoroUrl).TrimEnd('/');
        try { if (File.Exists(file)) cfg = JsonSerializer.Deserialize<DjVoiceConfig>(File.ReadAllText(file)) ?? new(); } catch (Exception) { }
        try { if (File.Exists(usageFile)) usage = JsonSerializer.Deserialize<DjUsage>(File.ReadAllText(usageFile)) ?? new(); } catch (Exception) { }
        // first start: take the account from the environment, so a Docker setup can hand it over
        if (cfg.AccountId.Length == 0 && conf["FLACIE_CLOUDFLARE_ACCOUNT"] is { Length: > 0 } a) cfg.AccountId = a.Trim();
        if (cfg.Token.Length == 0 && conf["FLACIE_CLOUDFLARE_TOKEN"] is { Length: > 0 } t) cfg.Token = Protect(t);
        // a file from before the Aura default (no Version) that still says "melotts" kept the old default: it moves to Aura once, and the file is written so the
        // disk, the memory and the clip ids all name the same voice. (Such a file can't say whether MeloTTS was picked on purpose; it is moved all the same.)
        var migrate = File.Exists(file) && cfg.Version < ConfigVersion;
        if (cfg.Version < ConfigVersion && NormVoice(cfg.Voice) == MeloTts) cfg.Voice = DefaultVoice;
        cfg.Version = ConfigVersion;
        cfg.Voice = NormVoice(cfg.Voice); cfg.CapPercent = Math.Clamp(cfg.CapPercent, 0, 100);
        if (migrate)
            try { File.WriteAllText(file, JsonSerializer.Serialize(cfg)); log.LogInformation("DJ voice settings moved to version {Version}: voice {Voice}", ConfigVersion, cfg.Voice); }
            catch (Exception e) { log.LogWarning("Saving the DJ voice settings failed: {Kind}", e.GetType().Name); }
    }

    // the token goes into an Authorization header: a line break in a pasted token would start another header line, so control characters are dropped
    string Protect(string s) { try { return protector.Protect(new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim()); } catch (Exception) { return ""; } }
    string Unprotect(string s) { try { return s.Length == 0 ? "" : protector.Unprotect(s); } catch (Exception) { return ""; } }

    /// <summary>"melotts", or "aura-1:&lt;speaker&gt;" / "aura-2:&lt;speaker&gt;" with a speaker of that family; anything else is MeloTTS.</summary>
    public static string NormVoice(string? v)
    {
        var s = (v ?? "").Trim().ToLowerInvariant();
        return s.StartsWith("aura-1:", StringComparison.Ordinal) && Speakers.Contains(s[7..]) ? s
            : s.StartsWith("aura-2:", StringComparison.Ordinal) && Speakers2.Contains(s[7..]) ? s
            : s.StartsWith("kokoro:", StringComparison.Ordinal) && KokoroVoices.Contains(s[7..]) ? s : MeloTts;
    }
    /// <summary>An Aura-2 voice (twice Aura-1's price, its own model).</summary>
    public static bool IsAura2(string voice) => voice.StartsWith("aura-2:", StringComparison.Ordinal);

    public DjVoiceConfig Current { get { lock (gate) return JsonSerializer.Deserialize<DjVoiceConfig>(JsonSerializer.Serialize(cfg))!; } }
    public bool HasToken => Current.Token.Length > 0;
    /// <summary>An account id and a token are set, so clips can be asked for.</summary>
    public bool Ready { get { var c = Current; return IsKokoro(NormVoice(c.Voice)) || CloudflareReady(c); } }
    bool CloudflareReady(DjVoiceConfig c) => AccountShape().IsMatch(c.AccountId) && Unprotect(c.Token).Length > 0;

    /// <summary>Saves the settings. A null [newToken] keeps the saved one; an empty one removes it.</summary>
    public void Save(DjVoiceConfig c, string? newToken)
    {
        lock (gate)
        {
            var token = newToken is null ? cfg.Token : newToken.Trim().Length == 0 ? "" : Protect(newToken.Trim());
            cfg = JsonSerializer.Deserialize<DjVoiceConfig>(JsonSerializer.Serialize(c))!;
            cfg.AccountId = new string(cfg.AccountId.Where(char.IsAsciiLetterOrDigit).ToArray());
            cfg.Voice = NormVoice(cfg.Voice); cfg.CapPercent = Math.Clamp(cfg.CapPercent, 0, 100); cfg.Token = token; cfg.Version = ConfigVersion;
            Interlocked.Exchange(ref failedTicks, 0); Volatile.Write(ref failedRun, 0);   // a new token or account is worth trying at once
            try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, JsonSerializer.Serialize(cfg)); } catch (Exception e) { log.LogWarning("Saving the DJ voice settings failed: {Kind}", e.GetType().Name); }
        }
    }

    /// <summary>Cloudflare's price of one line, in neurons: MeloTTS 18.63 per audio minute (the length guessed at 2.6 words a second), Aura-1 1,363.64 per 1,000 characters.</summary>
    public static double EstimateNeurons(string voice, string text) =>
        NormVoice(voice) is var n && IsKokoro(n) ? 0 : n == MeloTts ? DjText.WordCount(text) / 2.6 / 60.0 * 18.63 : text.Length / 1000.0 * (IsAura2(n) ? 2727.27 : 1363.64);

    string Today => clock().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    void Roll() { if (usage.Day != Today) usage = new DjUsage { Day = Today }; }

    /// <summary>Today's use (UTC day): lines made, neurons spent, and that as a percentage of the free allowance.</summary>
    public (int Lines, double Neurons, double Percent) Usage { get { lock (gate) { Roll(); return (usage.Lines, usage.Neurons, usage.Neurons / FreeNeurons * 100); } } }

    /// <summary>The most neurons the DJ may use today.</summary>
    public double Cap => FreeNeurons * Current.CapPercent / 100.0;

    /// <summary>Lines said today with a clip (the family together), and neurons used so far: what the reserve of pooled lines is worked out from.</summary>
    public (int Said, double Used) SoFar { get { lock (gate) { Roll(); return (usage.Said, usage.Neurons); } } }

    /// <summary>A line was handed out with a clip.</summary>
    public void CountSaid() { lock (gate) { Roll(); usage.Said++; try { File.WriteAllText(usageFile, JsonSerializer.Serialize(usage)); } catch (Exception) { } } }

    /// <summary>A whole line costing [cost] leaves room for the pooled lines still to come today (<see cref="DjBudget.WholeAllowed"/>).</summary>
    public bool WholeAllowed(double cost, int? localHour) { lock (gate) { Roll(); return DjBudget.WholeAllowed(Cap, usage.Neurons, usage.Said, localHour, cost); } }

    /// <summary>For the dashboard when a line was pooled to save the allowance.</summary>
    public void NoteWrite(string msg) => LastWrite = msg;

    /// <summary>[neurons] more fit under today's cap.</summary>
    public bool CanSpend(double neurons) { lock (gate) { Roll(); return usage.Neurons + neurons <= Cap; } }

    /// <summary>This line fits under today's cap.</summary>
    public bool CanAfford(string voice, string text, bool preview = false) { lock (gate) { Roll(); return usage.Neurons + EstimateNeurons(voice, text) <= Limit(preview); } }
    double Limit(bool preview) => preview ? Math.Max(Cap, FreeNeurons * PreviewShare) : Cap;

    /// <summary>Takes the line's estimate out of today's allowance up front (so lines made at the same time cannot go over the cap together); false when it does not fit.</summary>
    bool Reserve(double n, bool preview = false) { lock (gate) { Roll(); if (usage.Neurons + n > Limit(preview)) return false; usage.Neurons += n; return true; } }
    void Settle(double n, bool made)
    {
        lock (gate)
        {
            Roll();
            if (made) usage.Lines++; else usage.Neurons = Math.Max(0, usage.Neurons - n);
            try { File.WriteAllText(usageFile, JsonSerializer.Serialize(usage)); } catch (Exception) { }
        }
    }

    /// <summary>The speech-to-text model the pronunciation check listens with, and its price (neurons per audio minute).</summary>
    public const string HearModel = "@cf/openai/whisper-large-v3-turbo";
    public const double HearNeuronsPerMinute = 46.63;
    /// <summary>The share of the DJ's daily cap the pronunciation check may use; the rest waits for another day.</summary>
    public const double CheckShare = 0.10;

    /// <summary>Whisper's price for a clip, its length guessed from the MP3's size (Aura sends about 6 kB a second), a second at least.</summary>
    public static double EstimateHearNeurons(int mp3Bytes) => Math.Max(1.0, mp3Bytes / 6000.0) / 60.0 * HearNeuronsPerMinute;

    /// <summary>The most the pronunciation check may use today.</summary>
    public double CheckCap => Cap * CheckShare;

    /// <summary>Books [neurons] to today's pronunciation check, when they fit under its share and under the cap; false when they don't (the check stops for today).</summary>
    public bool TakeForChecks(double neurons)
    {
        lock (gate)
        {
            Roll();
            if (usage.Checks + neurons > CheckCap || usage.Neurons + neurons > Cap) return false;
            usage.Checks += neurons;
            try { File.WriteAllText(usageFile, JsonSerializer.Serialize(usage)); } catch (Exception) { }
            return true;
        }
    }

    /// <summary>What Whisper hears in [mp3], or null (not set up, over the cap, Cloudflare said no). Its neurons count into today's use. Never throws.</summary>
    public async Task<string?> TranscribeAsync(byte[] mp3, CancellationToken ct = default)
    {
        var c = Current; var token = Unprotect(c.Token);
        if (!AccountShape().IsMatch(c.AccountId) || token.Length == 0 || mp3.Length == 0) return null;
        var cost = EstimateHearNeurons(mp3.Length);
        if (!Reserve(cost)) return null;
        var heard = false;
        try
        {
            var body = new JsonObject { ["audio"] = Convert.ToBase64String(mp3), ["language"] = "en" };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/accounts/{c.AccountId}/ai/run/{HearModel}");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var http = hf.CreateClient("cloudflare");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var bytes = await ReadAtMost(res.Content, ct);
            if (bytes is null || !res.IsSuccessStatusCode) { log.LogInformation("DJ pronunciation check: Whisper answered {Status}{Error}", (int)res.StatusCode, bytes is null ? "" : ErrorOf(bytes)); return null; }
            heard = true;
            return JsonNode.Parse(bytes)?["result"]?["text"]?.GetValue<string>()?.Trim();
        }
        catch (Exception e) { log.LogInformation("DJ pronunciation check: Whisper failed ({Kind})", e.GetType().Name); return null; }
        finally
        {
            // a call that never reached Cloudflare costs nothing
            lock (gate) { Roll(); if (!heard) usage.Neurons = Math.Max(0, usage.Neurons - cost); try { File.WriteAllText(usageFile, JsonSerializer.Serialize(usage)); } catch (Exception) { } }
        }
    }

    /// <summary>The MP3 for [text] in [voice] (null: the saved voice), or null when it could not be made: not set up, over today's cap, or Cloudflare said no. Never throws.</summary>
    public async Task<byte[]?> SynthesizeAsync(string text, string? voice = null, CancellationToken ct = default, bool preview = false)
    {
        var c = Current; var v = NormVoice(voice ?? c.Voice);
        var token = Unprotect(c.Token);
        if (!IsKokoro(v) && (!AccountShape().IsMatch(c.AccountId) || token.Length == 0)) return Fail("Add the Cloudflare account id and API token first.");
        var cost = EstimateNeurons(v, text);
        if (!Reserve(cost, preview)) return Fail("Today's DJ share of the free Cloudflare allowance is used up (the daily cap).");
        var made = false;
        try
        {
            // a 5xx, a timeout or a dropped connection is often Cloudflare's own hiccup: the same line is asked for once more after a short pause
            for (var attempt = 1; ; attempt++)
            {
                var (audio, msg, again) = await CallAsync(c, token, v, text, ct);
                if (audio is not null)
                {
                    made = true;
                    LastResult = "Made a clip"; LastAt = clock(); Interlocked.Exchange(ref failedTicks, 0); Volatile.Write(ref failedRun, 0);
                    return audio;
                }
                if (ct.IsCancellationRequested) return Fail(msg);   // (the caller giving up is not Cloudflare failing)
                if (!again || attempt == 2) return Down(msg);
                log.LogInformation("DJ voice: {Message}; asking once more", msg);
                try { await Task.Delay(RetryPause, ct); } catch (OperationCanceledException) { return Fail(msg); }
            }
        }
        finally { Settle(cost, made); }
    }

    /// <summary>One call for a clip: the audio, or why not and whether asking again could help (a 5xx, a timeout, a dropped connection).</summary>
    async Task<(byte[]? Audio, string Why, bool Again)> CallAsync(DjVoiceConfig c, string token, string v, string text, CancellationToken ct)
    {
        if (IsKokoro(v)) return await KokoroAsync(v[7..], text, ct);
        try
        {
            var (model, body) = v == MeloTts
                ? ("@cf/myshell-ai/melotts", new JsonObject { ["prompt"] = text, ["lang"] = "en" })
                : (IsAura2(v) ? "@cf/deepgram/aura-2-en" : "@cf/deepgram/aura-1", new JsonObject { ["text"] = text, ["speaker"] = v[7..], ["encoding"] = "mp3" });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/accounts/{c.AccountId}/ai/run/{model}");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var http = hf.CreateClient("cloudflare");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (res.Content.Headers.ContentLength is > MaxAudioBytes) return (null, "Cloudflare sent more than a clip", false);
            var bytes = await ReadAtMost(res.Content, ct);
            if (bytes is null) return (null, "Cloudflare sent more than a clip", false);
            if (!res.IsSuccessStatusCode)
                return (null, res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "Cloudflare refused the token (it needs Workers AI access)"
                    : (int)res.StatusCode == 429 ? "Cloudflare says the allowance is used up or too many requests (429)"
                    : $"Cloudflare answered {(int)res.StatusCode} {res.ReasonPhrase}" + ErrorOf(bytes), (int)res.StatusCode >= 500);
            var audio = AudioOf(res.Content.Headers.ContentType?.MediaType ?? "", bytes, out var why);
            return (audio, why, false);
        }
        catch (Exception e)
        {
            var msg = e is TaskCanceledException or OperationCanceledException ? "Cloudflare didn't answer in time" : "Couldn't reach Cloudflare (" + e.GetType().Name + ")";
            return (null, msg, true);
        }
    }

    /// <summary>One clip from Kokoro on frank (ipodsync/tools/kokoro): it answers with the MP3 itself.</summary>
    async Task<(byte[]? Audio, string Why, bool Again)> KokoroAsync(string name, string text, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, kokoroUrl + "/tts");
            req.Content = new StringContent(new JsonObject { ["text"] = text, ["voice"] = name }.ToJsonString(), Encoding.UTF8, "application/json");
            using var http = hf.CreateClient("kokoro");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var bytes = await ReadAtMost(res.Content, ct);
            if (bytes is null) return (null, "The voice sent more than a clip", false);
            if (!res.IsSuccessStatusCode) return (null, $"FLACie's voice (Kokoro) answered {(int)res.StatusCode}", (int)res.StatusCode >= 500);
            return bytes.Length > 0 ? (bytes, "", false) : (null, "FLACie's voice (Kokoro) sent no sound", true);
        }
        catch (Exception e)
        {
            return (null, e is TaskCanceledException or OperationCanceledException ? "FLACie's voice (Kokoro) didn't answer in time" : "Couldn't reach FLACie's voice (Kokoro, " + e.GetType().Name + ")", true);
        }
    }

    /// <summary>The text model's price of one line: the prompt at about 4 characters a token, and the answer at its most.</summary>
    public static double EstimateTextNeurons(string system, string user, int maxTokens = TextMaxTokens, DjTextModel? model = null) =>
        (model ?? DjModels.Primary).Cost((system.Length + user.Length) / 4.0, maxTokens);

    /// <summary>What happened to the last line asked of the text model (for the dashboard).</summary>
    public string LastWrite { get; private set; } = "";
    /// <summary>The model that wrote the last line ("" before any).</summary>
    public string LastModel { get; private set; } = "";

    /// <summary>
    /// The text model's answer to [system] + [user], or null: not set up, the lines would leave no room under today's cap for [keepFor] more (the clip that says it),
    /// Cloudflare said no or did not answer before [ct] gave up. <see cref="TextModel"/> first; <see cref="FallbackModel"/> only when the first could not
    /// answer at all (an error or an unreadable answer: nothing was charged) or does not fit under the cap, never after a line that came back (one paid call a line).
    /// Its neurons count into today's use (Cloudflare's own token count when it gives one). Never throws, and a failure here never stops the clips.
    /// </summary>
    public async Task<string?> WriteAsync(string system, string user, double keepFor, CancellationToken ct = default, int maxTokens = TextMaxTokens)
    {
        var c = Current;
        var token = Unprotect(c.Token);
        if (!AccountShape().IsMatch(c.AccountId) || token.Length == 0) { LastWrite = "Not set up"; return null; }
        foreach (var model in new[] { DjModels.Primary, DjModels.Fallback })
        {
            if (ct.IsCancellationRequested) return null;
            var (text, again) = await WriteWithAsync(model, c, token, system, user, keepFor, ct, maxTokens);
            if (text is not null || !again) return text;
        }
        return null;
    }

    /// <summary>One call to [model]. [Again]: it could not answer and nothing was charged, so the next model may be asked.</summary>
    async Task<(string? Text, bool Again)> WriteWithAsync(DjTextModel model, DjVoiceConfig c, string token, string system, string user, double keepFor, CancellationToken ct, int maxTokens = TextMaxTokens)
    {
        var cost = EstimateTextNeurons(system, user, maxTokens, model);
        lock (gate) { Roll(); if (usage.Neurons + cost + keepFor > Cap) { LastWrite = "Near today's cap: a template line instead"; return (null, true); } usage.Neurons += cost; }
        var spent = 0.0;
        try
        {
            var body = new JsonObject
            {
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = user }),
                ["max_tokens"] = maxTokens, ["temperature"] = 0.8,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/accounts/{c.AccountId}/ai/run/{model.Id}");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var http = hf.CreateClient("cloudflare");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var bytes = await ReadAtMost(res.Content, ct);
            if (bytes is null || !res.IsSuccessStatusCode)
            {
                LastWrite = $"The text model answered {(int)res.StatusCode}" + (bytes is null ? "" : ErrorOf(bytes)); log.LogWarning("DJ text: {Message}", LastWrite);
                // a refused token fails every model the same way
                return (null, res.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden));
            }
            JsonNode? r;
            try { r = JsonNode.Parse(bytes)?["result"]; } catch (System.Text.Json.JsonException) { LastWrite = "The text model's answer could not be read"; return (null, true); }
            spent = cost;
            if (r?["usage"] is JsonObject u && u["prompt_tokens"] is JsonValue pi && pi.TryGetValue<int>(out var pin) && u["completion_tokens"] is JsonValue po && po.TryGetValue<int>(out var pout))
                spent = model.Cost(pin, pout);
            var text = r?["response"] is JsonValue rv && rv.TryGetValue<string>(out var s) ? s : null;
            LastWrite = text is null ? "The text model sent no line" : "Wrote a line";
            if (text is not null) LastModel = model.Id;
            return (text, false);
        }
        catch (Exception e)
        {
            // a timeout may still be charged: count the estimate, and there is no time left for another model
            spent = cost;
            LastWrite = e is TaskCanceledException or OperationCanceledException ? "The text model didn't answer in time" : "Couldn't reach the text model (" + e.GetType().Name + ")";
            log.LogWarning("DJ text: {Message}", LastWrite);
            return (null, false);
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

    /// <summary>Cloudflare was asked (and asked again where that could help) and no clip came back: only this line goes without, unless it is the
    /// <see cref="FailuresInARow"/>th in a row (see <see cref="Failing"/>).</summary>
    byte[]? Down(string msg) { Interlocked.Exchange(ref failedTicks, clock().Ticks); Interlocked.Increment(ref failedRun); return Fail(msg); }

    /// <summary>Why a new clip of [text] in [voiceName] can't be asked for right now, or null when it can: for the "line not said" log.</summary>
    public string? WhyNot(string voiceName, string text) =>
        !Ready ? "the voice is not set up"
        : Failing ? $"the voice failed {FailuresInARow} lines in a row (resting {FailureRest.TotalSeconds:0} s)"
        : !CanAfford(voiceName, text) ? "today's cap" : null;

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
