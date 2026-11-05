using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>F15: the first-song announcement ("Shuffling Pop... starting with LoveGame"), a setting that is on by default.</summary>
public class DjFirstSongTests
{
    static readonly DateTime T0 = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    static readonly DjSong Lovegame = new("LoveGame", "Lady Gaga", "The Fame", "", 2008, "pop", 200_000, "/m/fame/02.flac");

    static DjLine? Start(DjSettings s, int seed) =>
        DjPlanner.Plan(s, new DjRequest(DjSurface.Web, Lovegame, QueueStart: true, QueueName: "Pop", Shuffled: true), new DjMemory(), T0, new Random(seed));

    [Fact]
    public void On_by_default()
    {
        Assert.True(new DjSettings().FirstSong);
    }

    [Fact]
    public void With_the_setting_on_starting_Pop_shuffled_says_the_list_and_the_first_song()
    {
        var s = new DjSettings { Enabled = true };
        for (var seed = 0; seed < 40; seed++)
        {
            var line = Start(s, seed);
            Assert.True(line is not null, $"seed {seed}");
            Assert.Equal(DjSlot.Queue, line!.Slot);
            Assert.Contains("Pop", line.Caption);
            Assert.Contains("LoveGame", line.Caption);
        }
    }

    [Fact]
    public void With_the_setting_off_there_is_no_first_song_line()
    {
        var s = new DjSettings { Enabled = true, FirstSong = false };
        for (var seed = 0; seed < 40; seed++) Assert.NotEqual(DjSlot.Queue, Start(s, seed)?.Slot);
    }

    [Fact]
    public void The_setting_goes_through_json()
    {
        var was = new DjSettings();
        var now = new DjSettings { FirstSong = false };
        Assert.False((bool)DjJson.Patch(was, now)["firstSong"]!);
    }
}
