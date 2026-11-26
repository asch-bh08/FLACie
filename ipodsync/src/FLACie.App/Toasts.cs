using FLACie.Core;

namespace FLACie.Server;

/// <summary>A short message at the bottom of the page, with an optional action (Undo). One per browser tab; <c>ToastHost</c> draws it.</summary>
public sealed class Toasts
{
    public sealed record Toast(long Id, string Text, string? ActionLabel, Action? Action, bool Warn);

    public Toast? Current { get; private set; }
    public event Action? Changed;
    long next;
    CancellationTokenSource? timer;

    public void Show(string text, string? actionLabel = null, Action? action = null, bool warn = false, int seconds = 5)
    {
        var t = new Toast(++next, text, actionLabel, action, warn);
        Current = t; Changed?.Invoke();
        timer?.Cancel(); var cts = timer = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token).ContinueWith(_ => { if (!cts.IsCancellationRequested && Current?.Id == t.Id) Dismiss(); }, TaskScheduler.Default);
    }

    public void Dismiss() { timer?.Cancel(); Current = null; Changed?.Invoke(); }

    /// <summary>Adds a song to a playlist and says so: "Added to X" with Undo, or "Already in X" when it was there.</summary>
    public void Added(UserSession s, Playlist p, Track t, Action save)
    {
        switch (s.AddToPlaylist(p.Id, t))
        {
            case UserSession.PlaylistAdd.Added:
                save();
                Show($"Added “{t.Title}” to {p.Name}", "Undo", () => { s.RemoveFromPlaylist(p.Id, t); save(); Show($"Removed “{t.Title}” from {p.Name}"); });
                break;
            case UserSession.PlaylistAdd.Already:
                Show($"“{t.Title}” is already in {p.Name}", warn: true);
                break;
            default:
                Show("That playlist isn't there any more", warn: true);
                break;
        }
    }

    /// <summary>A heart: hearts the song or takes it back, and says which (kept when Shuffle and Repeat went quiet; owner, 26 Nov).</summary>
    public void Hearted(UserSession s, Track t, Action save)
    {
        Feedback.Heart(s, t); save();
        Show(s.IsFavorite(t) ? $"Added “{t.Title}” to favourites" : $"Removed “{t.Title}” from favourites");
    }

}
