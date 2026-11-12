using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>An imported song and the file downloaded for it, when their titles differ (owner, 6 Nov: "87, 11 still to come" in a playlist of 98).</summary>
public class LooseSameSongTests
{
    static Track T(string title, string artist) => new("/m/" + title, title, artist, "", artist, 1, 1, 1000, 1994, null, 0, TrackSource.Jellyfin);

    [Theory]
    [InlineData("Scatman (ski-ba-bop-ba-dop-bop)", "Scatman John", "Scatman", "Scatman John")]
    [InlineData("Gonna Make You Sweat (Everybody Dance Now) (feat. Freedom Williams)", "C & C Music Factory", "Gonna Make You Sweat", "C+C Music Factory")]
    [InlineData("Boom, Boom, Boom, Boom!!", "Vengaboys", "Boom Boom Boom Boom", "Vengaboys")]
    [InlineData("Holding Out for a Hero - From \"Footloose\" Soundtrack", "Bonnie Tyler", "Holding Out For A Hero", "Bonnie Tyler")]
    [InlineData("No Coke - 7'' Mix", "Dr. Alban", "No Coke", "Dr Alban")]
    [InlineData("The Riddle", "GIGI D'AGOSTINO", "The Riddle", "Gigi D'Agostino")]
    [InlineData("Dreamer - 2011 Remaster", "Livin' Joy", "Dreamer", "Livin' Joy")]
    [InlineData("Dreamer - Radio Edit", "Livin' Joy", "Dreamer", "Livin' Joy")]
    [InlineData("Dreamer (Janice Robinson Vocal)", "Livin' Joy", "Dreamer - Janice Robinson Vocal", "Livin' Joy")]
    public void The_same_song_with_a_longer_or_shorter_title(string title, string artist, string fileTitle, string fileArtist) =>
        Assert.True(Matching.SameSongLoosely(title, artist, T(fileTitle, fileArtist)));

    [Theory]
    [InlineData("Freed From Desire", "Gala", "Free", "Gala")]                       // too short a start to be sure
    [InlineData("The Rhythm of the Night", "Corona", "The Rhythm of the Night", "DeBarge")]
    [InlineData("Bad Boys", "Inner Circle", "Bad Girls", "Inner Circle")]
    // another mix or vocal is another recording (owner, 12 Nov: a playlist's "Dreamer - Re-Original 7-inch Mix" showed as plain "Dreamer" for a while)
    [InlineData("Dreamer - Re-Original 7-inch Mix", "Livin' Joy", "Dreamer", "Livin' Joy")]
    [InlineData("Dreamer - Janice Robinson Vocal", "Livin' Joy", "Dreamer", "Livin' Joy")]
    [InlineData("Dreamer", "Livin' Joy", "Dreamer (Janice Robinson Vocal)", "Livin' Joy")]
    [InlineData("Dreamer - Re-Original 7-inch Mix", "Livin' Joy", "Dreamer (Janice Robinson Vocal)", "Livin' Joy")]
    [InlineData("What Is Love - Live", "Haddaway", "What Is Love", "Haddaway")]
    public void Not_the_same_song(string title, string artist, string fileTitle, string fileArtist) =>
        Assert.False(Matching.SameSongLoosely(title, artist, T(fileTitle, fileArtist)));

    [Fact]
    public void A_loose_lookup_never_hands_back_another_mix()
    {
        var lib = new Library([T("Dreamer (Janice Robinson Vocal)", "Livin' Joy"), T("Dreamer - Re-Original 7-inch Mix", "Livin' Joy")]);
        Assert.Null(lib.FindLoose("Dreamer", "Livin' Joy"));
        var plain = new Library([T("Dreamer", "Livin' Joy")]);
        Assert.Null(plain.FindLoose("Dreamer - Janice Robinson Vocal", "Livin' Joy"));
        Assert.Null(plain.FindLoose("Dreamer (Janice Robinson Vocal)", "Livin' Joy"));
        Assert.NotNull(plain.FindLoose("Dreamer - Radio Edit", "Livin' Joy"));
    }
}
