using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>Importing a playlist from a Spotify or Deezer link (owner, 1 Nov).</summary>
public class PlaylistLinksTests
{
    [Theory]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=abc", PlaylistLinks.Service.Spotify, "playlist", "37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("https://open.spotify.com/intl-de/album/4aawyAB9vmqN3uQ7FjRGTy", PlaylistLinks.Service.Spotify, "album", "4aawyAB9vmqN3uQ7FjRGTy")]
    [InlineData("https://www.deezer.com/en/playlist/3155776842", PlaylistLinks.Service.Deezer, "playlist", "3155776842")]
    [InlineData("deezer.com/album/302127", PlaylistLinks.Service.Deezer, "album", "302127")]
    public void A_link_is_recognised(string url, PlaylistLinks.Service service, string kind, string id) =>
        Assert.Equal(new PlaylistLinks.Link(service, kind, id), PlaylistLinks.Recognise(url));

    [Theory]
    [InlineData("https://tidal.com/browse/playlist/0f1c4a3e-1234")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]   // a single video, not a playlist
    [InlineData("https://evil.example/soundcloud.com/x/sets/y")]
    [InlineData("not a link")]
    [InlineData("")]
    public void Other_links_are_not(string url) => Assert.Null(PlaylistLinks.Recognise(url));

    [Fact]
    public void A_spotify_embed_page_gives_the_name_and_each_songs_lead_artist()
    {
        const string html = """<html><script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"state":{"data":{"entity":{"type":"playlist","name":"Today's Top Hits","trackList":[{"title":"Loser","subtitle":"Tame Impala"},{"title":"Die With A Smile","subtitle":"Lady Gaga, Bruno Mars"},{"title":"","subtitle":"x"}]}}}}}}</script></html>""";
        var p = PlaylistLinks.FromSpotifyEmbed(html)!;
        Assert.Equal("Today's Top Hits", p.Name);
        Assert.Equal([new ImportedTrack("Tame Impala", "Loser"), new ImportedTrack("Lady Gaga", "Die With A Smile")], p.Tracks);
    }

    [Fact]
    public void A_page_with_no_songs_gives_nothing() => Assert.Null(PlaylistLinks.FromSpotifyEmbed("<html>sign in</html>"));

    [Fact]
    public void A_deezer_playlist_gives_its_songs_and_the_next_page()
    {
        const string json = """{"title":"Top Worldwide","tracks":{"data":[{"title":"Blinding Lights","artist":{"name":"The Weeknd"},"album":{"title":"After Hours"}}],"next":"https://api.deezer.com/playlist/1/tracks?index=25"}}""";
        var p = PlaylistLinks.FromDeezer(json, out var next)!;
        Assert.Equal("Top Worldwide", p.Name);
        Assert.Equal([new ImportedTrack("The Weeknd", "Blinding Lights", "After Hours")], p.Tracks);
        Assert.Equal("https://api.deezer.com/playlist/1/tracks?index=25", next);
        Assert.Null(PlaylistLinks.FromDeezer("""{"error":{"type":"DataException"}}""", out _));
    }

    [Theory]
    [InlineData("https://e-cdns-images.dzcdn.net/images/cover/abc/1000x1000-000000-80-0-0.jpg", "https://e-cdns-images.dzcdn.net/images/cover/abc/250x250-000000-80-0-0.jpg")]
    [InlineData("https://is1-ssl.mzstatic.com/image/thumb/x/600x600bb.jpg", "https://is1-ssl.mzstatic.com/image/thumb/x/250x250bb.jpg")]
    [InlineData("https://example.com/a.jpg", "https://example.com/a.jpg")]
    public void A_catalogue_cover_for_a_row_is_asked_for_at_250_px(string big, string small) => Assert.Equal(small, Art.SmallExternal(big));

    // ---- F25.1: YouTube, YouTube Music, SoundCloud and Apple Music links ----

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLx0sYbCqOb8TBPRdmBHs5Iftvv9TPboYG", PlaylistLinks.Service.YouTube, "playlist", "PLx0sYbCqOb8TBPRdmBHs5Iftvv9TPboYG")]
    [InlineData("https://music.youtube.com/playlist?list=RDCLAK5uy_kmPRjHDECIcuVwnKsx2Ng7fyNgFKWNJFs&si=x", PlaylistLinks.Service.YouTube, "playlist", "RDCLAK5uy_kmPRjHDECIcuVwnKsx2Ng7fyNgFKWNJFs")]
    [InlineData("https://www.youtube.com/watch?v=abc&list=PLx0sYbCqOb8TBPRdmBHs5Iftvv9TPboYG", PlaylistLinks.Service.YouTube, "playlist", "PLx0sYbCqOb8TBPRdmBHs5Iftvv9TPboYG")]
    [InlineData("https://soundcloud.com/the-prodigy/sets/the-fat-of-the-land-6?si=1", PlaylistLinks.Service.SoundCloud, "set", "the-prodigy/sets/the-fat-of-the-land-6")]
    [InlineData("https://music.apple.com/us/playlist/todays-hits/pl.f4d106fed2bd41149aaacabb233eb5eb", PlaylistLinks.Service.Apple, "playlist", "us/pl.f4d106fed2bd41149aaacabb233eb5eb")]
    [InlineData("https://music.apple.com/gb/album/the-fat-of-the-land/1443117453", PlaylistLinks.Service.Apple, "album", "gb/1443117453")]
    public void YouTube_SoundCloud_and_Apple_Music_links_are_recognised(string url, PlaylistLinks.Service service, string kind, string id) =>
        Assert.Equal(new PlaylistLinks.Link(service, kind, id), PlaylistLinks.Recognise(url));

    [Fact]
    public void Other_hosts_are_refused_with_the_list_of_supported_ones()
    {
        foreach (var name in new[] { "Spotify", "Deezer", "Apple Music", "YouTube", "YouTube Music", "SoundCloud" }) Assert.Contains(name, PlaylistLinks.Refusal);
        Assert.Equal("https://soundcloud.com/the-prodigy/sets/the-fat-of-the-land-6", PlaylistLinks.FetchUrl(PlaylistLinks.Recognise("https://soundcloud.com/the-prodigy/sets/the-fat-of-the-land-6")!));
    }

    [Theory]
    [InlineData("The Prodigy - Breathe (Official Video)", "The Prodigy", "The Prodigy", "Breathe")]
    [InlineData("Daft Punk - One More Time [Official Video]", "Daft Punk", "Daft Punk", "One More Time")]
    [InlineData("Firestarter (Official Audio)", "The Prodigy - Topic", "The Prodigy", "Firestarter")]
    [InlineData("Toxic (HD)", "BritneySpearsVEVO", "BritneySpears", "Toxic")]
    [InlineData("Corona – The Rhythm Of The Night (Lyric Video)", "Corona", "Corona", "The Rhythm Of The Night")]
    public void YouTube_titles_become_artist_and_title(string title, string channel, string artist, string song) =>
        Assert.Equal((artist, song), PlaylistLinks.CleanYouTube(title, channel));

    [Fact]
    public void The_ytdl_playlist_answer_is_read_capped_at_500_and_only_its_hosts_may_be_asked()
    {
        var many = "{\"name\": \"Gym\", \"tracks\": [" + string.Join(",", Enumerable.Range(0, 600).Select(i => $"{{\"title\": \"Artist {i} - Song {i} (Official Video)\", \"artist\": \"Channel\", \"durationMs\": 200000}}")) + "]}";
        var yt = PlaylistLinks.FromYtdl(many, PlaylistLinks.Service.YouTube)!;
        Assert.Equal(("Gym", 500), (yt.Name, yt.Tracks.Count));
        Assert.Equal(new ImportedTrack("Artist 0", "Song 0"), yt.Tracks[0]);
        var sc = PlaylistLinks.FromYtdl("{\"name\": \"The Fat of the Land\", \"tracks\": [{\"title\": \"Smack My Bitch Up\", \"artist\": \"The Prodigy\"}, {\"title\": \"Breathe\", \"artist\": \"The Prodigy\"}]}", PlaylistLinks.Service.SoundCloud)!;
        Assert.Equal([new ImportedTrack("The Prodigy", "Smack My Bitch Up"), new ImportedTrack("The Prodigy", "Breathe")], sc.Tracks);
        Assert.True(PlaylistLinks.YtdlAllowed("https://music.youtube.com/playlist?list=x"));
        Assert.True(PlaylistLinks.YtdlAllowed("https://soundcloud.com/a/sets/b"));
        Assert.False(PlaylistLinks.YtdlAllowed("https://example.com/playlist"));
        Assert.False(PlaylistLinks.YtdlAllowed("http://soundcloud.com/a/sets/b"));
        Assert.False(PlaylistLinks.YtdlAllowed("file:///etc/passwd"));
    }

    // ---- F25.3: an artist's own SoundCloud sets (the yt-dlp answers as Local saw them on frank, 5 Nov) ----

    const string ProdigySets = """
        {"account": "https://soundcloud.com/the-prodigy", "sets": [
          {"title": "The Fat Of The Land 25th Anniversary - Remixes", "url": "https://soundcloud.com/the-prodigy/sets/the-fat-of-the-land-25th"},
          {"title": "Experience: Expanded (Remixes & B-sides)", "url": "https://soundcloud.com/the-prodigy/sets/experience-expanded"},
          {"title": "PAY CLOSE ATTENTION: XL Recordings", "url": "https://soundcloud.com/the-prodigy/sets/pay-close-attention"},
          {"title": "The Fat of the Land", "url": "https://soundcloud.com/the-prodigy/sets/the-fat-of-the-land-6"},
          {"title": "Invaders Must Die", "url": "https://soundcloud.com/the-prodigy/sets/invaders-must-die"},
          {"title": "Invaders Must Die", "url": "https://soundcloud.com/the-prodigy/sets/invaders-must-die-2"},
          {"title": "Not a set", "url": "https://evil.example/the-prodigy/sets/x"}
        ]}
        """;

    // anyone's SoundCloud playlists found by name (owner, 12 Nov: "a playlist I saw on Spotify or my mate's one should be in my library")
    const string FoundSets = """
        {"sets": [
          {"title": "White Girl Summer 2000/2010s Playlist", "url": "https://soundcloud.com/user-272982561/sets/2010-white-girl-bangers", "uploader": "Collin", "tracks": 325, "likes": 1360, "art": "https://i1.sndcdn.com/artworks-x-large.jpg"},
          {"title": "Not a set", "url": "https://soundcloud.com/someone/a-track", "uploader": "x", "tracks": 1, "likes": 0, "art": ""},
          {"title": "White Girl Summer 2000/2010s Playlist", "url": "https://evil.example/sets/x", "uploader": "y", "tracks": 3, "likes": 0, "art": ""}
        ]}
        """;

    [Fact]
    public void Anyones_sets_found_by_name_become_lazy_playlists_with_their_size_likes_and_cover()
    {
        var sets = SoundCloudSets.FromSearch(FoundSets);
        var l = Assert.Single(sets);
        Assert.Equal(("soundcloud", "user-272982561/sets/2010-white-girl-bangers", "White Girl Summer 2000/2010s Playlist"), (l.Source, l.Id, l.Name));
        Assert.Equal(WebPlaylistKind.Other, l.Kind);
        Assert.Equal("Collin", l.Subject);
        Assert.Equal((325, 1360L), (l.Size, l.Likes));
        Assert.Equal("https://i1.sndcdn.com/artworks-x-t500x500.jpg", l.Picture);   // the bigger size SoundCloud keeps
        Assert.Empty(l.Tracks);   // songs read when it is opened or played
        // its cover can be shown (SoundCloud's image host is a public cover host); a look-alike host cannot
        Assert.Equal(l.Picture, Art.ExternalUrl(Art.ExternalKey(l.Picture!)));
        Assert.Null(Art.ExternalUrl(Art.ExternalKey("https://sndcdn.com.evil.example/x.jpg")));
        Assert.Empty(SoundCloudSets.FromSearch("""{"sets": []}"""));
        Assert.Empty(SoundCloudSets.FromSearch("""{"oops": 1}"""));
    }

    [Fact]
    public void An_artists_sets_are_found_duplicates_and_owned_albums_dropped_remix_sets_kept_beside_a_best_of()
    {
        Assert.True(SoundCloudSets.IsArtistsOwn("The Prodigy", "The Prodigy"));
        Assert.False(SoundCloudSets.IsArtistsOwn("Prodigy", "The Prodigy"));
        Assert.False(SoundCloudSets.IsArtistsOwn("The Prodigy Fan Remixes", "The Prodigy"));
        var sets = SoundCloudSets.FromAnswer(ProdigySets, "The Prodigy", ["The Fat of the Land (Remastered)"]);
        Assert.Equal(["The Fat Of The Land 25th Anniversary - Remixes", "Experience: Expanded (Remixes & B-sides)", "PAY CLOSE ATTENTION: XL Recordings", "Invaders Must Die"], sets.Select(l => l.Name));
        Assert.All(sets, l => { Assert.Equal("soundcloud", l.Source); Assert.True(l.Verified); Assert.Empty(l.Tracks); });   // songs read only when opened
        Assert.Equal("the-prodigy/sets/the-fat-of-the-land-25th", sets[0].Id);
        Assert.Empty(SoundCloudSets.FromAnswer("""{"account": null, "sets": []}""", "Prodigy", []));
        // with Spotify's best-of, the remix sets still show beside it; the plain album sets give way
        var spotify = new WebPlaylist("spotify", "37i9dQZF1DZ06evO1QVBQ2", "This Is The Prodigy", WebPlaylistKind.Artist, "The Prodigy", [new("Firestarter", "The Prodigy")]);
        var shown = WebPlaylistPick.ForAll([spotify, .. sets], "the prodigy", PlaylistQueryKind.Artist, DateTime.UtcNow).Select(l => l.Name).ToList();
        Assert.Equal("This Is The Prodigy", shown[0]);
        Assert.Contains("The Fat Of The Land 25th Anniversary - Remixes", shown);
        Assert.DoesNotContain("Invaders Must Die", shown);
    }
}
