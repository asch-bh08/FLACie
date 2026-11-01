using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

// A download for the family once hung on "Waiting for a peer..." and they gave up and used another app (owner, 1 Nov): lossless was taken
// "however slow", from a peer with a long queue, while a 320 kbps copy sat with someone who could send it at once. A peer that can send now
// comes first; lossless still wins between peers that are equally ready.
public class SoulseekSpeedTests
{
    static SlskdFile Flac(string user, bool slot = true, int queue = 0, long speed = 2_000_000) =>
        new(user, @"music\Artist\Artist - Song.flac", 30_000_000, slot, null, speed, queue, 200);
    static SlskdFile Mp3(string user, bool slot = true, int queue = 0, long speed = 2_000_000) =>
        new(user, @"music\Artist\Artist - Song.mp3", 8_000_000, slot, 320, speed, queue, 200);

    static List<string> Rank(params SlskdFile[] files) => SlskdClient.RankSong(files, "Artist", "Song", 200).Select(f => f.Username).ToList();

    [Fact]
    public void A_ready_320_copy_comes_before_a_lossless_copy_stuck_in_a_queue() =>
        Assert.Equal(["mp3-now", "flac-queued"], Rank(Flac("flac-queued", slot: false, queue: 40), Mp3("mp3-now")));

    [Fact]
    public void Between_ready_peers_lossless_still_wins() =>
        Assert.Equal(["flac-now", "mp3-now"], Rank(Mp3("mp3-now"), Flac("flac-now")));

    [Fact]
    public void Lossy_copies_stay_in_the_list_when_lossless_exists_so_there_is_someone_else_to_try() =>
        Assert.Contains("mp3-slow", Rank(Flac("flac-now"), Mp3("mp3-slow", slot: false, queue: 3)));

    [Fact]
    public void Below_320_is_still_never_taken() =>
        Assert.Empty(SlskdClient.RankSong([new SlskdFile("low", @"music\Artist\Artist - Song.mp3", 5_000_000, true, 192, 2_000_000, 0, 200)], "Artist", "Song", 200));
}

// How long a peer gets. With someone else left to try, a silent peer goes after 20s, as before. The last peer used to keep its chance for up
// to 8 minutes of "Waiting for a peer..."; now it gets a minute to start sending and 45s if it stalls, then the download moves on (the open
// sources, YouTube where it is on, Lidarr). A peer that is actually sending keeps going however long the song takes.
public class PeerRaceTests
{
    [Theory]
    [InlineData(0, 21_000, 21_000, true, true)]       // silent 21s, others waiting: swap
    [InlineData(0, 15_000, 15_000, true, false)]
    [InlineData(0, 59_000, 59_000, false, false)]     // the last peer: a minute to start
    [InlineData(0, 61_000, 61_000, false, true)]
    [InlineData(500_000, 200_000, 30_000, false, false)] // sending, paused 30s: keeps going
    [InlineData(500_000, 200_000, 46_000, false, true)]  // stalled 46s: give up
    public void When_a_peer_is_given_up(long bytes, long sinceStart, long sinceMoved, bool othersToTry, bool giveUp) =>
        Assert.Equal(giveUp, PeerRace.GiveUp(bytes, sinceStart, sinceMoved, othersToTry));

    [Fact]
    public void Up_to_three_peers_are_tried_at_once_until_one_is_sending()
    {
        Assert.True(PeerRace.StartAnother(active: 0, anySending: false, waiting: 5));
        Assert.True(PeerRace.StartAnother(active: 2, anySending: false, waiting: 5));
        Assert.False(PeerRace.StartAnother(active: 3, anySending: false, waiting: 5));
        Assert.False(PeerRace.StartAnother(active: 1, anySending: true, waiting: 5));
        Assert.False(PeerRace.StartAnother(active: 0, anySending: false, waiting: 0));
    }
}
