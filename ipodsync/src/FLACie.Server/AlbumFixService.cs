using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Finds the real album of songs filed under a playlist's name (<see cref="AlbumFixes.Candidates"/>; owner, 13 Nov: "Grenade" on "Pop Megahits"): the
/// catalog's (Deezer) copy of the song by the same artist, the same song within 4 s, not a remix, not on a best-of or compilation. Remembered by file
/// in data/album-fixes.json (a song with no such copy is remembered as none, so it is not asked again); one question a second, 80 songs a pass, three
/// minutes after start and then twice a day. The library takes the fixes the next time it is read.
/// </summary>
public sealed class AlbumFixService(SessionStore sessions, WebCatalog catalog, DataPaths paths, ILogger<AlbumFixService> log) : BackgroundService
{
    const int SongsPerPass = 80;
    readonly string file = Path.Combine(paths.Root, "album-fixes.json");
    Dictionary<string, AlbumFix?> fixes = [];
    readonly object gate = new();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { if (File.Exists(file)) fixes = JsonSerializer.Deserialize<Dictionary<string, AlbumFix?>>(await File.ReadAllTextAsync(file, stop)) ?? []; } catch (Exception e) when (e is IOException or JsonException) { }
        AlbumFixes.Lookup = key => { lock (gate) return fixes.GetValueOrDefault(key); };
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(3), stop);
            while (!stop.IsCancellationRequested)
            {
                try { var n = await PassAsync(stop); if (n > 0) log.LogInformation("Album fixes: {N} songs given their own album", n); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Album fixes pass failed"); }
                await Task.Delay(TimeSpan.FromHours(12), stop);
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task<int> PassAsync(CancellationToken ct)
    {
        var found = 0; var asked = 0;
        var libs = sessions.Active.Select(s => s.Library).Distinct().ToList();
        // a fix whose song no longer qualifies (the rules tightened, or the file was retagged) is dropped
        var still = libs.SelectMany(l => AlbumFixes.Candidates(l.Songs)).Select(t => t.FilePath ?? t.Path).ToHashSet();
        if (libs.Count > 0) lock (gate) foreach (var k in fixes.Keys.Where(k => !still.Contains(k)).ToList()) fixes.Remove(k);
        foreach (var lib in libs)
            foreach (var t in AlbumFixes.Candidates(lib.Songs))
            {
                var key = t.FilePath ?? t.Path;
                lock (gate) if (fixes.ContainsKey(key)) continue;
                if (asked++ >= SongsPerPass || ct.IsCancellationRequested) { Save(); return found; }
                try
                {
                    var core = Matching.NormTitle(WebCatalog.BaseTitle(t.Title)); var lead = Matching.PrimaryArtist(t.Artist);
                    var hit = (await catalog.SongsAsync($"{t.Artist} {WebCatalog.BaseTitle(t.Title)}", 15, ct)).FirstOrDefault(c =>
                        Matching.PrimaryArtist(c.Artist) == lead && Matching.NormTitle(WebCatalog.BaseTitle(c.Title)) == core && Matching.Recording(c.Title).Length == 0
                        && c.Album.Length > 0 && !Matching.IsCompilationName(c.Album) && (t.DurationMs <= 0 || c.DurationMs <= 0 || Math.Abs(c.DurationMs - t.DurationMs) <= 4000));
                    var fix = hit is null || Matching.AlbumNorm(hit.Album) == Matching.AlbumNorm(t.Album) || Matching.IsCompilationName(hit.Album) ? null : new AlbumFix(hit.Album, hit.Artist, hit.ArtUrl, 0);
                    lock (gate) fixes[key] = fix;
                    if (fix is not null) found++;
                }
                catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { }
                await Task.Delay(1000, ct);
            }
        Save();
        return found;
    }

    /// <summary>The songs given their own album, for Server admin to review (file, the fix).</summary>
    public IReadOnlyList<(string File, AlbumFix Fix)> Fixed() { lock (gate) return fixes.Where(kv => kv.Value is not null).Select(kv => (kv.Key, kv.Value!)).OrderBy(x => x.Item1).ToList(); }

    /// <summary>Takes a fix back: the song shows its tags again, and is not looked up again (the entry stays, as "no fix").</summary>
    public void Undo(string file) { lock (gate) { if (fixes.ContainsKey(file)) fixes[file] = null; } Save(); }

    void Save()
    {
        try { string json; lock (gate) json = JsonSerializer.Serialize(fixes); File.WriteAllText(file + ".tmp", json); File.Move(file + ".tmp", file, true); } catch (IOException) { }
    }
}
