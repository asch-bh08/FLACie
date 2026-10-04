namespace FLACie.Server;

public sealed record ActivityEntry(DateTime At, string Kind, string User, string Text);

/// <summary>The last few hundred things that happened on this server (sign-ins, plays, imports, downloads, charts), for the admin dashboard. Kept in memory.</summary>
public sealed class ActivityLog
{
    readonly LinkedList<ActivityEntry> items = new();
    public event Action? Added;

    public void Add(string kind, string user, string text)
    {
        lock (items) { if (items.First is { } f && f.Value.Text == text && f.Value.User == user && DateTime.UtcNow - f.Value.At < TimeSpan.FromSeconds(3)) return; items.AddFirst(new ActivityEntry(DateTime.UtcNow, kind, user, text)); while (items.Count > 300) items.RemoveLast(); }
        Added?.Invoke();
    }

    public List<ActivityEntry> Recent(int n = 60) { lock (items) return items.Take(n).ToList(); }
}
