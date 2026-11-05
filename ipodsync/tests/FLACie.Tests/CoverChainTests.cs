using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F1: a song every cover source fails for gets a made cover, never a 404.</summary>
public class CoverChainTests
{
    [Fact]
    public void A_cover_gone_for_good_is_made_not_missing()
    {
        Assert.Equal(CoverMiss.Generated, GeneratedCover.Answer(transient: false, hasFallback: false));
        Assert.Equal(CoverMiss.Fallback, GeneratedCover.Answer(transient: false, hasFallback: true));
        Assert.Equal(CoverMiss.TryAgain, GeneratedCover.Answer(transient: true, hasFallback: false));
    }

    [Fact]
    public void The_made_cover_has_the_artist_and_title_and_is_the_same_every_time()
    {
        var svg = GeneratedCover.Svg("Simon & Garfunkel", "The Boxer");
        Assert.StartsWith("<svg", svg);
        Assert.Contains("Simon &amp; Garfunkel", svg);
        Assert.Contains("The Boxer", svg);
        Assert.Contains(">SG<", svg);
        Assert.Equal(svg, GeneratedCover.Svg("Simon & Garfunkel", "The Boxer"));
        Assert.StartsWith("<svg", GeneratedCover.Svg("", ""));
    }

    [Fact]
    public void A_catalog_key_names_its_song()
    {
        var key = Art.LookupKey("Eminem", "", "Lose Yourself")!;
        Assert.Equal(("Eminem", "Lose Yourself"), GeneratedCover.Names(key));
        Assert.Equal(("", ""), GeneratedCover.Names("jfabc~tag"));
    }

    [Fact]
    public void Only_a_songs_cover_key_gets_a_made_cover()
    {
        Assert.True(GeneratedCover.ForKey(Art.LookupKey("Eminem", "", "Lose Yourself")!));
        Assert.True(GeneratedCover.ForKey("jfabc~t"));
        Assert.False(GeneratedCover.ForKey("ex" + Convert.ToBase64String("http://169.254.169.254/x"u8.ToArray())));
    }
}
