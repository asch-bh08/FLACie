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
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    sealed class Web(bool spotifyUp) : HttpMessageHandler
    {
        public List<string> Hosts = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Hosts.Add(r.RequestUri!.Host);
            var u = r.RequestUri.ToString();
            (HttpStatusCode, string) a = u.Contains("open.spotify.com/embed/playlist/")
                ? spotifyUp ? (HttpStatusCode.OK, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAudio", "embed", "this-is-the-prodigy.html")).Replace("This Is The Prodigy", "All Out 90s")) : (HttpStatusCode.TooManyRequests, "")
                : u.Contains("api.deezer.com/search/playlist") ? (HttpStatusCode.OK, """{"data": [{"id": 9001, "title": "100% The Prodigy", "user": {"name": "Deezer Electro Editor"}}]}""")
                : u.Contains("api.deezer.com/playlist/9001/tracks") ? (HttpStatusCode.OK, """{"data": [{"title": "Firestarter", "duration": 280, "artist": {"name": "The Prodigy"}}, {"title": "Breathe", "duration": 335, "artist": {"name": "The Prodigy"}}, {"title": "Omen", "duration": 216, "artist": {"name": "The Prodigy"}}]}""")
                : (HttpStatusCode.NotFound, "");
            return Task.FromResult(new HttpResponseMessage(a.Item1) { Content = new StringContent(a.Item2) });
        }
    }

    sealed class Dl : FLACie.Server.IDownloads
    {
        public List<string> Asked = [];
        public IReadOnlyList<DownloadJob> JobsOf(UserSession s) => [];
        public void Subscribe(UserSession s, Action a) { }
        public void Unsubscribe(UserSession s, Action a) { }
        public void DismissFinished(UserSession s) { }
        public DownloadJob? JobFor(UserSession s, string artist, string title) => null;
        public DownloadTagStatus StatusOfKey(UserSession s, string key, long cloudAddedMs = 0, long? nowMs = null) => DownloadTagStatus.None;
        public void Song(UserSession s, CatalogSong song, bool hiRes = false, bool fallbackToNormal = true) => Asked.Add(song.Title);
        public void Album(UserSession s, CatalogAlbum album) { }
        public Task FetchSongAsync(UserSession s, string kind, string artist, string title, string album, string? artUrl, long durationMs, CancellationToken ct = default) => Task.CompletedTask;
        public bool Allowed => true;
        public bool Ready(UserSession s) => true;
        public bool MayBulk(UserSession s) => true;
    }

    static Track T(string title, string artist, int year) => new($"/m/{artist}/{title}", title, artist, "LP", artist, 1, 1, 200_000, year, null, 1, TrackSource.Jellyfin, Genre: "Electronic");

    (SpotifyPlaylists Svc, Web Web, Dl Dl, UserSession Me, WebSourceGate Gate) Rig(bool spotifyUp, long free = 2_000_000_000_000)
    {
        var lib = new Library([.. Enumerable.Range(0, 10).Select(i => T($"Song {i}", "The Prodigy", 1997)), T("Firestarter", "The Prodigy", 1996)]);
        var web = new Web(spotifyUp); var dl = new Dl(); var t = T0; var gate = new WebSourceGate(() => t);
        var own = new List<MadePlaylist> { new("made:artist:prodigy", "Best of The Prodigy", "", null, lib.Songs.Take(8).Select(x => x.Path).ToList()), new("made:decade:1990", "1990s", "", null, lib.Songs.Select(x => x.Path).ToList()) };
        var svc = new SpotifyPlaylists(new HttpClient(web), new WebPlaylistStore(Path.Combine(dir, "l.json"), () => t), gate, dl, _ => free, _ => own, () => t, _ => lib);
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
}
