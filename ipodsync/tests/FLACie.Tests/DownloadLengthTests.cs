using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Downloads know how long the song is (owner, 6 Nov: an import filed a 12 s Scatman, a 29 s Colour of My Dreams, and a 2-minute mash-up as
/// Corona's The Rhythm of the Night, because songs from a playlist came with no length to check).</summary>
public class DownloadLengthTests
{
    static SlskdFile F(string name, int sec) => new("u", @"music\" + name, 30_000_000, true, null, LengthSec: sec);

    [Fact]
    public void Without_a_known_length_a_preview_clip_is_never_taken()
    {
        var ranked = SlskdClient.RankSong([F("Scatman John - Scatman.flac", 12), F("Scatman John - Scatman.flac", 211)], "Scatman John", "Scatman", 0);
        Assert.All(ranked, f => Assert.True(f.LengthSec >= 45));
        Assert.NotEmpty(ranked);
    }

    [Fact]
    public void The_length_comes_from_the_catalog_for_that_song_by_that_artist()
    {
        var hits = new List<CatalogSong>
        {
            new("James Cozmo", "NRG vs Rhythm Of The Night (Mash Up)", "", null, 138_000, 1, 1),
            new("Corona", "The Rhythm of the Night", "The Rhythm of the Night", null, 264_000, 1, 1),
            new("DeBarge", "Rhythm of the Night", "", null, 234_000, 1, 1),
        };
        Assert.Equal(264_000, DownloadCoordinator.ExpectedLength(hits, "Corona", "The Rhythm of the Night"));
        Assert.Equal(0, DownloadCoordinator.ExpectedLength(hits, "Nobody", "Nothing"));
        Assert.True(DownloadCoordinator.TooShort(12, 0));
        Assert.False(DownloadCoordinator.TooShort(211, 0));
        Assert.True(DownloadCoordinator.TooShort(138, 264));
        Assert.False(DownloadCoordinator.TooShort(30, 31));   // a song that really is short
    }
}
