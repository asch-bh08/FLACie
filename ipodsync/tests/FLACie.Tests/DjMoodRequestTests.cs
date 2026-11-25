using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 25 Nov: "play late night music" on the web played the song "Fight Music". A mood or genre said beats a song whose title only looks like it.</summary>
public class DjMoodRequestTests
{
    static Track S(string title, string artist, string genre) =>
        new($"/m/{artist}/{title}", title, artist, "Album", artist, 1, 1, 200000, 2001, null, 1, TrackSource.Jellyfin, Genre: genre, JellyfinId: Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("play late night music")]
    [InlineData("play some late night music")]
    [InlineData("late night music")]
    public async Task A_mood_is_played_as_a_mood(string said)
    {
        var u = new DjAskUnderstandTests.User();
        u.Library.AddRange([S("Fight Music", "D12", "Hip-Hop"), S("Teardrop", "Massive Attack", "Trip-Hop"), S("Angel", "Massive Attack", "Trip-Hop"),
            S("Night Owl", "Gerry Rafferty", "Rock")]);
        var a = await new DjAskEngine().AnswerAsync(u, said, null, []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.DoesNotContain("/m/D12/Fight Music", a.Paths!.Take(1));
    }

    [Fact]
    public async Task A_real_song_with_a_mood_word_in_its_title_still_plays()
    {
        var u = new DjAskUnderstandTests.User();
        u.Library.AddRange([S("Late Night Talking", "Harry Styles", "Pop"), S("Teardrop", "Massive Attack", "Trip-Hop")]);
        var a = await new DjAskEngine().AnswerAsync(u, "play late night talking", null, []);
        Assert.Equal("/m/Harry Styles/Late Night Talking", a.Paths![0]);
    }
}
