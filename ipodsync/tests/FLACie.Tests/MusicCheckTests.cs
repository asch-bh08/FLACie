using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 13 Nov: a YouTube "playlist" found by searching "Dreamer" was nine clips of the 2005 film, and opening it downloaded them. Nothing that
/// does not look like music is ever fetched by itself (a person can still fetch it by hand).</summary>
public class MusicCheckTests
{
    [Theory]
    [InlineData("Broken Leg (2005) HD", "Dreamer (1/9) Movie CLIP", false)]
    [InlineData("Official Trailer", "Some Studio", false)]
    [InlineData("Season 2 Episode 4", "A Show", false)]
    [InlineData("Full Movie", "x", false)]
    [InlineData("Minecraft Gameplay Walkthrough Part 3", "x", false)]
    [InlineData("Dreamer", "Livin' Joy", true)]
    [InlineData("Dreamer - Re-Original 7-inch Mix", "Livin' Joy", true)]
    [InlineData("Airplanes (feat. Hayley Williams)", "B.o.B", true)]
    [InlineData("Movie", "Tom Misch", true)]                // a song called Movie is a song
    [InlineData("Scene Stealer", "Some Band", true)]
    public void A_song_or_not(string title, string artist, bool song) => Assert.Equal(song, MusicCheck.LooksLikeSong(title, artist));

    [Fact]
    public void A_list_of_film_clips_is_not_music_a_list_of_songs_is()
    {
        var clips = new WebPlaylist("youtube", "PLx1234567890", "Dreamer (2005) - Movie", WebPlaylistKind.Other, "Movieclips",
            Enumerable.Range(1, 9).Select(i => new WebTrack($"Scene {i} (2005) HD", $"Dreamer ({i}/9) Movie CLIP")).ToList());
        Assert.False(MusicCheck.LooksLikeMusic(clips));
        Assert.False(MusicCheck.LooksLikeMusic(clips with { Tracks = [] }));   // by its name alone, before its songs are read
        var songs = new WebPlaylist("youtube", "PLy1234567890", "90s Club Classics", WebPlaylistKind.Other, "x", [new("Dreamer", "Livin' Joy"), new("What Is Love", "Haddaway")]);
        Assert.True(MusicCheck.LooksLikeMusic(songs));
        Assert.True(MusicCheck.LooksLikeMusic(songs with { Tracks = [] }));
    }
}
