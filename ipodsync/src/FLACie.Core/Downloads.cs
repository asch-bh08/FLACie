using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FLACie.Core;

public enum DownloadStage { Requested, Searching, Downloading, Importing, Scanning, Done, Failed }

/// <summary><see cref="NewTracks"/> are set on a Soulseek win's Done status: songs the caller can add to the library, playable at once
/// through the file mover. Lidarr wins arrive through the Jellyfin scan. <see cref="HiRes"/> on a Done status: the copy that arrived is Hi-Res, so
/// it needs no upgrade later (anything else that FLACie filed goes on the upgrade list).</summary>
public sealed record DownloadStatus(DownloadStage Stage, string Message, string? Source = null, IReadOnlyList<Track>? NewTracks = null, bool Miss = false, bool HiRes = false);

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
        return new(V("slskd", "url"), V("slskd", "key"), V("slskd", "path") is { Length: > 0 } p ? p : "downloads", V("filemover", "url"), V("filemover", "key"), V("lidarr", "url"), V("lidarr", "key"), V("nas", "folder"), OpenSourceSettings.From(s, Environment.GetEnvironmentVariable("FLACIE_JAMENDO_CLIENT_ID"), OpenSourceSettings.HostSwitch(Environment.GetEnvironmentVariable("FLACIE_YOUTUBE")))
            with { SoundCloudByHost = OpenSourceSettings.HostSwitch(Environment.GetEnvironmentVariable("FLACIE_SOUNDCLOUD")) });
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

/// <summary>"Download this", the quick way (smart downloads, docs/superpowers/specs/2026-11-01-smart-downloads-design.md): one Soulseek search ranked
/// into normal and Hi-Res copies, YouTube where it is on, and the open sources all start at once, and the first copy that is filed and playable plays
/// (a Hi-Res copy only a few seconds behind still wins, see <see cref="DownloadRace"/>). Lidarr's indexer search is the fallback when none of them has
/// it. A Soulseek file is moved by the file mover into the folder Jellyfin scans (<c>Artist/Artist - Title.ext</c>, <c>Artist/Artist - Title [Hi-Res].ext</c>
/// for a Hi-Res copy, or <c>Artist/Album/NN - Title.ext</c> for an album).</summary>
public sealed class DownloadCoordinator(HttpClient http, WebCatalog catalog, DownloadServices cfg, HttpClient? longHttp = null, IFileMover? fileMover = null)
{
    SlskdClient Slskd => new(http, cfg.SlskdUrl, cfg.SlskdKey);
    IFileMover Mover => fileMover ??= new FileMoverClient(http, cfg.FileMoverUrl, cfg.FileMoverKey);

    /// <summary>How long the song is by the catalog (its hit with this title by this artist), in ms; 0 when it has none.</summary>
    public static long ExpectedLength(IEnumerable<CatalogSong> hits, string artist, string title)
    {
        var a = Matching.PrimaryArtist(artist); var tt = Matching.NormTitle(WebCatalog.BaseTitle(title));
        return hits.FirstOrDefault(h => h.DurationMs > 0 && Matching.PrimaryArtist(h.Artist) == a && Matching.NormTitle(WebCatalog.BaseTitle(h.Title)) == tt)?.DurationMs ?? 0;
    }

    /// <summary>A copy too short to be the song: well under its known length (a 2-minute mash-up for a 4-minute song), or, with no length known, under
    /// 45 s (a shop's 30 s preview). 0 s means not known: never too short.</summary>
    public static bool TooShort(int sec, int expectedSec) =>
        sec > 0 && (expectedSec > 0 ? sec < expectedSec - Math.Max(20, expectedSec / 5) : sec < 45);

    /// <summary>The song's length when the caller did not know it (a song from a playlist): asked of the catalog, so every source can check what it
    /// found (owner, 6 Nov: a 12 s Scatman and a mash-up for Corona came in with an import). 0 when the catalog has nothing or does not answer soon.</summary>
    async Task<long> LengthOf(string artist, string title, long durationMs, CancellationToken ct)
    {
        if (durationMs > 0) return durationMs;
        try
        {
            using var soon = CancellationTokenSource.CreateLinkedTokenSource(ct);
            soon.CancelAfter(TimeSpan.FromSeconds(6));
            return ExpectedLength(await catalog.SongsAsync($"{Matching.PrimaryArtist(artist)} {WebCatalog.BaseTitle(title)}", 10, soon.Token), artist, title);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested) { return 0; }
    }

    public async Task DownloadAsync(string artist, string title, string album, string? artUrl, long durationMs, Action<DownloadStatus> on, CancellationToken ct = default)
    {
        on(new(DownloadStage.Requested, $"Requested \"{title}\""));
        durationMs = await LengthOf(artist, title, durationMs, ct);
        var sec = (int)(durationMs / 1000);
        // YouTube and the open sources can't be stopped once their file mover has started filing, so they only race when a copy that lost can be moved
        // out of the library again (a file mover with /retire). With an older file mover they keep their old turn, after Soulseek, as before. The file
        // mover is asked while Soulseek already searches: a slow answer never holds the download up.
        var canRace = cfg.FileMoverUrl.Length > 0 && cfg.OpenReady ? Mover.CanRetireAsync(ct) : Task.FromResult(false);
        using var finders = cfg.OpenReady ? OpenSources.Start(http, artist, title, sec, cfg.OpenCfg, TimeSpan.FromSeconds(25), ct) : null;
        using var searching = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var entrants = new List<RaceEntrant>();
        if (cfg.SoulseekReady) entrants.AddRange(SoulseekEntrants(artist, title, album, artUrl, sec, on, searching.Token, ct));
        if (cfg.OpenReady) entrants.AddRange(OpenEntrants(canRace, finders, artist, title, album, artUrl, durationMs, on, ct));
        var racing = false;
        if (entrants.Count > 0)
        {
            RaceWin? win;
            try { win = await DownloadRace.RunAsync(entrants, late => _ = RetireAsync(late), ct); }
            finally { searching.Cancel(); }
            racing = await canRace;
            if (win is not null)
            {
                on(new(DownloadStage.Done, win.HiRes ? $"\"{title}\" is ready to play (Hi-Res)" : $"\"{title}\" is ready to play", win.Source, [win.Track], HiRes: win.HiRes));
                return;
            }
            if (cfg.SoulseekReady) on(new(DownloadStage.Searching, racing ? "No source had a copy it could finish" : "Soulseek had nothing it could finish", "soulseek", null, true));
        }
        // an older file mover: Soulseek had nothing, so the open sources in the user's order, then Lidarr
        if (!racing && cfg.OpenReady && await Guard("The open sources", "archive", on, ct, () => TryOpen(finders, artist, title, album, artUrl, durationMs, on, ct))) return;
        if (!cfg.LidarrReady) { on(new(DownloadStage.Failed, cfg.SoulseekReady ? "Not found on Soulseek or the open sources" + Unreachable : cfg.OpenReady ? "Not found on the open sources" + Unreachable : "Downloads aren't set up (Settings > Downloads)")); return; }
        if (!await Guard("Lidarr", "lidarr", on, ct, () => new LidarrFlow(http, cfg).TryAsync(artist, title, album, on, ct))) on(new(DownloadStage.Failed, "Not found on Soulseek, the open sources or Lidarr" + Unreachable));
    }

    /// <summary>One source's turn. A source that errors or does not answer in time (a busy slskd, a service that is down) counts as "had nothing" and the next source
    /// is tried, instead of the whole download failing on the spot with the error text. A real cancel still cancels.</summary>
    async Task<bool> Guard(string name, string source, Action<DownloadStatus> on, CancellationToken ct, Func<Task<bool>> step)
    {
        try { return await step(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            var timedOut = e is OperationCanceledException or TimeoutException;
            on(new(DownloadStage.Searching, timedOut ? $"{name} did not answer in time" : $"{name} failed: {e.Message}", source, null, true));
            if (timedOut || e is HttpRequestException) serviceDown ??= $"{name} could not be reached";
            return false;
        }
    }

    /// <summary>Set when the YouTube service (the file mover) could not be reached, so a failure says that rather than only "not found": it was not a real "no".</summary>
    string? serviceDown;
    string Unreachable => serviceDown is null ? "" : $". {serviceDown}; try again in a minute";

    /// <summary>
    /// Soulseek's two runners in the race, sharing one search (spec section 1): normal copies (lossless or 320 kbps, up to three peers at once) and Hi-Res
    /// copies (up to two peers), each from its own ranking of the same results. The search and the transfers stop when the race is settled ([search] and
    /// the race's own token); filing a finished file into the library is not cut off half way ([ct]), and a copy filed that late is retired by the race.
    /// </summary>
    IEnumerable<RaceEntrant> SoulseekEntrants(string artist, string title, string album, string? artUrl, int durationSec, Action<DownloadStatus> on, CancellationToken search, CancellationToken ct)
    {
        var sl = Slskd;
        var found = new Lazy<Task<List<SlskdFile>>>(() => SearchSong(sl, artist, title, durationSec, false, on, search), LazyThreadSafetyMode.ExecutionAndPublication);
        async Task<Track?> Run(RaceEntrant e, bool hiRes, CancellationToken race)
        {
            using var both = CancellationTokenSource.CreateLinkedTokenSource(race, search);
            var cands = SlskdClient.RankSong(await found.Value, artist, title, durationSec, hiRes);
            if (cands.Count == 0) return null;
            var hit = await FetchFirst(sl, cands, m => on(new(DownloadStage.Downloading, hiRes ? m.Replace("Downloading", "Downloading a Hi-Res copy") : m, "soulseek")), both.Token,
                hiRes ? 2 : PeerRace.MaxAtOnce, hiRes ? (got, size) => e.Report(got, size) : null);
            if (hit is null) return null;
            on(new(DownloadStage.Importing, "Filing into the library...", "soulseek"));
            return await FileAsync(hit, artist, title, album, 0, 0, durationSec * 1000L, $"{Clean(artist)} - {Clean(WebCatalog.BaseTitle(title))}{(hiRes ? " [Hi-Res]" : "")}", null, artist, artUrl, ct);
        }
        on(new(DownloadStage.Searching, "Searching Soulseek...", "soulseek"));
        yield return new RaceEntrant("soulseek", false, (e, race) => Run(e, false, race));
        yield return new RaceEntrant("soulseek", true, (e, race) => Run(e, true, race));
    }

    /// <summary>YouTube (where it is on) and the open finders in the race. Each finder that has the song is fetched by the file mover; the first finder to
    /// answer is taken (two at once would write the same file). Neither can be stopped once the file mover is filing, so that part runs on [ct] and a copy
    /// that lands after the race is settled is retired. Both first wait for [canRace]: with an older file mover (no /retire) they step out of the race
    /// with nothing, and run in their old turn after Soulseek instead.</summary>
    IEnumerable<RaceEntrant> OpenEntrants(Task<bool> canRace, OpenSources.Finders? finders, string artist, string title, string album, string? artUrl, long durationMs, Action<DownloadStatus> on, CancellationToken ct)
    {
        if (cfg.OpenCfg.Active.Contains(OpenSourceSettings.YtdlId))
        {
            yield return new RaceEntrant(OpenSourceSettings.YtdlId, false, async (_, race) => await canRace.WaitAsync(race) ? await YtdlAsync(artist, title, album, artUrl, durationMs, on, race, ct) : null);
            // F7: SoundCloud through the same yt-dlp service (scsearch, the producer's original file when offered); the quickest copy plays, lossy ones go on the upgrade list
            if (cfg.OpenCfg.SoundCloudByHost) yield return new RaceEntrant(SoundCloud.Id, false, async (_, race) => await canRace.WaitAsync(race) ? await YtdlAsync(artist, title, album, artUrl, durationMs, on, race, ct, SoundCloud.Id) : null);
        }
        if (finders is { Tasks.Count: > 0 })
            yield return new RaceEntrant("open", false, async (e, race) =>
            {
                if (!await canRace.WaitAsync(race)) return null;
                var left = finders.Tasks.Values.ToList();
                while (left.Count > 0)
                {
                    var done = await Task.WhenAny(left).WaitAsync(race);
                    left.Remove(done);
                    if (await done is not { } hit || !OpenSources.Allowed(hit) || TooShort((int)hit.DurationSec, (int)(durationMs / 1000))) continue;
                    on(new(DownloadStage.Downloading, $"Downloading from {hit.Label}...", hit.Source));
                    var dest = string.Join("/", new[] { cfg.NasFolder.Trim('/'), Clean(artist) }.Where(x => x.Length > 0)) + $"/{Clean(artist)} - {Clean(WebCatalog.BaseTitle(title))}.{hit.Ext}";
                    if (!await FetchViaFileMover(hit.Url, dest, ct)) { on(new(DownloadStage.Searching, OpenSourceSettings.Name(hit.Source) + " had it, but the download failed", hit.Source, null, true)); continue; }
                    e.Source = hit.Source;   // the job's label says which open source it was (Internet Archive, Audius, Jamendo)
                    return OpenTrack(dest, title, artist, album, artUrl, durationMs > 0 ? durationMs : hit.DurationSec * 1000L);
                }
                return null;
            });
    }

    /// <summary>A copy that lost the race but was filed anyway: moved out of the library by the file mover, so the song is not there twice.</summary>
    async Task RetireAsync(RaceWin late)
    {
        if (FileOf(late.Track.Path) is { Length: > 0 } rel) try { await Mover.RetireAsync(rel); } catch (Exception) { }
    }

    /// <summary>The path under the file mover's root of a track that plays from <c>/file?path=</c>.</summary>
    static string? FileOf(string path) { var i = path.IndexOf("/file?path=", StringComparison.Ordinal); return i < 0 ? null : Uri.UnescapeDataString(path[(i + "/file?path=".Length)..]); }

    /// <summary>The open sources in the user's order: a finder that already found the song (or finds it within a few seconds more; Soulseek's own search
    /// has usually given them time) is downloaded from, yt-dlp runs its own search and download when its turn comes. False when none had it.</summary>
    async Task<bool> TryOpen(OpenSources.Finders? finders, string artist, string title, string album, string? artUrl, long durationMs, Action<DownloadStatus> on, CancellationToken ct)
    {
        try
        {
            var deadline = Environment.TickCount64 + (cfg.SoulseekReady ? 4_000 : 30_000);
            foreach (var id in cfg.OpenCfg.Active)
            {
                if (id == OpenSourceSettings.YtdlId)
                {
                    if (await TryYtdl(artist, title, album, artUrl, durationMs, on, ct)) return true;
                    continue;
                }
                if (finders is null || !finders.Tasks.TryGetValue(id, out var task)) continue;
                var sname = OpenSourceSettings.Name(id);
                var left = (int)Math.Max(0, deadline - Environment.TickCount64);
                if (await Task.WhenAny(task, Task.Delay(left, ct)) != task || await task is not { } hit || TooShort((int)hit.DurationSec, (int)(durationMs / 1000))) { on(new(DownloadStage.Searching, sname + " had nothing", id, null, true)); continue; }
                on(new(DownloadStage.Downloading, $"Downloading from {hit.Label}...", hit.Source));
                var dest = string.Join("/", new[] { cfg.NasFolder.Trim('/'), Clean(artist) }.Where(x => x.Length > 0)) + $"/{Clean(artist)} - {Clean(WebCatalog.BaseTitle(title))}.{hit.Ext}";
                on(new(DownloadStage.Importing, "Filing into the library...", hit.Source));
                if (!await FetchViaFileMover(hit.Url, dest, ct)) { on(new(DownloadStage.Searching, sname + " had it, but the download failed", id, null, true)); continue; }
                on(new(DownloadStage.Done, $"\"{title}\" is ready to play", hit.Source, [OpenTrack(dest, title, artist, album, artUrl, durationMs > 0 ? durationMs : hit.DurationSec * 1000L)]));
                return true;
            }
            return false;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }

    /// <summary>Exactly the song at a SoundCloud or YouTube link (owner, 8 Nov: "import single songs from a link, like SoundCloud remixes"): the yt-dlp
    /// service reads the page, names the artist and title, and files it under the library folder.</summary>
    public async Task DownloadLinkAsync(string url, Action<DownloadStatus> on, CancellationToken ct = default)
    {
        if (PlaylistLinks.SongLink(url) is not { } link) { on(new(DownloadStage.Failed, "That isn't a SoundCloud or YouTube song link")); return; }
        if (cfg.FileMoverUrl.Length == 0) { on(new(DownloadStage.Failed, "Song links need FLACie's download services (the file mover) set up in Server admin")); return; }
        var tag = link.Service == "SoundCloud" ? SoundCloud.Id : "ytdl";
        on(new(DownloadStage.Searching, $"Waiting for {link.Service}'s turn...", tag));
        await YtdlTurn.WaitAsync(ct);
        try
        {
            on(new(DownloadStage.Downloading, $"Getting it from {link.Service}...", tag));
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(240));
            using var req = new HttpRequestMessage(HttpMethod.Post, cfg.FileMoverUrl.TrimEnd('/') + "/ytdl")
            { Content = new StringContent(new JsonObject { ["url"] = link.Url, ["toDir"] = cfg.NasFolder.Trim('/') }.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.TryAddWithoutValidation("X-Api-Key", cfg.FileMoverKey);
            try
            {
                using var res = await (longHttp ?? http).SendAsync(req, limit.Token);
                var j = JsonNode.Parse(await res.Content.ReadAsStringAsync(limit.Token));
                if (res.IsSuccessStatusCode && j?["ok"]?.GetValue<bool>() == true && j["path"]?.GetValue<string>() is { Length: > 0 } path)
                {
                    var artist = j["artist"]?.GetValue<string>() ?? ""; var title = j["title"]?.GetValue<string>() ?? "";
                    var secs = j["durationSec"] is JsonValue d && d.TryGetValue<int>(out var n) ? n : 0;
                    on(new(DownloadStage.Done, $"\"{title}\" by {artist} is ready to play", tag, [OpenTrack(path, title, artist, "", null, secs * 1000L)]));
                    return;
                }
                var said = j?["error"]?.GetValue<string>() ?? "";
                on(new(DownloadStage.Failed, (int)res.StatusCode == 404 ? $"{link.Service} couldn't find a song at that link. Is it public?" : $"{link.Service} failed: " + (said.Length > 0 ? said : "HTTP " + (int)res.StatusCode)));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { on(new(DownloadStage.Failed, link.Service + " took too long")); }
            catch (HttpRequestException e) { on(new(DownloadStage.Failed, link.Service + " service unreachable: " + e.Message)); }
        }
        finally { YtdlTurn.Release(); }
    }

    Track OpenTrack(string dest, string title, string artist, string album, string? artUrl, long durationMs) =>
        new($"{cfg.FileMoverUrl.TrimEnd('/')}/file?path={Uri.EscapeDataString(dest)}", title, artist, album, artist, 0, 0, durationMs, 0,
            artUrl is null ? Art.LookupKey(artist, album, title) : Art.ExternalKey(artUrl), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TrackSource.Cloud);

    /// <summary>The last-resort source: the file mover forwards to the yt-dlp service, which finds the song on YouTube (official audio / Topic uploads
    /// first, length and title checked) and files the audio itself. Not offered on any screen: it only runs where the profile already had it on, or when the server is started with FLACIE_YOUTUBE=on.</summary>
    // yt-dlp does two searches and a download per song (30 s or more), and its service takes two songs at a time: the queue is kept here so a song
    // waiting its turn is not counted as a failure, and the time limit only starts when it is actually being fetched
    static readonly SemaphoreSlim YtdlTurn = new(2);

    async Task<bool> TryYtdl(string artist, string title, string album, string? artUrl, long durationMs, Action<DownloadStatus> on, CancellationToken ct)
    {
        if (await YtdlAsync(artist, title, album, artUrl, durationMs, on, ct, ct) is not { } track) return false;
        on(new(DownloadStage.Done, $"\"{title}\" is ready to play", "ytdl", [track]));
        return true;
    }

    /// <summary>YouTube's copy, filed and playable, or null. [before] stops it while it is still waiting for its turn (the race was settled elsewhere);
    /// once the YouTube service is asked, only [ct] stops it, since the service files the song whatever happens to this request.</summary>
    async Task<Track?> YtdlAsync(string artist, string title, string album, string? artUrl, long durationMs, Action<DownloadStatus> on, CancellationToken before, CancellationToken ct, string source = "youtube")
    {
        var sc = source == SoundCloud.Id; var name = sc ? "SoundCloud" : "YouTube"; var tag = sc ? SoundCloud.Id : "ytdl";
        string why = name + " had no matching upload";
        try
        {
            on(new(DownloadStage.Searching, $"Waiting for {name}'s turn...", tag));
            await YtdlTurn.WaitAsync(before);
            try
            {
                if (before.IsCancellationRequested) return null;
                on(new(DownloadStage.Searching, $"Looking on {name}...", tag));
                var noExt = string.Join("/", new[] { cfg.NasFolder.Trim('/'), Clean(artist) }.Where(x => x.Length > 0)) + $"/{Clean(artist)} - {Clean(WebCatalog.BaseTitle(title))}" + (sc ? " (SoundCloud)" : "");   // its own file: both may be filing at once
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(170));
                    using var req = new HttpRequestMessage(HttpMethod.Post, cfg.FileMoverUrl.TrimEnd('/') + "/ytdl")
                    { Content = new StringContent(new JsonObject { ["artist"] = artist, ["title"] = WebCatalog.BaseTitle(title), ["durationSec"] = (int)(durationMs / 1000), ["to"] = noExt, ["source"] = source }.ToJsonString(), Encoding.UTF8, "application/json") };
                    req.Headers.TryAddWithoutValidation("X-Api-Key", cfg.FileMoverKey);
                    try
                    {
                        using var res = await (longHttp ?? http).SendAsync(req, limit.Token);
                        var j = JsonNode.Parse(await res.Content.ReadAsStringAsync(limit.Token));
                        if (res.IsSuccessStatusCode && j?["ok"]?.GetValue<bool>() == true && j["ext"]?.GetValue<string>() is { Length: > 0 } ext)
                        {
                            on(new(DownloadStage.Importing, "Filing into the library...", tag));
                            var secs = j["durationSec"] is JsonValue d && d.TryGetValue<int>(out var n) ? n : 0;
                            if (TooShort(secs, (int)(durationMs / 1000)))
                            {
                                // not the song (a preview, a short edit): out of the library again
                                try { await Mover.RetireAsync($"{noExt}.{ext}"); } catch (Exception) { }
                                why = $"{name} only had a {secs} s copy"; break;
                            }
                            return OpenTrack($"{noExt}.{ext}", title, artist, album, artUrl, durationMs > 0 ? durationMs : secs * 1000L);
                        }
                        // 404 = the search really found nothing usable; anything else is the YouTube service failing, worth one more try
                        var said = j?["error"]?.GetValue<string>() ?? "";
                        if ((int)res.StatusCode == 404) { why = name + " had no matching upload"; break; }
                        why = name + " failed: " + (said.Length > 0 ? said : "HTTP " + (int)res.StatusCode); serviceDown = null;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { why = name + " took too long"; serviceDown = null; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { why = name + " service unreachable: " + e.Message; serviceDown = name + " was never asked: its service could not be reached"; }
                    await Task.Delay(3_000, ct);
                }
            }
            finally { YtdlTurn.Release(); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
        on(new(DownloadStage.Searching, why, tag, null, true));
        return null;
    }

    /// <summary>Asks the file mover to download [url] straight into the library folder ([to], relative to its root).</summary>
    async Task<bool> FetchViaFileMover(string url, string to, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, cfg.FileMoverUrl.TrimEnd('/') + "/fetch") { Content = new StringContent(new JsonObject { ["url"] = url, ["to"] = to }.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Api-Key", cfg.FileMoverKey);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromMinutes(4));
        using var res = await (longHttp ?? http).SendAsync(req, limit.Token);
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

    /// <summary>The searches to try for a song, best first. Soulseek's server refuses to search some full artist names (a query containing "kendrick lamar" returns
    /// nothing at all, while "kendrick all the stars" finds hundreds of files), so when the full-name query finds nothing usable the artist's first word, then
    /// the title alone, are tried; the ranking afterwards still requires the artist in the file's path.</summary>
    static List<(string Query, int TimeoutMs)> SearchPlan(string artist, string title)
    {
        var a = Matching.PrimaryArtist(artist); var t = WebCatalog.BaseTitle(title);
        var words = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var plan = new List<(string, int)> { ($"{a} {t}".Trim(), 15_000) };
        if (words.Length > 1) plan.Add(($"{words[0]} {t}", 10_000));
        // a title that is a single word ("Loser", "Treppen") is too broad alone, so it keeps the artist's last word with it instead
        if (t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2) plan.Add((t, 10_000));
        else if (words.Length > 1) plan.Add(($"{words[^1]} {t}", 10_000));
        // peers come and go: the same search once more, longer, when nothing at all answered
        plan.Add(($"{a} {t}".Trim(), 25_000));
        return plan;
    }

    /// <summary>One song from Soulseek. [hiRes] is the explicit Hi-Res download: only Hi-Res files, filed next to the normal copy as "Artist - Title [Hi-Res]".
    /// A normal download never takes a Hi-Res file (they are several times the size), and says so when that is all there is.</summary>
    async Task<bool> TrySoulseek(string artist, string title, string album, string? artUrl, int durationSec, Action<DownloadStatus> on, CancellationToken ct, bool hiRes = false)
    {
        try
        {
            on(new(DownloadStage.Searching, hiRes ? "Searching Soulseek for a Hi-Res copy..." : "Searching Soulseek...", "soulseek"));
            var sl = Slskd;
            var cands = SlskdClient.RankSong(await SearchSong(sl, artist, title, durationSec, hiRes, on, ct), artist, title, durationSec, hiRes);
            var hit = cands.Count == 0 ? null : await FetchFirst(sl, cands, m => on(new(DownloadStage.Downloading, m, "soulseek")), ct);
            if (hit is null) return false;
            on(new(DownloadStage.Importing, "Filing into the library...", "soulseek"));
            var track = await FileAsync(hit, artist, title, album, 0, 0, durationSec * 1000L, $"{Clean(artist)} - {Clean(WebCatalog.BaseTitle(title))}{(hiRes ? " [Hi-Res]" : "")}", null, artist, artUrl, ct);
            if (track is null) { on(new(DownloadStage.Searching, "Soulseek sent the file but it could not be filed: " + (FileWhy.Value ?? "unknown reason"), "soulseek", null, true)); return false; }
            on(new(DownloadStage.Done, hiRes ? $"\"{title}\" is ready to play (Hi-Res {HiResLabel(hit)})" : $"\"{title}\" is ready to play", "soulseek", [track], HiRes: hiRes && hit.HiRes));
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }

    /// <summary>One song's Soulseek search, through <see cref="SearchPlan"/> until a query finds a usable copy: a Hi-Res one for [hiResOnly], otherwise a
    /// normal or a Hi-Res one (the quick race ranks the same results both ways). Returns every file the last query found.</summary>
    static async Task<List<SlskdFile>> SearchSong(SlskdClient sl, string artist, string title, int durationSec, bool hiResOnly, Action<DownloadStatus> on, CancellationToken ct)
    {
        List<SlskdFile> found = [];
        var plan = SearchPlan(artist, title);
        for (var i = 0; i < plan.Count; i++)
        {
            // the closing repeat of the first search is only worth its extra seconds when every earlier search came back completely empty
            if (i == plan.Count - 1 && i > 0 && found.Count > 0) break;
            found = await sl.SearchAsync(plan[i].Query, plan[i].TimeoutMs, ct: ct);
            if (SlskdClient.RankSong(found, artist, title, durationSec, hiRes: true).Count > 0) break;
            if (!hiResOnly && SlskdClient.RankSong(found, artist, title, durationSec).Count > 0) break;
        }
        return found;
    }

    /// <summary>
    /// One look for a better copy of a song on the upgrade list (spec section 2): Soulseek's Hi-Res copies only, or for [lossless] (the step for a YouTube
    /// or lossy copy) a CD-quality lossless one. A peer that is sending keeps going however long it takes; a peer that never starts still gives up
    /// (<see cref="PeerRace"/>). The copy is filed next to [oldFile] in the same folder ("Artist - Title [Hi-Res].flac", or "Artist - Title.flac" for
    /// the lossless step), never on top of it. Null when nobody had one or nobody delivered.
    /// </summary>
    public async Task<UpgradeCopy?> UpgradeAsync(string artist, string title, string album, long durationMs, string oldFile, bool lossless, CancellationToken ct = default)
    {
        if (!cfg.SoulseekReady) return null;
        durationMs = await LengthOf(artist, title, durationMs, ct);
        var sl = Slskd; var sec = (int)(durationMs / 1000);
        var found = await SearchSong(sl, artist, title, sec, !lossless, _ => { }, ct);
        var cands = lossless ? SlskdClient.RankSong(found, artist, title, sec).Where(f => f.Lossless).ToList() : SlskdClient.RankSong(found, artist, title, sec, hiRes: true);
        if (cands.Count == 0) return null;
        var hit = await FetchFirst(sl, cands, _ => { }, ct, maxAtOnce: 2, patient: true);
        if (hit is null) return null;
        var ext = hit.Leaf.Contains('.') ? hit.Leaf[(hit.Leaf.LastIndexOf('.') + 1)..] : "flac";
        var folder = oldFile.Replace('\\', '/') is var o && o.Contains('/') ? o[..o.LastIndexOf('/')] : "";
        var dest = (folder.Length > 0 ? folder + "/" : "") + $"{Clean(artist)} - {Clean(WebCatalog.BaseTitle(title))}{(lossless ? "" : " [Hi-Res]")}.{ext}";
        if (string.Equals(dest, oldFile, StringComparison.OrdinalIgnoreCase)) return null;   // never filed on top of the file it is meant to replace
        var track = await FileAsync(hit, artist, title, album, 0, 0, durationMs, "", null, artist, null, ct, dest);
        return track is null ? null : new UpgradeCopy(track, dest, hit.Size);
    }

    static string HiResLabel(SlskdFile f) => f.BitDepth is { } d && f.SampleRate is { } r ? $"{d}-bit / {r / 1000.0:0.#} kHz" : f.SampleRate is { } r2 ? $"{r2 / 1000.0:0.#} kHz" : f.BitDepth is { } d2 ? $"{d2}-bit" : "lossless";

    /// <summary>The explicit Hi-Res download of one song (administrators only, per song, never automatic): Soulseek is searched for a Hi-Res copy first and, when there is none,
    /// the normal download runs instead (the same sources and order as a plain Download), so the song still arrives, just not in Hi-Res.</summary>
    public async Task DownloadHiResAsync(string artist, string title, string album, string? artUrl, long durationMs, Action<DownloadStatus> on, CancellationToken ct = default, bool fallbackToNormal = true)
    {
        on(new(DownloadStage.Requested, $"Requested \"{title}\" in Hi-Res"));
        durationMs = await LengthOf(artist, title, durationMs, ct);
        if (cfg.SoulseekReady)
        {
            if (await TrySoulseek(artist, title, album, artUrl, (int)(durationMs / 1000), on, ct, hiRes: true)) return;
            if (!fallbackToNormal) { on(new(DownloadStage.Failed, "No Hi-Res copy of this song was found on Soulseek. Nothing was changed.")); return; }
            on(new(DownloadStage.Searching, "No Hi-Res copy was found, so the normal quality is downloaded instead", "soulseek", null, true));
        }
        else on(new(DownloadStage.Searching, "Hi-Res needs Soulseek, so the normal quality is downloaded instead", null, null, true));
        await DownloadAsync(artist, title, album, artUrl, durationMs, on, ct);
    }

    async Task<List<Track>> TrySoulseekAlbum(CatalogAlbum album, List<CatalogSong> list, Action<DownloadStatus> on, CancellationToken ct)
    {
        var sl = Slskd; var name = album.CleanTitle;
        var got = new SlskdFile?[list.Count];
        void Status(string m) => on(new(DownloadStage.Downloading, m, "soulseek"));
        try
        {
            on(new(DownloadStage.Searching, $"Searching Soulseek for \"{name}\"...", "soulseek"));
            // the full artist name can be a phrase Soulseek refuses to search (see SearchPlan): fall back to the artist's first word, then the album alone
            var albumName = Regex.Replace(name, @"s*[([][^)]]*[)]]", "").Trim();
            var aw = Matching.PrimaryArtist(album.Artist).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var plan = new List<(string, int)> { ($"{string.Join(' ', aw)} {albumName}".Trim(), 15_000) };
            if (aw.Length > 1) plan.Add(($"{aw[0]} {albumName}", 10_000));
            if (albumName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2) plan.Add((albumName, 10_000));
            var folders = new List<SlskdFile?[]>();
            foreach (var (query, timeout) in plan)
            {
                var files = await sl.SearchAsync(query, timeout, enough: 80, ct: ct);
                folders = SlskdClient.RankAlbumFolders(files, album.Artist, name, list.Select(s => s.Title).ToList());
                if (folders.Count > 0) break;
            }
            foreach (var folder in folders.Take(3))
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

    /// <summary>Downloads one song from the best candidates: up to [maxAtOnce] peers at once (three, two for Hi-Res) until one is sending, a peer dropped
    /// when it fails or goes silent (see <see cref="PeerRace"/>), the rest cancelled once one finishes. Null if none delivered. [bytes] hears how the
    /// leading transfer is going (the race estimates a Hi-Res copy from it). [patient] is the upgrade worker: no overall time limit while a peer is
    /// sending. Cancelled (the race was settled elsewhere), the transfers still running are stopped in slskd too, not left to finish unseen.</summary>
    static async Task<SlskdFile?> FetchFirst(SlskdClient sl, List<SlskdFile> cands, Action<string> progress, CancellationToken ct, int maxAtOnce = PeerRace.MaxAtOnce, Action<long, long>? bytes = null, bool patient = false)
    {
        var active = new List<Active>();
        try { return await FetchFirstCore(sl, cands, progress, active, ct, maxAtOnce, bytes, patient); }
        finally { foreach (var a in active.ToList()) { active.Remove(a); if (a.Id is not null) await sl.CancelAsync(a.F.Username, a.Id); } }
    }

    static async Task<SlskdFile?> FetchFirstCore(SlskdClient sl, List<SlskdFile> cands, Action<string> progress, List<Active> active, CancellationToken ct, int maxAtOnce, Action<long, long>? bytes, bool patient)
    {
        var queue = new Queue<SlskdFile>(cands.Take(8));
        async Task Drop(Active a) { active.Remove(a); if (a.Id is not null) await sl.CancelAsync(a.F.Username, a.Id); }
        async Task StartNext()
        {
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                try { await sl.DownloadAsync([f], ct); active.Add(new Active(f, Environment.TickCount64)); return; } catch (OperationCanceledException) { throw; } catch (Exception) { }
            }
        }
        while (PeerRace.StartAnother(active.Count, false, queue.Count, maxAtOnce)) { var before = active.Count; await StartNext(); if (active.Count == before) break; }
        var deadline = Environment.TickCount64 + 8 * 60_000;
        while (active.Count > 0 && (Environment.TickCount64 < deadline || (patient && active.Any(x => x.Bytes > 0))))
        {
            await Task.Delay(1000, ct);
            var now = Environment.TickCount64;
            foreach (var a in active.ToList())
            {
                var t = (await sl.TransfersAsync(a.F.Username, ct)).GetValueOrDefault(a.F.Filename);
                if (t is not null) a.Id = t.Id;
                if (t?.Done == true) { active.Remove(a); bytes?.Invoke(a.F.Size, a.F.Size); foreach (var o in active.ToList()) await Drop(o); return a.F; }
                if (t?.Done == false) await Drop(a);
                else if (t is not null && t.Bytes > a.Bytes) { a.Bytes = t.Bytes; a.Moved = now; }
                else if (PeerRace.GiveUp(a.Bytes, now - a.Started, now - a.Moved, queue.Count > 0 || active.Count > 1)) await Drop(a);
            }
            // more peers while none is sending yet, and a dropped one replaced
            while (PeerRace.StartAnother(active.Count, active.Any(x => x.Bytes > 0), queue.Count, maxAtOnce)) { var before = active.Count; await StartNext(); if (active.Count == before) break; }
            if (active.MaxBy(x => x.Bytes) is { } best)
            {
                bytes?.Invoke(best.Bytes, best.F.Size);
                progress(best.Bytes == 0 ? "Waiting for a peer..." : $"Downloading {Math.Min(99, best.Bytes * 100 / Math.Max(1, best.F.Size))}%...");
            }
        }
        return null;   // the transfers still running are stopped by FetchFirst
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
    static readonly AsyncLocal<string?> FileWhy = new();
    async Task<Track?> FileAsync(SlskdFile hit, string artist, string title, string album, int trackNo, int discNo, long durationMs, string name, string? albumFolder, string trackArtist, string? artUrl, CancellationToken ct, string? destination = null)
    {
        var parts = hit.Filename.Replace('\\', '/').Split('/').Where(p => p.Trim().Length > 0).ToArray();
        var inbox = cfg.SlskdPath.Trim('/');
        var source = parts.Length >= 2 ? $"{inbox}/{parts[^2]}/{parts[^1]}" : $"{inbox}/{parts[^1]}";
        var ext = parts[^1].Contains('.') ? parts[^1][(parts[^1].LastIndexOf('.') + 1)..] : "mp3";
        // an upgrade says exactly where (next to the file it replaces); everything else goes in the artist's folder
        var dest = destination ?? string.Join("/", new[] { cfg.NasFolder.Trim('/'), Clean(artist), albumFolder }.Where(x => !string.IsNullOrEmpty(x))) + $"/{name}.{ext}";
        try
        {
            // slskd reports a transfer complete a moment before it has moved the file from its incomplete folder into the downloads folder, so the first
            // ask can find nothing there (seen: "Filing into the library..." then "had nothing it could finish" on songs that did arrive): ask again for up to ~16 s
            var moved = false;
            for (var attempt = 0; attempt < 8 && !moved; attempt++)
            {
                if (attempt > 0) await Task.Delay(2000, ct);
                using var req = new HttpRequestMessage(HttpMethod.Post, cfg.FileMoverUrl.TrimEnd('/') + "/move") { Content = new StringContent(new JsonObject { ["from"] = source, ["to"] = dest }.ToJsonString(), Encoding.UTF8, "application/json") };
                req.Headers.TryAddWithoutValidation("X-Api-Key", cfg.FileMoverKey);
                using var res = await http.SendAsync(req, ct);
                if (res.IsSuccessStatusCode) { moved = true; break; }
                var said = await res.Content.ReadAsStringAsync(ct);
                if (!said.Contains("source not found", StringComparison.OrdinalIgnoreCase)) { FileWhy.Value = "the file mover said: " + (said.Length > 160 ? said[..160] : said); return null; }   // any other refusal will not change by waiting
            }
            if (!moved) { FileWhy.Value ??= "the file never showed up in slskd's downloads folder"; return null; }
            return new Track($"{cfg.FileMoverUrl.TrimEnd('/')}/file?path={Uri.EscapeDataString(dest)}", title, trackArtist, album, artist, trackNo, discNo, durationMs, 0,
                artUrl is null ? Art.LookupKey(artist, album, title) : Art.ExternalKey(artUrl), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TrackSource.Cloud);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { FileWhy.Value = "the file mover was unreachable: " + e.Message; return null; }
    }
}

/// <summary>Cover keys for images that live on a public catalog (Deezer, iTunes), served through the app's own /art.</summary>
public static class Art
{
    public static string ExternalKey(string url) => "ex" + Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    /// <summary>A cover to be looked up in the public catalog by artist, album and title (same key format as the Android app).</summary>
    /// <summary>The cover to show for a song: its own, or (when a download left none) one looked up in the public catalog by artist, album and title.</summary>
    public static string? KeyFor(Track t) => t.ArtKey ?? LookupKey(t.Artist, t.Album, t.Title);

    public static string? LookupKey(string artist, string album, string title) => LookupKey(artist, album, title, null);

    /// <summary>A catalog lookup key that also carries a Jellyfin picture ([fallback], "jf" + id + "~" + tag) to show only when the catalog has nothing: songs that are
    /// in no catalog (TikTok "hoodtrap" remixes in a playlist folder) then show their folder's picture instead of none (owner, 5 Nov). Only a tagged Jellyfin key is
    /// carried (a picture that cannot change under it). The catalog is asked, and the cover saved, by the key without it (<see cref="LookupBase"/>), so covers
    /// already found keep their files.</summary>
    public static string? LookupKey(string artist, string album, string title, string? fallback)
    {
        if (artist.Length == 0 || (album.Length == 0 && title.Length == 0)) return null;
        var raw = $"{artist}\n{album}\n{title}"; if (raw.Length > 150) raw = raw[..150];
        // the fallbacks: Jellyfin pictures ("jf<item>~<tag>"; "jd<folder>", a folder's picture without a tag), then a Jellyfin artist's photo ("ja<artist>"),
        // "|" between them (F1). An untagged "jf" key is never carried (it could be a picture that was vetted out).
        static bool Ok(string x) => x.Length > 2 && !x.Contains('\n') && (x.StartsWith("jf", StringComparison.Ordinal) ? x.IndexOf('~') > 2 : x.StartsWith("jd", StringComparison.Ordinal) || x.StartsWith("ja", StringComparison.Ordinal));
        if (fallback is { Length: < 200 } f && raw.Split('\n').Length == 3 && f.Split('|').All(Ok))
            raw += "\n" + f;
        return "it" + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    static string[]? LookupParts(string key)
    {
        if (!key.StartsWith("it")) return null;
        var b = key[2..].Replace('-', '+').Replace('_', '/'); b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
        try { var p = Encoding.UTF8.GetString(Convert.FromBase64String(b)).Split('\n'); return p.Length >= 3 ? p : null; } catch (Exception) { return null; }
    }

    /// <summary>The Jellyfin picture a lookup key carries for when the catalog has nothing, or null.</summary>
    public static string? LookupFallback(string key) => LookupFallbacks(key).FirstOrDefault(f => f.StartsWith("jf", StringComparison.Ordinal) && f.IndexOf('~') > 2);

    /// <summary>Every fallback a lookup key carries, in order (F1: the folder's picture, then the artist's photo).</summary>
    public static string[] LookupFallbacks(string key) => LookupParts(key) is { Length: >= 4 } p ? p[3].Split('|', StringSplitOptions.RemoveEmptyEntries) : [];

    /// <summary>The lookup key without its fallback picture: what the catalog is asked by and the cover is saved under.</summary>
    public static string LookupBase(string key) =>
        LookupParts(key) is { Length: >= 4 } p ? "it" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{p[0]}\n{p[1]}\n{p[2]}")).Replace('+', '-').Replace('/', '_').TrimEnd('=') : key;

    static readonly System.Text.RegularExpressions.Regex CompilationName = new(@"\b(now that'?s what i call|now \d+|various|ministry of sound|hits?|anthems?|summer|party|workout|karaoke|tribute|mix)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    /// <summary>A song filed on a compilation ("Now That's What I Call Music 84", "Various Artists"): its embedded cover is the compilation's, not the song's own album.</summary>
    public static bool OnCompilation(string artist, string albumArtist, string album) =>
        albumArtist.Contains("various", StringComparison.OrdinalIgnoreCase)
        || (album.Length > 0 && CompilationName.IsMatch(album) && albumArtist.Length > 0 && Matching.PrimaryArtist(albumArtist) != Matching.PrimaryArtist(artist));

    public static (string Artist, string Album, string Title)? ParseLookupKey(string key) => LookupParts(key) is { } p ? (p[0], p[1], p[2]) : null;

    static readonly System.Text.RegularExpressions.Regex SizedPath = new(@"/(\d{3,4})x(\d{3,4})(bb|-)", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>The same catalogue cover at 250 px (Deezer's and iTunes' addresses name the size): what a list row or a card shows, a twentieth of the bytes of the
    /// 1000 px one (covers took ages on a weak signal; owner, 1 Nov). The phone has the same rule (CoverLookup.small).</summary>
    public static string SmallExternal(string url) => SizedPath.Replace(url, m => int.Parse(m.Groups[1].Value) > 250 ? "/250x250" + m.Groups[3].Value : m.Value);

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
            // two minutes to be grabbed; once Lidarr's queue has it the wait follows the download (a torrent or usenet grab often takes longer than two minutes,
            // and a song that arrived minutes later used to be logged as "Not found"), up to 30 minutes
            var seen = false; var deadline = Environment.TickCount64 + 120_000; var hardStop = Environment.TickCount64 + 30 * 60_000;
            while (Environment.TickCount64 < deadline)
            {
                var items = (await Call(HttpMethod.Get, "/api/v1/queue?pageSize=200", null, ct))?["records"] as JsonArray ?? [];
                // by artist, not album: Lidarr may grab a different release (a "Best Of") that has the same song
                var item = items.OfType<JsonObject>().FirstOrDefault(o => o["artistId"]?.GetValue<int>() == artistId);
                if (item is not null)
                {
                    seen = true;
                    deadline = Math.Min(hardStop, Environment.TickCount64 + 150_000);
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
