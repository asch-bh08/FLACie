using System.Text.Json.Nodes;
using FLACie.Core;
using FLACie.Server.Dj;
using Xunit;

namespace FLACie.Tests;

/// <summary>The DJ's HTTP shapes (docs/spec/ai-dj.md): camelCase, enums as lower-case strings, a wrong type is a 400 and changes nothing.</summary>
public class DjJsonTests
{
    static JsonObject J(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Settings_have_the_spec_field_names()
    {
        var s = new DjSettings { Enabled = true, Chatty = DjChatty.Rare, Auto = false, Playlists = { ["pl1"] = false } };
        var j = DjJson.Settings(s, voiceReady: true);
        Assert.Equal(new[] { "enabled", "chatty", "phone", "auto", "web", "windows", "backup", "talkAfterSkips", "style", "playlists", "deviceVoice", "volume", "duck", "songs", "voiceReady" }, j.Select(kv => kv.Key));
        Assert.Equal("rare", j["chatty"]!.GetValue<string>());
        Assert.False(j["auto"]!.GetValue<bool>());
        Assert.False(j["playlists"]!["pl1"]!.GetValue<bool>());
        Assert.True(j["voiceReady"]!.GetValue<bool>());
        Assert.Equal("often", DjJson.Settings(new DjSettings(), false)["chatty"]!.GetValue<string>());
    }

    [Fact]
    public void A_partial_update_changes_only_what_it_names()
    {
        var s = new DjSettings();
        Assert.Null(DjJson.Apply(J("""{ "enabled": true, "chatty": "Rare", "windows": false }"""), s));
        Assert.True(s.Enabled); Assert.Equal(DjChatty.Rare, s.Chatty); Assert.False(s.Windows); Assert.True(s.Web); Assert.True(s.Backup);
        Assert.Null(DjJson.Apply(J("""{ "playlist": { "id": "bedtime", "on": false } }"""), s));
        Assert.False(s.Playlists["bedtime"]);
        Assert.Null(DjJson.Apply(J("""{ "playlist": { "id": "bedtime", "on": null } }"""), s));
        Assert.Empty(s.Playlists);
        Assert.Null(DjJson.Apply(J("""{ "playlists": { "a": true, "b": false } }"""), s));
        Assert.Equal(2, s.Playlists.Count);
    }

    [Theory]
    [InlineData("""{ "enabled": "yes" }""")]
    [InlineData("""{ "enabled": null }""")]
    [InlineData("""{ "backup": 1 }""")]
    [InlineData("""{ "chatty": "loud" }""")]
    [InlineData("""{ "chatty": 1 }""")]
    [InlineData("""{ "playlists": { "a": 1 } }""")]
    [InlineData("""{ "playlists": [] }""")]
    [InlineData("""{ "playlist": { "on": true } }""")]
    [InlineData("""{ "playlist": { "id": "x", "on": "yes" } }""")]
    [InlineData("""{ "enabled": true, "phone": "no" }""")]
    public void A_wrong_type_is_refused_and_nothing_changes(string body)
    {
        var s = new DjSettings();
        Assert.NotNull(DjJson.Apply(J(body), s));
        Assert.False(s.Enabled); Assert.True(s.Phone); Assert.Equal(DjChatty.Often, s.Chatty); Assert.Empty(s.Playlists);
    }

    [Fact]
    public void The_next_request_is_read_in_camelCase()
    {
        var body = J("""
            { "surface": "auto",
              "next": { "title": "Yellow", "artist": "Coldplay", "album": "Parachutes", "albumArtist": "Coldplay", "year": 2000, "genre": "Rock", "durationMs": 266000,
                        "path": "/m/y.flac", "jellyfinId": "abc", "trackNo": 5, "discNo": 1 },
              "prev": { "title": "Shiver", "artist": "Coldplay" },
              "prevPlayedOut": false, "skippedRecently": true, "playlistId": "pl1", "queueDj": true, "localHour": 8, "weekday": 6, "why": "Same energy" }
            """);
        Assert.Null(DjJson.Request(body, out var r));
        Assert.Equal(DjSurface.Auto, r!.Surface);
        Assert.Equal(new DjSong("Yellow", "Coldplay", "Parachutes", "Coldplay", 2000, "Rock", 266000, "/m/y.flac", "abc", 5, 1), r.Next);
        Assert.Equal("Shiver", r.Prev!.Title);
        Assert.False(r.PrevPlayedOut); Assert.True(r.SkippedRecently);
        Assert.Equal("pl1", r.PlaylistId); Assert.True(r.QueueDj); Assert.Equal(8, r.LocalHour); Assert.Equal(6, r.Weekday); Assert.Equal("Same energy", r.Why);
    }

    [Fact]
    public void Missing_optional_fields_take_the_contract_defaults()
    {
        Assert.Null(DjJson.Request(J("""{ "surface": "phone", "next": { "title": "Yellow" }, "prev": null }"""), out var r));
        Assert.Equal("", r!.Next.Artist);
        Assert.Null(r.Prev); Assert.True(r.PrevPlayedOut); Assert.False(r.SkippedRecently);
        Assert.Null(r.PlaylistId); Assert.Null(r.QueueDj); Assert.Null(r.LocalHour); Assert.Null(r.Weekday); Assert.Null(r.Why);
    }

    [Theory]
    [InlineData("""{ "next": { "title": "Yellow" } }""")]
    [InlineData("""{ "surface": "car", "next": { "title": "Yellow" } }""")]
    [InlineData("""{ "surface": "web" }""")]
    [InlineData("""{ "surface": "web", "next": "Yellow" }""")]
    [InlineData("""{ "surface": "web", "next": { "title": "Yellow", "year": "2000" } }""")]
    [InlineData("""{ "surface": "web", "next": { "title": "Yellow", "durationMs": 1.5 } }""")]
    [InlineData("""{ "surface": "web", "next": { "title": 5 } }""")]
    [InlineData("""{ "surface": "web", "next": { "title": "Yellow" }, "prev": [] }""")]
    [InlineData("""{ "surface": "web", "next": { "title": "Yellow" }, "queueDj": "on" }""")]
    [InlineData("""{ "surface": "web", "next": { "title": "Yellow" }, "weekday": "Saturday" }""")]
    public void A_bad_next_request_is_refused(string body)
    {
        Assert.NotNull(DjJson.Request(J(body), out var r));
        Assert.Null(r);
    }

    [Fact]
    public void Playlist_ids_that_are_the_same_once_trimmed_are_one_playlist_and_the_last_wins()
    {
        var s = new DjSettings();
        Assert.Null(DjJson.Apply(J("""{ "playlists": { "bedtime": true, " bedtime ": false, "gym": true } }"""), s));   // was a 500 (ToDictionary threw)
        Assert.Equal(2, s.Playlists.Count);
        Assert.False(s.Playlists["bedtime"]); Assert.True(s.Playlists["gym"]);
    }

    [Fact]
    public void The_answer_is_speak_false_or_the_full_cue()
    {
        Assert.Equal("""{"speak":false}""", DjJson.Cue(null).ToJsonString());
        var j = DjJson.Cue(new DjCue("From nineteen ninety-seven, this is it.", "From 1997, this is it.", null, true));
        Assert.Equal("""{"speak":true,"text":"From nineteen ninety-seven, this is it.","caption":"From 1997, this is it.","clip":null,"backup":true,"introMs":null}""", j.ToJsonString());
        Assert.Equal("/api/dj/clip/x.mp3", DjJson.Cue(new DjCue("t", "c", "/api/dj/clip/x.mp3", false))["clip"]!.GetValue<string>());
    }
}
