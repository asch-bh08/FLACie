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
    public static async Task ThumbDown(UserSession s, Track t, PlayerState? player = null)
    {
        RetroHost.Listen?.Invoke(s, ListenEvent.Of(ListenKind.ThumbDown, t));
        if (player is not null) await player.Dislike(t.Path);
    }

    /// <summary>What the toast says after "Never pick this song" (the phone says the same).</summary>
    public static string NeverPicked(Track t) => $"“{t.Title}” won't be picked for you again";

    /// <summary>"Less like this": the song is fine, but fewer like it.</summary>
    public static void LessLikeThis(UserSession s, Track t) => RetroHost.Listen?.Invoke(s, ListenEvent.Of(ListenKind.LessLikeThis, t));
}
