using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FLACie.Tests;

/// <summary>/api/made: the phone finds, opens and keeps ready-made playlists the way the web Search does (owner, 2 Nov).</summary>
public class MadeApiTests
{
    static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    static UserSession Person()
    {
        var s = new UserSession("local", null, null);
        s.AddDownloaded(Enumerable.Range(0, 20).Select(i => new Track($"jf:{i:x32}", $"Song {i}", $"Band {i}", "LP", $"Band {i}", 1, 1, 200_000, 2014, $"art{i}", i, TrackSource.Jellyfin, Genre: "Rock")));
        return s;
    }

    static JsonElement Body(IResult r) => JsonSerializer.SerializeToElement(((IValueHttpResult)r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    static int Status(IResult r) => ((IStatusCodeHttpResult)r).StatusCode ?? 200;

    [Fact] public void NobodySignedInIs401()
    {
        Assert.Equal(401, Status(MadeApi.Search(null, null, "rock")));
        Assert.Equal(401, Status(MadeApi.One(null, null, "made:genre:rock")));
        Assert.Equal(401, Status(MadeApi.Save(null, null, "made:genre:rock", out var saved)));
        Assert.False(saved);
    }

    [Fact] public void SearchListsTheMatches()
    {
        var s = Person();
        var lists = Body(MadeApi.Search(s, MadePlaylists.For(s, null, Now), "2010s")).GetProperty("lists").EnumerateArray().ToList();
        var d = Assert.Single(lists);
        Assert.Equal("made:decade:2010", d.GetProperty("id").GetString());
        Assert.Equal("2010s", d.GetProperty("name").GetString());
        Assert.Equal(20, d.GetProperty("count").GetInt32());
        Assert.StartsWith("art", d.GetProperty("artKey").GetString());
        Assert.Equal("20 songs · made for you", d.GetProperty("subtitle").GetString());
        Assert.StartsWith("jf:", d.GetProperty("first").GetProperty("path").GetString());
    }

    [Fact] public void OneListCarriesItsSongs()
    {
        var s = Person();
        var r = MadeApi.One(s, MadePlaylists.For(s, null, Now), "made:genre:rock");
        var tracks = Body(r).GetProperty("tracks").EnumerateArray().ToList();
        Assert.Equal(20, tracks.Count);
        Assert.StartsWith("jf:", tracks[0].GetProperty("path").GetString());
        Assert.Equal(200_000, tracks[0].GetProperty("durationMs").GetInt64());
        Assert.Equal(404, Status(MadeApi.One(s, MadePlaylists.For(s, null, Now), "made:genre:polka")));
    }

    [Fact] public void SaveMakesAPlaylistOfTheAccount()
    {
        var s = Person();
        var r = MadeApi.Save(s, MadePlaylists.For(s, null, Now), "made:genre:rock", out var saved);
        Assert.True(saved);
        var id = Body(r).GetProperty("id").GetString();
        var pl = Assert.Single(s.Playlists);
        Assert.Equal(id, pl.Id);
        Assert.Equal("Rock", pl.Name);
        Assert.Equal(20, pl.Entries.Count);
        Assert.Equal(404, Status(MadeApi.Save(s, MadePlaylists.For(s, null, Now), "made:x", out saved)));
        Assert.False(saved);
    }
}
