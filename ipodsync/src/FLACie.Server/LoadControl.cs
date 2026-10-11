using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace FLACie.Server;

/// <summary>Whether this server is serving music right now: an open /stream request, or a player tab that reported its time in the last 30 seconds.
/// Background jobs wait while it is true, so they never compete with a song that is starting or playing.</summary>
public sealed class PlaybackActivity
{
    public static PlaybackActivity? Current { get; private set; }
    public static readonly TimeSpan Recent = TimeSpan.FromSeconds(30);

    int streams;
    long lastBeat;   // ticks of the last heartbeat; 0 = never
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    public PlaybackActivity() { Current = this; }

    public int OpenStreams => Volatile.Read(ref streams);
    /// <summary>A player tab says it is playing (called with every time update it reports).</summary>
    public void Heartbeat() => Interlocked.Exchange(ref lastBeat, Clock().Ticks);

    /// <summary>An audio request has started; dispose the result when it ends (or is abandoned).</summary>
    public IDisposable BeginStream()
    {
        Interlocked.Increment(ref streams);
        Heartbeat();
        return new End(this);
    }
    sealed class End(PlaybackActivity owner) : IDisposable
    {
        int done;
        public void Dispose() { if (Interlocked.Exchange(ref done, 1) == 0) { Interlocked.Decrement(ref owner.streams); owner.Heartbeat(); } }
    }

    public bool Busy
    {
        get
        {
            if (OpenStreams > 0) return true;
            var b = Interlocked.Read(ref lastBeat);
            return b != 0 && Clock() - new DateTime(b, DateTimeKind.Utc) < Recent;
        }
    }

    /// <summary>Returns when nothing is being served (checks every <paramref name="poll"/>).</summary>
    public async Task WaitIdleAsync(TimeSpan poll, CancellationToken ct)
    {
        while (Busy) await Task.Delay(poll, ct);
    }
}

/// <summary>How long a song whose real format could not be read is left alone before it is tried again: 1 hour, 6 hours, 24 hours, then every 7 days.</summary>
public static class ProbeBackoff
{
    static readonly TimeSpan[] Steps = [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromDays(7)];
    /// <summary>The wait after the <paramref name="failures"/>th failed read in a row (1 = the first).</summary>
    public static TimeSpan Delay(int failures) => Steps[Math.Clamp(failures, 1, Steps.Length) - 1];
}

/// <summary>Remembers who a Jellyfin token belongs to for a short while, so a burst of API calls costs Jellyfin one /Users/Me, not one each.
/// Only successes are kept. A token Jellyfin has revoked therefore keeps working here for at most <see cref="Window"/> unless it is dropped
/// at once: sign-out and any 401 Jellyfin gives for it call <see cref="Invalidate"/>.</summary>
public sealed class TokenCache
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(45);
    public sealed record Who(string UserId, string Name);
    readonly ConcurrentDictionary<string, (Who Who, DateTime Until, string Token)> entries = new();
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    static string Key(string server, string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server + "|" + token)));

    public Who? Get(string server, string token)
    {
        if (!entries.TryGetValue(Key(server, token), out var e)) return null;
        if (Clock() < e.Until) return e.Who;
        entries.TryRemove(Key(server, token), out _);
        return null;
    }

    public void Put(string server, string token, Who who)
    {
        if (entries.Count > 500) { var now = Clock(); foreach (var kv in entries) if (kv.Value.Until <= now) entries.TryRemove(kv.Key, out _); }
        entries[Key(server, token)] = (who, Clock() + Window, token);
    }

    /// <summary>Forgets a token on every server (sign-out, or Jellyfin refused it).</summary>
    public void Invalidate(string token)
    {
        foreach (var kv in entries) if (kv.Value.Token == token) entries.TryRemove(kv.Key, out _);
    }
}

/// <summary>Several callers asking for the same thing at once share one lookup (a page of tiles asks for the same cover many times).
/// The lookup runs on its own, so one caller giving up does not cancel it for the others.</summary>
public sealed class SingleFlight<T>
{
    readonly ConcurrentDictionary<string, Lazy<Task<T>>> running = new();
    int started;
    /// <summary>How many lookups really ran (callers that shared one count once).</summary>
    public int Started => Volatile.Read(ref started);

    public Task<T> RunAsync(string key, Func<Task<T>> work, CancellationToken caller = default)
    {
        var lazy = running.GetOrAdd(key, k => new Lazy<Task<T>>(() =>
        {
            Interlocked.Increment(ref started);
            return Task.Run(async () => { try { return await work(); } finally { running.TryRemove(key, out _); } });
        }));
        return lazy.Value.WaitAsync(caller);
    }
}
