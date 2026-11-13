using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// The real names of the library's other versions of a song (owner, 13 Nov: two "Voodoo People" with nothing to tell them apart; SoundCloud calls the
/// Their Law one "Voodoo People (05 Edit)"). Each song with another copy of a different length is looked up on its own album in the catalog (Deezer):
/// the catalog's title for it, same song and within 4 s, gives its version ("05 Edit"). Remembered by file path in data/version-names.json; one album a
/// second at most, 60 albums a pass, a pass three minutes after start and then twice a day.
/// </summary>
public sealed class VersionNameService(SessionStore sessions, WebCatalog catalog, DataPaths paths, ILogger<VersionNameService> log) : BackgroundService
{
    const int AlbumsPerPass = 60;
    readonly string file = Path.Combine(paths.Root, "version-names.json");
    Dictionary<string, string> names = [];
    readonly object gate = new();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { if (File.Exists(file)) names = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(file, stop)) ?? []; } catch (Exception e) when (e is IOException or JsonException) { }
        VersionNames.Lookup = path => { lock (gate) return names.GetValueOrDefault(path); };
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(3), stop);
            while (!stop.IsCancellationRequested)
            {
                try { var n = await PassAsync(stop); if (n > 0) log.LogInformation("Version names: {N} found", n); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Version names pass failed"); }
                await Task.Delay(TimeSpan.FromHours(12), stop);
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task<int> PassAsync(CancellationToken ct)
    {
        var found = 0; var albums = 0;
        foreach (var lib in sessions.Active.Select(s => s.Library).Distinct().ToList())
        {
            // the copies that are not the main one and differ from it (a second copy of the same recording is hidden, no name needed)
            var todo = lib.Songs.Where(t => { lock (gate) if (names.ContainsKey(t.Path)) return false; return lib.VersionNote(t) is not null; }).GroupBy(t => t.AlbumKey).ToList();
            foreach (var album in todo)
            {
                if (albums++ >= AlbumsPerPass || ct.IsCancellationRequested) return found;
                var head = album.First();
                var artist = head.AlbumArtist.Length > 0 ? head.AlbumArtist : head.Artist;
                var want = Matching.NormTitle(WebCatalog.BaseTitle(head.Album));
                try
                {
                    var hit = (await catalog.AlbumsAsync(artist + " " + head.Album, 10, ct)).FirstOrDefault(a => Matching.NormTitle(WebCatalog.BaseTitle(a.CleanTitle)) == want);
                    if (hit is null) { await Task.Delay(1000, ct); continue; }
                    await Task.Delay(1000, ct);
                    var songs = await catalog.TracksAsync(hit, ct);
                    foreach (var t in album)
                    {
                        var core = Matching.NormTitle(t.Title);
                        var c = songs.FirstOrDefault(x => Matching.NormTitle(x.Title).StartsWith(core, StringComparison.Ordinal) && Math.Abs(x.DurationMs - t.DurationMs) <= 4000);
                        if (c is null) continue;
                        var name = VersionNames.NameFrom(c.Title, t.Title);
                        lock (gate) names[t.Path] = name;
                        if (name.Length > 0) found++;
                    }
                }
                catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { }
                await Task.Delay(1000, ct);
            }
        }
        Save();
        return found;
    }

    void Save()
    {
        try { string json; lock (gate) json = JsonSerializer.Serialize(names); File.WriteAllText(file + ".tmp", json); File.Move(file + ".tmp", file, true); } catch (IOException) { }
    }
}
