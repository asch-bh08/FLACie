using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Search shows the person's playlists that hold what was typed, not only those named like it (owner, 7 Nov: searching "dreamer", "with me",
/// "eminem" or "aura" should list the playlists the song or artist is in).</summary>
public class PlaylistHitsTests
{
    static Playlist P(string id, string name, params (string Title, string Artist)[] songs) =>
        new(id, name, 0, null, songs.Select(s => new PlaylistEntry("/m/" + s.Title, s.Title, s.Artist)).ToList());

    static readonly List<Playlist> Lists =
    [
        P("1", "Gym", ("Lose Yourself", "Eminem"), ("Stronger", "Kanye West"), ("Without Me", "Eminem")),
        P("2", "Chill", ("Dreamer", "Ozzy Osbourne"), ("Stay With Me", "Sam Smith")),
        P("3", "Dreamer vibes", ("Yellow", "Coldplay")),
        P("4", "Empty"),
    ];

    static List<string> Ids(string q) => PlaylistHits.Find(Lists, Matching.SearchWords(q)).Select(h => h.List.Id).ToList();

    [Fact] public void APlaylistNamedLikeTheSearchComesFirstThenThoseHoldingTheSong()
    {
        Assert.Equal(["3", "2"], Ids("dreamer"));
        var hits = PlaylistHits.Find(Lists, Matching.SearchWords("dreamer"));
        Assert.Null(hits[0].Holds);
        Assert.Equal("Has “Dreamer”", hits[1].Holds);
    }

    [Fact] public void AnArtistFindsThePlaylistsWithTheirSongsSayingHowMany()
    {
        var hits = PlaylistHits.Find(Lists, Matching.SearchWords("eminem"));
        Assert.Equal(["1"], hits.Select(h => h.List.Id));
        Assert.Equal("2 songs by Eminem", hits[0].Holds);
    }

    [Fact] public void ASongTitleOfSeveralWordsFindsItsPlaylist()
    {
        Assert.Contains("2", Ids("with me"));
        Assert.Equal("Has “Stay With Me”", PlaylistHits.Find(Lists, Matching.SearchWords("stay with me"))[0].Holds);
    }

    [Fact] public void NothingTypedOrNothingHeldFindsNothing()
    {
        Assert.Empty(Ids(""));
        Assert.Empty(Ids("aura"));
    }

    [Fact] public void MoreMatchingSongsRankHigherAndTheListIsCapped()
    {
        var many = Enumerable.Range(0, 20).Select(i => P("x" + i, "List " + i, Enumerable.Range(0, i % 4 + 1).Select(j => ("Song " + j, "Aura")).ToArray())).ToList();
        var hits = PlaylistHits.Find(many, Matching.SearchWords("aura"), max: 5);
        Assert.Equal(5, hits.Count);
        Assert.All(hits, h => Assert.Equal(4, h.List.Entries.Count));
    }
}
