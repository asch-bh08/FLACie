using FLACie.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FLACie.Server;

/// <summary>
/// Sharing a song or a set of songs: a share is made on this server (its songs by title and artist, see <see cref="ShareStore"/>) and its link
/// (/s/{id}) goes to the device's share sheet, or is copied. Whoever opens the link signs in to FLACie as themselves and plays the songs from their
/// own library. One per browser tab.
/// </summary>
public sealed class Sharing(ShareStore store, NavigationManager nav, IJSRuntime js, Toasts toast)
{
    public async Task ShareAsync(UserSession session, string kind, string name, IEnumerable<Track> songs)
    {
        var entries = songs.Select(t => new PlaylistEntry(t.Path, t.Title, t.Artist)).ToList();
        if (entries.Count == 0) { toast.Show("Nothing to share", warn: true); return; }
        var id = store.Create(kind, name, session.Jellyfin?.UserName ?? session.Nas?.User ?? "Someone", entries);
        var link = nav.BaseUri.TrimEnd('/') + "/s/" + id;
        string how;
        try { how = await js.InvokeAsync<string>("flacie.shareLink", name, link); } catch (Exception) { how = ""; }
        if (how == "copied") toast.Show("Link copied. Anyone signed in to FLACie here can open it");
        else if (how == "") toast.Show("Couldn't share it: " + link, warn: true, seconds: 15);
    }
}
