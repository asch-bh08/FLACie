using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The iPod desk's Now Playing card: the line that says what the file is.</summary>
public class DeskFormatTests
{
    static Track T(string path = "/music/a.flac", long size = 0, long ms = 200_000) =>
        new(path, "A", "Ann", "Alb", "Ann", 1, 1, ms, 2000, null, 0, TrackSource.Jellyfin, FilePath: path, Size: size);
    static MediaInfo I(string codec, int kbps, int rate, int depth) => new("flac", codec, kbps, rate, depth, 2, 0, 0, "", "jellyfin");

    [Fact] public void TheFileAsReadFormatDepthRateBitrate() =>
        Assert.Equal("FLAC · 24-bit / 96 kHz · 2,304 kbps", DeskFormat.Line(I("flac", 2304, 96_000, 24), T()));

    [Fact] public void CdQualityAndALossyFile()
    {
        Assert.Equal("FLAC · 16-bit / 44.1 kHz · 912 kbps", DeskFormat.Line(I("flac", 912, 44_100, 16), T()));
        Assert.Equal("MP3 · 44.1 kHz · 320 kbps", DeskFormat.Line(I("mp3", 320, 44_100, 0), T("/m/a.mp3")));
        Assert.Equal("ALAC · 24-bit / 48 kHz", DeskFormat.Line(I("alac", 0, 48_000, 24), T("/m/a.m4a")));
    }

    [Fact] public void NotReadYetTheExtensionAndABitrateFromTheSizeAndLength()
    {
        // 25 MB over 200 s: about 1,000 kbps; marked as about
        Assert.Equal("FLAC · ~1,000 kbps", DeskFormat.Line(null, T(size: 25_000_000)));
        Assert.Equal("FLAC", DeskFormat.Line(null, T()));
        Assert.Equal("MP3", DeskFormat.Line(new("", "", 0, 0, 0, 0, 0, 0, "", ""), T("/m/a.mp3")));
        Assert.Equal("", DeskFormat.Line(null, T("/m/noext", ms: 0)));
    }
}
