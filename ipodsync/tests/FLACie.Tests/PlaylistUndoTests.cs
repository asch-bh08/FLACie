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

/// <summary>The Edit page's Remove duplicates: the first copy of each song stays where it was.</summary>
public class PlaylistDuplicatesTests
{
    // ReplacePlaylistTracks drops copies itself, so the list is put together an entry at a time, as an import or another device can leave it
    static FLACie.Core.Playlist Make(UserSession s, params (string Title, string Path)[] songs)
    {
        var p = s.CreatePlaylist("Mix");
        foreach (var (t, path) in songs) s.InsertIntoPlaylist(p.Id, 99, new PlaylistEntry(path.Length > 0 ? path : "/m/" + t + ".flac", t, "Artist"));
        return p;
    }

    [Fact]
    public void Later_copies_go_and_the_first_keeps_its_place()
    {
        var s = new UserSession("local", null, null);
        var p = Make(s, ("One", ""), ("Two", ""), ("One", ""), ("Three", ""), ("Two", ""));
        Assert.Equal(2, s.RemoveDuplicates(p.Id));
        Assert.Equal(["One", "Two", "Three"], s.Playlists[0].Entries.Select(e => e.Title));
    }

    [Fact]
    public void Two_files_of_the_same_song_count_as_one()
    {
        var s = new UserSession("local", null, null);
        var p = Make(s, ("One", ""), ("one", "/m/other/One.flac"));
        Assert.Equal(1, s.RemoveDuplicates(p.Id));
        Assert.Single(s.Playlists[0].Entries);
    }

    [Fact]
    public void None_to_remove_changes_nothing()
    {
        var s = new UserSession("local", null, null);
        var p = Make(s, ("One", ""), ("Two", ""));
        var changed = 0; s.Changed += () => changed++;
        Assert.Equal(0, s.RemoveDuplicates(p.Id));
        Assert.Equal(0, changed);
        Assert.Equal(0, s.RemoveDuplicates("nope"));
    }
}
