using System.Text.Json.Nodes;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F7: SoundCloud in the source race: lossless &gt; SoundCloud original download &gt; YouTube Opus &gt; SoundCloud 128k.</summary>
public class SoundCloudSourceTests
{
    static readonly SourceCopy Flac = new("soulseek", "flac", 900, false);
    static readonly SourceCopy YouTube = new("ytdl", "opus", 160, false);
    static readonly SourceCopy Sc128 = new("soundcloud", "mp3", 128, false);
    static readonly SourceCopy ScOriginalMp3 = new("soundcloud", "mp3", 320, true);

    [Fact]
    public void A_SoundCloud_128k_copy_ranks_below_lossless_and_is_queued_for_upgrade()
    {
        Assert.True(SourceRank.Of(Flac) < SourceRank.Of(Sc128));
        Assert.True(SourceRank.Of(YouTube) < SourceRank.Of(Sc128));
        Assert.True(SourceRank.NeedsUpgrade(Sc128));
        Assert.True(SourceRank.NeedsUpgrade(YouTube));
        Assert.False(SourceRank.NeedsUpgrade(Flac));
        Assert.Equal([Flac, ScOriginalMp3, YouTube, Sc128], new[] { Sc128, YouTube, Flac, ScOriginalMp3 }.OrderBy(SourceRank.Of));
    }

    [Theory]
    [InlineData("wav")] [InlineData("flac")] [InlineData("aiff")]
    public void A_SoundCloud_original_WAV_or_FLAC_download_ranks_as_lossless(string ext)
    {
        var original = new SourceCopy("soundcloud", ext, 1411, true);
        Assert.Equal(SourceRank.Of(Flac), SourceRank.Of(original));
        Assert.False(SourceRank.NeedsUpgrade(original));
    }

    [Fact]
    public void The_original_download_is_picked_over_the_stream()
    {
        var formats = JsonNode.Parse("""[{"format_id": "hls_mp3_128", "ext": "mp3", "abr": 128}, {"format_id": "http_opus_64", "ext": "opus", "abr": 64}, {"format_id": "download", "ext": "wav"}]""")!.AsArray();
        var c = SoundCloud.Best(formats)!;
        Assert.Equal(("wav", true), (c.Codec, c.Original));
        var stream = SoundCloud.Best(JsonNode.Parse("""[{"format_id": "http_opus_64", "ext": "opus", "abr": 64}, {"format_id": "hls_mp3_128", "ext": "mp3", "abr": 128}]""")!.AsArray())!;
        Assert.Equal(("mp3", 128, false), (stream.Codec, stream.Kbps, stream.Original));
    }
}
