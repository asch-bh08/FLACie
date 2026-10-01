using FLACie.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace FLACie.Server.Components.Shared;

/// <summary>Base for signed-in views: the user's session, the tab's player, and a redraw whenever either changes.</summary>
public abstract class UserComponent : ComponentBase, IDisposable
{
    [Inject] SessionStore Store { get; set; } = default!;
    [Inject] protected PlayerState Player { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected JellyfinClient Jellyfin { get; set; } = default!;
    [CascadingParameter] Task<AuthenticationState> Auth { get; set; } = default!;
    protected UserSession Session { get; private set; } = default!;
    protected Library Lib => Session.Library;
    /// <summary>Connect and Jams; null when signed in with a NAS only.</summary>
    protected Live? Live { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        Session = Store.For((await Auth).User);
        Session.Changed += Refresh;
        Player.Changed += Refresh;
        Player.SetLibrary(() => Session.Library);
        Player.SetRelated(cur => Session.Playlists.Where(p => p.Entries.Any(e => e.Path == cur.Path || (e.Title == cur.Title && e.Artist == cur.Artist))).SelectMany(p => Session.Resolve(p.Entries)).DistinctBy(t => t.Path).ToList());
        if (Store.LiveFor(Session) is { } l)
        {
            Live = l;
            Player.Bind(l.Connect);
            l.Connect.Changed += Refresh; l.Jam.Changed += Refresh;
        }
    }

    void Refresh() => InvokeAsync(StateHasChanged);

    /// <summary>Writes the profile to the user's accounts in the background (after a playlist or favourite edit).</summary>
    protected void SaveProfile() => _ = Task.Run(async () => { try { await Session.SaveAsync(Jellyfin); } catch { } });

    protected static string Time(double s) => s <= 0 || double.IsNaN(s) ? "0:00" : $"{(int)s / 60}:{(int)s % 60:00}";
    protected static string AlbumLink(Group g) => "/album/" + Uri.EscapeDataString(g.Tracks[0].AlbumKey);

    public virtual void Dispose()
    {
        if (Session is not null) Session.Changed -= Refresh;
        Player.Changed -= Refresh;
        if (Live is { } l) { l.Connect.Changed -= Refresh; l.Jam.Changed -= Refresh; }
    }
}
