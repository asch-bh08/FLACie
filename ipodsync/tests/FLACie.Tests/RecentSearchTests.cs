using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Search remembers what was looked for (in the profile, so every device sees it): newest first, each once, the half-typed steps to it left out.</summary>
public class RecentSearchTests
{
    static UserSession New() => new("local", null, null);

    [Fact]
    public void Newest_first_and_each_once()
    {
        var s = New();
        s.AddRecentSearch("radiohead"); s.AddRecentSearch("bjork"); s.AddRecentSearch("Radiohead");
        Assert.Equal(["Radiohead", "bjork"], s.RecentSearches);
    }

    [Fact]
    public void A_search_replaces_the_half_typed_steps_to_it()
    {
        var s = New();
        s.AddRecentSearch("beat"); s.AddRecentSearch("beatles");
        Assert.Equal(["beatles"], s.RecentSearches);
    }

    [Fact]
    public void Keeps_the_last_ten_and_ignores_blank_and_one_letter()
    {
        var s = New();
        for (var i = 0; i < 14; i++) s.AddRecentSearch("song " + i);
        s.AddRecentSearch(" "); s.AddRecentSearch("a");
        Assert.Equal(10, s.RecentSearches.Count);
        Assert.Equal("song 13", s.RecentSearches[0]);
    }

    [Fact]
    public void Clear_empties_it()
    {
        var s = New();
        s.AddRecentSearch("blur");
        s.ClearRecentSearches();
        Assert.Empty(s.RecentSearches);
    }
}
