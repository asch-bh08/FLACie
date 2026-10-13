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

public class FadeBetweenTests
{
    static Track T(string album, int no, int disc = 1, string artist = "A") => new("p" + album + no + disc, "t" + no, artist, album, artist, no, disc, 200_000, 0, null, 0, TrackSource.Jellyfin);

    [Fact] public void NextTrackOfTheSameAlbumIsContinuous()
    {
        Assert.True(FadeBetween.Continuous(T("X", 3), T("X", 4))); Assert.True(FadeBetween.Continuous(T("X", 12), T("X", 1, disc: 2)));
        Assert.False(FadeBetween.Continuous(T("X", 3), T("X", 5))); Assert.False(FadeBetween.Continuous(T("X", 3), T("Y", 4))); Assert.False(FadeBetween.Continuous(T("X", 3, artist: "A"), T("X", 4, artist: "B")));
        Assert.False(FadeBetween.Continuous(null, T("X", 1))); Assert.False(FadeBetween.Continuous(T("", 1), T("", 2)));
    }

    [Fact] public void FlagsAndGain()
    {
        var (fin, fout) = FadeBetween.Flags(T("X", 1), T("X", 2), T("Y", 1)); Assert.False(fin); Assert.True(fout);
        Assert.Equal(1, FadeBetween.Gain(100_000, 200_000, 4_000, true, true)); Assert.Equal(0, FadeBetween.Gain(0, 200_000, 4_000, true, true));
        Assert.Equal(0.5, FadeBetween.Gain(2_000, 200_000, 4_000, true, true), 3); Assert.Equal(0.5, FadeBetween.Gain(198_000, 200_000, 4_000, true, true), 3);
        Assert.Equal(1, FadeBetween.Gain(0, 200_000, 4_000, false, true)); Assert.Equal(1, FadeBetween.Gain(0, 200_000, 0, true, true)); Assert.Equal(1, FadeBetween.Gain(0, 0, 4_000, true, true));
        Assert.Equal(1, FadeBetween.Gain(3_000, 6_000, 12_000, true, true));
        for (long p = -5_000; p <= 205_000; p += 777) { var g = FadeBetween.Gain(p, 200_000, 8_000, true, true); Assert.InRange(g, 0, 1); }
    }
}

public class RatingTests
{
    static Track T(string? path = null, string title = "Song", string artist = "Art") => new(path ?? ("http://jf/Audio/" + title + "/stream"), title, artist, "Al", artist, 1, 1, 1000, 0, null, 0, TrackSource.Jellyfin);
    static UserSession S() => new("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "r1" };

    [Fact] public void StarsMapToJellyfinsTenPointScaleAndBack()
    {
        Assert.Equal(8.0, Ratings.ToJellyfin(4)); Assert.Equal(10.0, Ratings.ToJellyfin(9)); Assert.Equal(0.0, Ratings.ToJellyfin(-3));
        Assert.Equal(4, Ratings.FromJellyfin(8)); Assert.Equal(4, Ratings.FromJellyfin(7.4)); Assert.Equal(0, Ratings.FromJellyfin(double.NaN)); Assert.Equal(5, Ratings.FromJellyfin(20));
        Assert.Equal("not rated", Ratings.Label(0)); Assert.Equal("1 star", Ratings.Label(1)); Assert.Equal("5 stars", Ratings.Label(5));
    }

    [Fact] public void ARatingIsKeptAndFoundByPathOrByTitleAndArtist()
    {
        var s = S(); var t = T();
        Assert.Equal(0, s.Rating(t));
        s.SetRating(t, 4); Assert.Equal(4, s.Rating(t));
        Assert.Equal(4, s.Rating(T("/other/path.flac")));   // the same song from another source
        Assert.Equal(0, s.Rating(T(title: "Another song")));
        s.SetRating(t, 2); Assert.Equal(2, s.Rating(t)); Assert.Single(s.Rated);
        s.SetRating(t, 9); Assert.Equal(5, s.Rating(t));
        s.SetRating(t, 0); Assert.Equal(0, s.Rating(t)); Assert.Empty(s.Rated);
    }

    [Fact] public void RatedListsNewestFirst()
    {
        var s = S();
        s.SetRating(T(title: "A"), 3); s.SetRating(T(title: "B"), 5);
        Assert.Equal(["B", "A"], s.Rated.Select(r => r.Entry.Title).ToArray()); Assert.Equal([5, 3], s.Rated.Select(r => r.Stars).ToArray());
    }
}
