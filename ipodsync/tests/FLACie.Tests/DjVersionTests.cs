using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 9 Nov: "play Out of Space by the Prodigy" played a demo, and "the other one" then failed. The album version wins over demos, remixes
/// and live versions; "the other one" moves to the next best and says which it is.</summary>
public class DjVersionTests
{
    static readonly DateTime T0 = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    static Track S(string title, string album, string artist = "The Prodigy") =>
        new($"/p/{artist}/{album}/{title}", title, artist, album, artist, 1, 1, 200000, 1992, null, 1, TrackSource.Jellyfin, JellyfinId: Guid.NewGuid().ToString("N"));

    static DjAskUnderstandTests.User Library(bool demoPlayed = false)
    {
        var u = new DjAskUnderstandTests.User();
        // the demo and remix come first in the library, as they did on the owner's
        u.Library.AddRange([
            S("Out of Space (Demo)", "Experience (Expanded)"),
            S("Out of Space - Audio Bullys Remix", "Their Law: The Singles"),
            S("Out of Space", "Live at Brixton"),
            S("Out of Space", "Experience"),
            S("Firestarter", "The Fat of the Land"),
        ]);
        if (demoPlayed) u.Top.Add(u.Library[0]);
        return u;
    }

    [Theory]
    [InlineData("play out of space by the prodigy")]
    [InlineData("play out of space")]
    public async Task The_album_version_beats_demo_remix_and_live(string said)
    {
        var a = await new DjAskEngine().AnswerAsync(Library(), said, null, []);
        Assert.Equal(DjAskDo.Play, a.Do);
        Assert.Equal("/p/The Prodigy/Experience/Out of Space", a.Paths![0]);
    }

    [Fact]
    public async Task A_played_demo_still_loses_to_the_album_version()
    {
        var a = await new DjAskEngine().AnswerAsync(Library(demoPlayed: true), "play out of space by the prodigy", null, []);
        Assert.Equal("/p/The Prodigy/Experience/Out of Space", a.Paths![0]);
    }

    [Fact]
    public async Task The_other_one_moves_down_the_versions_and_says_which()
    {
        var u = Library(); var t = T0;
        var e = new DjAskEngine(clock: () => t);
        var first = await e.AnswerAsync(u, "play out of space by the prodigy", null, []);
        var cur = first.Paths![0];
        var seen = new List<string> { cur };
        foreach (var want in new[] { "live", "remix", "demo" })
        {
            t += TimeSpan.FromSeconds(10);
            var a = await e.AnswerAsync(u, "the other one", cur, []);
            Assert.Equal(DjAskDo.Play, a.Do);
            Assert.DoesNotContain(a.Paths![0], seen);
            Assert.Contains(want, a.Say, StringComparison.OrdinalIgnoreCase);
            cur = a.Paths[0]; seen.Add(cur);
        }
        t += TimeSpan.FromSeconds(10);
        var none = await e.AnswerAsync(u, "the other one", cur, []);
        Assert.NotEqual(DjAskDo.Play, none.Do);
    }

    [Theory]
    [InlineData("Out of Space", "Experience", 0)]
    [InlineData("Out of Space - Radio Edit", "Singles", 1)]
    [InlineData("Out of Space", "Live at Brixton", 2)]
    [InlineData("Out of Space (Live)", "Experience", 2)]
    [InlineData("Out of Space - Audio Bullys Remix", "Singles", 3)]
    [InlineData("Out of Space (Demo)", "Experience", 4)]
    [InlineData("Out of Space", "Demos and Rarities", 4)]
    public void Versions_are_ranked(string title, string album, int rank) => Assert.Equal(rank, DjPick.VersionRank(S(title, album)));
    [Fact]
    public void The_phone_gets_each_songs_album_so_it_can_tell_same_named_versions_apart()
    {
        var u = Library();
        var live = u.Library[2];
        var o = DjAskService.Json(new DjAskAnswer(DjAskDo.Play, "x", [live.Path]), p => u.Library.FirstOrDefault(t => t.Path == p));
        Assert.Equal("Live at Brixton", (string?)o["songs"]![0]!["album"]);
    }
}
