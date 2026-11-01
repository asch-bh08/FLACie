using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Importing a playlist from a Spotify or Deezer link (owner, 1 Nov).</summary>
public class PlaylistLinksTests
{
    [Theory]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=abc", PlaylistLinks.Service.Spotify, "playlist", "37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("https://open.spotify.com/intl-de/album/4aawyAB9vmqN3uQ7FjRGTy", PlaylistLinks.Service.Spotify, "album", "4aawyAB9vmqN3uQ7FjRGTy")]
    [InlineData("https://www.deezer.com/en/playlist/3155776842", PlaylistLinks.Service.Deezer, "playlist", "3155776842")]
    [InlineData("deezer.com/album/302127", PlaylistLinks.Service.Deezer, "album", "302127")]
    public void A_link_is_recognised(string url, PlaylistLinks.Service service, string kind, string id) =>
        Assert.Equal(new PlaylistLinks.Link(service, kind, id), PlaylistLinks.Recognise(url));

    [Theory]
    [InlineData("https://music.apple.com/gb/playlist/x/pl.123")]
    [InlineData("not a link")]
    [InlineData("")]
    public void Other_links_are_not(string url) => Assert.Null(PlaylistLinks.Recognise(url));

    [Fact]
    public void A_spotify_embed_page_gives_the_name_and_each_songs_lead_artist()
    {
        const string html = """<html><script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"state":{"data":{"entity":{"type":"playlist","name":"Today's Top Hits","trackList":[{"title":"Loser","subtitle":"Tame Impala"},{"title":"Die With A Smile","subtitle":"Lady Gaga, Bruno Mars"},{"title":"","subtitle":"x"}]}}}}}}</script></html>""";
        var p = PlaylistLinks.FromSpotifyEmbed(html)!;
        Assert.Equal("Today's Top Hits", p.Name);
        Assert.Equal([new ImportedTrack("Tame Impala", "Loser"), new ImportedTrack("Lady Gaga", "Die With A Smile")], p.Tracks);
    }

    [Fact]
    public void A_page_with_no_songs_gives_nothing() => Assert.Null(PlaylistLinks.FromSpotifyEmbed("<html>sign in</html>"));

    [Fact]
    public void A_deezer_playlist_gives_its_songs_and_the_next_page()
    {
        const string json = """{"title":"Top Worldwide","tracks":{"data":[{"title":"Blinding Lights","artist":{"name":"The Weeknd"},"album":{"title":"After Hours"}}],"next":"https://api.deezer.com/playlist/1/tracks?index=25"}}""";
        var p = PlaylistLinks.FromDeezer(json, out var next)!;
        Assert.Equal("Top Worldwide", p.Name);
        Assert.Equal([new ImportedTrack("The Weeknd", "Blinding Lights", "After Hours")], p.Tracks);
        Assert.Equal("https://api.deezer.com/playlist/1/tracks?index=25", next);
        Assert.Null(PlaylistLinks.FromDeezer("""{"error":{"type":"DataException"}}""", out _));
    }
}
