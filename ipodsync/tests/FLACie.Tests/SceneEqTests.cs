using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The listening scenes' sound presets (the car's EQ key, the MiniDisc's SOUND key): named settings of the player's own five-band EQ, stepped through
/// one press at a time; and the short format line their small displays have room for.</summary>
public class SceneEqTests
{
    [Fact] public void EveryPresetSetsAllFiveBandsWithinTheEqsRange()
    {
        Assert.Equal("FLAT", SceneEq.Presets[0].Name);
        Assert.All(SceneEq.Presets, p => { Assert.Equal(5, p.Bands.Length); Assert.All(p.Bands, b => Assert.InRange(b, -12, 12)); });
        Assert.Equal(SceneEq.Presets.Count, SceneEq.Presets.Select(p => p.Name).Distinct().Count());
        Assert.Equal(SceneEq.Presets.Count, SceneEq.Presets.Select(p => string.Join(",", p.Bands)).Distinct().Count());
    }

    [Fact] public void BandsAreNamedForTheirPresetOrCustom()
    {
        Assert.Equal("FLAT", SceneEq.NameOf([0, 0, 0, 0, 0]));
        Assert.Equal("ROCK", SceneEq.NameOf(SceneEq.Presets.First(p => p.Name == "ROCK").Bands));
        Assert.Equal("CUSTOM", SceneEq.NameOf([1, 0, 0, 0, 0]));
        Assert.Equal("FLAT", SceneEq.NameOf(null));   // (not read yet: the EQ starts flat)
        Assert.Equal("FLAT", SceneEq.NameOf([0, 0]));
    }

    [Fact] public void EachPressGoesOnToTheNextPresetAndRoundToFlat()
    {
        var at = new[] { 0, 0, 0, 0, 0 };
        var seen = new List<string>();
        for (var i = 0; i < SceneEq.Presets.Count; i++) { at = SceneEq.Next(at, null); seen.Add(SceneEq.NameOf(at)); }
        Assert.Equal([.. SceneEq.Presets.Skip(1).Select(p => p.Name), "FLAT"], seen);
    }

    [Fact] public void ASettingMadeByHandIsKeptAsCustomAtTheEndOfTheRound()
    {
        int[] mine = [4, -2, 0, 3, 6];
        var at = SceneEq.Next(mine, mine);
        Assert.Equal("FLAT", SceneEq.NameOf(at));
        for (var i = 1; i < SceneEq.Presets.Count; i++) at = SceneEq.Next(at, mine);
        Assert.Equal("BASS", SceneEq.NameOf(at));   // (the last preset)
        at = SceneEq.Next(at, mine);
        Assert.Equal(mine, at);                      // back to your own
        Assert.Equal("FLAT", SceneEq.NameOf(SceneEq.Next(at, mine)));
    }

    [Fact] public void NextNeverHandsOutAPresetsOwnArray()
    {
        var a = SceneEq.Next([0, 0, 0, 0, 0], null); a[0] = 99;
        Assert.NotEqual(99, SceneEq.Presets[1].Bands[0]);
    }

    static Track T(string path = "/music/a.flac", long size = 0, long ms = 200_000) =>
        new(path, "A", "Ann", "Alb", "Ann", 1, 1, ms, 2000, null, 0, TrackSource.Jellyfin, FilePath: path, Size: size);
    static MediaInfo I(string codec, int kbps, int rate, int depth) => new("flac", codec, kbps, rate, depth, 2, 0, 0, "", "jellyfin");

    [Fact] public void TheShortFormatLineForASmallDisplay()
    {
        Assert.Equal("FLAC 24/96 2304K", DeskFormat.Short(I("flac", 2304, 96_000, 24), T()));
        Assert.Equal("FLAC 16/44.1 1000K", DeskFormat.Short(I("flac", 1000, 44_100, 16), T()));
        Assert.Equal("MP3 44.1 320K", DeskFormat.Short(I("mp3", 320, 44_100, 0), T("/m/a.mp3")));
        Assert.Equal("ALAC 24/48", DeskFormat.Short(I("alac", 0, 48_000, 24), T("/m/a.m4a")));
        Assert.Equal("FLAC ~1000K", DeskFormat.Short(null, T(size: 25_000_000)));
        Assert.Equal("FLAC", DeskFormat.Short(null, T()));
        Assert.Equal("", DeskFormat.Short(null, T("/m/noext", ms: 0)));
    }
}
