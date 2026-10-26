// Offline evaluation of recommendations on a COPY of the server's data folder (never the live one): replays each account's listening and prints
// the hit rate at 10 of the old ranker and the new engine. It only reads its argument folder.
//
//   dotnet run --project ipodsync/tools/receval -- <copy of data> [max cases per account]
//
// Listening comes from listen/<account>.jsonl when an account has a log; otherwise from jellyfin/plays.tsv (user, time, Jellyfin item id: the
// playback lines of Jellyfin's activity log, exported read-only). vibe.json is used when there is one; without it the engine goes on genre and artist.
using System.Globalization;
using System.Text.Json;
using FLACie.Core;

if (args.Length < 1 || !Directory.Exists(args[0])) { Console.Error.WriteLine("usage: receval <copy of data folder> [max cases per account]"); return 2; }
var dir = args[0];
var max = 400;
if (args.Length > 1 && (!int.TryParse(args[1], out max) || max < 1)) { Console.Error.WriteLine($"max cases must be a positive number, not \"{args[1]}\""); return 2; }
var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// anything missing or damaged is said and skipped, so the tool prints what it can
var libs = new Dictionary<string, Library>();
var libDir = Path.Combine(dir, "library");
if (!Directory.Exists(libDir)) Console.WriteLine("missing: library/ (no libraries, nothing to evaluate)");
else
    foreach (var f in Directory.EnumerateFiles(libDir, "*.v2.json"))
        try { if (JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(f)) is { } songs) libs[Path.GetFileName(f)[..^".v2.json".Length]] = new Library(songs); }
        catch (Exception e) when (e is JsonException or IOException or NotSupportedException) { Console.WriteLine($"skipped damaged library {Path.GetFileName(f)}: {e.Message}"); }
var vibeFile = Path.Combine(dir, "vibe.json");
var vibes = new Dictionary<string, Vibe>();
if (!File.Exists(vibeFile)) Console.WriteLine("missing: vibe.json");
else
    try { vibes = JsonSerializer.Deserialize<Dictionary<string, Vibe>>(File.ReadAllText(vibeFile)) ?? []; }
    catch (Exception e) when (e is JsonException or IOException or NotSupportedException) { Console.WriteLine($"skipped damaged vibe.json: {e.Message}"); }
Console.WriteLine($"libraries {libs.Count}, vibes {vibes.Count}{(vibes.Count == 0 ? " (vibe-less: genre and artist only)" : "")}");
if (libs.Count == 0) { Console.WriteLine("no usable library: nothing to evaluate"); return 1; }

// each account: its library and what it played, as events
var accounts = new List<(string Name, Library Lib, List<ListenEvent> Events)>();
var listenDir = Path.Combine(dir, "listen");
if (!Directory.Exists(listenDir)) Console.WriteLine("missing: listen/");
else
    foreach (var f in Directory.EnumerateFiles(listenDir, "*.jsonl"))
    {
        var id = Path.GetFileNameWithoutExtension(f);
        if (!libs.TryGetValue(id, out var lib)) { Console.WriteLine($"skipped listen log {id}: no library for it"); continue; }
        var evs = File.ReadLines(f).Select(l => { try { return JsonSerializer.Deserialize<ListenEvent>(l, web); } catch (JsonException) { return null; } }).OfType<ListenEvent>().ToList();
        if (evs.Count > 0) accounts.Add((id.Length > 8 ? id[..8] : id, lib, evs));
    }
var tsv = Path.Combine(dir, "jellyfin", "plays.tsv");
if (accounts.Count == 0 && !File.Exists(tsv)) Console.WriteLine("missing: jellyfin/plays.tsv (and no listen logs): no listening to replay");
if (accounts.Count == 0 && File.Exists(tsv))
{
    var bad = 0;
    foreach (var g in File.ReadLines(tsv).Select(l => l.Split('\t')).Where(p => p.Length == 3 || bad++ < 0).GroupBy(p => p[0]))
    {
        var rows = g.ToList();
        // the library that knows most of this person's songs
        var lib = libs.Values.MaxBy(l => rows.Count(r => l.ByJellyfinId(r[2]) is not null))!;
        var evs = new List<ListenEvent>();
        foreach (var r in rows)
        {
            if (lib.ByJellyfinId(r[2]) is not { } t) continue;
            if (!DateTime.TryParse(r[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)) { bad++; continue; }
            var at = new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            var len = t.DurationMs / 1000.0;
            evs.Add(new ListenEvent($"jf:{r[1]}:{r[2]}", ListenKind.Play, at, t.Path, t.Title, t.Artist, len, len, "history"));
        }
        accounts.Add((g.Key, lib, evs));
    }
    if (bad > 0) Console.WriteLine($"skipped {bad} unreadable rows in plays.tsv");
}

foreach (var (name, lib, events) in accounts)
{
    var ordered = events.OrderBy(e => e.At).ToList();
    var plays = ordered.Where(e => e.Kind == ListenKind.Play).Select(e => (lib.Resolve(e.Path, e.Title, e.Artist), e.At)).Where(p => p.Item1 is not null).Select(p => (p.Item1!, p.At));
    // the latest cases, so each has the most history before it; at least a day of history before the first
    var first = ordered.Count > 0 ? ordered[0].At : 0;
    var cases = RecEval.Cases(plays).Where(c => c.At - first > 86_400_000).TakeLast(max).ToList();
    if (cases.Count == 0) { Console.WriteLine($"{name}: no cases"); continue; }
    // taste from what happened strictly before each case, never after
    var at = new Dictionary<IReadOnlyList<Track>, (TasteProfile Taste, DateTime Now)>(ReferenceEqualityComparer.Instance);
    foreach (var c in cases)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(c.At).UtcDateTime;
        at[c.Session] = (new TasteProfile(lib, [], [], [], now, events: ordered.TakeWhile(e => e.At < c.At).ToList()), now);
    }
    var pairs = cases.Select(c => (c.Session, c.Next)).ToList();
    if (Environment.GetEnvironmentVariable("RECEVAL_WHY") is not null)
    {
        int heard = 0, recent4 = 0, sameArtist = 0, sameAlbum = 0, genreless = 0, sameGenre = 0;
        foreach (var c in cases)
        {
            var (tp, now) = at[c.Session]; var k = Matching.MatchKey(c.Next);
            if (tp.Known(c.Next) > 0) heard++;
            if (tp.PlayedWithin(c.Next, now, 4)) recent4++;
            if (Matching.PrimaryArtist(c.Next.Artist) == Matching.PrimaryArtist(c.Session[^1].Artist)) sameArtist++;
            if (c.Next.AlbumKey == c.Session[^1].AlbumKey) sameAlbum++;
            if (c.Next.Genre.Length == 0) genreless++;
            else if (c.Next.Genre == c.Session[^1].Genre) sameGenre++;
        }
        Console.WriteLine($"  why: heard {heard}, within4h {recent4}, sameArtist {sameArtist}, sameAlbum {sameAlbum}, genreless {genreless}, sameGenre {sameGenre}, songs {lib.Songs.Count}");
    }
    var old = RecEval.HitRateAt10(s => Recommender.Recommend(lib, at[s].Taste, at[s].Now, 10, 0, 2, s.Select(Matching.MatchKey)), pairs);
    var neu = RecEval.HitRateAt10(s => Engine.Next(lib, at[s].Taste, t => vibes.GetValueOrDefault(t.Path), s, [], at[s].Now, new EngineOptions())
        .Select(p => p.Track).OfType<Track>().ToList(), pairs);
    int Hits(double r) => (int)Math.Round(r * cases.Count);
    Console.WriteLine($"{name}: {cases.Count} cases from {ordered.Count} events, old {old:0.000} ({Hits(old)}/{cases.Count}), new {neu:0.000} ({Hits(neu)}/{cases.Count}), ratio {(old > 0 ? (neu / old).ToString("0.00") : "n/a (old has no hits)")}");
}
return 0;
