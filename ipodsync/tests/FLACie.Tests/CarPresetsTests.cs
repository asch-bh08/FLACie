using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

public class CarPresetsTests
{
    static Track T(string title, string genre) =>
        new("p/" + title, title, "Ann", "Alb", "Ann", 1, 1, 200_000, 2000, null, 0, TrackSource.Jellyfin, Genre: genre);

    static Track[] Many(string genre, int n) => Enumerable.Range(0, n).Select(i => T($"{genre}{i}", genre)).ToArray();

    [Fact] public void TheBiggestGenresInOrderAsShortCapitalNames()
    {
        Track[] songs = [.. Many("Pop", 5), .. Many("Rock", 9), .. Many("Dance", 3), .. Many("Jazz", 1)];
        var s = CarPresets.Stations(songs);
        Assert.Equal(["ROCK", "POP", "DANCE", "JAZZ"], s.Select(x => x.Label).ToArray());
        Assert.Equal([9, 5, 3, 1], s.Select(x => x.Tracks.Count).ToArray());
        Assert.All(s[0].Tracks, t => Assert.Equal("Rock", t.Genre));
    }

    [Fact] public void AtMostSixAndTiesByName()
    {
        string[] g = ["Metal", "Folk", "Blues", "Soul", "Funk", "Disco", "Punk"];
        var s = CarPresets.Stations(g.SelectMany(x => Many(x, 2)).ToArray());
        Assert.Equal(["BLUES", "DISCO", "FOLK", "FUNK", "METAL", "PUNK"], s.Select(x => x.Label).ToArray());
    }

    [Fact] public void NoGenreIsNoStationAndOneGenreSpelledTwoWaysIsOne()
    {
        Track[] songs = [T("a", ""), T("b", "  "), T("c", "Unknown"), T("d", "pop"), T("e", "Pop "), T("f", "POP")];
        var s = CarPresets.Stations(songs);
        Assert.Single(s);
        Assert.Equal("POP", s[0].Label); Assert.Equal(3, s[0].Tracks.Count);
    }

    [Fact] public void ACompoundTagFilesUnderItsFirstGenre()
    {
        var s = CarPresets.Stations([T("a", "Pop; Dance"), T("b", "Pop/Rock"), T("c", "Pop, Synth")]);
        Assert.Single(s); Assert.Equal("POP", s[0].Label); Assert.Equal(3, s[0].Tracks.Count);
    }

    [Theory]
    [InlineData("Alternative", "ALT")]
    [InlineData("Electronic", "ELECTRO")]
    [InlineData("Classical", "CLASSIC")]
    [InlineData("Soundtrack", "OST")]
    [InlineData("Hip-Hop", "HIP-HOP")]
    [InlineData("Singer-Songwriter", "SINGER")]
    [InlineData("Contemporary Christian", "CONTEMPO")]
    [InlineData("R&B", "R&B")]
    public void LabelsFitAPresetKey(string genre, string label)
    {
        Assert.Equal(label, CarPresets.Label(genre));
        Assert.True(CarPresets.Label(genre).Length <= 8);
    }

    [Fact] public void AnEmptyLibraryHasNoStations() => Assert.Empty(CarPresets.Stations([]));
}
