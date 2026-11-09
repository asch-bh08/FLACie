using System.Net;
using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F23 on the server: lists read from fixtures (never the real Spotify or Deezer), the fallback, and songs queued as interest shows.</summary>
public sealed class SpotifyEmbedServiceTests : IDisposable
{
    static readonly DateTime T0 = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    readonly string dir = Path.Combine(Path.GetTempPath(), "spotsvc-" + Guid.NewGuid().ToString("N"));
    DateTime clock = T0;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    sealed class Web(bool spotifyUp) : HttpMessageHandler
    {
        // Deezer's playlist search for "The Prodigy" (F26.1): the editors' best-of, fans' lists that name the band, and some that do not
        public string DeezerSearch = """
            {"data": [
            {"id": 9001, "title": "100% The Prodigy", "nb_tracks": 40, "user": {"name": "Deezer Electro Editor"}, "picture_medium": "https://e-cdns-images.dzcdn.net/images/playlist/a/250x250.jpg"},
            {"id": 11, "title": "The Prodigy - Best Of", "nb_tracks": 60, "user": {"name": "raver88"}},
            {"id": 12, "title": "The Prodigy Mix", "nb_tracks": 50, "user": {"name": "Deezer Mixes"}},
            {"id": 13, "title": "The Prodigy - Remixes", "nb_tracks": 25, "user": {"name": "dj k"}},
            {"id": 14, "title": "the prodigy all songs", "nb_tracks": 120, "user": {"name": "fan"}},
            {"id": 15, "title": "THE PRODIGY ALL SONGS", "nb_tracks": 118, "user": {"name": "fan2"}},
            {"id": 16, "title": "The Prodigy tiny", "nb_tracks": 3, "user": {"name": "fan3"}},
            {"id": 17, "title": "Oldschool Rave Anthems 88-94", "nb_tracks": 80, "user": {"name": "raver"}},
            {"id": 18, "title": "Prodigy Mixtape (rap)", "nb_tracks": 30, "user": {"name": "hiphop"}},
            {"id": 19, "title": "Breakbeat with The Prodigy", "nb_tracks": 40, "user": {"name": "bb"}}
            ]}
            """;
        public List<string> Hosts = []; readonly List<string> urls = [];
        /// <summary>Deezer and the file mover never answer (a slow or dead source).</summary>
        public bool Hang;
        public int Asked(string url) => urls.Count(u => u == url);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            lock (urls) { Hosts.Add(r.RequestUri!.Host); urls.Add(r.RequestUri.ToString()); }
            if (Hang && (r.RequestUri!.Host == "api.deezer.com" || r.RequestUri.Host == "mover.test")) await Task.Delay(Timeout.Infinite, ct);
            var u = r.RequestUri.ToString();
            (HttpStatusCode, string) a = u.Contains("open.spotify.com/embed/playlist/")
                ? spotifyUp ? (HttpStatusCode.OK, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAudio", "embed", "this-is-the-prodigy.html")).Replace("This Is The Prodigy", "All Out 90s")) : (HttpStatusCode.TooManyRequests, "")
                : u.Contains("api.deezer.com/search/playlist") ? (HttpStatusCode.OK, DeezerSearch)
                : u == "http://mover.test/artist-sets" ? (HttpStatusCode.OK, """{"account": "https://soundcloud.com/the-prodigy", "sets": [{"title": "Experience: Expanded (Remixes & B-sides)", "url": "https://soundcloud.com/the-prodigy/sets/experience-expanded"}]}""")
                : u == "http://mover.test/playlist" ? (HttpStatusCode.OK, """{"name": "Experience: Expanded", "tracks": [{"title": "Charly", "artist": "The Prodigy"}, {"title": "Your Love", "artist": "The Prodigy"}]}""")
                : u.Contains("api.deezer.com/playlist/9001/tracks") ? (HttpStatusCode.OK, """{"data": [{"title": "Firestarter", "duration": 280, "artist": {"name": "The Prodigy"}}, {"title": "Breathe", "duration": 335, "artist": {"name": "The Prodigy"}}, {"title": "Omen", "duration": 216, "artist": {"name": "The Prodigy"}}]}""")
                : (HttpStatusCode.NotFound, "");
            return new HttpResponseMessage(a.Item1) { Content = new StringContent(a.Item2) };
        }
    }

    sealed class Dl : FLACie.Server.IDownloads
    {
        public List<string> Asked = [], Kinds = [];
        public IReadOnlyList<DownloadJob> JobsOf(UserSession s) => [];
        public void Subscribe(UserSession s, Action a) { }
        public void Unsubscribe(UserSession s, Action a) { }
        public void DismissFinished(UserSession s) { }
        public DownloadJob? JobFor(UserSession s, string artist, string title) => null;
        public DownloadTagStatus StatusOfKey(UserSession s, string key, long cloudAddedMs = 0, long? nowMs = null) => DownloadTagStatus.None;
        public void Song(UserSession s, CatalogSong song, bool hiRes = false, bool fallbackToNormal = true, string kind = "Search") { Asked.Add(song.Title); Kinds.Add(kind); }
        public void Album(UserSession s, CatalogAlbum album) { }
        public void Link(UserSession s, string url) { }
        public Task FetchSongAsync(UserSession s, string kind, string artist, string title, string album, string? artUrl, long durationMs, CancellationToken ct = default) => Task.CompletedTask;
        public bool Allowed => true;
        public bool Ready(UserSession s) => true;
        public bool MayBulk(UserSession s) => true;
    }

    static Track T(string title, string artist, int year) => new($"/m/{artist}/{title}", title, artist, "LP", artist, 1, 1, 200_000, year, null, 1, TrackSource.Jellyfin, Genre: "Electronic");

    (SpotifyPlaylists Svc, Web Web, Dl Dl, UserSession Me, WebSourceGate Gate) Rig(bool spotifyUp, long free = 2_000_000_000_000)
    {
        var lib = new Library([.. Enumerable.Range(0, 10).Select(i => T($"Song {i}", "The Prodigy", 1997)), T("Firestarter", "The Prodigy", 1996)]);
        var web = new Web(spotifyUp); var dl = new Dl(); clock = T0; var gate = new WebSourceGate(() => clock);
        var own = new List<MadePlaylist> { new("made:artist:prodigy", "Best of The Prodigy", "", null, lib.Songs.Take(8).Select(x => x.Path).ToList()), new("made:decade:1990", "1990s", "", null, lib.Songs.Select(x => x.Path).ToList()) };
        var svc = new SpotifyPlaylists(new HttpClient(web), new WebPlaylistStore(Path.Combine(dir, "l.json"), () => clock), gate, dl, _ => free, _ => own, () => clock, _ => lib);
        return (svc, web, dl, new UserSession("jellyfin", new JellyfinAccount("http://jf.test", "u1", "tester", "tok"), null) { Id = "u1" }, gate);
    }

    [Fact]
    public async Task A_decade_search_shows_spotifys_list_first_and_only_known_hosts_are_asked()
    {
        var (svc, web, dl, me, _) = Rig(spotifyUp: true);
        var shown = await svc.ForSearchAsync(me, "90s", default);
        Assert.Equal("spotify", shown[0].Source);
        Assert.Equal("All Out 90s", shown[0].Name);
        Assert.DoesNotContain(shown, l => l.Id == "made:decade:1990");             // one list per purpose
        Assert.All(web.Hosts, h => Assert.Contains(h, new[] { "open.spotify.com", "api.deezer.com" }));
        Assert.Equal(PlaylistFetchPolicy.PopularTake, dl.Asked.Count);              // a popular list: its first 2 missing songs
        Assert.Equal(["Breathe", "Smack My Bitch Up"], dl.Asked);                   // Firestarter is in the library already
    }

    [Fact]
    public async Task When_spotify_cannot_be_read_the_row_falls_back_and_spotify_rests_for_6_hours()
    {
        var (svc, web, _, me, gate) = Rig(spotifyUp: false);
        var shown = await svc.ForSearchAsync(me, "90s", default);
        Assert.Equal("made:decade:1990", Assert.Single(shown).Id);                  // never empty
        Assert.NotNull(gate.RestingUntil("spotify"));
        var asked = web.Hosts.Count;
        await svc.ForSearchAsync(me, "90s", default);
        Assert.Equal(asked, web.Hosts.Count);                                       // not asked again while resting
    }

    [Fact]
    public async Task An_artist_search_takes_deezers_best_of_and_queues_its_missing_songs_top_half_first()
    {
        var (svc, _, dl, me, _) = Rig(spotifyUp: true);
        var shown = await svc.ForSearchAsync(me, "prodigy", default);
        Assert.Equal("100% The Prodigy", shown[0].Name);
        Assert.DoesNotContain(shown, l => l.Source == "own" && l.Kind == WebPlaylistKind.Artist);
        Assert.Equal(["Breathe", "Omen"], dl.Asked);
        Assert.All(dl.Kinds, k => Assert.Equal("Playlist", k));   // downloads made as interest shows stay out of Search's Downloads panel (owner, 5 Nov: a search filled the screen with 20 of them)
        // opening it again within the hour queues nothing twice
        svc.Interest(me, shown[0], FetchTrigger.ArtistSearch);
        Assert.Equal(2, dl.Asked.Count);
    }

    [Fact]
    public async Task Under_100_GB_free_nothing_is_queued_automatically()
    {
        var (svc, _, dl, me, _) = Rig(spotifyUp: true, free: 50_000_000_000);
        var shown = await svc.ForSearchAsync(me, "90s", default);
        Assert.NotEmpty(shown);
        Assert.Empty(dl.Asked);
    }

    [Fact]
    public async Task An_artist_search_finds_their_own_soundcloud_sets_quietly_and_reads_songs_only_when_one_is_opened()
    {
        var (svc, web, dl, me, _) = Rig(spotifyUp: true);
        me.Profile["services"] = System.Text.Json.Nodes.JsonNode.Parse("""{"filemover": {"url": "http://mover.test", "key": "k"}}""");
        var shown = await svc.ForSearchAsync(me, "prodigy", default);
        var set = Assert.Single(shown, l => l.Source == "soundcloud");
        Assert.Equal("Experience: Expanded (Remixes & B-sides)", set.Name);
        Assert.Equal(0, web.Asked("http://mover.test/playlist"));                   // names only while searching
        clock = clock.AddSeconds(2);                                                // (one request a second to SoundCloud)
        var full = await svc.FillAsync(me, set, default);
        Assert.Equal(["Charly", "Your Love"], full.Tracks.Select(t => t.Title));
        Assert.Equal(1, web.Asked("http://mover.test/playlist"));
        Assert.Equal(1, web.Asked("http://mover.test/artist-sets"));
        await svc.ForSearchAsync(me, "prodigy", default);
        Assert.Equal(1, web.Asked("http://mover.test/artist-sets"));               // the account and its sets are remembered for 7 days
    }

    [Fact]
    public async Task An_artist_search_shows_many_real_lists_from_deezers_search_ours_only_when_under_4_and_last()
    {
        var (svc, web, dl, me, _) = Rig(spotifyUp: true);
        var shown = await svc.ForSearchAsync(me, "prodigy", default);
        var names = shown.Select(l => l.Name).ToList();
        Assert.Equal("100% The Prodigy", names[0]);                                     // the editors' first
        Assert.Contains("The Prodigy Mix", names); Assert.Contains("The Prodigy - Remixes", names); Assert.Contains("Breakbeat with The Prodigy", names);
        Assert.Single(names, n => n.Equals("the prodigy all songs", StringComparison.OrdinalIgnoreCase));   // near-duplicates once
        Assert.DoesNotContain("The Prodigy tiny", names);                               // under 8 songs
        Assert.DoesNotContain("Oldschool Rave Anthems 88-94", names);                   // neither names them nor is known to hold them
        Assert.DoesNotContain("Prodigy Mixtape (rap)", names);                          // Prodigy is not The Prodigy
        Assert.DoesNotContain(shown, l => l.Source == "own");                           // 4 or more real lists: ours stay out
        Assert.True(shown.Count <= WebPlaylistPick.ArtistRow);
        Assert.Equal(["Breathe", "Omen"], dl.Asked);                                    // only the best-of downloads (its top half)
        Assert.Equal(1, web.Asked("https://api.deezer.com/playlist/9001/tracks?limit=100"));
        Assert.Equal(0, web.Hosts.Count(h => h == "api.deezer.com") - 2);               // one search, one best-of read: nothing else
        // a fans' list is read when it is opened
        var mix = shown.First(l => l.Name == "The Prodigy Mix");
        Assert.Empty(mix.Tracks);
        clock = clock.AddSeconds(2);
        Assert.NotNull(await svc.FillAsync(me, mix, default));
        Assert.Equal(1, web.Asked("https://api.deezer.com/playlist/12/tracks?limit=100"));
    }

    [Fact]
    public async Task With_under_4_real_lists_our_own_come_after_them()
    {
        var (svc, web, _, me, _) = Rig(spotifyUp: true);
        web.DeezerSearch = """{"data": [{"id": 9001, "title": "100% The Prodigy", "nb_tracks": 40, "user": {"name": "Deezer Electro Editor"}}]}""";
        var shown = await svc.ForSearchAsync(me, "prodigy", default);
        Assert.Equal("deezer", shown[0].Source);
        Assert.Contains(shown, l => l.Source == "own");
        var order = shown.Select(l => l.Source).ToList();
        Assert.True(order.IndexOf("own") > order.FindLastIndex(x => x != "own"));
    }

    [Fact]
    public async Task After_a_restart_an_artist_search_answers_at_once_from_the_lists_already_read()
    {
        // owner, 6 Nov: searching Prodigy, the playlists took a while to come (every restart forgot what Deezer had said)
        var (svc, _, _, me, _) = Rig(spotifyUp: true);
        await svc.ForSearchAsync(me, "prodigy", default);
        var (again, web, _, me2, _) = Rig(spotifyUp: true);   // the same saved lists, a new server
        web.Hang = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var shown = await again.ForSearchAsync(me2, "prodigy", default);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"{sw.ElapsedMilliseconds} ms");
        Assert.Contains(shown, l => l.Name == "100% The Prodigy");
        Assert.Contains(shown, l => l.Name == "The Prodigy Mix");
    }

    [Fact]
    public async Task A_first_search_with_a_source_that_does_not_answer_shows_what_it_has_soon()
    {
        var (svc, web, _, me, _) = Rig(spotifyUp: true);
        web.Hang = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var shown = await svc.ForSearchAsync(me, "prodigy", default);
        Assert.True(sw.ElapsedMilliseconds < SpotifyPlaylists.FirstAnswer.TotalMilliseconds + 1000, $"{sw.ElapsedMilliseconds} ms");
        Assert.Contains(shown, l => l.Source == "own");
    }

    // owner, 9 Nov: search should show the playlists already saved at once, not wait for the online look-up
    [Fact]
    public async Task A_quick_search_shows_the_saved_lists_at_once_and_asks_nobody()
    {
        var (svc, _, _, me, _) = Rig(spotifyUp: true);
        var first = await svc.ForSearchAsync(me, "the prodigy", default);
        Assert.Contains(first, l => l.Name == "100% The Prodigy");
        // later, with every source slow: a new server reads the saved lists
        var (again, web, _, me2, _) = Rig(spotifyUp: true);
        web.Hang = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var quick = await again.ForSearchAsync(me2, "the prodigy", default, quick: true);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Contains(quick, l => l.Name == "100% The Prodigy");
        Assert.Empty(web.Hosts);
    }
}
