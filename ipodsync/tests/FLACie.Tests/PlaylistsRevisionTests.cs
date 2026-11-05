using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 5 Nov: the phone polls a playlists stamp and refetches only when it changed, so a web edit reaches it within seconds.</summary>
public class PlaylistsRevisionTests
{
    static Track T(string title) => new("/m/" + title + ".flac", title, "Artist", "Album", "Artist", 1, 1, 1000, 2020, null, 0, TrackSource.Local, null, null, "", 0);

    [Fact]
    public void The_stamp_is_steady_until_a_playlist_changes()
    {
        var s = new UserSession("local", null, null);
        var p = s.CreatePlaylist("pop");
        var a = s.PlaylistsRevision;
        Assert.Equal(a, s.PlaylistsRevision);
        s.AddToPlaylist(p.Id, T("Lovegame"));
        var b = s.PlaylistsRevision;
        Assert.NotEqual(a, b);
        s.RemoveFromPlaylist(p.Id, 0);
        Assert.NotEqual(b, s.PlaylistsRevision);
    }

    [Fact]
    public void A_deleted_playlist_changes_it_too()
    {
        var s = new UserSession("local", null, null);
        var p = s.CreatePlaylist("pop");
        var a = s.PlaylistsRevision;
        s.DeletePlaylist(p.Id);
        Assert.NotEqual(a, s.PlaylistsRevision);
    }
}
