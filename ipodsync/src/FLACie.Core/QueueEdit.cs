namespace FLACie.Core;

/// <summary>
/// The arithmetic behind editing the web player's queue (move a song up or down in Up next, play one next, take one out), with no player in it so it can be unit
/// tested. Same semantics as the Android app's QueueEdit.kt: moving never changes what is playing; taking the playing song out starts the one that follows.
///
/// Two notions of order, as in <c>PlayerState</c>: the queue (the list of songs; indices <c>0..n-1</c>) and the play order, an array of queue indices in the
/// order the songs are reached (the queue order, or the shuffle order). Editing Up next only ever rearranges the play order after the current song.
/// </summary>
public static class QueueEdit
{
    public enum Step { Up, Down, Top }

    /// <summary>The queue indices that follow <paramref name="current"/> in play order. With <paramref name="repeatAll"/> the ones before it follow too, after the wrap. Never the current one.</summary>
    public static List<int> Upcoming(IReadOnlyList<int> order, int current, bool repeatAll)
    {
        var res = new List<int>(order.Count);
        var p = IndexOf(order, current);
        if (p < 0) return res;
        for (var k = p + 1; k < order.Count; k++) res.Add(order[k]);
        if (repeatAll) for (var k = 0; k < p; k++) res.Add(order[k]);
        return res;
    }

    static int IndexOf(IReadOnlyList<int> order, int value) { for (var i = 0; i < order.Count; i++) if (order[i] == value) return i; return -1; }

    /// <summary>The upcoming sequence after moving <paramref name="item"/> one place up or down, or to the top ("Play next"). Null when nothing changes (not upcoming, already first / last).</summary>
    public static List<int>? Moved(IReadOnlyList<int> upcoming, int item, Step step)
    {
        var i = IndexOf(upcoming, item);
        if (i < 0) return null;
        var res = upcoming.ToList();
        switch (step)
        {
            case Step.Up: if (i == 0) return null; (res[i], res[i - 1]) = (res[i - 1], res[i]); break;
            case Step.Down: if (i == res.Count - 1) return null; (res[i], res[i + 1]) = (res[i + 1], res[i]); break;
            default: if (i == 0) return null; res.RemoveAt(i); res.Insert(0, item); break;
        }
        return res;
    }

    /// <summary>
    /// The play order after moving the upcoming song <paramref name="item"/> (a queue index). The slots after the current song (cyclic with repeat-all) are refilled with the new
    /// upcoming sequence; everything else, the current song's own slot included, stays. Null when nothing changes.
    /// </summary>
    public static List<int>? Reorder(IReadOnlyList<int> order, int current, bool repeatAll, int item, Step step)
    {
        var up = Upcoming(order, current, repeatAll);
        if (Moved(up, item, step) is not { } newUp) return null;
        var p = IndexOf(order, current);
        var slots = new List<int>(order.Count);
        for (var k = p + 1; k < order.Count; k++) slots.Add(k);
        if (repeatAll) for (var k = 0; k < p; k++) slots.Add(k);
        var res = order.ToList();
        for (var k = 0; k < slots.Count; k++) res[slots[k]] = newUp[k];
        return res;
    }

    /// <summary>What taking a song out leaves: the play order and current index in the shortened queue, and what happened to the playing song.</summary>
    /// <param name="Order">The new play order (queue indices of the queue with the song removed).</param>
    /// <param name="Current">The new current queue index (-1 when the queue is empty).</param>
    /// <param name="CurrentRemoved">The playing song was the one removed: <see cref="Current"/> is the song that follows (it should start).</param>
    /// <param name="Stopped">Nothing followed: <see cref="Current"/> is the song before it, which should wait paused (or -1: the queue is empty).</param>
    public sealed record Removal(List<int> Order, int Current, bool CurrentRemoved, bool Stopped);

    /// <summary>
    /// Takes the song at queue index <paramref name="index"/> out (the caller removes it from its list; later queue indices shift down by one). Another song than the current one
    /// changes nothing about what is playing. The current one: the song that follows in play order takes over (with repeat-all, the first one after the last); with nothing
    /// after it, the song before it becomes current and playback stops there. Null for an index that is not in the order.
    /// </summary>
    public static Removal? Remove(IReadOnlyList<int> order, int current, int index, bool repeatAll)
    {
        var p = IndexOf(order, index);
        if (p < 0) return null;
        int Shift(int v) => v > index ? v - 1 : v;
        var rest = order.Where(v => v != index).Select(Shift).ToList();
        if (index != current) return new Removal(rest, Shift(current), false, false);
        if (order.Count == 1) return new Removal(rest, -1, true, true);
        if (p + 1 < order.Count) return new Removal(rest, Shift(order[p + 1]), true, false);
        if (repeatAll) return new Removal(rest, Shift(order[0]), true, false);
        return new Removal(rest, Shift(order[p - 1]), true, true);
    }

    /// <summary>
    /// The play order after a song was put into the queue at queue index <paramref name="at"/> (everything at or after it moves up by one), to be played right after the current
    /// song: "Play next". <paramref name="current"/> is the current index BEFORE the insert (it shifts too when it is at or after <paramref name="at"/>); returns the order and the new current.
    /// </summary>
    public static (List<int> Order, int Current) InsertNext(IReadOnlyList<int> order, int current, int at)
    {
        int Shift(int v) => v >= at ? v + 1 : v;
        var shifted = order.Select(Shift).ToList();
        var newCurrent = current < 0 ? current : Shift(current);
        var p = newCurrent < 0 ? -1 : shifted.IndexOf(newCurrent);
        shifted.Insert(p + 1, at);
        return (shifted, newCurrent);
    }

    /// <summary>
    /// The play order when a run of songs (queue indices, e.g. one album, in its own order) is played from here: the run right after the current song, then what was
    /// still to come without it. Songs of the run already played come round again with it; the current song, indices outside the order and repeats are left out of the run.
    /// </summary>
    public static List<int> PlayRunNext(IReadOnlyList<int> order, int current, IReadOnlyList<int> run)
    {
        var inOrder = order.ToHashSet();
        var r = run.Where(i => i != current && inOrder.Contains(i)).Distinct().ToList();
        var taken = r.ToHashSet();
        var p = order.ToList().IndexOf(current);
        return [.. order.Take(p + 1).Where(i => !taken.Contains(i)), .. r, .. order.Skip(p + 1).Where(i => !taken.Contains(i))];
    }
}
