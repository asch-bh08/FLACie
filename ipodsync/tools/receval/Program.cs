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
var max = args.Length > 1 ? int.Parse(args[1]) : 400;
var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var libs = Directory.EnumerateFiles(Path.Combine(dir, "library"), "*.v2.json")
    .ToDictionary(f => Path.GetFileName(f)[..^".v2.json".Length], f => new Library(JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(f))!));
var vibeFile = Path.Combine(dir, "vibe.json");
var vibes = File.Exists(vibeFile) ? JsonSerializer.Deserialize<Dictionary<string, Vibe>>(File.ReadAllText(vibeFile))! : [];
Console.WriteLine($"libraries {libs.Count}, vibes {vibes.Count}{(vibes.Count == 0 ? " (vibe-less: genre and artist only)" : "")}");

// each account: its library and what it played, as events
var accounts = new List<(string Name, Library Lib, List<ListenEvent> Events)>();
var listenDir = Path.Combine(dir, "listen");
if (Directory.Exists(listenDir))
    foreach (var f in Directory.EnumerateFiles(listenDir, "*.jsonl"))
    {
        var id = Path.GetFileNameWithoutExtension(f);
        if (!libs.TryGetValue(id, out var lib)) continue;
        var evs = File.ReadLines(f).Select(l => { try { return JsonSerializer.Deserialize<ListenEvent>(l, web); } catch (JsonException) { return null; } }).OfType<ListenEvent>().ToList();
        if (evs.Count > 0) accounts.Add((id[..8], lib, evs));
    }
var tsv = Path.Combine(dir, "jellyfin", "plays.tsv");
if (accounts.Count == 0 && File.Exists(tsv))
    foreach (var g in File.ReadLines(tsv).Select(l => l.Split('\t')).Where(p => p.Length == 3).GroupBy(p => p[0]))
    {
        var rows = g.ToList();
        // the library that knows most of this person's songs
        var lib = libs.Values.MaxBy(l => rows.Count(r => l.ByJellyfinId(r[2]) is not null))!;
        var evs = new List<ListenEvent>();
        foreach (var r in rows)
        {
            if (lib.ByJellyfinId(r[2]) is not { } t) continue;
            var at = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Parse(r[1], CultureInfo.InvariantCulture), DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            var len = t.DurationMs / 1000.0;
            evs.Add(new ListenEvent($"jf:{r[1]}:{r[2]}", ListenKind.Play, at, t.Path, t.Title, t.Artist, len, len, "history"));
        }
        accounts.Add((g.Key, lib, evs));
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
    Console.WriteLine($"{name}: {cases.Count} cases from {ordered.Count} events, old {old:0.000}, new {neu:0.000}, ratio {(old > 0 ? (neu / old).ToString("0.00") : "inf")}");
}
return 0;
