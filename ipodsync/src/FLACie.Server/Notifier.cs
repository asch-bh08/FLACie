using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FLACie.Core;
using Microsoft.AspNetCore.DataProtection;

namespace FLACie.Server;

/// <summary>What the server tells the admin about, and where (an ntfy server and topic). Kept in the server's data folder; the access token is stored encrypted.</summary>
public sealed class NotifyConfig
{
    public string Url { get; set; } = "";
    public string Topic { get; set; } = "";
    public string Token { get; set; } = "";       // protected; empty when none
    public bool Enabled { get; set; }
    public bool Imports { get; set; } = true;     // a playlist import finished
    public bool Charts { get; set; } = true;      // the daily charts run
    public bool LowSpace { get; set; } = true;    // downloads stopped for lack of space
    public bool SignIns { get; set; } = true;     // someone signed in
    public bool Started { get; set; } = true;     // the server started
}

/// <summary>Sends notifications to an ntfy server (https://ntfy.sh/ or your own). Nothing is sent unless an admin has set it up and turned it on.</summary>
public sealed class Notifier
{
    readonly string file; readonly IHttpClientFactory hf; readonly IDataProtector protector; readonly ILogger<Notifier> log; readonly ActivityLog activity;
    readonly object gate = new();
    NotifyConfig cfg = new();
    public string LastResult { get; private set; } = "";
    public DateTime? LastAt { get; private set; }

    public Notifier(DataPaths paths, IHttpClientFactory hf, IDataProtectionProvider dp, IConfiguration conf, ILogger<Notifier> log, ActivityLog activity)
    {
        file = Path.Combine(paths.Root, "notify.json"); this.hf = hf; protector = dp.CreateProtector("FLACie.Notify"); this.log = log; this.activity = activity;
        try { if (File.Exists(file)) cfg = JsonSerializer.Deserialize<NotifyConfig>(File.ReadAllText(file)) ?? new(); } catch (Exception) { }
        // first start: take the address from the environment, so a Docker setup can hand it over
        if (cfg.Url.Length == 0 && conf["FLACIE_NTFY_URL"] is { Length: > 0 } u)
        {
            cfg.Url = u; cfg.Topic = conf["FLACIE_NTFY_TOPIC"] ?? ""; cfg.Enabled = cfg.Topic.Length > 0;
            if (conf["FLACIE_NTFY_TOKEN"] is { Length: > 0 } t) cfg.Token = Protect(t);
        }
    }

    string Protect(string s) { try { return protector.Protect(s); } catch (Exception) { return ""; } }
    string Unprotect(string s) { try { return s.Length == 0 ? "" : protector.Unprotect(s); } catch (Exception) { return ""; } }

    public NotifyConfig Current { get { lock (gate) return JsonSerializer.Deserialize<NotifyConfig>(JsonSerializer.Serialize(cfg))!; } }
    public bool HasToken => Current.Token.Length > 0;

    /// <summary>Saves the settings. A null [newToken] keeps the saved one; an empty one removes it.</summary>
    public void Save(NotifyConfig c, string? newToken)
    {
        lock (gate)
        {
            var token = newToken is null ? cfg.Token : newToken.Length == 0 ? "" : Protect(newToken.Trim());
            cfg = JsonSerializer.Deserialize<NotifyConfig>(JsonSerializer.Serialize(c))!;
            cfg.Url = cfg.Url.Trim().TrimEnd('/'); cfg.Topic = cfg.Topic.Trim().Trim('/'); cfg.Token = token;
            try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, JsonSerializer.Serialize(cfg)); } catch (Exception e) { log.LogWarning(e, "Saving notification settings failed"); }
        }
    }

    /// <summary>Sends if this kind of notification is switched on. Never throws.</summary>
    public Task NotifyAsync(string kind, string title, string message, int priority = 3, string? tags = null)
    {
        var c = Current;
        var on = kind switch { "imports" => c.Imports, "charts" => c.Charts, "space" => c.LowSpace, "signin" => c.SignIns, "started" => c.Started, _ => true };
        return c.Enabled && on ? Task.Run(() => SendAsync(title, message, priority, tags)) : Task.CompletedTask;
    }

    /// <summary>Sends one message now, whatever the toggles say (the test button). Returns whether the server accepted it and what it said.</summary>
    public async Task<(bool Ok, string Message)> SendAsync(string title, string message, int priority = 3, string? tags = null)
    {
        var c = Current;
        if (c.Url.Length == 0 || c.Topic.Length == 0) return (false, "Add the ntfy address and a topic first.");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, c.Url);
            var body = new JsonObject { ["topic"] = c.Topic, ["title"] = title, ["message"] = message, ["priority"] = Math.Clamp(priority, 1, 5) };
            if (!string.IsNullOrWhiteSpace(tags)) body["tags"] = new JsonArray(tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => (JsonNode)t!).ToArray());
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            if (Unprotect(c.Token) is { Length: > 0 } tok) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + tok);
            using var http = hf.CreateClient("catalog");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var res = await http.SendAsync(req, cts.Token);
            var ok = res.IsSuccessStatusCode;
            var msg = ok ? "Sent" : $"ntfy answered {(int)res.StatusCode} {res.ReasonPhrase}";
            LastResult = msg; LastAt = DateTime.UtcNow;
            if (!ok) activity.Add("alert", "", "Notification not sent: " + msg);
            return (ok, msg);
        }
        catch (Exception e)
        {
            var msg = e is TaskCanceledException ? "ntfy didn't answer in time" : e.Message;
            LastResult = msg; LastAt = DateTime.UtcNow;
            log.LogWarning("ntfy send failed: {Message}", msg);
            activity.Add("alert", "", "Notification not sent: " + msg);
            return (false, msg);
        }
    }
}
