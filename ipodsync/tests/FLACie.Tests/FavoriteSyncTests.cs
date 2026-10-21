using System.Text.Json.Nodes;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// Favourites on several devices: whichever happened last wins, adding or removing. Before, devices just added each other's favourites together, so a
/// song unfavourited on one came back from any other that still had it.
/// </summary>
public class FavoriteSyncTests
{
    static JsonArray A(params (string t, long w)[] xs) => new(xs.Select(x => (JsonNode)new JsonObject { ["p"] = "/m/" + x.t, ["t"] = x.t, ["a"] = "Artist", ["w"] = x.w }).ToArray());
    static string[] Titles(JsonArray a) => a.OfType<JsonObject>().Select(o => o["t"]!.GetValue<string>()).OrderBy(x => x).ToArray();

    [Fact]
    public void Favourites_from_both_sides_are_kept()
    {
        var (favs, _) = FavoriteSync.Merge(A(("One", 10)), [], A(("Two", 20)), []);
        Assert.Equal(["One", "Two"], Titles(favs));
    }

    [Fact]
    public void A_removal_elsewhere_after_the_favourite_removes_it_here()
    {
        var (favs, gone) = FavoriteSync.Merge(A(("One", 10), ("Two", 10)), [], [], A(("One", 50)));
        Assert.Equal(["Two"], Titles(favs));
        Assert.Equal(["One"], Titles(gone));
    }

    [Fact]
    public void A_favourite_made_again_after_its_removal_stays()
    {
        var (favs, _) = FavoriteSync.Merge(A(("One", 90)), [], [], A(("One", 50)));
        Assert.Equal(["One"], Titles(favs));
    }

    [Fact]
    public void An_old_favourite_with_no_time_loses_to_any_removal()
    {
        var old = new JsonArray(new JsonObject { ["p"] = "/m/One", ["t"] = "One", ["a"] = "Artist" });
        var (favs, _) = FavoriteSync.Merge(old, [], [], A(("One", 1)));
        Assert.Empty(favs);
    }

    [Fact]
    public void The_same_song_under_another_path_is_one_favourite()
    {
        var other = new JsonArray(new JsonObject { ["p"] = "jf:abc", ["t"] = "One", ["a"] = "Artist", ["w"] = 5 });
        var (favs, _) = FavoriteSync.Merge(A(("One", 10)), [], other, []);
        Assert.Single(favs);
    }
}

/// <summary>The session's own favourites: removing records the removal; Clear takes them all away and Undo brings them back.</summary>
public class FavoriteClearTests
{
    static Track T(string title) => new("/m/" + title + ".flac", title, "Artist", "Album", "Artist", 1, 1, 1000, 2020, null, 0, TrackSource.Local, null, null, "", 0);

    [Fact]
    public void Clear_takes_them_all_away_and_Undo_brings_them_back()
    {
        var s = new UserSession("local", null, null);
        s.ToggleFavorite(T("One")); s.ToggleFavorite(T("Two"));
        var was = s.ClearFavorites();
        Assert.Empty(s.Favorites);
        Assert.Equal(2, (s.Profile["favgone"] as System.Text.Json.Nodes.JsonArray)!.Count);
        s.RestoreFavorites(was);
        Assert.Equal(["Two", "One"], s.Favorites.Select(f => f.Title));
    }

    [Fact]
    public void Unfavouriting_is_recorded_for_the_other_devices()
    {
        var s = new UserSession("local", null, null);
        s.ToggleFavorite(T("One")); s.ToggleFavorite(T("One"));
        Assert.Empty(s.Favorites);
        Assert.Single((s.Profile["favgone"] as System.Text.Json.Nodes.JsonArray)!);
    }
}
