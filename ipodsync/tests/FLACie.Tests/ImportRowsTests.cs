using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F26.6: an imported playlist lists every song in order; missing ones are greyed with their live state.</summary>
public class ImportRowsTests
{
    static Track T(string a, string t) => new("/p/" + t, t, a, "Alb", a, 1, 1, 1000, 2000, null, 0, TrackSource.Jellyfin);
    static DownloadJob J(DownloadStage st, string msg) => new("j", "l", null) { Stage = st, Message = msg };

    [Fact]
    public void Every_song_keeps_its_place_and_missing_ones_show_their_state()
    {
        var songs = new List<ImportSong> { new("A", "One", "", "owned", null), new("B", "Two", "", "pending", null), new("C", "Three", "", "pending", null),
            new("D", "Four", "", "done", null), new("E", "Five", "", "failed", "no FLAC found"), new("F", "Six", "", "pending", null), new("G", "Seven", "", "requested", null) };
        var lib = new Dictionary<string, Track> { ["One"] = T("A", "One"), ["Four"] = T("D", "Four") };
        var jobs = new Dictionary<string, DownloadJob> { ["Two"] = J(DownloadStage.Downloading, "Downloading 40%"), ["Three"] = J(DownloadStage.Searching, "Searching") };

        var (play, wait) = ImportRows.Build(songs, (a, t) => lib.GetValueOrDefault(t), (a, t) => jobs.GetValueOrDefault(t));

        Assert.Equal(["One", "Four"], play.Select(t => t.Title));
        Assert.Equal(["Two", "Three", "Five", "Six", "Seven"], wait.Select(w => w.Title));
        Assert.Equal([1, 1, 2, 2, 2], wait.Select(w => w.Before));      // Two and Three sit before Four
        Assert.Equal("Downloading 40 %", wait[0].Status); Assert.Equal(40, wait[0].Percent);
        Assert.Equal("Searching", wait[1].Status);
        Assert.Equal("Couldn't find it (no FLAC found)", wait[2].Status); Assert.True(wait[2].Failed);
        Assert.Equal("Waiting", wait[3].Status); Assert.False(wait[3].Failed);
        Assert.Equal("Requested from Lidarr", wait[4].Status);
    }

    [Fact]
    public void A_retried_song_shows_its_new_job_not_the_old_failure()
    {
        var songs = new List<ImportSong> { new("E", "Five", "", "failed", "gone") };
        var (_, wait) = ImportRows.Build(songs, (a, t) => null, (a, t) => J(DownloadStage.Searching, "Searching"));
        Assert.Equal("Searching", wait[0].Status); Assert.False(wait[0].Failed);
    }
}

public class ServerImportsTests
{
    [Fact]
    public void A_playlist_finds_the_newest_import_that_made_it_with_its_songs_in_order()
    {
        ImportJob J(string id, int daysAgo, string pl, params string[] titles) => new() { Id = id, File = "f.txt", Created = DateTime.UtcNow.AddDays(-daysAgo),
            Lists = [new() { Name = "Mix", PlaylistId = pl, Items = titles.Select(t => new ImportItem { Artist = "A", Title = t, State = t == "x" ? "failed" : "pending", Message = t == "x" ? "no FLAC" : null }).ToList() }] };
        var jobs = new List<ImportJob> { J("old", 3, "p1", "a"), J("new", 1, "p1", "b", "x", "c"), J("other", 0, "p2", "z") };

        var f = ServerImports.Find(jobs, "p1")!.Value;
        Assert.Equal("new", f.JobId); Assert.Equal("Mix", f.Name);
        Assert.Equal(["b", "x", "c"], f.Songs.Select(s => s.Title));
        Assert.Equal("no FLAC", f.Songs[1].Message);
        Assert.Null(ServerImports.Find(jobs, "p9"));
        Assert.Equal("p2", ServerImports.OfJob(jobs, "other")!.Value.PlaylistId);
    }
}

public class ImportHealTests
{
    static Track T(string a, string t) => new("/p/" + t, t, a, "Alb", a, 1, 1, 1000, 2000, null, 0, TrackSource.Jellyfin);

    [Fact]
    public void Songs_of_a_finished_import_that_dropped_out_of_its_playlist_go_back_in_their_place()
    {
        // owner, 6 Nov: 25 of the 99 songs of "80s - 90s rally" were gone from the playlist after their Hi-Res swap
        var s = new UserSession("local", null, null);
        s.AddDownloaded([T("A", "One"), T("B", "Two"), T("C", "Three"), T("D", "Four")]);
        var pl = s.CreatePlaylist("Rally");
        s.AddToPlaylist(pl.Id, s.Library.Find("One", "A")!);
        s.AddToPlaylist(pl.Id, s.Library.Find("Four", "D")!);
        var list = new ImportList { Name = "Rally", PlaylistId = pl.Id, Items = [
            new() { Artist = "A", Title = "One", State = "owned" }, new() { Artist = "B", Title = "Two", State = "done" },
            new() { Artist = "C", Title = "Three", State = "done" }, new() { Artist = "D", Title = "Four", State = "done" },
            new() { Artist = "E", Title = "Five", State = "failed" }] };

        Assert.Equal(2, ImportManager.Heal(s, list));
        Assert.Equal(["One", "Two", "Three", "Four"], s.Playlists.Single().Entries.Select(e => e.Title));
        Assert.Equal(0, ImportManager.Heal(s, list));   // nothing twice
    }

    [Fact]
    public void A_playlist_the_person_deleted_is_not_made_again()
    {
        var s = new UserSession("local", null, null);
        s.AddDownloaded([T("A", "One")]);
        var list = new ImportList { Name = "Gone", PlaylistId = "deleted-id", Items = [new() { Artist = "A", Title = "One", State = "owned" }] };
        Assert.Equal(0, ImportManager.Heal(s, list));
        Assert.Empty(s.Playlists);
    }
}

public class PlaylistRepointTests
{
    [Fact]
    public void A_song_swapped_for_a_better_copy_is_the_new_copy_in_every_playlist()
    {
        var s = new UserSession("local", null, null);
        var old = new Track("/file?path=music%2Fx.m4a", "The Rhythm of the Night", "Corona", "Alb", "Corona", 1, 1, 1000, 1994, null, 0, TrackSource.Jellyfin);
        var other = new Track("/p/other", "Other", "B", "Alb", "B", 1, 1, 1000, 1994, null, 0, TrackSource.Jellyfin);
        s.AddDownloaded([old, other]);
        var a = s.CreatePlaylist("Rally"); s.AddToPlaylist(a.Id, other); s.AddToPlaylist(a.Id, old);
        var b = s.CreatePlaylist("Pop"); s.AddToPlaylist(b.Id, old);
        var now = old with { Path = "/file?path=music%2Fx%20%5BHi-Res%5D.flac" };

        Assert.Equal(2, s.RepointPlaylists(now));
        Assert.Equal(["/p/other", now.Path], s.Playlists.Single(p => p.Name == "Rally").Entries.Select(e => e.Path));
        Assert.Equal([now.Path], s.Playlists.Single(p => p.Name == "Pop").Entries.Select(e => e.Path));
    }
}
