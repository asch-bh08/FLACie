namespace FLACie.Core;

/// <summary>What a person did that tells us about their taste: played a song, or marked one liked or not wanted.</summary>
public enum ListenKind { Play, Heart, Unheart, ThumbDown, LessLikeThis, PlaylistAdd, PlaylistRemove }

/// <summary>
/// One thing a person did, as the device saw it. Id is made by the device so a resend is not counted twice.
/// Heard and Length are seconds and only mean something for a Play. Context is the playlist id, or "autoplay", "search" or "home".
/// </summary>
public sealed record ListenEvent(string Id, ListenKind Kind, long At, string Path, string Title, string Artist, double Heard = 0, double Length = 0, string? Context = null)
{
    /// <summary>An event for this song happening now, with a fresh id.</summary>
    public static ListenEvent Of(ListenKind kind, Track t, double heard = 0, double length = 0, string? context = null) =>
        new(Guid.NewGuid().ToString("N"), kind, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), t.Path, t.Title, t.Artist, heard, length, context);
}

/// <summary>Was a play a skip, or did the person listen to the end?</summary>
public static class ListenClass
{
    // Old plays folded into a "day:" event are daily totals, not single plays, so skip/finish do not mean much for them.
    /// <summary>Under 30 seconds heard: they moved on.</summary>
    public static bool IsSkip(ListenEvent e) => e.Kind == ListenKind.Play && e.Heard < 30;

    /// <summary>At least 90 % of the song heard.</summary>
    public static bool IsFinish(ListenEvent e) => e.Kind == ListenKind.Play && e.Length > 0 && e.Heard >= e.Length * 0.9;
}
