using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>F23.1: Spotify's embed page read from a saved fixture (never the real site), the small list store, the polite source gate and which lists search shows.</summary>
public sealed class SpotifyEmbedTests : IDisposable
{
    static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    readonly string dir = Path.Combine(Path.GetTempPath(), "spot-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    static WebPlaylist L(string source, string id, string name, WebPlaylistKind kind, string subject, long likes = 0, bool verified = false, int daysOld = 100) =>
        new(source, id, name, kind, subject, [new("Song", "Artist")], likes, likes * 50, likes / 10, Now.AddDays(-daysOld), verified);

    [Fact]
    public void A_saved_embed_page_parses_into_the_name_and_its_tracks()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAudio", "embed", "this-is-the-prodigy.html"));
        var (name, tracks) = SpotifyEmbed.Parse(html)!.Value;
        Assert.Equal("This Is The Prodigy", name);
        Assert.Equal(12, tracks.Count);
        Assert.Equal(new WebTrack("Firestarter", "The Prodigy", 280000), tracks[0]);
        Assert.Null(SpotifyEmbed.Parse("<html>nothing here</html>"));
    }

    [Fact]
    public void A_real_embed_page_saved_from_spotify_parses()   // Today's Top Hits, saved by Local on 5 Nov 2026
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAudio", "embed", "todays-top-hits-real.html"));
        var (name, tracks) = SpotifyEmbed.Parse(html)!.Value;
        Assert.Equal("Today’s Top Hits", name);
        Assert.Equal(50, tracks.Count);
        Assert.Equal(new WebTrack("Loser", "Tame Impala", 223069), tracks[0]);
        Assert.All(tracks, t => Assert.False(string.IsNullOrEmpty(t.Title) || string.IsNullOrEmpty(t.Artist)));
    }

    [Fact]
    public void Every_fixed_spotify_list_has_a_real_22_letter_id()   // Rock Classics had a 23rd letter and Spotify answered 500 (checked by Local, 5 Nov)
    {
        Assert.All(FLACie.Server.SpotifyPlaylists.Fixed, l => Assert.Matches("^[0-9A-Za-z]{22}$", l.Id));
        Assert.Equal(FLACie.Server.SpotifyPlaylists.Fixed.Length, FLACie.Server.SpotifyPlaylists.Fixed.Select(l => l.Id).Distinct().Count());
    }

    [Fact]
    public void Deezers_best_of_must_name_exactly_that_artist()   // real Deezer answers for "100% The Prodigy" (5 Nov) hold all of these
    {
        static bool Is(string title, string user, string artist) => FLACie.Server.SpotifyPlaylists.IsEditorsBestOf(title, user, artist);
        Assert.True(Is("100% The Prodigy", "Deezer Artist Editor", "The Prodigy"));
        Assert.True(Is("This Is Eminem", "Deezer Hip-Hop Editor", "Eminem"));
        Assert.True(Is("Best of Black Eyed Peas", "Deezer Pop Editor", "Black Eyed Peas"));
        Assert.True(Is("100% The Prodigy", "Deezer Artist Editor", "The Prodigy feat. Maxim"));   // the lead artist counts
        Assert.False(Is("100% Kay The Prodigy", "Deezer Artist Editor", "The Prodigy"));        // another artist whose name holds theirs
        Assert.False(Is("100% Prodigy", "Deezer Artist Editor", "The Prodigy"));                 // Prodigy of Mobb Deep
        Assert.False(Is("100% the prodigy", "georgeryder12", "The Prodigy"));                    // a fan's list
        Assert.False(Is("Eminem Workout", "Deezer Hip-Hop Editor", "Eminem"));                   // not a best-of
    }

    [Fact]
    public void Only_text_is_kept_capped_and_only_spotifys_own_ids_are_asked()
    {
        Assert.Equal(200, SpotifyEmbed.Clean(new string('x', 500)).Length);
        Assert.Equal("a b", SpotifyEmbed.Clean("a\u0000b"));
        Assert.Equal("https://open.spotify.com/embed/playlist/37i9dQZF1DXcBWIGoYBM5M", SpotifyEmbed.Url("37i9dQZF1DXcBWIGoYBM5M"));
        Assert.Null(SpotifyEmbed.Url("../../evil.com/x"));
        var big = "<script id=\"__NEXT_DATA__\" type=\"application/json\">{\"a\":{\"name\":\"Big\",\"trackList\":[" + string.Join(",", Enumerable.Range(0, 150).Select(i => $"{{\"title\":\"T{i}\",\"subtitle\":\"A\"}}")) + "]}}</script>";
        Assert.Equal(100, SpotifyEmbed.Parse(big)!.Value.Tracks.Count);   // the embed page's first 100
    }

    [Fact]
    public void The_store_holds_at_most_2000_lists_and_drops_lists_unused_for_30_days()
    {
        var t = Now; var file = Path.Combine(dir, "lists.json");
        var s = new WebPlaylistStore(file, () => t);
        for (var i = 0; i < 2100; i++) { s.Put(L("spotify", "id" + i, "List " + i, WebPlaylistKind.Other, "")); t = t.AddSeconds(1); }
        Assert.Equal(WebPlaylistStore.Max, s.Count);
        Assert.Null(s.Use("spotify", "id0"));                 // the least recently used went
        t = t.AddDays(20); s.Use("spotify", "id2099");
        t = t.AddDays(11); s.Put(L("deezer", "new", "New", WebPlaylistKind.Other, ""));
        Assert.Equal(2, s.Count);                              // only the one used 11 days ago and the new one are left
        s.Save();
        Assert.Equal(2, new WebPlaylistStore(file, () => t).Count);
        Assert.False(s.NeedsFetch("deezer", "new")); t = t.AddDays(2); Assert.True(s.NeedsFetch("deezer", "new"));
    }

    [Fact]
    public void A_source_that_errors_is_not_asked_again_for_6_hours_and_each_is_asked_once_a_second_at_most()
    {
        var t = Now; var g = new WebSourceGate(() => t);
        Assert.True(g.TryTake("spotify")); Assert.False(g.TryTake("spotify"));
        Assert.True(g.TryTake("deezer"));
        t = t.AddSeconds(1); Assert.True(g.TryTake("spotify"));
        g.Failed("spotify");
        t = t.AddHours(5.9); Assert.False(g.TryTake("spotify"));
        t = t.AddHours(0.2); Assert.True(g.TryTake("spotify"));
        g.Switch("deezer", false); t = t.AddSeconds(5); Assert.False(g.TryTake("deezer"));
    }

    static readonly WebPlaylist[] Found =
    [
        L("spotify", "s1", "This Is The Prodigy", WebPlaylistKind.Artist, "The Prodigy"),
        L("deezer", "d1", "100% The Prodigy", WebPlaylistKind.Artist, "The Prodigy"),
        L("own", "o1", "Best of The Prodigy", WebPlaylistKind.Artist, "The Prodigy"),
        L("soundcloud", "c1", "The Prodigy Best Of", WebPlaylistKind.Artist, "The Prodigy", likes: 5000),
        L("spotify", "s2", "Pop Rising", WebPlaylistKind.Genre, "Pop"), L("spotify", "s3", "Pop Hits", WebPlaylistKind.Other, "Pop"),
        L("deezer", "d2", "Pop Party", WebPlaylistKind.Other, "Pop"), L("own", "o2", "Pop", WebPlaylistKind.Genre, "Pop"),
        L("spotify", "s4", "Today's Top Pop", WebPlaylistKind.Chart, "Top Pop"), L("spotify", "s5", "Rock Classics", WebPlaylistKind.Genre, "Rock"),
        L("spotify", "s6", "All Out 90s", WebPlaylistKind.Decade, "1990s"), L("spotify", "s7", "Breakbeat Prodigy Remixes", WebPlaylistKind.Other, "The Prodigy remixes"),
    ];

    [Fact]
    public void With_a_spotify_best_of_no_other_best_of_for_that_artist_shows()
    {
        var shown = WebPlaylistPick.ForAll(Found, "prodigy", PlaylistQueryKind.Artist, Now);
        Assert.Contains(shown, l => l.Id == "s1");
        Assert.DoesNotContain(shown, l => l.Id is "d1" or "o1" or "c1");
    }

    [Fact]
    public void An_artist_search_also_shows_the_lists_with_their_songs_in_them()   // owner, 5 Nov: "only 2 playlists... where's the 90s mix"
    {
        static WebPlaylist With(string source, string id, string name, WebPlaylistKind kind, string subject, params (string Title, string Artist)[] songs) =>
            new(source, id, name, kind, subject, songs.Select(s => new WebTrack(s.Title, s.Artist)).ToList(), 0, 0, 0, Now.AddDays(-100), false);
        var found = new[]
        {
            With("deezer", "d1", "100% The Prodigy", WebPlaylistKind.Artist, "The Prodigy", ("Firestarter", "The Prodigy")),
            With("own", "r1", "The Prodigy Radio", WebPlaylistKind.Other, "The Prodigy Radio", ("Breathe", "The Prodigy")),
            With("spotify", "s6", "All Out 90s", WebPlaylistKind.Decade, "1990s", ("Firestarter", "The Prodigy"), ("Wonderwall", "Oasis"), ("Breathe", "The Prodigy feat. Maxim")),
            With("own", "o9", "Electronic", WebPlaylistKind.Genre, "Electronic", ("Omen", "The Prodigy"), ("Around the World", "Daft Punk")),
            With("spotify", "s5", "Rock Classics", WebPlaylistKind.Genre, "Rock", ("Wonderwall", "Oasis")),
            With("own", "o5", "Prodigy of Mobb Deep mix", WebPlaylistKind.Other, "x", ("Keep It Thoro", "Prodigy")),
        };
        var shown = WebPlaylistPick.ForAll(found, "the prodigy", PlaylistQueryKind.Artist, Now).Select(l => l.Id).ToList();
        Assert.Equal("d1", shown[0]);                                  // their own best-of first
        Assert.Contains("s6", shown); Assert.Contains("o9", shown);    // lists that hold their songs, the one with more of them first
        Assert.True(shown.IndexOf("s6") < shown.IndexOf("o9"));
        Assert.DoesNotContain("s5", shown);                            // nothing of theirs
        Assert.True(WebPlaylistPick.ArtistRow >= 8);
    }

    [Fact]
    public void Without_spotify_it_falls_back_to_deezer_then_our_own_never_empty()
    {
        var noSpotify = Found.Where(l => l.Source != "spotify").ToList();
        Assert.Equal("d1", WebPlaylistPick.ForAll(noSpotify, "the prodigy", PlaylistQueryKind.Artist, Now)[0].Id);
        var ownOnly = Found.Where(l => l.Source == "own").ToList();
        Assert.Equal("o1", WebPlaylistPick.ForAll(ownOnly, "the prodigy", PlaylistQueryKind.Artist, Now)[0].Id);
    }

    [Fact]
    public void Soundcloud_lists_under_1000_likes_and_not_verified_or_in_the_bottom_half_are_not_shown()
    {
        var sc = new[]
        {
            L("soundcloud", "a", "Hoodtrap Remixes", WebPlaylistKind.Other, "hoodtrap", likes: 40_000, daysOld: 30),
            L("soundcloud", "b", "Hoodtrap Bootlegs", WebPlaylistKind.Other, "hoodtrap", likes: 900, daysOld: 10),
            L("soundcloud", "c", "Hoodtrap Label Set", WebPlaylistKind.Other, "hoodtrap", likes: 200, verified: true),
            L("soundcloud", "d", "Hoodtrap Edits", WebPlaylistKind.Other, "hoodtrap", likes: 2000, daysOld: 2000),
            L("soundcloud", "e", "Hoodtrap Vol 2", WebPlaylistKind.Other, "hoodtrap", likes: 30_000, daysOld: 60),
        };
        var shown = WebPlaylistPick.SoundCloudShown(sc, Now).Select(l => l.Id).ToList();
        Assert.Equal(["c", "a", "e"], shown);   // verified first; b under 1,000; d in the bottom half (old)
    }

    [Fact]
    public void A_playlist_or_genre_search_gets_a_full_row_an_artist_a_few_a_song_one_or_none_and_the_filter_shows_all()
    {
        var artists = new[] { "The Prodigy", "Katy Perry" }; var genres = new[] { "Pop", "Rock" };
        Assert.Equal(PlaylistQueryKind.Playlists, WebPlaylistPick.Classify("pop", artists, genres));
        Assert.Equal(PlaylistQueryKind.Playlists, WebPlaylistPick.Classify("pop playlists", artists, genres));
        Assert.Equal(PlaylistQueryKind.Playlists, WebPlaylistPick.Classify("90s", artists, genres));
        Assert.Equal(PlaylistQueryKind.Artist, WebPlaylistPick.Classify("prodigy", artists, genres));
        Assert.Equal(PlaylistQueryKind.Song, WebPlaylistPick.Classify("firestarter", artists, genres));
        var pop = WebPlaylistPick.ForAll(Found, "pop", PlaylistQueryKind.Playlists, Now);
        Assert.True(pop.Count >= 4);
        Assert.Equal("s2", pop[0].Id);
        Assert.DoesNotContain(pop, l => l.Id == "o2");     // one Pop genre list: Spotify's
        Assert.Single(WebPlaylistPick.ForAll(Found, "90s", PlaylistQueryKind.Playlists, Now));
        Assert.True(WebPlaylistPick.ForAll(Found, "prodigy", PlaylistQueryKind.Artist, Now).Count <= WebPlaylistPick.ArtistRow);
        Assert.Empty(WebPlaylistPick.ForAll(Found, "firestarter", PlaylistQueryKind.Song, Now));
        // the Playlists filter: every match, best first; a genre narrows it
        var filter = WebPlaylistPick.ForFilter(Found, "pop", null, Now);
        Assert.True(filter.Count >= pop.Count);
        var rock = WebPlaylistPick.ForFilter(Found, "", "Rock", Now);
        Assert.Equal(["s5"], rock.Select(l => l.Id));
    }

    [Fact]
    public void Lists_with_the_artist_in_name_which_of_their_songs_each_holds()
    {
        static WebTrack W(string t, string a) => new(t, a);
        var known = new[]
        {
            new WebPlaylist("spotify", "a", "All Out 90s", WebPlaylistKind.Decade, "1990s", [W("Wannabe", "Spice Girls"), W("Firestarter", "The Prodigy"), W("Breathe", "The Prodigy")]),
            new WebPlaylist("deezer", "b", "Big Beat Classics", WebPlaylistKind.Other, "", [W("Firestarter", "The Prodigy"), W("Breathe", "The Prodigy"), W("Block Rockin' Beats", "The Chemical Brothers"), W("Poison", "The Prodigy"), W("Omen", "The Prodigy feat. Someone")]),
            new WebPlaylist("deezer", "c", "100% The Prodigy", WebPlaylistKind.Artist, "The Prodigy", [W("Firestarter", "The Prodigy")]),   // about them: in Playlists, not here
            new WebPlaylist("deezer", "d", "Rap Classics", WebPlaylistKind.Other, "", [W("Shook Ones", "Prodigy"), W("Keep It Thoro", "Prodigy")]),   // the other Prodigy
            new WebPlaylist("soundcloud", "e", "Rave Set", WebPlaylistKind.Other, "", []),                                                   // songs not read yet
        };
        var rows = WebPlaylistPick.WithArtistIn(known, "The Prodigy");
        Assert.Equal(["Big Beat Classics", "All Out 90s"], rows.Select(r => r.List.Name));
        Assert.Equal(["Firestarter", "Breathe", "Poison", "Omen"], rows[0].Songs);
        Assert.Equal("Firestarter, Breathe + 2", WebPlaylistPick.SongsLabel(rows[0].Songs));
        Assert.Equal("Firestarter, Breathe", WebPlaylistPick.SongsLabel(rows[1].Songs));
        Assert.Equal("Shook Ones, Keep It Thoro", WebPlaylistPick.SongsLabel(WebPlaylistPick.WithArtistIn(known, "Prodigy").Single().Songs));
    }
}
