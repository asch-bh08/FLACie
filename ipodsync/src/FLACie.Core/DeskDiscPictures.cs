using System.Collections.Concurrent;

namespace FLACie.Core;

/// <summary>A release's own disc picture, as the server serves it.</summary>
public sealed record DiscPicture(byte[] Bytes, string Mime);

/// <summary>The iPod desk's disc pictures: the release's own "Medium" picture from the Cover Art Archive, for a release a person matched on MusicBrainz.
/// The server fetches it (the browser only ever asks FLACie) and keeps it for everyone: one lookup per release however many viewers ask at once; a release
/// with no disc picture is asked about again after a day, a failed lookup after ten minutes; the newest <c>keep</c> releases are kept in memory.</summary>
public sealed class DeskDiscPictures(
    Func<string, string?, CancellationToken, Task<IReadOnlyList<CaaImage>>> list, Func<string, CancellationToken, Task<byte[]?>> fetch, TimeProvider? clock = null, int keep = 64)
{
    public DeskDiscPictures(CoverArtArchiveClient caa) : this(caa.ListAsync, caa.FetchAsync) { }

    sealed class Entry
    {
        public Lazy<Task<DiscPicture?>> Picture = null!;
        public long UntilTicks = DateTimeOffset.MaxValue.UtcTicks;   // when the answer is asked for again (set when the lookup ends)
    }
    readonly ConcurrentDictionary<string, Entry> known = new();
    readonly ConcurrentQueue<string> age = new();
    readonly TimeProvider time = clock ?? TimeProvider.System;
    static readonly TimeSpan NoneFor = TimeSpan.FromDays(1), FailedFor = TimeSpan.FromMinutes(10), LookupLimit = TimeSpan.FromSeconds(30);

    /// <summary>A MusicBrainz id (the only thing that is ever looked up).</summary>
    public static bool ValidId(string? id) => Guid.TryParse(id, out _);

    /// <summary>The release's disc picture, or null when it has none (or the lookup failed). A caller giving up does not stop the lookup for the others.</summary>
    public Task<DiscPicture?> GetAsync(string release, string? releaseGroup, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcTicks;
        var e = known.AddOrUpdate(release, _ => New(release, releaseGroup),
            (_, old) => old.Picture.IsValueCreated && old.Picture.Value.IsCompleted && now >= Interlocked.Read(ref old.UntilTicks) ? New(release, releaseGroup) : old);
        return e.Picture.Value.WaitAsync(ct);   // (only the entry that was kept is ever started: one lookup)
    }

    Entry New(string release, string? releaseGroup)
    {
        age.Enqueue(release);
        while (age.Count > keep && age.TryDequeue(out var old)) if (old != release) known.TryRemove(old, out _);
        var e = new Entry();
        e.Picture = new Lazy<Task<DiscPicture?>>(() => Task.Run(() => Lookup(release, releaseGroup, e)));
        return e;
    }

    async Task<DiscPicture?> Lookup(string release, string? releaseGroup, Entry e)
    {
        void Hold(TimeSpan span) => Interlocked.Exchange(ref e.UntilTicks, (time.GetUtcNow() + span).UtcTicks);
        using var limit = new CancellationTokenSource(LookupLimit);
        try
        {
            var images = await list(release, releaseGroup, limit.Token);
            if (images.FirstOrDefault(i => i.Types.Contains("Medium", StringComparer.OrdinalIgnoreCase)) is not { } disc) { Hold(NoneFor); return null; }
            if (await fetch(disc.Full, limit.Token) is not { } bytes || CoverArtArchiveClient.Sniff(bytes) is not { } mime) { Hold(NoneFor); return null; }
            return new DiscPicture(bytes, mime);   // kept for good
        }
        catch (Exception) { Hold(FailedFor); return null; }   // no picture: the desk prints one from the cover
    }
}
