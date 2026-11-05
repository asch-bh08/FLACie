namespace FLACie.Core;

/// <summary>What showed interest in a playlist's songs (F23.2).</summary>
public enum FetchTrigger { Opened, Played, ArtistSearch, Popular, Asked }

/// <summary>
/// F23.2: which of a playlist's missing songs are downloaded, and in what order, as interest shows (never a whole catalog at once):
/// opening or playing it fetches all its missing songs in play order, the ones after the song playing first; an artist search fetches their This Is
/// list's missing songs, the top half first; a popular list (charts, and the decades and genres the person plays) only its first
/// <see cref="PopularTake"/> missing songs, so they start at once. No daily cap; under <see cref="DiskFloor"/> free on the music disk every automatic
/// download stops, and only a song the person asked for still downloads.
/// </summary>
public static class PlaylistFetchPolicy
{
    public const long DiskFloor = 100L * 1000 * 1000 * 1000;
    public const int PopularTake = 2;

    /// <summary>The songs to download, in order. [from]: the song opened or played (its place in the list); [freeBytes]: free space on the music disk
    /// (null when not known, then the downloads go ahead).</summary>
    public static List<WebTrack> Plan(FetchTrigger trigger, IReadOnlyList<WebTrack> tracks, Func<WebTrack, bool> have, long? freeBytes, int from = 0)
    {
        from = Math.Clamp(from, 0, Math.Max(0, tracks.Count - 1));
        if (tracks.Count == 0) return [];
        if (freeBytes is { } free && free < DiskFloor)
            return trigger == FetchTrigger.Asked && !have(tracks[from]) ? [tracks[from]] : [];
        return trigger switch
        {
            FetchTrigger.Asked => have(tracks[from]) ? [] : [tracks[from]],
            FetchTrigger.Popular => tracks.Where(t => !have(t)).Take(PopularTake).ToList(),
            // the This Is list is in order of the artist's best known songs: its top half first, then the rest
            FetchTrigger.ArtistSearch => tracks.Where(t => !have(t)).ToList(),
            // the song played, then the ones after it, then the ones before (play order from there)
            _ => tracks.Skip(from).Concat(tracks.Take(from)).Where(t => !have(t)).ToList(),
        };
    }

    /// <summary>For an artist search: how many of the plan are the top half (fetched first; the rest after them).</summary>
    public static int TopHalf(IReadOnlyList<WebTrack> tracks, Func<WebTrack, bool> have) => tracks.Take((tracks.Count + 1) / 2).Count(t => !have(t));
}
