using FLACie.Core;
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

    [Fact]
    public void An_answer_or_a_small_change_keeps_listening()
    {
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.None, 1));
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.UpNext, 2));
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.VolumeUp, 3));
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.DjOn, 3));
    }

    [Fact]
    public void Choosing_what_the_music_does_ends_it_and_the_old_song_stays_as_chosen()
    {
        foreach (var d in new[] { DjAskDo.Play, DjAskDo.Next, DjAskDo.Resume, DjAskDo.Pause })
        {
            Assert.Equal(DjTurn.EndMusicChosen, DjConversation.After(d, 1));
            Assert.False(DjConversation.ResumeAtEnd(true, DjTurn.EndMusicChosen));
        }
    }

    [Fact]
    public void It_stops_by_itself_after_the_last_turn()
    {
        Assert.Equal(DjTurn.ListenAgain, DjConversation.After(DjAskDo.None, DjConversation.MaxTurns - 1));
        Assert.Equal(DjTurn.End, DjConversation.After(DjAskDo.None, DjConversation.MaxTurns));
    }

    [Fact]
    public void The_song_the_mic_paused_carries_on_and_one_that_was_not_playing_stays_quiet()
    {
        Assert.True(DjConversation.ResumeAtEnd(true, DjTurn.End));
        Assert.False(DjConversation.ResumeAtEnd(false, DjTurn.End));
    }
}
