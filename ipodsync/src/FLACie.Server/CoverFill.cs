using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// F1.3: a pass that fetches and saves every song's catalog cover once, so rows never wait on the catalog. One lookup per [pace]; a cover already saved is
/// skipped. When the catalog stops answering (its quota, an outage) the pass stops and remembers nothing: the songs it did not reach, or could not ask about,
/// are tried again on the next pass. A song the catalog has nothing for is left to the made cover (<see cref="GeneratedCover"/>).
/// </summary>
public sealed class CoverFill(CoverStore covers, Func<string, string, string, CancellationToken, Task<(byte[]? Bytes, CatalogAnswer Answer)>> catalog, TimeSpan pace,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;

    /// <summary>Where the last pass got to, for Server admin.</summary>
    public sealed record Progress(int Total, int Done, int Saved, int NoCover, string? Stopped);
    public Progress Last { get; private set; } = new(0, 0, 0, 0, null);

    public async Task<Progress> RunAsync(IEnumerable<Track> songs, CancellationToken ct)
    {
        var keys = songs.Select(t => t.ArtKey).OfType<string>().Where(k => Art.ParseLookupKey(k) is not null).Distinct().ToList();
        int done = 0, saved = 0, none = 0;
        Last = new(keys.Count, 0, 0, 0, null);
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            if (covers.Find(key, CoverStore.Other) is null)
            {
                var (artist, album, title) = Art.ParseLookupKey(key)!.Value;
                var (bytes, answer) = await catalog(artist, album, title, ct);
                if (answer == CatalogAnswer.Unasked) return Last = new(keys.Count, done, saved, none, "The catalog stopped answering (its daily limit?); the rest are tried on the next pass.");
                if (bytes is { Length: > 0 }) { await covers.SaveAsync(key, CoverStore.Other, bytes, "image/jpeg"); saved++; }
                else none++;
                await wait(pace, ct);
            }
            Last = new(keys.Count, ++done, saved, none, null);
        }
        return Last;
    }
}

/// <summary>Runs <see cref="CoverFill"/> on frank over every signed-in library: ten minutes after the start, then once a day.</summary>
public sealed class CoverFillService(SessionStore sessions, CoverFill fill, ILogger<CoverFillService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(10), stop);
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var p = await fill.RunAsync(sessions.Active.ToList().SelectMany(s => s.Library.Songs), stop);
                    log.LogInformation("Cover fill: {Done}/{Total} songs, {Saved} saved, {None} with no catalog cover. {Stopped}", p.Done, p.Total, p.Saved, p.NoCover, p.Stopped ?? "");
                }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Cover fill failed"); }
                await Task.Delay(TimeSpan.FromDays(1), stop);
            }
        }
        catch (OperationCanceledException) { }
    }
}
