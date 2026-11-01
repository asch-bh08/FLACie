using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace FLACie.Server;

/// <summary>
/// Which hosts may tell this server who the browser really is (X-Forwarded-For / -Proto / -Host).
/// FLACIE_TRUSTED_PROXIES is a comma separated list of addresses or CIDR ranges ("10.0.0.5, 172.18.0.0/16"); "*" trusts every sender (only for a server nobody can reach except through the proxy).
/// Not set: loopback and private networks (10/8, 172.16/12, 192.168/16, Tailscale 100.64/10, IPv6 fc00::/7), never an address on the public internet,
/// so a stranger who reaches the port directly cannot make the server believe a different client address or scheme.
/// </summary>
public static class ProxyTrust
{
    static readonly string[] PrivateRanges = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10", "fc00::/7", "fe80::/10"];
    static List<(IPAddress Net, int Bits)> ranges = [];
    static bool any;

    public static void Configure(ForwardedHeadersOptions o, IConfiguration conf)
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        o.KnownNetworks.Clear(); o.KnownProxies.Clear();
        var setting = conf["FLACIE_TRUSTED_PROXIES"]?.Trim() ?? "";
        if (setting == "*") { any = true; o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Any, 0)); o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.IPv6Any, 0)); return; }
        var parts = setting.Length > 0 ? setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : PrivateRanges;
        ranges = [];
        foreach (var p in parts)
        {
            var bits = -1; var addr = p;
            if (p.Contains('/')) { var i = p.IndexOf('/'); addr = p[..i]; if (!int.TryParse(p[(i + 1)..], out bits)) continue; }
            if (!IPAddress.TryParse(addr, out var ip)) continue;
            if (bits < 0) bits = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            ranges.Add((ip, bits));
            o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(ip, bits));
        }
    }

    /// <summary>The sender of this connection is a configured proxy.</summary>
    public static bool IsTrusted(IPAddress? ip)
    {
        if (any) return true;
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        foreach (var (net, bits) in ranges) if (InRange(ip, net, bits)) return true;
        return false;
    }

    static bool InRange(IPAddress ip, IPAddress net, int bits)
    {
        if (net.IsIPv4MappedToIPv6) net = net.MapToIPv4();
        if (ip.AddressFamily != net.AddressFamily) return false;
        var a = ip.GetAddressBytes(); var b = net.GetAddressBytes();
        for (var i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (0xFF << (8 - bits)) & 0xFF;
            if ((a[i] & mask) != (b[i] & mask)) return false;
        }
        return true;
    }
}

/// <summary>
/// The headers on every answer. A self-hosted music server has no business in search results (X-Robots-Tag, and robots.txt for crawlers that read it);
/// no other site may frame it (clickjacking); nothing is sniffed into a script; the referrer never leaves the site. The Content-Security-Policy keeps
/// 'unsafe-inline' scripts because App.razor starts Blazor (and sets the theme) from inline scripts, so it is not a cure for script injection; what it
/// does shut out is plugins, a foreign &lt;base&gt;, forms posting to another site, and being framed. Covers may come from the music catalogs (https:)
/// and pages draw some pictures themselves (data:, blob:); audio comes from this server; Blazor's live connection is a WebSocket (ws:/wss:);
/// 'wasm-unsafe-eval' lets the Retro WebAssembly start. All of it is one constant string: nothing is built per request.
/// </summary>
public static class SecurityHeaders
{
    public const string Csp = "default-src 'self'; script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https:; "
        + "media-src 'self' blob: data:; font-src 'self' data:; connect-src 'self' ws: wss:; worker-src 'self' blob:; manifest-src 'self'; "
        + "frame-ancestors 'self'; object-src 'none'; base-uri 'self'; form-action 'self'";

    public const string RobotsTxt = "User-agent: *\nDisallow: /\n";

    public static void Apply(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "same-origin";
        h["X-Frame-Options"] = "SAMEORIGIN";
        h["X-Robots-Tag"] = "noindex, nofollow";
        h.ContentSecurityPolicy = Csp;
        // the web player's mic button ("talk to the DJ") uses the browser's speech recognition, which needs the microphone of the page itself
        h["Permissions-Policy"] = "camera=(), microphone=(self), geolocation=()";
        if (ctx.Request.IsHttps) h.StrictTransportSecurity = "max-age=15552000";
    }
}

/// <summary>
/// Slows down password guessing at the sign-in page, without ever letting a stranger lock the real person out for good. Four rules, kept in memory (a restart clears them):
/// <list type="bullet">
/// <item>One address and one name: after <see cref="MaxPerName"/> wrong passwords within <see cref="PairWindow"/> that name is refused from that address until
/// the window passes (short, and only there: the same name from anywhere else is not touched).</item>
/// <item>One address and many names: after <see cref="MaxPerAddress"/> failures within <see cref="Window"/> the address is refused (someone trying a list of names).</item>
/// <item>One name from many addresses (credential stuffing): never refused, but each further try at that name waits longer, 1, 2, 4, 8, then 16 seconds
/// (<see cref="Delay"/>), from any address.</item>
/// <item>A known device: a browser that has signed in as that name before carries a signed cookie (<see cref="SignInDevices"/>). Its tries at that name skip
/// all of the above and count only against the device itself (<see cref="MaxPerName"/> within <see cref="PairWindow"/>), so however much someone else
/// guesses, the person on their own phone or laptop signs in at once (OWASP's "device cookies").</item>
/// </list>
/// The table of counters is bounded (<see cref="MaxKeys"/>): the oldest counters go first when a flood of addresses fills it.
/// </summary>
public sealed class LoginThrottle(Func<DateTime>? clock = null)
{
    public const int MaxPerName = 8, MaxPerAddress = 30, MaxKeys = 20_000;
    public static readonly TimeSpan PairWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    readonly ConcurrentDictionary<string, Queue<DateTime>> failures = new();

    static string Name(string user) => user.Trim().ToLowerInvariant();
    static string AddressKey(IPAddress? ip) => "ip:" + (ip?.ToString() ?? "?");
    static string PairKey(IPAddress? ip, string user) => "p:" + (ip?.ToString() ?? "?") + "|" + Name(user);
    static string UserKey(string user) => "u:" + Name(user);
    static string DeviceKey(string device) => "d:" + device;
    static TimeSpan WindowOf(string key) => key[0] is 'p' or 'd' ? PairWindow : Window;

    /// <summary>Counters held (for tests): bounded by <see cref="MaxKeys"/>.</summary>
    public int KeyCount => failures.Count;

    /// <summary>How long until this try is allowed (null: allowed now). [device]: the known-device id of the browser, when its cookie names this user.</summary>
    public TimeSpan? Blocked(IPAddress? ip, string? user = null, string? device = null)
    {
        if (device is { Length: > 0 }) return Wait(DeviceKey(device), MaxPerName);
        return Wait(AddressKey(ip), MaxPerAddress) ?? (user is { Length: > 0 } ? Wait(PairKey(ip, user), MaxPerName) : null);
    }

    public TimeSpan? Blocked(HttpContext ctx, string? user = null, string? device = null) => Blocked(ctx.Connection.RemoteIpAddress, user, device);

    /// <summary>How long to hold back a try at this name: nothing for the first few wrong guesses, then 1, 2, 4, 8, at most 16 seconds. Never a refusal, and
    /// nothing at all for a known device.</summary>
    public TimeSpan Delay(string? user, string? device = null)
    {
        if (user is not { Length: > 0 } || device is { Length: > 0 }) return TimeSpan.Zero;
        var n = Count(UserKey(user)) - 4;
        return n <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(16, 1 << Math.Min(n - 1, 4)));
    }

    public void Fail(IPAddress? ip, string? user = null, string? device = null)
    {
        if (device is { Length: > 0 }) { Add(DeviceKey(device)); return; }   // the person's own device mistyping: it slows nobody else
        Add(AddressKey(ip));
        if (user is { Length: > 0 }) { Add(PairKey(ip, user)); Add(UserKey(user)); }
    }

    public void Fail(HttpContext ctx, string? user = null, string? device = null) => Fail(ctx.Connection.RemoteIpAddress, user, device);

    /// <summary>A good sign-in clears the name's counters and this address's for that name (not the address's own, which may be shared).</summary>
    public void Succeed(IPAddress? ip, string? user)
    {
        if (user is not { Length: > 0 }) return;
        failures.TryRemove(UserKey(user), out _);
        failures.TryRemove(PairKey(ip, user), out _);
    }

    public void Succeed(HttpContext ctx, string? user) => Succeed(ctx.Connection.RemoteIpAddress, user);

    TimeSpan? Wait(string key, int max)
    {
        if (!failures.TryGetValue(key, out var q)) return null;
        lock (q)
        {
            Trim(q, key);
            if (q.Count < max) return null;
            // refused until the oldest failure that still counts has left the window
            var ends = q.ElementAt(q.Count - max) + WindowOf(key);
            return ends - now() is var w && w > TimeSpan.Zero ? w : TimeSpan.FromSeconds(1);
        }
    }

    int Count(string key)
    {
        if (!failures.TryGetValue(key, out var q)) return 0;
        lock (q) { Trim(q, key); return q.Count; }
    }

    void Add(string key)
    {
        var q = failures.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (q) { Trim(q, key); q.Enqueue(now()); if (q.Count > 64) q.Dequeue(); }   // (no rule needs more than the last 64)
        if (failures.Count > MaxKeys) Prune();
    }

    void Prune()
    {
        lock (failures)
        {
            if (failures.Count <= MaxKeys) return;
            foreach (var kv in failures) lock (kv.Value) { Trim(kv.Value, kv.Key); if (kv.Value.Count == 0) failures.TryRemove(kv.Key, out _); }
            if (failures.Count <= MaxKeys * 9 / 10) return;
            // a flood of addresses within the window: the counters whose last failure is oldest go first, down to nine tenths of the table
            var oldest = failures.Select(kv => { lock (kv.Value) return (kv.Key, Last: kv.Value.Count > 0 ? kv.Value.Last() : DateTime.MinValue); })
                .OrderBy(x => x.Last).Take(failures.Count - MaxKeys * 9 / 10).Select(x => x.Key).ToList();
            foreach (var k in oldest) failures.TryRemove(k, out _);
        }
    }

    void Trim(Queue<DateTime> q, string key) { var cut = now() - WindowOf(key); while (q.Count > 0 && q.Peek() < cut) q.Dequeue(); }
}

/// <summary>
/// The known-device cookie (<see cref="LoginThrottle"/>): after a good sign-in the browser gets a cookie, signed and encrypted with the server's keys, that
/// names the account (as a hash) and a random id for this browser. It is only sent to /auth, is HTTP-only, and proves nothing on its own: it only lets that
/// browser's tries at that one name skip the slow-downs other people's guessing causes. A cookie copied to another name, edited or made up reads as none.
/// </summary>
public sealed class SignInDevices(IDataProtectionProvider dp)
{
    public const string Cookie = "flacie.device";
    readonly IDataProtector protector = dp.CreateProtector("FLACie.SignInDevice.v1");

    static string NameHash(string user) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("flacie-device|" + user.Trim().ToLowerInvariant())))[..32];

    public string Issue(string user) => protector.Protect(NameHash(user) + "|" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    /// <summary>This browser's device id when [cookie] is one this server made for [user], else null.</summary>
    public string? Read(string? cookie, string? user)
    {
        if (cookie is not { Length: > 0 } || user is not { Length: > 0 } || cookie.Length > 1024) return null;
        try { var s = protector.Unprotect(cookie); var bar = s.IndexOf('|'); return bar > 0 && s[..bar] == NameHash(user) ? s[(bar + 1)..] : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Hands the browser its cookie after a good sign-in as [user].</summary>
    public void Remember(HttpContext ctx, string user) =>
        ctx.Response.Cookies.Append(Cookie, Issue(user), new CookieOptions { HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/auth", MaxAge = TimeSpan.FromDays(365), IsEssential = true });

    /// <summary>The device id this request's cookie gives for [user], or null.</summary>
    public string? Of(HttpContext ctx, string? user) => Read(ctx.Request.Cookies[Cookie], user);
}

