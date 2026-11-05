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
