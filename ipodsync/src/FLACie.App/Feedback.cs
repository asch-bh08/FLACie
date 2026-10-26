using FLACie.Core;

namespace FLACie.Server;

/// <summary>What a person says about a song in a menu or with the heart. Each goes to their listening log, where the recommendations learn from it.</summary>
public static class Feedback
{
    /// <summary>Hearts the song, or takes the heart away, and tells the log which.</summary>
    public static void Heart(UserSession s, Track t)
    {
        var on = !s.IsFavorite(t);
        s.ToggleFavorite(t);
        RetroHost.Listen?.Invoke(s, ListenEvent.Of(on ? ListenKind.Heart : ListenKind.Unheart, t));
    }

    /// <summary>"Thumbs down": not this song, ever again in picks.</summary>
    public static void ThumbDown(UserSession s, Track t) => RetroHost.Listen?.Invoke(s, ListenEvent.Of(ListenKind.ThumbDown, t));

    /// <summary>"Less like this": the song is fine, but fewer like it.</summary>
    public static void LessLikeThis(UserSession s, Track t) => RetroHost.Listen?.Invoke(s, ListenEvent.Of(ListenKind.LessLikeThis, t));
}
