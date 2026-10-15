namespace FLACie.Core;

/// <summary>Which lyric line the iPod desk's journal is writing: synced lines follow the playback clock (a hair early), plain lines are spread
/// evenly over the song. Pure and unit-tested.</summary>
public static class DeskLyrics
{
    /// <summary>Lyrics fetched for one song are only shown if that song is still the one playing (a late answer for the previous song is dropped).</summary>
    public static bool Accept(string? requestedFor, string? currentPath) => requestedFor is not null && requestedFor == currentPath;

    /// <summary>The index of the line being sung, or -1 before the first (or with no lyrics).</summary>
    public static int CurrentLine(Lyrics? lyrics, double positionSeconds, double durationSeconds)
    {
        if (lyrics is null || lyrics.Lines.Count == 0) return -1;
        var n = lyrics.Lines.Count;
        if (!lyrics.Synced) return durationSeconds <= 0 ? n - 1 : Math.Clamp((int)(positionSeconds / (durationSeconds / n)), 0, n - 1);
        var ms = positionSeconds * 1000 + 150;   // the eye needs the line just before it is sung
        int lo = 0, hi = n - 1, found = -1;
        while (lo <= hi) { var mid = (lo + hi) / 2; if (lyrics.Lines[mid].TimeMs <= ms) { found = mid; lo = mid + 1; } else hi = mid - 1; }
        return found;
    }
}
