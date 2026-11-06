using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FLACie.Tests;

/// <summary>The phone's search shows the web's playlists (Spotify's, Deezer's, SoundCloud sets, our own), the "Lists with" row, and opens a list with
/// every song in order, the missing ones marked (owner, 6 Nov: "everything we built, phone as well").</summary>
public class WebListsApiTests
{
    static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    static UserSession Person()
    {
        var s = new UserSession("local", null, null);
        s.AddDownloaded(Enumerable.Range(0, 20).Select(i => new Track($"jf:{i:x32}", $"Song {i}", "The Prodigy", "LP", "The Prodigy", 1, i + 1, 200_000, 1997, $"art{i}", i, TrackSource.Jellyfin, Genre: "Electronic")));
        return s;
    }

    static WebPlaylist Spotify(params WebTrack[] tracks) => new("spotify", "abc", "This Is The Prodigy", WebPlaylistKind.Artist, "The Prodigy", tracks, Editor: true, Picture: "https://i.scdn.co/image/x");

    static JsonElement Body(IResult r) => JsonSerializer.SerializeToElement(((IValueHttpResult)r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    static int Status(IResult r) => ((IStatusCodeHttpResult)r).StatusCode ?? 200;

    [Fact] public void NobodySignedInIs401()
    {
        Assert.Equal(401, Status(WebListsApi.Lists(null, [], [])));
        Assert.Equal(401, Status(WebListsApi.One(null, Spotify())));
        Assert.Equal(401, Status(WebListsApi.Featuring(null, [])));
    }

    [Fact] public void AnOutsideListIsACardWithItsSourceSizeAndPicture()
    {
        var s = Person();
        var l = Spotify(new WebTrack("Song 1", "The Prodigy"), new WebTrack("Firestarter", "The Prodigy"), new WebTrack("Song 3", "The Prodigy"));
        var d = Assert.Single(Body(WebListsApi.Lists(s, [l], [])).GetProperty("lists").EnumerateArray());
        Assert.Equal("web:spotify:abc", d.GetProperty("id").GetString());
        Assert.Equal("This Is The Prodigy", d.GetProperty("name").GetString());
        Assert.Equal("3 songs · Spotify editors", d.GetProperty("subtitle").GetString());
        Assert.Equal(3, d.GetProperty("count").GetInt32());
        Assert.Equal("https://i.scdn.co/image/x", d.GetProperty("picture").GetString());
        // the first song the library has gives the card its cover on the phone
        Assert.Equal("Song 1", d.GetProperty("first").GetProperty("title").GetString());
    }

    [Fact] public void AListKnownByNameOnlyShowsItsSize()
    {
        var l = new WebPlaylist("soundcloud", "set1", "Prodigy rave set", WebPlaylistKind.Other, "", [], Size: 42);
        var d = Assert.Single(Body(WebListsApi.Lists(Person(), [l], [])).GetProperty("lists").EnumerateArray());
        Assert.Equal("42 songs · SoundCloud", d.GetProperty("subtitle").GetString());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("first").ValueKind);
    }

    [Fact] public void OurOwnListKeepsItsOwnId()
    {
        var s = Person();
        var own = MadePlaylists.For(s, null, Now);
        var m = own.First();
        var l = new WebPlaylist("own", m.Id, m.Name, WebPlaylistKind.Genre, "", []);
        var d = Assert.Single(Body(WebListsApi.Lists(s, [l], own)).GetProperty("lists").EnumerateArray());
        Assert.Equal(m.Id, d.GetProperty("id").GetString());
    }

    [Fact] public void OpeningAListGivesEverySongInOrderWithTheMissingOnesMarked()
    {
        var s = Person();
        var r = WebListsApi.One(s, Spotify(new WebTrack("Firestarter", "The Prodigy", 280_000), new WebTrack("Song 2", "The Prodigy")));
        var b = Body(r);
        Assert.Equal("web:spotify:abc", b.GetProperty("id").GetString());
        var t = b.GetProperty("tracks").EnumerateArray().ToList();
        Assert.Equal(2, t.Count);
        Assert.Equal("Firestarter", t[0].GetProperty("title").GetString());
        Assert.True(t[0].GetProperty("missing").GetBoolean());
        Assert.Equal(JsonValueKind.Null, t[0].GetProperty("path").ValueKind);
        Assert.Equal(280_000, t[0].GetProperty("durationMs").GetInt64());
        Assert.False(t[1].GetProperty("missing").GetBoolean());
        Assert.StartsWith("jf:", t[1].GetProperty("path").GetString());
    }

    [Fact] public void TheListsWithAnArtistNameTheirSongs()
    {
        var l = new WebPlaylist("deezer", "9", "90s Rave", WebPlaylistKind.Decade, "1990s", [new WebTrack("Song 1", "The Prodigy"), new WebTrack("Other", "Someone")]);
        var d = Assert.Single(Body(WebListsApi.Featuring(Person(), [(l, (IReadOnlyList<string>)["Song 1"])])).GetProperty("lists").EnumerateArray());
        Assert.Equal("web:deezer:9", d.GetProperty("id").GetString());
        Assert.Equal(WebPlaylistPick.SongsLabel(["Song 1"]), d.GetProperty("subtitle").GetString());
        Assert.Equal("Song 1", d.GetProperty("first").GetProperty("title").GetString());
    }

    [Fact] public void AnOutsideListBecomesAMadeListToSave()
    {
        var s = Person();
        var m = WebListsApi.AsMade(s, Spotify(new WebTrack("Song 1", "The Prodigy"), new WebTrack("Firestarter", "The Prodigy")));
        Assert.Equal("web:spotify:abc", m.Id);
        Assert.Single(m.Paths);
    }
}
