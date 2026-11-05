using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F13: the shuffle toggle keeps the songs up to the current one and shuffles the rest; off puts the original order back.</summary>
public class QueueShuffleTests
{
    [Fact]
    public void Toggle_on_keeps_the_current_song_and_shuffles_the_rest()
    {
        var order = QueueShuffle.On(10, 3, new Random(7));
        Assert.Equal([0, 1, 2, 3], order.Take(4));
        Assert.Equal(Enumerable.Range(4, 6), order.Skip(4).Order());
        Assert.NotEqual(Enumerable.Range(4, 6), order.Skip(4)); // seed 7 does move them
    }

    [Fact]
    public void Toggle_off_restores_the_order_and_keeps_playing_the_same_song()
    {
        var order = QueueShuffle.On(10, 3, new Random(7));
        var pos = 6; var playing = order[pos];
        var (back, at) = QueueShuffle.Off(order, pos);
        Assert.Equal(Enumerable.Range(0, 10), back);
        Assert.Equal(playing, back[at]);
    }

    [Fact]
    public void Nothing_after_the_current_song_is_left_alone()
    {
        Assert.Equal([0, 1, 2], QueueShuffle.On(3, 2, new Random(1)));
        Assert.Empty(QueueShuffle.On(0, -1, new Random(1)));
        Assert.Equal(Enumerable.Range(0, 5), QueueShuffle.On(5, -1, new Random(1)).Order());
    }
}
