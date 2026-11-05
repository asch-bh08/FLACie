namespace FLACie.Core;

/// <summary>
/// F13: the shuffle toggle as play orders (indexes into the queue as it was built). On keeps everything up to the current song and shuffles the rest;
/// off goes back to the original order with the same song playing. "Shuffle all" is separate and stays as it is. The phone has the same rules in Kotlin.
/// </summary>
public static class QueueShuffle
{
    /// <summary>The order with the first [current]+1 songs in place and the rest shuffled ([current] -1: nothing playing, all shuffled).</summary>
    public static List<int> On(int count, int current, Random rng)
    {
        var order = Enumerable.Range(0, count).ToList();
        for (var i = count - 1; i > current + 1; i--) { var j = rng.Next(current + 1, i + 1); (order[i], order[j]) = (order[j], order[i]); }
        return order;
    }

    /// <summary>The original order back, and where the song at [pos] of [order] now is.</summary>
    public static (List<int> Order, int Pos) Off(IReadOnlyList<int> order, int pos) =>
        (Enumerable.Range(0, order.Count).ToList(), pos >= 0 && pos < order.Count ? order[pos] : -1);
}
