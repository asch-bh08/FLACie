using System.Collections.Concurrent;
using System.Net;
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
/// Slows down password guessing: too many failed sign-ins from one address (or against one account name) within the window are refused until it passes.
/// Kept in memory, so a restart clears it; that is enough to make guessing impractical without locking out the real owner for long.
/// </summary>
public sealed class LoginThrottle
{
    public const int MaxFailures = 8;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    readonly ConcurrentDictionary<string, Queue<DateTime>> failures = new();

    static string Ip(HttpContext ctx) => "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "?");
    static string User(string name) => "u:" + name.Trim().ToLowerInvariant();

    /// <summary>True when this address, or this account name, has failed too often just now.</summary>
    public bool Blocked(HttpContext ctx, string? user = null) => Count(Ip(ctx)) >= MaxFailures || (user is { Length: > 0 } && Count(User(user)) >= MaxFailures);

    public void Fail(HttpContext ctx, string? user = null)
    {
        Add(Ip(ctx));
        if (user is { Length: > 0 }) Add(User(user));
    }

    /// <summary>A good sign-in clears the account's counter (not the address's, which may be shared).</summary>
    public void Succeed(string? user) { if (user is { Length: > 0 }) failures.TryRemove(User(user), out _); }

    int Count(string key)
    {
        if (!failures.TryGetValue(key, out var q)) return 0;
        lock (q) { Trim(q); return q.Count; }
    }

    void Add(string key)
    {
        var q = failures.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (q) { Trim(q); q.Enqueue(DateTime.UtcNow); }
        if (failures.Count > 5000) foreach (var kv in failures) lock (kv.Value) { Trim(kv.Value); if (kv.Value.Count == 0) failures.TryRemove(kv.Key, out _); }
    }

    static void Trim(Queue<DateTime> q) { var cut = DateTime.UtcNow - Window; while (q.Count > 0 && q.Peek() < cut) q.Dequeue(); }
}
