namespace FLACie.Core;

/// <summary>One album on the iPod desk: the next CD (open) or one in the stack under it. <see cref="QueueIndex"/> is where its first song (in play order) sits in the queue;
/// <see cref="Tracks"/> are all its songs in the queue, in album order (disc, then track number).</summary>
public sealed record DeskAlbum(string Key, string Album, string Artist, string? ArtKey, int QueueIndex, Track First, IReadOnlyList<int> Tracks);

/// <summary>What the iPod desk's CDs show: the next albums coming up, in play order (shuffle included), each album once, the playing album left out. Pure and unit-tested.</summary>
public static class DeskSources
{
    /// <summary>The next <paramref name="count"/> distinct albums after the song at queue position <paramref name="index"/>, following <paramref name="order"/>
    /// (play position → queue position); an order that does not cover the queue is ignored and the queue's own order is used. <paramref name="wrap"/> (repeat all): after the
    /// end the play order starts again, and the playing album itself comes round again there.</summary>
    public static List<DeskAlbum> Next(IReadOnlyList<Track> queue, IReadOnlyList<int> order, int index, int count = 3, bool wrap = false)
    {
        var result = new List<DeskAlbum>();
        if (index < 0 || index >= queue.Count) return result;
        var play = order.Count == queue.Count ? order : Enumerable.Range(0, queue.Count).ToList();
        var at = IndexOf(play, index);
        if (at < 0) return result;
        var current = AlbumKey(queue[index]);
        var seen = new HashSet<string> { current };
        var steps = wrap ? play.Count - 1 : play.Count - at - 1;   // (all the way round, short of the playing song itself)
        for (var n = 1; n <= steps && result.Count < count; n++)
        {
            var p = (at + n) % play.Count;
            if (wrap && at + n == play.Count) seen.Remove(current);   // round again: the playing album is up next once more
            var qi = play[p];
            if (qi < 0 || qi >= queue.Count) continue;
            var t = queue[qi];
            var key = AlbumKey(t);
            if (!seen.Add(key)) continue;
            var tracks = Enumerable.Range(0, queue.Count).Where(i => AlbumKey(queue[i]) == key).OrderBy(i => queue[i].DiscNo).ThenBy(i => queue[i].TrackNo).ThenBy(i => i).ToList();
            result.Add(new DeskAlbum(key, t.Album, t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist, t.ArtKey, qi, t, tracks));
        }
        return result;
    }

    /// <summary>Which album a song belongs to (album artist and album, ignoring case); a song with no album stands on its own.</summary>
    public static string AlbumKey(Track t) =>
        t.Album.Length == 0 ? t.Path : $"{(t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist).ToLowerInvariant()}|{t.Album.ToLowerInvariant()}";

    static int IndexOf(IReadOnlyList<int> list, int value) { for (var i = 0; i < list.Count; i++) if (list[i] == value) return i; return -1; }
}
