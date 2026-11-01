using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Shared songs and playlists: a short random link id per share, kept on the server, read back as it was made; anything that is not an id finds nothing.</summary>
public class ShareStoreTests
{
    static ShareStore New() => new(Path.Combine(Path.GetTempPath(), "flacie-share-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void A_share_reads_back_as_it_was_made()
    {
        var s = New();
        var id = s.Create("playlist", "Road trip", "ashley", [new PlaylistEntry("/a/1.flac", "Song 1", "Artist"), new PlaylistEntry("/a/2.flac", "Song 2", "Artist")]);
        var got = s.Get(id);
        Assert.NotNull(got);
        Assert.Equal("Road trip", got!.Name);
        Assert.Equal("playlist", got.Kind);
        Assert.Equal("ashley", got.By);
        Assert.Equal(["Song 1", "Song 2"], got.Entries.Select(e => e.Title));
    }

    [Fact]
    public void Ids_are_short_and_differ()
    {
        var s = New();
        var a = s.Create("song", "x", "me", [new PlaylistEntry("p", "t", "a")]);
        var b = s.Create("song", "x", "me", [new PlaylistEntry("p", "t", "a")]);
        Assert.NotEqual(a, b);
        Assert.InRange(a.Length, 8, 16);
        Assert.Matches("^[A-Za-z0-9]+$", a);
    }

    [Fact]
    public void Ids_cannot_be_guessed()
    {
        // ten characters from a 55-character alphabet, drawn by the system's cryptographic generator: about 58 bits. Only a signed-in person can open a link,
        // and past ten links that lead nowhere a minute they see nothing at all (the "share-miss" limit), so guessing would take longer than the universe has.
        var s = New();
        var ids = Enumerable.Range(0, 200).Select(_ => s.Create("song", "x", "me", [new PlaylistEntry("p", "t", "a")])).ToList();
        Assert.Equal(200, ids.Distinct().Count());
        var alphabet = ids.SelectMany(i => i).Distinct().Count();
        Assert.True(alphabet >= 50, alphabet.ToString());   // the whole alphabet is in use, not a few letters
        Assert.True(ids.Min(i => i.Length) * Math.Log2(55) >= 56);
    }

    [Theory]
    [InlineData("../keys/key")]
    [InlineData("nope")]
    [InlineData("")]
    public void Anything_that_is_not_a_share_finds_nothing(string id) => Assert.Null(New().Get(id));

    [Fact]
    public void An_empty_share_is_refused() => Assert.Throws<ArgumentException>(() => New().Create("playlist", "x", "me", []));
}
