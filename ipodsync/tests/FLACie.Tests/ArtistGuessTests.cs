using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F1: the artist of a song with no artist tag, from its file and folder names.</summary>
public class ArtistGuessTests
{
    [Theory]
    [InlineData("/music/Eminem/Singles/Eminem_Lose_Yourself.mp3.mp3", "Eminem")]
    [InlineData("Eminem/Singles/Eminem_Lose_Yourself.mp3.mp3", "Eminem")]
    [InlineData("/music/Unknown artist/Jessie J - Who You Are [Deluxe Edition]/03 Price Tag.flac", "Jessie J")]
    [InlineData("/music/SINGLES/Corona - The Rhythm of the Night.mp3", "Corona")]
    [InlineData("/music/The Prodigy/Fat of the Land/01 Smack My Bitch Up.flac", "The Prodigy")]
    [InlineData("/music/FAVOURITES/track01.mp3", null)]
    [InlineData("/music/Unknown artist/Unknown album/01 Track 1.mp3", null)]
    public void From_the_path(string path, string? artist) => Assert.Equal(artist, ArtistGuess.FromPath(path));

    [Theory]
    [InlineData("Jessie J - Who You Are [Deluxe Edition]", "Jessie J")]
    [InlineData("Eminem", "Eminem")]
    [InlineData("Unknown artist", null)]
    [InlineData("SINGLES", null)]
    [InlineData("FAVOURITES", null)]
    [InlineData("CD 1", null)]
    public void From_a_folder(string folder, string? artist) => Assert.Equal(artist, ArtistGuess.FromFolder(folder));

    [Fact]
    public void A_jellyfin_song_without_an_artist_takes_the_guess()
    {
        var o = System.Text.Json.Nodes.JsonNode.Parse("{\"Id\": \"x1\", \"Name\": \"Eminem_Lose_Yourself.mp3\", \"Path\": \"/music/Eminem/Singles/Eminem_Lose_Yourself.mp3.mp3\", \"Artists\": []}")!;
        Assert.Equal("Eminem", JellyfinClient.ToTrack("http://jf", o).Artist);
    }
}
