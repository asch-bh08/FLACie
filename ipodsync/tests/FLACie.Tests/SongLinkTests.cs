using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>One song from a link (owner, 8 Nov: "import single songs from a link like SoundCloud remixes"): a SoundCloud track or a YouTube video, cleaned to
/// the address the yt-dlp service fetches. Sets, playlists, profiles and other sites are not one song.</summary>
public class SongLinkTests
{
    [Theory]
    [InlineData("https://soundcloud.com/someproducer/dreamer-remix", "https://soundcloud.com/someproducer/dreamer-remix", "SoundCloud")]
    [InlineData("  https://m.soundcloud.com/someproducer/dreamer-remix?si=abc&utm_source=x ", "https://soundcloud.com/someproducer/dreamer-remix", "SoundCloud")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42", "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "YouTube")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=xyz", "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "YouTube")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "YouTube")]
    public void A_song_link_is_recognised(string url, string fetch, string service)
    {
        var l = PlaylistLinks.SongLink(url);
        Assert.NotNull(l);
        Assert.Equal(fetch, l!.Url);
        Assert.Equal(service, l.Service);
    }

    [Theory]
    [InlineData("https://soundcloud.com/someproducer/sets/summer")]           // a set: the playlist import
    [InlineData("https://soundcloud.com/someproducer")]                        // a profile
    [InlineData("https://soundcloud.com/someproducer/tracks")]
    [InlineData("https://soundcloud.com/discover")]
    [InlineData("https://www.youtube.com/playlist?list=PL1234567890ab")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL1234567890ab")]   // a video in a playlist: the playlist import
    [InlineData("http://soundcloud.com/someproducer/dreamer-remix")]           // not https
    [InlineData("https://evil.example/soundcloud.com/a/b")]
    [InlineData("dreamer remix")]
    [InlineData("")]
    public void Other_text_is_not(string url) => Assert.Null(PlaylistLinks.SongLink(url));
}
