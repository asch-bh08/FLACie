using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>owner, 25 Nov: "if Heads Will Roll downloads are irrelevant keep them irrelevant because we have the song".</summary>
public class SearchLayoutTests
{
    static readonly string[] Catalog = ["Heads Will Roll (A-Trak Remix)", "Heads Will Roll", "Heads Will Roll - Live", "Heads Will Roll Tonight", "Zero"];

    [Fact]
    public void The_song_asked_for_folds_its_catalog_copies_and_keeps_other_songs()
    {
        var asked = SearchLayout.AskedSong("heads will roll", "Heads Will Roll");
        Assert.Equal("Heads Will Roll", asked);
        var (shown, folded) = SearchLayout.FoldCopies(Catalog, s => s, asked);
        Assert.Equal(["Heads Will Roll Tonight", "Zero"], shown);
        Assert.Equal(["Heads Will Roll (A-Trak Remix)", "Heads Will Roll", "Heads Will Roll - Live"], folded);
    }

    [Fact]
    public void The_artist_may_be_typed_too()
    {
        Assert.Equal("Heads Will Roll", SearchLayout.AskedSong("heads will roll yeah yeah yeahs", "Heads Will Roll"));
        Assert.Equal("Heads Will Roll (Radio Edit)", SearchLayout.AskedSong("Heads Will Roll", "Heads Will Roll (Radio Edit)"));
    }

    [Fact]
    public void A_part_of_a_name_is_not_the_song_asked_for_and_nothing_is_folded()
    {
        Assert.Null(SearchLayout.AskedSong("heads", "Heads Will Roll"));
        Assert.Null(SearchLayout.AskedSong("heads will roll", null));
        var (shown, folded) = SearchLayout.FoldCopies(Catalog, s => s, null);
        Assert.Equal(Catalog, shown);
        Assert.Empty(folded);
    }
}
