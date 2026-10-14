using System.Diagnostics;
using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server.Alexa;
using Xunit;

namespace FLACie.Tests;

public class AlexaCatalogTests
{
    static Track T(string title, string artist, string album = "Alb", string? jf = null, long added = 0) =>
        new($"/p/{artist}/{title}", title, artist, album, artist, 1, 1, 1000, 2000, null, added, TrackSource.Jellyfin, JellyfinId: jf ?? Guid.NewGuid().ToString("N"));
    static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    static JsonArray Ents(CatalogExport c, string type) => (JsonArray)c.Catalogs[type]["entities"]!;

    [Fact] public void CountsAndShapeFollowTheCatalogFormat()
    {
        var songs = new[] { T("Blinding Lights", "The Weeknd", "After Hours"), T("Save Your Tears", "The Weeknd", "After Hours"), T("Creep", "Radiohead", "Pablo Honey") };
        var pls = new[] { new Playlist("p1", "Road Trip 2024", 0, null, [new("/p/x", "Creep", "Radiohead")]), new Playlist("p2", "Empty", 0, null, []) };
        var c = AlexaCatalog.Build(songs, pls);
        Assert.Equal(3, c.Counts[AlexaCatalog.Recording]); Assert.Equal(2, c.Counts[AlexaCatalog.Album]); Assert.Equal(2, c.Counts[AlexaCatalog.Group]); Assert.Equal(1, c.Counts[AlexaCatalog.PlaylistType]);
        var cat = c.Catalogs[AlexaCatalog.Recording];
        Assert.Equal("AMAZON.MusicRecording", (string?)cat["type"]); Assert.Equal("2.0", (string?)cat["version"]);
        Assert.Equal("en", (string?)cat["locales"]![0]!["language"]); Assert.Equal("US", (string?)cat["locales"]![0]!["country"]);
        var e = Ents(c, AlexaCatalog.Recording).OfType<JsonObject>().First(x => (string?)x["names"]![0]!["value"] == "Blinding Lights");
        Assert.StartsWith("flacie:t:", (string?)e["id"]);
        Assert.Equal("The Weeknd", (string?)e["artists"]![0]!["names"]![0]!["value"]); Assert.Equal("After Hours", (string?)e["albums"]![0]!["names"]![0]!["value"]);
        Assert.InRange((int)e["popularity"]!["default"]!, 1, 100); Assert.EndsWith("Z", (string?)e["lastUpdatedTime"]);
    }

    [Fact] public void EveryEntityMeetsTheDocumentedLimits()
    {
        var songs = new[] { T(new string('x', 600), "A"), T("Hey Jude - Remastered 2015", "The Beatles"), T("Umbrella (feat. Jay-Z)", "Rihanna"), T("XTC song", "XTC"), T("Satisfaction", "AC/DC") };
        var c = AlexaCatalog.Build(songs, []);
        var ids = new HashSet<string>();
        foreach (var type in c.Catalogs.Keys)
            foreach (var e in Ents(c, type).OfType<JsonObject>())
            {
                var id = (string)e["id"]!; Assert.InRange(id.Length, 1, 256); Assert.True(ids.Add(id), "ids are unique across catalogs: " + id);
                var name = (string)e["names"]![0]!["value"]!; Assert.InRange(name.Length, 1, 512);
                if (e["alternateNames"] is JsonArray an) { var values = an[0]!["values"]!.AsArray(); Assert.InRange(values.Count, 1, 8); Assert.All(values, v => Assert.NotEqual(name.ToLowerInvariant(), ((string)v!).ToLowerInvariant())); }
            }
    }

    [Fact] public void SpokenAliasesAreIncluded()
    {
        var c = AlexaCatalog.Build([T("Making Plans", "XTC"), T("Hey Jude - Remastered 2015", "The Beatles")], []);
        string[] Alt(string type, string name) => Ents(c, type).OfType<JsonObject>().First(x => (string?)x["names"]![0]!["value"] == name)["alternateNames"]![0]!["values"]!.AsArray().Select(v => (string)v!).ToArray();
        Assert.Contains("x t c", Alt(AlexaCatalog.Group, "XTC"));
        Assert.Contains("beatles", Alt(AlexaCatalog.Group, "The Beatles"));
        Assert.Equal("Hey Jude", (string?)Ents(c, AlexaCatalog.Recording).OfType<JsonObject>().First(x => ((string)x["names"]![0]!["value"]!).StartsWith("Hey"))["names"]![0]!["value"]);
    }

    [Fact] public void LiveAndRemasterCopiesAreOneSong()
    {
        var c = AlexaCatalog.Build([T("Heroes", "David Bowie"), T("Heroes (Live)", "David Bowie"), T("Heroes - 2017 Remaster", "David Bowie"), T("Heroes", "Peter Gabriel")], []);
        Assert.Equal(2, c.Counts[AlexaCatalog.Recording]); Assert.Equal(2, c.Counts[AlexaCatalog.Group]);
    }

    [Fact] public void IdsAreStableAcrossExportsAndTheSecondDeltaIsEmpty()
    {
        var songs = new[] { T("A", "X", jf: "aaaa"), T("B", "X", jf: "bbbb"), T("C", "Y", jf: "cccc") };
        var one = AlexaCatalog.Build(songs, []); var two = AlexaCatalog.Build(songs.Reverse().ToArray(), []);
        Assert.Equal(one.Hashes.OrderBy(x => x.Key), two.Hashes.OrderBy(x => x.Key));
        Assert.Contains("flacie:t:aaaa", one.Hashes.Keys);
        var delta = AlexaCatalog.Delta(two, one.Hashes, Now);
        Assert.All(delta.Values, d => Assert.Empty((JsonArray)d["entities"]!));
    }

    [Fact] public void DeltaCarriesNewChangedAndDeletedEntities()
    {
        var before = AlexaCatalog.Build([T("A", "X", jf: "aaaa"), T("B", "X", jf: "bbbb"), T("C", "Y", jf: "cccc")], []);
        var after = AlexaCatalog.Build([T("A", "X", jf: "aaaa"), T("B2", "X", jf: "bbbb"), T("D", "Z", jf: "dddd")], []);   // B renamed, C gone, D new
        var d = AlexaCatalog.Delta(after, before.Hashes, Now);
        var rec = ((JsonArray)d[AlexaCatalog.Recording]["entities"]!).OfType<JsonObject>().ToList();
        Assert.Equal(["flacie:t:bbbb", "flacie:t:cccc", "flacie:t:dddd"], rec.Select(e => (string)e["id"]!).OrderBy(x => x).ToArray());
        var gone = rec.Single(e => (string)e["id"]! == "flacie:t:cccc"); Assert.True((bool)gone["deleted"]!); Assert.Null(gone["names"]); Assert.EndsWith("Z", (string?)gone["lastUpdatedTime"]);
        var artists = ((JsonArray)d[AlexaCatalog.Group]["entities"]!).OfType<JsonObject>().ToList();
        Assert.Contains(artists, e => (bool?)e["deleted"] == true);   // Y has no songs left
        Assert.Contains(artists, e => (string?)e["names"]?[0]?["value"] == "Z");
    }

    [Fact] public void TenThousandSongsExportQuicklyFromMemoryAndStayUnderTheLimit()
    {
        var rnd = new Random(3);
        var songs = Enumerable.Range(0, 10_000).Select(i => T($"Song {i} {rnd.Next(100)}", $"Band {i % 400}", $"Album {i % 1200}", added: 1_700_000_000_000 + i)).ToList();
        var pls = Enumerable.Range(0, 30).Select(i => new Playlist("p" + i, "List " + i, 0, null, songs.Take(5).Select(t => new PlaylistEntry(t.Path, t.Title, t.Artist)).ToList())).ToList();
        var sw = Stopwatch.StartNew(); var c = AlexaCatalog.Build(songs, pls); sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");
        Assert.Equal(10_000, c.Counts[AlexaCatalog.Recording]); Assert.Equal(400, c.Counts[AlexaCatalog.Group]); Assert.Equal(1200, c.Counts[AlexaCatalog.Album]);
        Assert.All(c.Counts.Values, n => Assert.True(n < AlexaCatalog.MaxEntities));
        var again = Stopwatch.StartNew(); var d = AlexaCatalog.Delta(AlexaCatalog.Build(songs, pls), c.Hashes, Now); again.Stop();
        Assert.All(d.Values, x => Assert.Empty((JsonArray)x["entities"]!));
        Console.WriteLine($"CATALOG 10k: build {sw.ElapsedMilliseconds} ms, rebuild+delta {again.ElapsedMilliseconds} ms, recording json {c.Catalogs[AlexaCatalog.Recording].ToJsonString().Length / 1024} KB");
    }
}
