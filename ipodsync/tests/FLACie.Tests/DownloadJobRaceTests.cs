using FLACie.Server;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 11 Nov: 22 downloads sat at "Filing into the library..." for hours although their files had arrived. Several sources race for a song; the
/// winner says Done, but a slower source still reports ("Filing into the library...") afterwards, and that late word put the job back to running.</summary>
public class DownloadJobRaceTests
{
    static DownloadJob Job() => new("id", "Sea People · Opus III", null, "Autoplay", "Opus III", "Sea People");

    [Fact]
    public void A_late_word_from_a_losing_source_does_not_undo_Done()
    {
        var j = Job();
        j.Apply(new(DownloadStage.Searching, "Looking on YouTube...", "ytdl"));
        j.Apply(new(DownloadStage.Done, "\"Sea People\" is ready to play", "soulseek"));
        j.Apply(new(DownloadStage.Importing, "Filing into the library...", "ytdl"));
        Assert.True(j.Finished);
        Assert.Equal(DownloadStage.Done, j.Stage);
        Assert.Equal("soulseek", j.Source);
        Assert.Equal("\"Sea People\" is ready to play", j.Message);
    }

    [Fact]
    public void A_late_win_still_turns_a_failure_into_Done() 
    {
        var j = Job();
        j.Apply(new(DownloadStage.Failed, "Not found on Soulseek", "soulseek"));
        j.Apply(new(DownloadStage.Searching, "Looking on YouTube...", "ytdl"));
        Assert.Equal(DownloadStage.Failed, j.Stage);
        j.Apply(new(DownloadStage.Done, "\"Sea People\" is ready to play", "ytdl"));
        Assert.Equal(DownloadStage.Done, j.Stage);
    }
}
