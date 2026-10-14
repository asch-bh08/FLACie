using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FLACie.Core;
using Microsoft.AspNetCore.DataProtection;

namespace FLACie.Server.Alexa;

/// <summary>Server-wide Alexa settings. Off by default; kept in alexa.json in the data folder.</summary>
public sealed class AlexaConfig
{
    public bool Enabled { get; set; }
    /// <summary>Alexa may ask FLACie to search and download songs that are not in the library.</summary>
    public bool Downloads { get; set; } = true;
    /// <summary>Skill ids (amzn1.ask.skill...) whose requests are accepted, comma separated. Empty = every request is refused.</summary>
    public string SkillIds { get; set; } = "";
}

/// <summary>One Alexa account allowed to use one FLACie user's library. The Jellyfin sign-in is kept encrypted on this server and never leaves it.</summary>
public sealed class AlexaLink
{
    /// <summary>Hash of the Alexa userId (the lookup key).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Hash of the FLACie account (server and user) behind the link, so a person can see and revoke their own links.</summary>
    public string Owner { get; set; } = "";
    public string Secret { get; set; } = "";   // protected JSON: the account behind the link, and the raw Alexa userId
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; }
    /// <summary>The person allowed "send me notifications" on the Alexa app.</summary>
    public bool CanNotify { get; set; }
    public string Locale { get; set; } = "";
}

sealed record LinkSecret(string AlexaUserId, string Server, string UserId, string UserName, string Token);

public sealed record PairingCode(string Code, string Name, JellyfinAccount Account, DateTime Expires);

/// <summary>
/// Who may talk to FLACie through Alexa. An Alexa account is linked with a one-time pairing code that a signed-in person creates in Settings and then says to the skill.
/// Allowlist only: anything not linked is refused. Revoking removes the stored sign-in.
/// </summary>
public sealed class AlexaStore
{
    public static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(10);
    readonly string file; readonly IDataProtector protector; readonly object gate = new();
    readonly Dictionary<string, PairingCode> codes = [];
    readonly Dictionary<string, (int Count, DateTime At)> failures = [];
    static readonly TimeSpan LockLife = TimeSpan.FromMinutes(10);
    readonly Func<DateTime> clock;
    AlexaConfig cfg = new();
    List<AlexaLink> links = [];

    public AlexaStore(DataPaths paths, IDataProtectionProvider dp, IConfiguration conf, Func<DateTime>? clock = null)
    {
        file = Path.Combine(paths.Root, "alexa.json"); protector = dp.CreateProtector("FLACie.Alexa"); this.clock = clock ?? (() => DateTime.UtcNow);
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<State>(File.ReadAllText(file)) is { } s) { cfg = s.Config ?? new(); links = s.Links ?? []; }
        }
        catch (Exception) { }
        if (cfg.SkillIds.Length == 0 && conf["FLACIE_ALEXA_SKILL_IDS"] is { Length: > 0 } ids) cfg.SkillIds = ids;
    }

    sealed class State { public AlexaConfig? Config { get; set; } public List<AlexaLink>? Links { get; set; } }

    public AlexaConfig Config { get { lock (gate) return new() { Enabled = cfg.Enabled, Downloads = cfg.Downloads, SkillIds = cfg.SkillIds }; } }
    public void Update(Action<AlexaConfig> change) { lock (gate) { change(cfg); Save(); } }
    /// <summary>The links without their secrets, for the Settings page.</summary>
    public IReadOnlyList<AlexaLink> Links { get { lock (gate) return links.Select(l => new AlexaLink { Id = l.Id, Name = l.Name, Owner = l.Owner, LinkedAt = l.LinkedAt, LastSeen = l.LastSeen, CanNotify = l.CanNotify, Locale = l.Locale }).ToList(); } }

    void Save()
    {
        try
        {
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new State { Config = cfg, Links = links }));
            File.Move(tmp, file, true);
        }
        catch (Exception) { }
    }

    public static string OwnerOf(JellyfinAccount a) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("flacie-owner|" + a.Server + "|" + a.UserId)))[..16];

    public static string HashUser(string alexaUserId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("flacie-alexa|" + alexaUserId)))[..32];

    // ---- pairing ----

    /// <summary>A fresh six digit code for this signed-in person; saying it to the skill links that Alexa account. An earlier code of the same person is dropped.</summary>
    public PairingCode NewCode(JellyfinAccount account)
    {
        lock (gate)
        {
            Prune();
            foreach (var old in codes.Values.Where(c => c.Account.UserId == account.UserId && c.Account.Server == account.Server).ToList()) codes.Remove(old.Code);
            string code;
            do code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"); while (codes.ContainsKey(code));
            var pc = new PairingCode(code, account.UserName, account, clock() + CodeLife);
            codes[code] = pc;
            return pc;
        }
    }

    void Prune() { foreach (var k in codes.Where(c => c.Value.Expires <= clock()).Select(c => c.Key).ToList()) codes.Remove(k); }

    /// <summary>The Alexa account says a code. Wrong guesses are counted per Alexa account (5 in a row lock it out: six digits are not guessable at that rate).</summary>
    public AlexaLink? Redeem(string alexaUserId, string spokenCode, string locale)
    {
        var digits = new string((spokenCode ?? "").Where(char.IsDigit).ToArray());
        var id = HashUser(alexaUserId);
        lock (gate)
        {
            Prune();
            if (failures.TryGetValue(id, out var f) && clock() - f.At > LockLife) failures.Remove(id);   // the lock-out ends after 10 minutes: a few typos must not shut the owner out for good
            if (failures.TryGetValue(id, out f) && f.Count >= 5) return null;
            if (!codes.Remove(digits, out var pc)) { failures[id] = (failures.GetValueOrDefault(id).Count + 1, clock()); if (failures.Count > 500) failures.Clear(); return null; }
            failures.Remove(id);
            links.RemoveAll(l => l.Id == id);
            var link = new AlexaLink { Id = id, Name = pc.Name, Owner = OwnerOf(pc.Account), LinkedAt = clock(), LastSeen = clock(), Locale = locale, Secret = protector.Protect(JsonSerializer.Serialize(new LinkSecret(alexaUserId, pc.Account.Server, pc.Account.UserId, pc.Account.UserName, pc.Account.Token))) };
            links.Add(link);
            Save();
            return link;
        }
    }

    public bool IsLinked(string alexaUserId) { var id = HashUser(alexaUserId); lock (gate) return links.Any(l => l.Id == id); }

    /// <summary>The account behind a link (null when not linked or unreadable).</summary>
    public JellyfinAccount? AccountFor(string alexaUserId)
    {
        var id = HashUser(alexaUserId);
        lock (gate)
        {
            var l = links.FirstOrDefault(x => x.Id == id); if (l is null) return null;
            try
            {
                var s = JsonSerializer.Deserialize<LinkSecret>(protector.Unprotect(l.Secret))!;
                l.LastSeen = clock();
                return new JellyfinAccount(s.Server, s.UserId, s.UserName, s.Token);
            }
            catch (Exception) { return null; }
        }
    }

    public void Touch(string alexaUserId, string locale, bool? canNotify = null)
    {
        var id = HashUser(alexaUserId);
        lock (gate) { if (links.FirstOrDefault(x => x.Id == id) is { } l) { l.LastSeen = clock(); if (locale.Length > 0) l.Locale = locale; if (canNotify is { } c && c != l.CanNotify) { l.CanNotify = c; Save(); } } }
    }

    /// <summary>Raw Alexa user ids of the links that belong to this FLACie account (for notifications).</summary>
    public IReadOnlyList<(string AlexaUserId, bool CanNotify)> UsersOf(string server, string userId)
    {
        lock (gate)
        {
            var res = new List<(string, bool)>();
            foreach (var l in links)
                try { var s = JsonSerializer.Deserialize<LinkSecret>(protector.Unprotect(l.Secret))!; if (s.UserId == userId && s.Server == server) res.Add((s.AlexaUserId, l.CanNotify)); } catch (Exception) { }
            return res;
        }
    }

    /// <summary>The account behind a link id (what a signed stream URL names), null when revoked.</summary>
    public JellyfinAccount? AccountForLink(string linkId)
    {
        lock (gate)
        {
            var l = links.FirstOrDefault(x => x.Id == linkId); if (l is null) return null;
            try { var s = JsonSerializer.Deserialize<LinkSecret>(protector.Unprotect(l.Secret))!; return new JellyfinAccount(s.Server, s.UserId, s.UserName, s.Token); } catch (Exception) { return null; }
        }
    }

    /// <summary>The link whose id hashes to this short value (the u= of a signed URL).</summary>
    public string? LinkIdByUrlHash(string u) { lock (gate) return links.FirstOrDefault(l => AlexaUrlSigner.UserHash(l.Id) == u)?.Id; }

    public bool IsNotifiable(string alexaUserId) { var id = HashUser(alexaUserId); lock (gate) return links.FirstOrDefault(l => l.Id == id)?.CanNotify == true; }

    /// <summary>A person revokes their own link (owner must match); null owner = an administrator, any link.</summary>
    public bool Revoke(string linkId, string? owner) { lock (gate) { var n = links.RemoveAll(l => l.Id == linkId && (owner is null || l.Owner == owner)); if (n > 0) Save(); return n > 0; } }

    public bool Revoke(string linkId) { lock (gate) { var n = links.RemoveAll(l => l.Id == linkId); if (n > 0) Save(); return n > 0; } }
    public int RevokeAll() { lock (gate) { var n = links.Count; links.Clear(); Save(); return n; } }
}
