namespace FLACie.Core;

/// <summary>The iPod desk's journal: lyrics fetched for one song are shown only while it plays. (Which line is being sung is worked out in the browser,
/// from the song's own clock: desk.js.)</summary>
public static class DeskLyrics
{
    /// <summary>Lyrics fetched for one song are only shown if that song is still the one playing (a late answer for the previous song is dropped).</summary>
    public static bool Accept(string? requestedFor, string? currentPath) => requestedFor is not null && requestedFor == currentPath;
}
