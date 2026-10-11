using System.Text;
using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

public class G1Tests
{
    static SlskdFile F(string name, string user = "u") => new(user, @"music\" + name, 30_000_000, true, null, LengthSec: 200);

    [Fact]
    public void Piano_version_request_never_picks_the_original_file()
    {
        var files = new[] { F("Patient Zero.flac", "a"), F("Patient Zero (Piano Version).flac", "b") };
        var r = SlskdClient.RankSong(files, "Artist", "Patient Zero (Piano Version)");
        Assert.DoesNotContain(r, f => f.Username == "a");
    }

    [Theory]
    [InlineData("Patient Zero (Piano Version)")]
    [InlineData("Patient Zero (Acoustic)")]
    [InlineData("Patient Zero (Live)")]
    [InlineData("Patient Zero (Remix)")]
    [InlineData("Patient Zero (Instrumental)")]
    public void Variant_files_are_not_taken_for_the_original(string variantFile)
    {
        var files = new[] { F(variantFile + ".flac", "v") };
        Assert.Empty(SlskdClient.RankSong(files, "Artist", "Patient Zero"));
    }

    [Theory]
    [InlineData("Patient Zero (Piano Version)")]
    [InlineData("Song (Orchestral Version)")]
    [InlineData("Song (Acoustic)")]
    public void Base_title_keeps_variant_tags(string t) => Assert.Equal(t, WebCatalog.BaseTitle(t));

    [Fact]
    public void Base_title_still_drops_remaster_and_feat() => Assert.Equal("Song", WebCatalog.BaseTitle("Song (feat. X) (Remastered 2011)"));

    [Fact]
    public void Recent_keys_are_bounded_and_forget_the_oldest()
    {
        var r = new RecentKeys(3);
        foreach (var k in new[] { "a", "b", "c", "d" }) r.Add(k);
        Assert.False(r.Contains("a")); Assert.True(r.Contains("d")); Assert.Equal(3, r.Count);
        r.Add("d"); Assert.Equal(3, r.Count);
    }

    static List<ImportedPlaylist> Ok(string name, string text) => PlaylistImport.Parse(name, Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Text_playlists_still_import()
    {
        Assert.Single(Ok("a.txt", "Artist - Song\nOther - Two\n"));
        Assert.Single(Ok("a.txt", "Just A Song Name\n"));
        Assert.Single(Ok("a.m3u", "#EXTM3U\n#EXTINF:1,A - B\nx.mp3\n"));
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 2 })]
    [InlineData(new byte[] { 0x41, 0x20, 0x2d, 0x20, 0x42, 0x00, 0x0a })]
    [InlineData(new byte[] { 0xC3, 0x28, 0x0A, 0x80, 0x81 })]
    public void Binary_or_invalid_utf8_is_refused(byte[] b) => Assert.Throws<InvalidDataException>(() => PlaylistImport.Parse("playlist.txt", b));

    [Theory]
    [InlineData("{\"a\": 1}")]
    [InlineData("<html><body>hi</body></html>")]
    public void Non_playlists_are_refused(string t) => Assert.Throws<InvalidDataException>(() => PlaylistImport.Parse("x.txt", Encoding.UTF8.GetBytes(t)));

    [Fact]
    public void Absurdly_long_line_is_refused() => Assert.Throws<InvalidDataException>(() => PlaylistImport.Parse("x.txt", Encoding.UTF8.GetBytes(new string('a', 5000))));
}
