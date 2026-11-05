using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>Conversation mode for "talk to the DJ": when it listens again, when it ends, and whether the paused song carries on.</summary>
public class DjConversationTests
{
    [Theory]
    [InlineData("that's all")] [InlineData("That's all.")] [InlineData("thats it")] [InlineData("thanks")] [InlineData("Thank you")] [InlineData("thank you DJ")]
    [InlineData("okay that's all")] [InlineData("no thanks")] [InlineData("bye")] [InlineData("goodbye")] [InlineData("never mind")] [InlineData("nevermind")]
    [InlineData("I'm good")] [InlineData("I’m done")] [InlineData("cheers")] [InlineData("stop listening")] [InlineData("nothing else")]
    public void Goodbyes_end_it(string said) => Assert.True(DjConversation.IsGoodbye(said));

    [Theory]
    [InlineData("play that's all folks")] [InlineData("thanks for the memories by fall out boy")] [InlineData("what's this song")] [InlineData("play bye bye bye")]
    [InlineData("something chill")] [InlineData("")] [InlineData(null)]
    public void Requests_that_merely_contain_the_words_do_not(string? said) => Assert.False(DjConversation.IsGoodbye(said));

    // owner, 31 Oct: smart turn-taking. A plain control the DJ carried out ends it at once; a normal answer leaves a short window for a follow-up; a question back a longer one
    [Fact]
    public void A_plain_control_ends_it_at_once_with_no_listening_again()
    {
        foreach (var d in new[] { DjAskDo.UpNext, DjAskDo.VolumeUp, DjAskDo.VolumeDown, DjAskDo.DjOn, DjAskDo.DjOff })
            Assert.Equal(DjTurn.End, DjConversation.After(d, asked: false, 1));
        // a thumbs down is done too (the DJ only says "Fair enough"), though it moves nothing in the player
        Assert.Equal(DjTurn.End, DjConversation.After(DjAskDo.None, asked: false, 1, "ThumbsDown"));
    }

    [Fact]
    public void A_normal_answer_listens_again_for_a_short_while()
    {
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.None, asked: false, 1));
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.None, asked: false, 1, "Answer"));
        Assert.Equal(4000, DjConversation.FollowUpMs);
        Assert.Equal(DjConversation.FollowUpMs, DjConversation.ListenWindowMs(asked: false));
    }

    [Fact]
    public void A_question_back_listens_again_for_longer()
    {
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.None, asked: true, 1, "Clarify"));
        Assert.Equal(8000, DjConversation.AnswerWindowMs);
        Assert.Equal(DjConversation.AnswerWindowMs, DjConversation.ListenWindowMs(asked: true));
        Assert.True(DjConversation.AnswerWindowMs > DjConversation.FollowUpMs);
    }

    [Fact]
    public void A_goodbye_ends_it_whatever_the_answer_and_over_a_second_of_quiet_ends_the_persons_turn()
    {
        Assert.Equal(DjTurn.End, DjConversation.After(DjAskDo.None, asked: false, 1, "Goodbye"));
        Assert.Equal("Anytime.", DjConversation.Anytime);
        Assert.Equal(1200, DjConversation.EndOfSpeechMs);
    }

    [Fact]
    public void Choosing_what_the_music_does_ends_it_and_the_old_song_stays_as_chosen()
    {
        foreach (var d in new[] { DjAskDo.Play, DjAskDo.Next, DjAskDo.Resume, DjAskDo.Pause })
        {
            Assert.Equal(DjTurn.EndMusicChosen, DjConversation.After(d, asked: false, 1));
            Assert.False(DjConversation.ResumeAtEnd(true, DjTurn.EndMusicChosen));
        }
    }

    [Fact]
    public void Listening_again_stops_after_the_last_turn()
    {
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.None, asked: true, DjConversation.MaxTurns - 1));
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.None, asked: false, DjConversation.MaxTurns - 1));
        Assert.Equal(DjTurn.End, DjConversation.After(DjAskDo.None, asked: true, DjConversation.MaxTurns));
        Assert.Equal(DjTurn.End, DjConversation.After(DjAskDo.None, asked: false, DjConversation.MaxTurns));
    }

    [Fact]
    public void The_song_the_mic_paused_carries_on_and_one_that_was_not_playing_stays_quiet()
    {
        Assert.True(DjConversation.ResumeAtEnd(true, DjTurn.End));
        Assert.False(DjConversation.ResumeAtEnd(false, DjTurn.End));
    }

    // ---- F12: the pending question (confirm a removal, offer a rename) ----

    static DjAskUnderstandTests.User PopLibrary()
    {
        var u = new DjAskUnderstandTests.User();
        u.Library.AddRange([DjAskUnderstandTests.T("Toxic", "Britney Spears"), DjAskUnderstandTests.T("Firework", "Katy Perry"), DjAskUnderstandTests.T("Roads", "Portishead")]);
        u.Lists.Add(new("pl-pop", "Pop", 1, null, [new(u.Library[0].Path, "Toxic", "Britney Spears"), new(u.Library[1].Path, "Firework", "Katy Perry")]));
        return u;
    }

    [Fact]
    public async Task Remove_asks_first_and_removes_only_after_yes()
    {
        var u = PopLibrary(); var e = new DjAskEngine();
        var q = await e.AnswerAsync(u, "remove this from the playlist", "/p/Britney Spears/Toxic", []);
        Assert.DoesNotContain(q.AllSteps, s => s.Do == DjAskDo.RemoveFromPlaylist);
        Assert.Contains(q.AllSteps, s => s.Do == DjAskDo.Ask);
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(q.Do, true, 1, q.Intent));
        var yes = await e.AnswerAsync(u, "yes", "/p/Britney Spears/Toxic", []);
        var rm = Assert.Single(yes.AllSteps);
        Assert.Equal(DjAskDo.RemoveFromPlaylist, rm.Do);
        Assert.Equal("Pop", rm.Playlist);
        Assert.Equal(["/p/Britney Spears/Toxic"], rm.Paths);
    }

    [Fact]
    public async Task Remove_then_no_leaves_it()
    {
        var u = PopLibrary(); var e = new DjAskEngine();
        await e.AnswerAsync(u, "remove this from my pop playlist", "/p/Britney Spears/Toxic", []);
        var no = await e.AnswerAsync(u, "no", "/p/Britney Spears/Toxic", []);
        Assert.Empty(no.AllSteps);
        var later = await e.AnswerAsync(u, "yes", "/p/Britney Spears/Toxic", []);
        Assert.DoesNotContain(later.AllSteps, s => s.Do == DjAskDo.RemoveFromPlaylist);
    }

    [Fact]
    public async Task The_question_is_forgotten_after_a_minute()
    {
        var u = PopLibrary(); var t = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var e = new DjAskEngine(clock: () => t);
        await e.AnswerAsync(u, "remove this from the playlist", "/p/Britney Spears/Toxic", []);
        t += TimeSpan.FromSeconds(61);
        var yes = await e.AnswerAsync(u, "yes", "/p/Britney Spears/Toxic", []);
        Assert.DoesNotContain(yes.AllSteps, s => s.Do == DjAskDo.RemoveFromPlaylist);
    }

    [Fact]
    public async Task Make_a_playlist_fills_and_names_it_then_offers_a_rename()
    {
        var u = PopLibrary(); var e = new DjAskEngine();
        var a = await e.AnswerAsync(u, "make a backroads playlist", null, []);
        var create = a.AllSteps[0];
        Assert.Equal(DjAskDo.CreatePlaylist, create.Do);
        Assert.Equal("Backroads", create.Playlist);
        Assert.NotEmpty(create.Paths!);
        Assert.Equal(DjAskDo.Ask, a.AllSteps[^1].Do);
        var r = await e.AnswerAsync(u, "call it B-Road Bangers", null, []);
        var rename = Assert.Single(r.AllSteps);
        Assert.Equal(DjAskDo.RenamePlaylist, rename.Do);
        Assert.Equal("Backroads", rename.Playlist);
        Assert.Equal("B-Road Bangers", rename.Title);
    }
}
