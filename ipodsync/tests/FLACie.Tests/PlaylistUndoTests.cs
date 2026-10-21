using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Undo after taking a song out of a playlist: it goes back in the same place.</summary>
public class PlaylistUndoTests
{
    static Track T(string title) => new("/m/" + title + ".flac", title, "Artist", "Album", "Artist", 1, 1, 1000, 2020, null, 0, TrackSource.Local, null, null, "", 0);

    [Fact]
    public void A_removed_song_goes_back_where_it_was()
    {
        var s = new UserSession("local", null, null);
        var p = s.CreatePlaylist("Mix");
        s.ReplacePlaylistTracks(p.Id, [T("One"), T("Two"), T("Three")]);
        var gone = s.Playlists[0].Entries[1];
        s.RemoveFromPlaylist(p.Id, 1);
        Assert.Equal(["One", "Three"], s.Playlists[0].Entries.Select(e => e.Title));
        s.InsertIntoPlaylist(p.Id, 1, gone);
        Assert.Equal(["One", "Two", "Three"], s.Playlists[0].Entries.Select(e => e.Title));
    }

    [Fact]
    public void A_place_past_the_end_puts_it_last()
    {
        var s = new UserSession("local", null, null);
        var p = s.CreatePlaylist("Mix");
        s.ReplacePlaylistTracks(p.Id, [T("One")]);
        s.InsertIntoPlaylist(p.Id, 9, new PlaylistEntry("/m/Two.flac", "Two", "Artist"));
        Assert.Equal(["One", "Two"], s.Playlists[0].Entries.Select(e => e.Title));
    }
}
