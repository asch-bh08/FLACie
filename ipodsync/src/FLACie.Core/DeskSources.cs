namespace FLACie.Core;

/// <summary>One album on the iPod desk: the next CD (open) or one in the stack under it. <see cref="QueueIndex"/> is where its first song sits in the queue.</summary>
public sealed record DeskAlbum(string Key, string Album, string Artist, string? ArtKey, int QueueIndex, Track First);

/// <summary>What the iPod desk's CDs show: the next albums coming up, in play order (shuffle included), each album once, the playing album left out. Pure and unit-tested.</summary>
public static class DeskSources
{
    /// <summary>The next <paramref name="count"/> distinct albums after the song at queue position <paramref name="index"/>, following <paramref name="order"/>
    /// (play position → queue position); an order that does not cover the queue is ignored and the queue's own order is used.</summary>
    public static List<DeskAlbum> Next(IReadOnlyList<Track> queue, IReadOnlyList<int> order, int index, int count = 3)
    {
        var result = new List<DeskAlbum>();
        if (index < 0 || index >= queue.Count) return result;
        var play = order.Count == queue.Count ? order : Enumerable.Range(0, queue.Count).ToList();
        var at = IndexOf(play, index);
        if (at < 0) return result;
        var seen = new HashSet<string> { AlbumKey(queue[index]) };
        for (var p = at + 1; p < play.Count && result.Count < count; p++)
        {
            var qi = play[p];
            if (qi < 0 || qi >= queue.Count) continue;
            var t = queue[qi];
            if (!seen.Add(AlbumKey(t))) continue;
            result.Add(new DeskAlbum(AlbumKey(t), t.Album, t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist, t.ArtKey, qi, t));
        }
        return result;
    }

    /// <summary>Which album a song belongs to (album artist and album, ignoring case); a song with no album stands on its own.</summary>
    public static string AlbumKey(Track t) =>
        t.Album.Length == 0 ? t.Path : $"{(t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist).ToLowerInvariant()}|{t.Album.ToLowerInvariant()}";

    static int IndexOf(IReadOnlyList<int> list, int value) { for (var i = 0; i < list.Count; i++) if (list[i] == value) return i; return -1; }
}
