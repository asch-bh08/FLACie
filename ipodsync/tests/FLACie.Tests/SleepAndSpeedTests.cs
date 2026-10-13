using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The C# twin of the Android SleepAndSpeedTest: the sleep timer arithmetic and the speed rules must agree on both.</summary>
public class SleepAndSpeedTests
{
    static Track T(string album, string artist = "A", string albumArtist = "", long dur = 180_000, string genre = "") =>
        new($"/Audio/{(album + dur + artist).GetHashCode():x32}/x", "t", artist, album, albumArtist, 1, 1, dur, 0, null, 0, TrackSource.Jellyfin, Genre: genre);

    [Fact] public void FadeIsFullThenFallsToSilence()
    {
        Assert.Equal(1, SleepPlan.FadeGain(60_000)); Assert.Equal(1, SleepPlan.FadeGain(30_000));
        Assert.Equal(0, SleepPlan.FadeGain(0)); Assert.Equal(0, SleepPlan.FadeGain(-5)); Assert.Equal(0.5, SleepPlan.FadeGain(15_000), 3);
        var prev = 2.0;
        for (long ms = 30_000; ms >= 0; ms -= 500) { var g = SleepPlan.FadeGain(ms); Assert.True(g <= prev + 1e-9 && g is >= 0 and <= 1); prev = g; }
    }

    [Fact] public void TrackRemaining()
    {
        Assert.Equal(60_000, SleepPlan.TrackRemaining(120_000, 180_000)); Assert.Equal(0, SleepPlan.TrackRemaining(200_000, 180_000)); Assert.Null(SleepPlan.TrackRemaining(10, 0));
    }

    [Fact] public void AlbumEndsWhereTheNextSongIsAnotherAlbum()
    {
        var q = new[] { T("One"), T("One"), T("Two"), T("Two"), T("Two") };
        Assert.False(SleepPlan.AlbumEndsAfter(q, 0)); Assert.True(SleepPlan.AlbumEndsAfter(q, 1));
        Assert.False(SleepPlan.AlbumEndsAfter(q, 2)); Assert.True(SleepPlan.AlbumEndsAfter(q, 4)); Assert.True(SleepPlan.AlbumEndsAfter(q, 9));
    }

    [Fact] public void SameAlbumNameByAnotherArtistIsAnotherAlbumAndNoNameMeansOwnAlbum()
    {
        Assert.True(SleepPlan.AlbumEndsAfter([T("Hits", artist: "X"), T("Hits", artist: "Y")], 0));
        Assert.False(SleepPlan.AlbumEndsAfter([T("Hits", artist: "X", albumArtist: "VA"), T("hits ", artist: "Y", albumArtist: "va")], 0));
        Assert.True(SleepPlan.AlbumEndsAfter([T(""), T("")], 0));
    }

    [Fact] public void AlbumRemainingAddsTheRestOfTheAlbum()
    {
        var q = new[] { T("One", dur: 100_000), T("One", dur: 200_000), T("One", dur: 300_000), T("Two", dur: 50_000) };
        Assert.Equal(40_000 + 200_000 + 300_000, SleepPlan.AlbumRemaining(q, 0, 60_000));
        Assert.Equal(200_000, SleepPlan.AlbumRemaining(q, 2, 100_000));
        Assert.Null(SleepPlan.AlbumRemaining([T("One", dur: 100_000), T("One", dur: 0)], 0, 0));
    }

    [Fact] public void ExtendAndCanExtend()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddMinutes(5 + 15), SleepPlan.Extend(now.AddMinutes(5), now));
        Assert.Equal(now.AddMinutes(15), SleepPlan.Extend(now.AddMinutes(-9), now));
        Assert.True(SleepPlan.CanExtend(SleepMode.Minutes, 60_000)); Assert.True(SleepPlan.CanExtend(SleepMode.EndOfTrack, 0));
        Assert.False(SleepPlan.CanExtend(SleepMode.Minutes, 600_000)); Assert.False(SleepPlan.CanExtend(SleepMode.Off, 10)); Assert.False(SleepPlan.CanExtend(SleepMode.Minutes, null));
    }

    [Fact] public void FormatLeft() { Assert.Equal("15 min left", SleepPlan.FormatLeft(14 * 60_000 + 1)); Assert.Equal("59 s left", SleepPlan.FormatLeft(59_000)); Assert.Equal("0 s left", SleepPlan.FormatLeft(-5)); }

    [Fact] public void SpeedStepsClampAndLabels()
    {
        Assert.Equal(1.25, SpeedPolicy.Step(1.0, true)); Assert.Equal(0.75, SpeedPolicy.Step(1.0, false)); Assert.Equal(3.0, SpeedPolicy.Step(3.0, true)); Assert.Equal(0.5, SpeedPolicy.Step(0.5, false)); Assert.Equal(1.5, SpeedPolicy.Step(1.3, true));
        Assert.Equal(3, SpeedPolicy.Clamp(9)); Assert.Equal(0.5, SpeedPolicy.Clamp(0.1)); Assert.Equal(1, SpeedPolicy.Clamp(double.NaN));
        Assert.Equal("1x", SpeedPolicy.Label(1)); Assert.Equal("1.25x", SpeedPolicy.Label(1.25)); Assert.Equal("0.5x", SpeedPolicy.Label(0.5)); Assert.Equal("3x", SpeedPolicy.Label(3)); Assert.Equal("1.5x", SpeedPolicy.Label(1.5));
    }

    [Fact] public void SpokenWordAndLongItemControls()
    {
        foreach (var g in new[] { "Audiobook", "Podcast", "Spoken Word", "Hörbuch", "Speech", "Lecture" }) Assert.True(SpeedPolicy.IsSpoken(g), g);
        foreach (var g in new[] { "Rock", "Jazz", "Electronic", "Classical", "Soundtrack", "" }) Assert.False(SpeedPolicy.IsSpoken(g), g);
        Assert.False(SpeedPolicy.ShowLongItemControls("Rock", 200_000, 1)); Assert.True(SpeedPolicy.ShowLongItemControls("Audiobook", 200_000, 1));
        Assert.True(SpeedPolicy.ShowLongItemControls("Rock", 25 * 60_000, 1)); Assert.True(SpeedPolicy.ShowLongItemControls("Rock", 200_000, 1.5));
    }

    [Fact] public void ItemKeyAndResumeRules()
    {
        var id = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        Assert.Equal(id, SpeedPolicy.KeyOf($"https://srv/Audio/{id.ToUpperInvariant()}/stream?static=true"));
        Assert.Equal("/music/a.flac", SpeedPolicy.KeyOf("/music/a.flac"));
        const long book = 3 * 3_600_000L;
        Assert.Equal(0, SpeedPolicy.ResumeAt(null, book)); Assert.Equal(0, SpeedPolicy.ResumeAt(2_000, book)); Assert.Equal(600_000, SpeedPolicy.ResumeAt(600_000, book));
        Assert.Equal(0, SpeedPolicy.ResumeAt(book - 5_000, book)); Assert.Equal(0, SpeedPolicy.ResumeAt(100_000, 200_000));
        Assert.True(SpeedPolicy.ShouldSavePlace(600_000, book)); Assert.False(SpeedPolicy.ShouldSavePlace(1_000, book)); Assert.False(SpeedPolicy.ShouldSavePlace(book - 1_000, book)); Assert.False(SpeedPolicy.ShouldSavePlace(100_000, 200_000));
    }

    [Fact] public void MemoryKeepsTheNewestAndRoundTrips()
    {
        var m = new ItemMemory(3);
        m.Set("a", 1.5); m.Set("b", 2.0); m.Set("c", 0.75); m.Set("d", 1.25);
        Assert.Null(m.Get("a")); Assert.Equal(1.25, m.Get("d")); Assert.Equal(3, m.Count);
        m.Set("c", null); Assert.Null(m.Get("c"));
        var r = new ItemMemory(); r.Set("id=with=equals", 1.75); r.Set("plain", 2.5); r.Set("/music/a b.flac", 0.5);
        var back = ItemMemory.Decode(r.Encode());
        Assert.Equal(1.75, back.Get("id=with=equals")); Assert.Equal(2.5, back.Get("plain")); Assert.Equal(0.5, back.Get("/music/a b.flac")); Assert.Equal(3, back.Count);
        Assert.Equal(0, ItemMemory.Decode(null).Count); Assert.Equal(0, ItemMemory.Decode("garbage\n=\nx=notanumber").Count);
    }

    [Fact] public void MemoryFormatMatchesAndroid()
    {
        // the Android ItemMemory writes exactly this (so a synced or exported value reads the same on both)
        var m = new ItemMemory(); m.Set("k", 1.25);
        Assert.Equal("k=1.25", m.Encode());
    }
}

public class TimeSpeechTests
{
    [Fact] public void SpokenTimesMatchTheAndroidWording()
    {
        Assert.Equal("1 minute 37 seconds", TimeSpeech.Spoken(97)); Assert.Equal("5 minutes 2 seconds", TimeSpeech.Spoken(302)); Assert.Equal("45 seconds", TimeSpeech.Spoken(45));
        Assert.Equal("0 seconds", TimeSpeech.Spoken(0)); Assert.Equal("0 seconds", TimeSpeech.Spoken(double.NaN)); Assert.Equal("1 second", TimeSpeech.Spoken(1));
        Assert.Equal("1 hour 2 minutes", TimeSpeech.Spoken(3720)); Assert.Equal("2 hours", TimeSpeech.Spoken(7200)); Assert.Equal("1 minute", TimeSpeech.Spoken(60));
        Assert.Equal("1 minute 37 seconds of 5 minutes 2 seconds", TimeSpeech.Position(97, 302)); Assert.Equal("10 seconds", TimeSpeech.Position(10, 0));
    }
}

public class LoudnessTests
{
    [Fact] public void OffOrUnknownChangesNothing() { Assert.Equal(1, Loudness.Factor(-6, false)); Assert.Equal(1, Loudness.Factor(null, true)); Assert.Equal(1, Loudness.Factor(double.NaN, true)); }

    [Fact] public void DecibelsBecomeAFactorAndExtremesAreClamped()
    {
        Assert.Equal(1, Loudness.Factor(0, true), 4); Assert.Equal(0.5012, Loudness.Factor(-6, true), 3); Assert.Equal(1.9953, Loudness.Factor(6, true), 3);
        Assert.Equal(Loudness.Factor(-15, true), Loudness.Factor(-40, true)); Assert.Equal(Loudness.Factor(6, true), Loudness.Factor(20, true));
    }

    [Fact] public void JellyfinGainIsReadIntoTheTrack()
    {
        var t = new Track("p", "t", "a", "al", "a", 1, 1, 1000, 0, null, 0, TrackSource.Jellyfin, GainDb: -7.25);
        Assert.Equal(-7.25, t.GainDb);
        Assert.Null(new Track("p", "t", "a", "al", "a", 1, 1, 1000, 0, null, 0, TrackSource.Jellyfin).GainDb);
    }
}
