using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FLACie.Tests;

/// <summary>"Recommended songs" under a playlist: library songs not in it, close to its songs, two per artist at most, the same list for the same playlist.</summary>
public class PlaylistRecsTests
{
    static Track T(int i, string artist, string genre, int year = 2000, long ms = 200_000) =>
        new($"jf:{i:x32}", $"Song {i}", artist, $"Album {artist}", artist, 1, 1, ms, year, null, 0, TrackSource.Jellyfin, Genre: genre, JellyfinId: $"{i:x32}");

    static Library Lib()
    {
        var songs = new List<Track>();
        for (var i = 0; i < 40; i++) songs.Add(T(i, "Rocker " + i % 8, "Rock"));
        for (var i = 40; i < 80; i++) songs.Add(T(i, "Popper " + i % 8, "Pop"));
        songs.Add(T(90, "Rocker 1", "Rock", ms: 20_000));            // a 20-second jingle
        songs.Add(T(91, "Reader", "Audiobook"));                     // not music
        return new Library(songs);
    }

    [Fact] public void Leaves_out_the_playlists_songs_and_caps_each_artist_at_two()
    {
        var lib = Lib(); var list = lib.Songs.Take(6).ToList();
        var picks = PlaylistRecs.Pick(lib, list, null, 50);
        Assert.NotEmpty(picks);
        Assert.DoesNotContain(picks, p => list.Any(l => l.Path == p.Path));
        Assert.All(picks.GroupBy(p => p.Artist), g => Assert.True(g.Count() <= 2, g.Key));
        Assert.Equal(picks.Count, picks.Select(p => p.Path).Distinct().Count());
        Assert.DoesNotContain(picks, p => p.DurationMs < 40_000 || p.Genre == "Audiobook");
        // a rock playlist gets rock first
        Assert.All(picks.Take(10), p => Assert.Equal("Rock", p.Genre));
    }

    [Fact] public void Same_playlist_same_list_and_n_and_skip_step_through_it()
    {
        var lib = Lib(); var list = lib.Songs.Take(6).ToList();
        var a = PlaylistRecs.Pick(lib, list, null, 4); var b = PlaylistRecs.Pick(lib, list.AsEnumerable().Reverse().ToList(), null, 4);
        Assert.Equal(4, a.Count);
        Assert.Equal(a.Select(t => t.Path), b.Select(t => t.Path));
        var all = PlaylistRecs.Ranked(lib, list, null);
        Assert.Equal(all.Skip(4).Take(4).Select(t => t.Path), PlaylistRecs.Pick(lib, list, null, 4, 4).Select(t => t.Path));
        // past the end it starts again
        Assert.Equal(all.Take(4).Select(t => t.Path), PlaylistRecs.Pick(lib, list, null, 4, all.Count).Select(t => t.Path));
        Assert.Empty(PlaylistRecs.Pick(lib, [], null, 10));
    }

    [Fact] public void A_big_playlist_is_judged_by_a_fixed_sample()
    {
        var lib = Lib(); var list = lib.Songs.Take(70).ToList();
        var a = PlaylistRecs.Pick(lib, list, null, 10).Select(t => t.Path).ToList();
        Assert.Equal(a, PlaylistRecs.Pick(lib, list.OrderBy(t => t.Title).ToList(), null, 10).Select(t => t.Path));
        Assert.DoesNotContain(a, p => list.Any(l => l.Path == p));
    }

    [Fact] public void Endpoint_finds_the_playlist_by_its_jellyfin_or_own_id()
    {
        var lib = Lib();
        var s = new UserSession("local", null, null) { Id = "ann" };
        typeof(UserSession).GetProperty("Library")!.SetValue(s, lib);
        var p = s.CreatePlaylist("Rock mix");
        foreach (var t in lib.Songs.Take(5)) s.AddToPlaylist(p.Id, t);
        static List<JsonElement> Picks(IResult r) => JsonSerializer.SerializeToElement(((IValueHttpResult)r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("picks").EnumerateArray().ToList();
        var got = Picks(PlaylistRecsApi.Get(s, p.Id, 3, 0, null));
        Assert.Equal(3, got.Count);
        Assert.All(got, g => Assert.DoesNotContain(lib.Songs.Take(5), t => t.Path == g.GetProperty("path").GetString()));
        Assert.Equal(404, ((IStatusCodeHttpResult)PlaylistRecsApi.Get(s, "nope", 3, 0, null)).StatusCode);
        Assert.Equal(401, ((IStatusCodeHttpResult)PlaylistRecsApi.Get(null, p.Id, 3, 0, null)).StatusCode);
    }
}
