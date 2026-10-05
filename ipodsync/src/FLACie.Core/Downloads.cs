using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FLACie.Core;

public enum DownloadStage { Requested, Searching, Downloading, Importing, Scanning, Done, Failed }

/// <summary><see cref="NewTracks"/> are set on a Soulseek win's Done status: songs the caller can add to the library, playable at once
/// through the file mover. Lidarr wins arrive through the Jellyfin scan.</summary>
public sealed record DownloadStatus(DownloadStage Stage, string Message, string? Source = null, IReadOnlyList<Track>? NewTracks = null);

/// <summary>The download stack's addresses and keys, as the Android app keeps them in the shared profile (<c>services</c>). They stay
/// on the server: the browser only ever sees progress messages.</summary>
public sealed record DownloadServices(string SlskdUrl, string SlskdKey, string SlskdPath, string FileMoverUrl, string FileMoverKey, string LidarrUrl, string LidarrKey, string NasFolder, OpenSourceSettings? Open = null)
{
    public bool SoulseekReady => SlskdUrl.Length > 0 && SlskdKey.Length > 0 && FileMoverUrl.Length > 0 && FileMoverKey.Length > 0;
    public bool LidarrReady => LidarrUrl.Length > 0 && LidarrKey.Length > 0;
    /// <summary>The open sources (Internet Archive, Jamendo, Audius) only need the file mover to fetch and file what they find.</summary>
    public OpenSourceSettings OpenCfg => Open ?? OpenSourceSettings.Default;
    public bool OpenReady => FileMoverUrl.Length > 0 && FileMoverKey.Length > 0 && OpenCfg.Any;
    public bool Any => SoulseekReady || LidarrReady || OpenReady;

    public static DownloadServices From(JsonObject profile)
    {
        var s = profile["services"] as JsonObject;
        string V(string svc, string k) => s?[svc]?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
        return new(V("slskd", "url"), V("slskd", "key"), V("slskd", "path") is { Length: > 0 } p ? p : "downloads", V("filemover", "url"), V("filemover", "key"), V("lidarr", "url"), V("lidarr", "key"), V("nas", "folder"), OpenSourceSettings.From(s, Environment.GetEnvironmentVariable("FLACIE_JAMENDO_CLIENT_ID")));
    }

    /// <summary>Writes these settings into the profile's <c>services</c>, the same fields the phone reads.</summary>
    public void WriteTo(JsonObject profile)
    {
        var s = profile["services"] as JsonObject ?? new JsonObject();
        void Put(string svc, bool on, JsonObject o) { if (on) s[svc] = o; else s.Remove(svc); }
        Put("slskd", SlskdUrl.Length > 0, new JsonObject { ["url"] = SlskdUrl, ["key"] = SlskdKey, ["path"] = SlskdPath });
        Put("filemover", FileMoverUrl.Length > 0, new JsonObject { ["url"] = FileMoverUrl, ["key"] = FileMoverKey });
        Put("lidarr", LidarrUrl.Length > 0, new JsonObject { ["url"] = LidarrUrl, ["key"] = LidarrKey });
        OpenCfg.WriteTo(s);
        profile["services"] = s;
    }
}

/// <summary>"Download this": Soulseek (slskd) first, since a live peer usually has a popular song right now, with Lidarr's indexer search
/// as the fallback. A Soulseek file is moved by the file mover into the folder Jellyfin scans (<c>Artist/Artist - Title.ext</c>, or
/// <c>Artist/Album/NN - Title.ext</c> for an album). Same behaviour as the Android DownloadCoordinator.</summary>
public sealed class DownloadCoordinator(HttpClient http, WebCatalog catalog, DownloadServices cfg)
{
    SlskdClient Slskd => new(http, cfg.SlskdUrl, cfg.SlskdKey);

    public async Task DownloadAsync(string artist, string title, string album, string? artUrl, long durationMs, Action<DownloadStatus> on, CancellationToken ct = default)
    {
        on(new(DownloadStage.Requested, $"Requested \"{title}\""));
        // the open sources look for the song while Soulseek searches; they only find, and download nothing unless Soulseek came up empty
        using var openCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var open = cfg.OpenReady ? OpenSources.FindAsync(http, artist, title, (int)(durationMs / 1000), cfg.OpenCfg, TimeSpan.FromSeconds(25), openCts.Token) : Task.FromResult<OpenHit?>(null);
        try
        {
            if (cfg.SoulseekReady && await TrySoulseek(artist, title, album, artUrl, (int)(durationMs / 1000), on, ct)) return;
            // Soulseek had nothing: an open source that already found it is used, one still looking gets a few seconds (never longer than that before Lidarr)
            if (await TryOpen(open, artist, title, album, artUrl, durationMs, on, ct)) return;
        }
        finally { openCts.Cancel(); }
        if (!cfg.LidarrReady) { on(new(DownloadStage.Failed, cfg.SoulseekReady ? "Not found on Soulseek or the open sources" : cfg.OpenReady ? "Not found on the open sources" : "Downloads aren't set up (Settings > Downloads)")); return; }
        if (!await new LidarrFlow(http, cfg).TryAsync(artist, title, album, on, ct)) on(new(DownloadStage.Failed, "Not found on Soulseek, the open sources or Lidarr"));
    }

    async Task<bool> TryOpen(Task<OpenHit?> open, string artist, string title, string album, string? artUrl, long durationMs, Action<DownloadStatus> on, CancellationToken ct)
    {
        try
        {
            var grace = Task.Delay(cfg.SoulseekReady ? 4_000 : 30_000, ct);
            if (await Task.WhenAny(open, grace) != open || await open is not { } hit) return false;
            on(new(DownloadStage.Downloading, $"Downloading from {hit.Label}...", hit.Source));
            var dest = string.Join("/", new[] { cfg.NasFolder.Trim('/'), Clean(artist) }.Where(x => x.Length > 0)) + $"/{Clean(artist)} - {Clean(title)}.{hit.Ext}";
            on(new(DownloadStage.Importing, "Filing into the library...", hit.Source));
            if (!await FetchViaFileMover(hit.Url, dest, ct)) return false;
            var track = new Track($"{cfg.FileMoverUrl.TrimEnd('/')}/file?path={Uri.EscapeDataString(dest)}", title, artist, album, artist, 0, 0, durationMs > 0 ? durationMs : hit.DurationSec * 1000L, 0,
                artUrl is null ? null : Art.ExternalKey(artUrl), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TrackSource.Cloud);
            on(new(DownloadStage.Done, $"\"{title}\" is ready to play", hit.Source, [track]));
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }

    /// <summary>Asks the file mover to download [url] straight into the library folder ([to], relative to its root).</summary>
    async Task<bool> FetchViaFileMover(string url, string to, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, cfg.FileMoverUrl.TrimEnd('/') + "/fetch") { Content = new StringContent(new JsonObject { ["url"] = url, ["to"] = to }.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Api-Key", cfg.FileMoverKey);
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    /// <summary>A whole album or EP: one peer's folder where possible, missing songs one by one; Lidarr if Soulseek finds nothing.</summary>
    public async Task DownloadAlbumAsync(CatalogAlbum album, Action<DownloadStatus> on, CancellationToken ct = default)
    {
        var name = album.CleanTitle;
        on(new(DownloadStage.Requested, $"Requested \"{name}\""));
        var list = await catalog.TracksAsync(album, ct);
        if (cfg.SoulseekReady && list.Count > 0)
        {
            var tracks = await TrySoulseekAlbum(album, list, on, ct);
            if (tracks.Count > 0)
            {
                on(new(DownloadStage.Done, tracks.Count == list.Count ? $"\"{name}\" is ready to play" : $"\"{name}\": {tracks.Count} of {list.Count} songs ready", "soulseek", tracks));
                return;
            }
        }
        if (!cfg.LidarrReady) { on(new(DownloadStage.Failed, $"\"{name}\" wasn't found on Soulseek")); return; }
        if (!await new LidarrFlow(http, cfg).TryAsync(album.Artist, "", name, on, ct)) on(new(DownloadStage.Failed, $"\"{name}\" wasn't found"));
    }

    async Task<bool> TrySoulseek(string artist, string title, string album, string? artUrl, int durationSec, Action<DownloadStatus> on, CancellationToken ct)
    {
        try
        {
            on(new(DownloadStage.Searching, "Searching Soulseek...", "soulseek"));
            var sl = Slskd;
            var cands = SlskdClient.RankSong(await sl.SearchAsync($"{Matching.PrimaryArtist(artist)} {WebCatalog.BaseTitle(title)}", ct: ct), artist, title, durationSec);
            var hit = cands.Count == 0 ? null : await FetchFirst(sl, cands, m => on(new(DownloadStage.Downloading, m, "soulseek")), ct);
            if (hit is null) return false;
            on(new(DownloadStage.Importing, "Filing into the library...", "soulseek"));
            var track = await FileAsync(hit, artist, title, album, 0, 0, durationSec * 1000L, $"{Clean(artist)} - {Clean(title)}", null, artist, artUrl, ct);
            if (track is null) return false;
            on(new(DownloadStage.Done, $"\"{title}\" is ready to play", "soulseek", [track]));
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }

    async Task<List<Track>> TrySoulseekAlbum(CatalogAlbum album, List<CatalogSong> list, Action<DownloadStatus> on, CancellationToken ct)
    {
        var sl = Slskd; var name = album.CleanTitle;
        var got = new SlskdFile?[list.Count];
        void Status(string m) => on(new(DownloadStage.Downloading, m, "soulseek"));
        try
        {
            on(new(DownloadStage.Searching, $"Searching Soulseek for \"{name}\"...", "soulseek"));
            var files = await sl.SearchAsync($"{Matching.PrimaryArtist(album.Artist)} {Regex.Replace(name, @"\s*[(\[][^)\]]*[)\]]", "")}", enough: 80, ct: ct);
            foreach (var folder in SlskdClient.RankAlbumFolders(files, album.Artist, name, list.Select(s => s.Title).ToList()).Take(3))
            {
                var want = Enumerable.Range(0, list.Count).Where(i => got[i] is null && folder[i] is not null).ToList();
                if (want.Count == 0) continue;
                var ok = await FetchBatch(sl, want.Select(i => folder[i]!).ToList(), m => Status($"Downloading \"{name}\": {m}"), ct);
                foreach (var i in want) if (ok.Contains(folder[i]!)) got[i] = folder[i];
                if (got.All(g => g is not null)) break;
            }
            // songs no folder had: each from its own best peer, three at a time; at most 8 single-song searches per album
            var missing = Enumerable.Range(0, list.Count).Where(i => got[i] is null).Take(8).ToList();
            if (missing.Count > 0)
            {
                Status($"Finding {missing.Count} more song{(missing.Count == 1 ? "" : "s")}...");
                var gate = new SemaphoreSlim(3);
                await Task.WhenAll(missing.Select(async i =>
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        var s = list[i];
                        var cands = SlskdClient.RankSong(await sl.SearchAsync($"{Matching.PrimaryArtist(s.Artist)} {WebCatalog.BaseTitle(s.Title)}", 12_000, ct: ct), s.Artist, s.Title, (int)(s.DurationMs / 1000));
                        if (cands.Count > 0) got[i] = await FetchFirst(sl, cands, _ => { }, ct);
                    }
                    finally { gate.Release(); }
                }));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
        if (got.All(g => g is null)) return [];
        on(new(DownloadStage.Importing, $"Filing \"{name}\" into the library...", "soulseek"));
        var multiDisc = list.Any(s => s.DiscNo > 1);
        var tracks = new List<Track>();
        for (var i = 0; i < list.Count; i++)
        {
            if (got[i] is not { } hit) continue;
            var s = list[i];
            var no = (multiDisc ? $"{s.DiscNo}-" : "") + s.TrackNo.ToString().PadLeft(2, '0');
            if (await FileAsync(hit, album.Artist, s.Title, name, s.TrackNo, s.DiscNo, s.DurationMs, $"{no} - {Clean(s.Title)}", Clean(name), s.Artist, album.ArtUrl, ct) is { } t) tracks.Add(t);
        }
        return tracks;
    }

    /// <summary>Downloads one song from the best candidate: starts the top peer, adds the next if it hasn't sent anything after 6s, drops a
    /// peer that stalls (nothing for 20s) or fails, and cancels the rest once one finishes. Null if none delivered.</summary>
    static async Task<SlskdFile?> FetchFirst(SlskdClient sl, List<SlskdFile> cands, Action<string> progress, CancellationToken ct)
    {
        var queue = new Queue<SlskdFile>(cands.Take(8));
        var active = new List<Active>();
        async Task Drop(Active a) { active.Remove(a); if (a.Id is not null) await sl.CancelAsync(a.F.Username, a.Id); }
        async Task StartNext()
        {
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                try { await sl.DownloadAsync([f], ct); active.Add(new Active(f, Environment.TickCount64)); return; } catch (OperationCanceledException) { throw; } catch (Exception) { }
            }
        }
        await StartNext();
        var deadline = Environment.TickCount64 + 8 * 60_000;
        while (active.Count > 0 && Environment.TickCount64 < deadline)
        {
            await Task.Delay(1000, ct);
            var now = Environment.TickCount64;
            foreach (var a in active.ToList())
            {
                var t = (await sl.TransfersAsync(a.F.Username, ct)).GetValueOrDefault(a.F.Filename);
                if (t is not null) a.Id = t.Id;
                if (t?.Done == true) { foreach (var o in active.Where(x => x != a).ToList()) await Drop(o); return a.F; }
                if (t?.Done == false) await Drop(a);
                else if (t is not null && t.Bytes > a.Bytes) { a.Bytes = t.Bytes; a.Moved = now; }
                // a silent peer is swapped only when there is someone else to try; the last one keeps its chance
                else if (now - a.Moved > 20_000 && (queue.Count > 0 || active.Count > 1)) await Drop(a);
            }
            // race a second peer while the first is still queued remotely or silent; replace a dropped one
            if (queue.Count > 0 && (active.Count == 0 || (active.Count == 1 && active[0].Bytes == 0 && now - active[0].Started > 6_000))) await StartNext();
            if (active.MaxBy(x => x.Bytes) is { } best) progress(best.Bytes == 0 ? "Waiting for a peer..." : $"Downloading {Math.Min(99, best.Bytes * 100 / Math.Max(1, best.F.Size))}%...");
        }
        foreach (var a in active.ToList()) await Drop(a);
        return null;
    }

    sealed class Active(SlskdFile f, long started) { public SlskdFile F = f; public long Started = started, Moved = started, Bytes; public string? Id; }

    /// <summary>Several files from one peer at once; returns the ones that arrived. Gives up on the rest after 30s with no progress at all.</summary>
    static async Task<HashSet<SlskdFile>> FetchBatch(SlskdClient sl, List<SlskdFile> files, Action<string> progress, CancellationToken ct)
    {
        var user = files[0].Username;
        try { await sl.DownloadAsync(files, ct); } catch (OperationCanceledException) { throw; } catch (Exception) { return []; }
        var total = Math.Max(1, files.Sum(f => f.Size));
        long last = 0, moved = Environment.TickCount64;
        var deadline = Environment.TickCount64 + 20 * 60_000;
        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(1500, ct);
            var ts = await sl.TransfersAsync(user, ct);
            var mine = files.Select(f => ts.GetValueOrDefault(f.Filename)).ToList();
            var bytes = files.Select((f, i) => mine[i]?.Done == true ? f.Size : mine[i]?.Bytes ?? 0).Sum();
            if (bytes > last) { last = bytes; moved = Environment.TickCount64; }
            progress($"{mine.Count(t => t?.Done == true)} of {files.Count} songs, {Math.Min(99, bytes * 100 / total)}%");
            if (mine.All(t => t?.Done is not null)) break;
            if (Environment.TickCount64 - moved > 30_000) break;
        }
        var fin = await sl.TransfersAsync(user, ct);
        foreach (var f in files) if (fin.GetValueOrDefault(f.Filename) is { Done: null } t) await sl.CancelAsync(user, t.Id);
        return files.Where(f => fin.GetValueOrDefault(f.Filename)?.Done == true).ToHashSet();
    }

    static string Clean(string s) => Regex.Replace(s, "[\\\\/:*?\"<>|]", "_").Trim().TrimEnd('.');

    /// <summary>Moves a finished file from slskd's inbox into the folder Jellyfin scans, <c>Artist/[Album/]name.ext</c>, via the file mover,
    /// and returns a Track that plays from the file mover's <c>/file?path=</c> URL (before Jellyfin has scanned it). slskd keeps only the
    /// peer's immediate parent folder, which is how the inbox path is rebuilt.</summary>
    async Task<Track?> FileAsync(SlskdFile hit, string artist, string title, string album, int trackNo, int discNo, long durationMs, string name, string? albumFolder, string trackArtist, string? artUrl, CancellationToken ct)
    {
        var parts = hit.Filename.Replace('\\', '/').Split('/').Where(p => p.Trim().Length > 0).ToArray();
        var inbox = cfg.SlskdPath.Trim('/');
        var source = parts.Length >= 2 ? $"{inbox}/{parts[^2]}/{parts[^1]}" : $"{inbox}/{parts[^1]}";
        var ext = parts[^1].Contains('.') ? parts[^1][(parts[^1].LastIndexOf('.') + 1)..] : "mp3";
        var dest = string.Join("/", new[] { cfg.NasFolder.Trim('/'), Clean(artist), albumFolder }.Where(x => !string.IsNullOrEmpty(x))) + $"/{name}.{ext}";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, cfg.FileMoverUrl.TrimEnd('/') + "/move") { Content = new StringContent(new JsonObject { ["from"] = source, ["to"] = dest }.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.TryAddWithoutValidation("X-Api-Key", cfg.FileMoverKey);
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            return new Track($"{cfg.FileMoverUrl.TrimEnd('/')}/file?path={Uri.EscapeDataString(dest)}", title, trackArtist, album, artist, trackNo, discNo, durationMs, 0,
                artUrl is null ? null : Art.ExternalKey(artUrl), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TrackSource.Cloud);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }
}

/// <summary>Cover keys for images that live on a public catalog (Deezer, iTunes), served through the app's own /art.</summary>
public static class Art
{
    public static string ExternalKey(string url) => "ex" + Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    /// <summary>A cover to be looked up in the public catalog by artist, album and title (same key format as the Android app).</summary>
    public static string? LookupKey(string artist, string album, string title)
    {
        if (artist.Length == 0 || (album.Length == 0 && title.Length == 0)) return null;
        var raw = $"{artist}\n{album}\n{title}"; if (raw.Length > 150) raw = raw[..150];
        return "it" + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    static readonly System.Text.RegularExpressions.Regex CompilationName = new(@"\b(now that'?s what i call|now \d+|various|ministry of sound|hits?|anthems?|summer|party|workout|karaoke|tribute|mix)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    /// <summary>A song filed on a compilation ("Now That's What I Call Music 84", "Various Artists"): its embedded cover is the compilation's, not the song's own album.</summary>
    public static bool OnCompilation(string artist, string albumArtist, string album) =>
        albumArtist.Contains("various", StringComparison.OrdinalIgnoreCase)
        || (album.Length > 0 && CompilationName.IsMatch(album) && albumArtist.Length > 0 && Matching.PrimaryArtist(albumArtist) != Matching.PrimaryArtist(artist));

    public static (string Artist, string Album, string Title)? ParseLookupKey(string key)
    {
        if (!key.StartsWith("it")) return null;
        var b = key[2..].Replace('-', '+').Replace('_', '/'); b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
        try { var p = Encoding.UTF8.GetString(Convert.FromBase64String(b)).Split('\n'); return p.Length >= 3 ? (p[0], p[1], p[2]) : null; } catch (Exception) { return null; }
    }

    public static string? ExternalUrl(string key)
    {
        if (!key.StartsWith("ex")) return null;
        var b = key[2..].Replace('-', '+').Replace('_', '/'); b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
        try
        {
            var u = new Uri(Encoding.UTF8.GetString(Convert.FromBase64String(b)));
            // only public cover hosts, so this can't be pointed at anything on the server's own network
            return u.Scheme == "https" && new[] { "dzcdn.net", "mzstatic.com", "deezer.com", "lidarr.audio" }.Any(h => u.Host == h || u.Host.EndsWith("." + h)) ? u.ToString() : null;
        }
        catch (Exception) { return null; }
    }
}

/// <summary>Lidarr as the fallback when Soulseek finds nothing: find or add the artist, search the album that has the song, and wait for the grab.</summary>
sealed class LidarrFlow(HttpClient http, DownloadServices cfg)
{
    string Base => cfg.LidarrUrl.TrimEnd('/');

    async Task<JsonNode?> Call(HttpMethod m, string path, JsonNode? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(m, Base + path);
        req.Headers.TryAddWithoutValidation("X-Api-Key", cfg.LidarrKey);
        if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new IOException($"HTTP {(int)res.StatusCode}");
        var text = await res.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<bool> TryAsync(string artist, string title, string album, Action<DownloadStatus> on, CancellationToken ct)
    {
        try
        {
            on(new(DownloadStage.Searching, $"Looking up \"{artist}\" on Lidarr...", "lidarr"));
            var found = (await Call(HttpMethod.Get, $"/api/v1/artist/lookup?term={Uri.EscapeDataString(artist)}", null, ct) as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            if (found is null) return false;
            var foreign = found["foreignArtistId"]!.GetValue<string>();
            var existing = (await Call(HttpMethod.Get, "/api/v1/artist", null, ct) as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => o["foreignArtistId"]?.GetValue<string>() == foreign);
            var justAdded = existing is null;
            int artistId;
            if (existing is not null) artistId = existing["id"]!.GetValue<int>();
            else
            {
                on(new(DownloadStage.Searching, $"Adding \"{found["artistName"]?.GetValue<string>()}\" to Lidarr...", "lidarr"));
                var profiles = await Call(HttpMethod.Get, "/api/v1/qualityprofile", null, ct) as JsonArray;
                var profile = profiles?.OfType<JsonObject>().FirstOrDefault(o => string.Equals(o["name"]?.GetValue<string>(), "Lossless", StringComparison.OrdinalIgnoreCase)) ?? profiles?.OfType<JsonObject>().First();
                var root = (await Call(HttpMethod.Get, "/api/v1/rootfolder", null, ct) as JsonArray)?.OfType<JsonObject>().First()["path"]!.GetValue<string>();
                var added = await Call(HttpMethod.Post, "/api/v1/artist", new JsonObject
                {
                    ["foreignArtistId"] = foreign, ["artistName"] = found["artistName"]?.GetValue<string>(), ["qualityProfileId"] = profile!["id"]!.GetValue<int>(), ["metadataProfileId"] = 1,
                    ["rootFolderPath"] = root, ["monitored"] = true, ["addOptions"] = new JsonObject { ["monitor"] = "all", ["searchForMissingAlbums"] = false },
                }, ct);
                artistId = added!["id"]!.GetValue<int>();
            }
            int? albumId = null;
            if (title.Length > 0)
            {
                albumId = await AlbumForTrack(artistId, title, ct);
                for (var i = 0; albumId is null && justAdded && i < 10; i++) { await Task.Delay(1500, ct); albumId = await AlbumForTrack(artistId, title, ct); }
            }
            if (albumId is null && album.Length > 0)
            {
                for (var i = 0; justAdded && i < 10 && (await Albums(artistId, ct)).Count == 0; i++) await Task.Delay(1500, ct);
                albumId = (await Albums(artistId, ct)).FirstOrDefault(a => a.Title.Contains(album, StringComparison.OrdinalIgnoreCase) || album.Contains(a.Title, StringComparison.OrdinalIgnoreCase)).Id is var id && id != 0 ? id : null;
            }
            on(new(DownloadStage.Searching, "Searching indexers...", "lidarr"));
            await Call(HttpMethod.Post, "/api/v1/command", albumId is { } alb
                ? new JsonObject { ["name"] = "AlbumSearch", ["albumIds"] = new JsonArray(alb) } : new JsonObject { ["name"] = "ArtistSearch", ["artistId"] = artistId }, ct);

            on(new(DownloadStage.Downloading, "Waiting for a grab...", "lidarr"));
            var seen = false; var deadline = Environment.TickCount64 + 120_000;
            while (Environment.TickCount64 < deadline)
            {
                var items = (await Call(HttpMethod.Get, "/api/v1/queue?pageSize=200", null, ct))?["records"] as JsonArray ?? [];
                // by artist, not album: Lidarr may grab a different release (a "Best Of") that has the same song
                var item = items.OfType<JsonObject>().FirstOrDefault(o => o["artistId"]?.GetValue<int>() == artistId);
                if (item is not null)
                {
                    seen = true;
                    if (item["statusMessages"] is JsonArray { Count: > 0 } sm && sm[0]?["messages"] is JsonArray { Count: > 0 }) return false;
                    var state = item["trackedDownloadState"]?.GetValue<string>() ?? "";
                    if ((state.Contains("import", StringComparison.OrdinalIgnoreCase) || state.Contains("warning", StringComparison.OrdinalIgnoreCase)) && item["downloadId"]?.GetValue<string>() is { Length: > 0 } dl)
                    {
                        on(new(DownloadStage.Importing, "Importing...", "lidarr"));
                        if (await Call(HttpMethod.Get, $"/api/v1/manualimport?downloadId={dl}", null, ct) is JsonArray { Count: > 0 } cands) await ManualImport(cands, ct);
                    }
                }
                else if (seen)
                {
                    on(new(DownloadStage.Done, $"\"{(title.Length > 0 ? title : album)}\" should appear in your library soon", "lidarr"));
                    return true;
                }
                await Task.Delay(3000, ct);
            }
            return false;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }

    async Task<int?> AlbumForTrack(int artistId, string title, CancellationToken ct)
    {
        var tracks = (await Call(HttpMethod.Get, $"/api/v1/track?artistId={artistId}", null, ct) as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var target = title.Trim().ToLowerInvariant();
        string T(JsonObject o) => (o["title"]?.GetValue<string>() ?? "").Trim().ToLowerInvariant();
        var hit = tracks.FirstOrDefault(o => T(o) == target) ?? tracks.FirstOrDefault(o => T(o).Contains(target) || target.Contains(T(o)));
        return hit?["albumId"]?.GetValue<int>() is > 0 and var id ? id : null;
    }

    async Task<List<(int Id, string Title)>> Albums(int artistId, CancellationToken ct) =>
        (await Call(HttpMethod.Get, $"/api/v1/album?artistId={artistId}", null, ct) as JsonArray)?.OfType<JsonObject>().Select(o => (o["id"]!.GetValue<int>(), o["title"]?.GetValue<string>() ?? "")).ToList() ?? [];

    /// <summary>Applies Lidarr's own suggested match for each candidate file; it already knows which artist/album/track each matches.</summary>
    async Task ManualImport(JsonArray cands, CancellationToken ct)
    {
        var files = new JsonArray();
        foreach (var c in cands.OfType<JsonObject>())
            files.Add(new JsonObject
            {
                ["path"] = c["path"]?.GetValue<string>(), ["artistId"] = c["artist"]?["id"]?.GetValue<int>(), ["albumId"] = c["album"]?["id"]?.GetValue<int>(),
                ["albumReleaseId"] = c["albumReleaseId"]?.DeepClone(), ["quality"] = c["quality"]?.DeepClone(), ["importMode"] = "move",
            });
        await Call(HttpMethod.Post, "/api/v1/command", new JsonObject { ["name"] = "ManualImport", ["files"] = files }, ct);
    }
}
