using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F25.1: Apple Music's public page (a real page saved by Local on 5 Nov, never fetched in a test).</summary>
public class AppleMusicPageTests
{
    [Fact]
    public void A_real_page_parses_into_the_playlist_name_and_its_songs_with_artists()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAudio", "apple", "todays-hits-real.html"));
        var p = AppleMusicPage.Parse(html)!;
        Assert.Equal("Today’s Hits", p.Name);
        Assert.Equal(50, p.Tracks.Count);
        Assert.Equal(new ImportedTrack("Drake", "Solar Eclipse"), p.Tracks[0]);   // "Drake & Don Toliver": the lead artist
        Assert.All(p.Tracks, t => { Assert.NotEmpty(t.Artist); Assert.NotEmpty(t.Title); });
        Assert.Null(AppleMusicPage.Parse("<html>no songs</html>"));
    }
}
