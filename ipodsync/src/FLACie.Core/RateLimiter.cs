using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace FLACie.Core;

/// <summary>
/// Who a limit counts. An address (what the server believes the client's address is: a trusted proxy's X-Forwarded-For, never a header from anyone else),
/// an account (its id, when the caller is signed in) or a device token (kept only as a hash). An IPv6 address counts by its /64 network: one home or one
/// phone is given a whole /64, and could otherwise step through billions of addresses to dodge a limit. A plain value with no allocation, so counting a
/// request costs nothing that grows with traffic.
/// </summary>
public readonly record struct RateKey(string? Name, UInt128 Address)
{
    const string TokenTag = "#token";
    static readonly ulong Seed = (ulong)Random.Shared.NextInt64();

    /// <summary>An account or any other name (null and "" are the same key).</summary>
    public static RateKey Of(string? name) => new(name ?? "", 0);

    /// <summary>A client address: IPv4 as itself (also when it arrives as ::ffff:a.b.c.d), IPv6 by its /64 network.</summary>
    public static RateKey Of(IPAddress? ip)
    {
        if (ip is null) return new(null, 0);
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        Span<byte> b = stackalloc byte[16];
        if (!ip.TryWriteBytes(b, out var n)) return new(null, 0);
        if (ip.AddressFamily == AddressFamily.InterNetwork && n == 4)
            return new(null, ((UInt128)0xFFFF << 32) | ((uint)b[0] << 24 | (uint)b[1] << 16 | (uint)b[2] << 8 | b[3]));
        ulong net = 0;
        for (var i = 0; i < 8; i++) net = net << 8 | b[i];
        return new(null, (UInt128)net << 64);
    }

    /// <summary>A device by its token (the phone app's Jellyfin token): only a keyed hash is kept, never the token itself.</summary>
    public static RateKey OfToken(string token)
    {
        var h = 14695981039346656037UL ^ Seed;   // FNV-1a over the characters, keyed by a value picked at start-up
        foreach (var c in token) { h ^= c; h *= 1099511628211UL; }
        return new(TokenTag, h);
    }
}

/// <summary>
/// So many requests a minute per key, in fixed one-minute windows: the first request of a key opens its window, and once the limit is reached every
/// further one is refused until that window ends (<c>retryAfter</c> says how long that is, for a Retry-After header). The table of keys is bounded:
/// windows that have ended are swept out once a minute, and when it is full anyway (a flood from thousands of addresses) the oldest tenth goes first,
/// so a key that is being refused right now is kept and stays refused. Thread safe; one short lock per request.
/// </summary>
public sealed class RateLimiter(int perMinute, int maxKeys = 10_000, Func<DateTimeOffset>? clock = null)
{
    static readonly long Window = TimeSpan.FromMinutes(1).Ticks;
    readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    readonly Dictionary<RateKey, (long Start, int Count)> keys = [];
    readonly object gate = new();
    long lastSweep;

    public int PerMinute => perMinute;

    /// <summary>Keys currently tracked (for tests and the admin page).</summary>
    public int KeyCount { get { lock (gate) return keys.Count; } }

    public bool TryTake(string? key) => TryTake(RateKey.Of(key), out _);

    public bool TryTake(RateKey key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        lock (gate)
        {
            var t = now().UtcTicks;
            if (keys.TryGetValue(key, out var w) && t - w.Start < Window)
            {
                if (w.Count >= perMinute) { retryAfter = TimeSpan.FromTicks(w.Start + Window - t); return false; }
                keys[key] = (w.Start, w.Count + 1);
                return true;
            }
            if (t - lastSweep >= Window || (keys.Count >= maxKeys && !keys.ContainsKey(key))) Sweep(t);
            if (keys.Count >= maxKeys && !keys.ContainsKey(key)) DropOldest(Math.Max(1, maxKeys / 10));
            keys[key] = (t, 1);
            return true;
        }
    }

    /// <summary>True when [key] has used up its current minute (a further <see cref="TryTake(RateKey, out TimeSpan)"/> would be refused). Counts nothing.</summary>
    public bool Over(RateKey key)
    {
        lock (gate) return keys.TryGetValue(key, out var w) && now().UtcTicks - w.Start < Window && w.Count >= perMinute;
    }

    void Sweep(long t)   // gate held
    {
        lastSweep = t;
        List<RateKey>? gone = null;
        foreach (var kv in keys) if (t - kv.Value.Start >= Window) (gone ??= []).Add(kv.Key);
        if (gone is not null) foreach (var k in gone) keys.Remove(k);
    }

    void DropOldest(int n)   // gate held: the n windows that started first
    {
        var starts = new long[keys.Count]; var i = 0;
        foreach (var kv in keys) starts[i++] = kv.Value.Start;
        Array.Sort(starts);
        var cut = starts[Math.Min(n, starts.Length) - 1];
        var gone = new List<RateKey>(n);
        foreach (var kv in keys) if (kv.Value.Start <= cut && gone.Count < n) gone.Add(kv.Key);
        foreach (var k in gone) keys.Remove(k);
    }
}

/// <summary>
/// The server's limits by name, each so many requests a minute (see docs/SELF-HOSTING.md, Rate limits). <c>FLACIE_RATE_&lt;NAME&gt;</c> sets one
/// (a number, or 0 / off to switch it off), <c>FLACIE_RATE_SCALE</c> multiplies every default (a test server that signs in hundreds of times), and
/// <c>FLACIE_RATE_LIMITS=off</c> switches them all off (a server only its owner can reach). The defaults are far above what a person does at full
/// speed (measured: see RateLimitTests) and low enough that a script, a runaway app or a stolen session is stopped within a minute.
/// </summary>
public sealed class RateLimits
{
    /// <summary>The limits a minute. Doors anyone can reach are counted by address; the others by account (or device, or address when neither is known).</summary>
    public static readonly IReadOnlyDictionary<string, int> Defaults = new Dictionary<string, int>
    {
        // anyone, by address
        ["signin"] = 10,        // password and NAS sign-in, starting Quick Connect
        ["qc-poll"] = 60,       // the sign-in page asks every 2.5 s whether the Quick Connect code was approved (24 a minute)
        ["public"] = 600,       // /healthz, /robots.txt, /api/ping, /licenses, the Alexa store page's policy and icons (every open tab asks /healthz each 2-3.5 s while the server is away)
        ["token-check"] = 20,   // a phone token not seen in the last 45 s is checked with Jellyfin: one call there each
        ["alexa"] = 120,        // the skill endpoint, before the request is verified
        // signed in, by account
        ["alexa-account"] = 30, // the skill endpoint, per linked Alexa account
        ["share-page"] = 30,    // opening /s/{id}
        ["share-miss"] = 10,    // share links that lead nowhere (guessing): past this, every link shows nothing for the rest of the minute
        ["share"] = 30,         // making a share link
        ["art"] = 30_000,       // covers (a fling through 10,000 songs: about 11,600 a minute)
        ["stream"] = 1_200,     // starting or seeking a song (/stream, /api/alac, Alexa's stream links)
        ["wave"] = 1_200,       // the seek bar's shape
        ["dj"] = 60,            // the DJ's lines between songs (each can cost a text-model and a voice call)
        ["dj-ask"] = 20,        // "talk to the DJ" requests
        ["search"] = 360,       // online catalog searches from the Search page (one per keystroke: typing five characters a second for a whole minute is 300)
        ["api"] = 600,          // every other /api call
    };

    readonly Dictionary<string, RateLimiter?> limiters = new(StringComparer.Ordinal);

    public RateLimits(Func<string, string?> setting, Func<DateTimeOffset>? clock = null)
    {
        var allOff = setting("FLACIE_RATE_LIMITS")?.Trim().ToLowerInvariant() is "off" or "0" or "false" or "no";
        var scale = double.TryParse(setting("FLACIE_RATE_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 1;
        foreach (var (name, def) in Defaults)
        {
            var v = setting("FLACIE_RATE_" + name.ToUpperInvariant().Replace('-', '_'))?.Trim();
            int? n = allOff || v?.ToLowerInvariant() is "off" or "0" ? null
                : int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) && x > 0 ? x
                : (int)Math.Min(int.MaxValue, Math.Ceiling(def * scale));
            limiters[name] = n is { } m ? new RateLimiter(m, clock: clock) : null;
        }
    }

    /// <summary>The limit a minute, or null when it is switched off (or there is no such limit).</summary>
    public int? PerMinute(string policy) => limiters.TryGetValue(policy, out var l) ? l?.PerMinute : null;

    /// <summary>Counts one request; false (and how long until the window opens again) when the key is over the limit. A limit that is off never refuses.</summary>
    public bool TryTake(string policy, RateKey key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        return !limiters.TryGetValue(policy, out var l) || l is null || l.TryTake(key, out retryAfter);
    }

    /// <summary>True when [key] has used up this minute of [policy] (never for a limit that is off). Counts nothing.</summary>
    public bool Over(string policy, RateKey key) => limiters.TryGetValue(policy, out var l) && l is not null && l.Over(key);

    /// <summary>Keys tracked by each limit (the admin page): a number that stays bounded however many addresses knock.</summary>
    public IReadOnlyDictionary<string, int> KeyCounts() => limiters.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!.KeyCount);
}
