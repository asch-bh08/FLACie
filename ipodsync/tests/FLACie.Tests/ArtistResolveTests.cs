using FLACie.Core;
using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>F24: artist names fixed per song in Jellyfin, from a dry-run list the owner approves; backed up and undoable. No real Jellyfin.</summary>
public sealed class ArtistResolveTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "artfix-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }
    static int n;
    static Track T(string title, string artist, string album, string? albumArtist = null) =>
        new($"/m/{++n}", title, artist, album, albumArtist ?? artist, 1, 1, 200_000, 1997, null, 0, TrackSource.Jellyfin, JellyfinId: "id" + n);

    static List<Track> Library() =>
    [
        T("Smack My Bitch Up", "The Prodigy", "The Fat of the Land"), T("Firestarter", "The Prodigy", "The Fat of the Land"), T("Omen", "The Prodigy", "Invaders Must Die"),
        T("Breathe", "Prodigy", "The Fat of the Land"),            // The Prodigy's album, tagged wrong
        T("Firestarter", "Prodigy", "Their Law: The Singles"),     // The Prodigy's song title
        T("Out of Space", "Prodigy", "Experience"),                // only the catalog knows
        T("Keep It Thoro", "Prodigy", "H.N.I.C."), T("Rock Dat Shit", "Prodigy", "H.N.I.C."),   // the rapper: stays
        T("Shook Ones", "Prodigy of Mobb Deep", "The Infamous"),
        T("Stan", "Eminem feat. Dido", "The Marshall Mathers LP", "Eminem"), T("Lose Yourself", "Eminem", "8 Mile"),
        T("Bridge over Troubled Water", "Simon & Garfunkel", "Bridge over Troubled Water"),   // a duo: stays
    ];

    static ArtistChange For(List<ArtistChange> plan, string title, string artist) => plan.Single(c => c.Title == title && c.OldArtists[0] == artist);

    [Fact]
    public void Prodigy_songs_go_to_The_Prodigy_only_when_their_album_title_or_the_catalog_says_so()
    {
        var plan = ArtistResolve.Plan(Library(), (title, artist) => title == "Out of Space" && artist == "The Prodigy");
        Assert.Equal(["The Prodigy"], For(plan, "Breathe", "Prodigy").NewArtists);
        Assert.Equal("The Prodigy", For(plan, "Breathe", "Prodigy").NewAlbumArtist);
        Assert.Contains("album", For(plan, "Breathe", "Prodigy").Why);
        Assert.Equal(["The Prodigy"], For(plan, "Firestarter", "Prodigy").NewArtists);
        Assert.Equal(["The Prodigy"], For(plan, "Out of Space", "Prodigy").NewArtists);
        Assert.DoesNotContain(plan, c => c.Album == "H.N.I.C.");
        Assert.DoesNotContain(plan, c => c.Title == "Bridge over Troubled Water");
        Assert.Equal([("Out of Space", "The Prodigy"), ("Keep It Thoro", "The Prodigy"), ("Rock Dat Shit", "The Prodigy")], ArtistResolve.CatalogQuestions(Library()));
    }

    [Fact]
    public void Prodigy_of_Mobb_Deep_joins_Prodigy_the_rapper_not_The_Prodigy()
    {
        var c = For(ArtistResolve.Plan(Library()), "Shook Ones", "Prodigy of Mobb Deep");
        Assert.Equal(["Prodigy"], c.NewArtists);
    }

    [Fact]
    public void Eminem_feat_Dido_gives_artist_Eminem_with_Dido_featured_and_the_album_artist_stays()
    {
        var c = For(ArtistResolve.Plan(Library()), "Stan", "Eminem feat. Dido");
        Assert.Equal(["Eminem", "Dido"], c.NewArtists);
        Assert.Equal("Eminem", c.NewAlbumArtist);
        var amp = ArtistResolve.Plan([.. Library(), T("Guilty Conscience", "Eminem & Dr. Dre", "The Slim Shady LP", "Eminem")]);
        Assert.Equal(["Eminem", "Dr. Dre"], For(amp, "Guilty Conscience", "Eminem & Dr. Dre").NewArtists);
    }

    sealed class Jellyfin : IArtistWriter
    {
        public Dictionary<string, (IReadOnlyList<string> Artists, string AlbumArtist)> Items = [];
        public List<string> Writes = [];
        public Task<(IReadOnlyList<string> Artists, string AlbumArtist)?> ReadAsync(string id, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<string>, string)?>(Items.TryGetValue(id, out var v) ? v : null);
        public Task<bool> WriteAsync(string id, IReadOnlyList<string> artists, string albumArtist, CancellationToken ct) { Writes.Add(id); Items[id] = (artists, albumArtist); return Task.FromResult(true); }
    }

    [Fact]
    public async Task Nothing_is_written_before_approval_then_written_with_a_backup_and_undoable()
    {
        var lib = Library(); var jf = new Jellyfin();
        foreach (var t in lib) jf.Items[t.JellyfinId!] = ([t.Artist], t.AlbumArtist);
        var asked = 0;
        var svc = new ArtistResolveService(Path.Combine(dir, "fix.json"), () => DateTime.UtcNow);
        var plan = await svc.PlanAsync(lib, (title, artist, ct) => { asked++; return Task.FromResult<bool?>(title == "Out of Space"); }, default);
        Assert.Equal(5, plan.Count);
        Assert.Empty(jf.Writes);                                                    // a dry run: nothing written
        Assert.Equal(3, asked);
        await svc.PlanAsync(lib, (_, _, _) => { asked++; return Task.FromResult<bool?>(false); }, default);
        Assert.Equal(3, asked);                                                     // the catalog's answers are kept
        // the list survives a restart, still unapproved
        var again = new ArtistResolveService(Path.Combine(dir, "fix.json"), () => DateTime.UtcNow);
        Assert.Equal(5, again.Pending.Count); Assert.False(again.Approved);
        Assert.Equal(5, await again.ApproveAsync(jf, default));
        var breathe = lib.First(t => t.Title == "Breathe").JellyfinId!;
        Assert.Equal(["The Prodigy"], jf.Items[breathe].Artists);
        Assert.Contains(again.Done, d => d.Change.ItemId == breathe && d.BackupArtists.SequenceEqual(["Prodigy"]));
        // undo one, then all: Jellyfin gets the old values back
        Assert.Equal(1, await again.UndoAsync(jf, breathe, default));
        Assert.Equal(["Prodigy"], jf.Items[breathe].Artists);
        Assert.Equal(4, await again.UndoAsync(jf, null, default));
        Assert.Equal(["Eminem feat. Dido"], jf.Items[lib.First(t => t.Title == "Stan").JellyfinId!].Artists);
        Assert.False(again.Approved);
    }

    [Fact]
    public async Task After_approval_new_songs_are_fixed_after_a_sync_and_never_before()
    {
        var lib = Library(); var jf = new Jellyfin();
        foreach (var t in lib) jf.Items[t.JellyfinId!] = ([t.Artist], t.AlbumArtist);
        var svc = new ArtistResolveService(null, () => DateTime.UtcNow);
        Func<string, string, CancellationToken, Task<bool?>> no = (_, _, _) => Task.FromResult<bool?>(false);
        Assert.Equal(0, await svc.AfterSyncAsync(lib, jf, no, default));            // not approved: nothing
        Assert.Empty(jf.Writes);
        await svc.PlanAsync(lib, no, default); await svc.ApproveAsync(jf, default);
        var writes = jf.Writes.Count;
        var newSong = T("Diesel Power", "Prodigy", "The Fat of the Land"); jf.Items[newSong.JellyfinId!] = (["Prodigy"], "Prodigy");
        Assert.Equal(1, await svc.AfterSyncAsync([.. lib, newSong], jf, no, default));   // only the new one: the others are done
        Assert.Equal(writes + 1, jf.Writes.Count);
    }

    sealed class FakeJellyfin : HttpMessageHandler
    {
        public string? Posted; public string? PostedTo;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.Method == HttpMethod.Post) { PostedTo = r.RequestUri!.AbsolutePath; Posted = await r.Content!.ReadAsStringAsync(ct); return new(System.Net.HttpStatusCode.NoContent); }
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"Id": "id9", "Name": "Breathe", "Artists": ["Prodigy"], "AlbumArtist": "Prodigy", "Genres": ["Electronic"], "ProductionYear": 1997}""") };
        }
    }

    [Fact]
    public async Task The_write_is_jellyfins_item_update_keeping_the_rest_of_the_item()
    {
        var h = new FakeJellyfin();
        var w = new JellyfinArtistWriter(new JellyfinClient(new HttpClient(h), "test"), new JellyfinAccount("http://jf.test", "u1", "tester", "tok"));
        var r = (await w.ReadAsync("id9", default))!.Value;
        Assert.Equal(["Prodigy"], r.Artists); Assert.Equal("Prodigy", r.AlbumArtist);
        Assert.True(await w.WriteAsync("id9", ["The Prodigy"], "The Prodigy", default));
        Assert.Equal("/Items/id9", h.PostedTo);
        var o = System.Text.Json.Nodes.JsonNode.Parse(h.Posted!)!;
        Assert.Equal("The Prodigy", (string?)o["Artists"]![0]);
        Assert.Equal("The Prodigy", (string?)o["AlbumArtist"]);
        Assert.Equal("The Prodigy", (string?)o["AlbumArtists"]![0]!["Name"]);
        Assert.Equal(1997, (int)o["ProductionYear"]!);                             // the rest of the item as Jellyfin had it
    }

    // the owner's first dry-run list (5 Nov) split bands, matched "Bob" to B.o.B, turned P!nk into PNK and read "&amp;" as a name
    static List<Track> Owners() =>
    [
        T("Baby Love", "Diana Ross & the Supremes", "40 Golden Motown Greats"), T("Upside Down", "Diana Ross", "Diana"),
        T("Paid in Full", "Eric B. & Rakim", "Paid in Full"), T("Harlem Shuffle", "Bob & Earl", "Baby Driver"), T("Airplanes", "B.o.B", "B.o.B Presents"),
        T("Room at the Top", "Tom Petty and the Heartbreakers", "Echo"), T("Free Fallin'", "Tom Petty", "Full Moon Fever"),
        T("Just Give Me a Reason", "P!nk feat. Nate Ruess", "Now 84"), T("So What", "P!nk", "Funhouse"),
        T("The Message", "Grandmaster Flash &amp; The Furious Five", "The Best Rap Album"),
        T("Nude Night", "The Chemical Brothers feat. Th", "The Golden Path CDM"), T("Galvanize", "The Chemical Brothers", "Push the Button"),
        T("One Dance", "Drake feat. WizKid (4) & Kyla", "Now 95"), T("Hotline Bling", "Drake", "Views"),
        T("How Do U Want It", "2Pac Featuring KC And Jojo", "How Do U Want It"), T("Changes", "2Pac", "Greatest Hits"),
        T("Die With A Smile", "Lady Gaga & Bruno Mars", "MAYHEM"), T("Poker Face", "Lady Gaga", "The Fame"), T("Grenade", "Bruno Mars", "Doo-Wops"),
        T("Everyday", "A$AP Rocky feat. Rod Stewart x Miguel x Mark Ronson", "AT.LONG.LAST.A$AP"), T("Praise the Lord", "A$AP Rocky", "TESTING"),
        T("I Second That Emotion", "Diana Ross & the Supremes/The Temptations", "40 Golden Motown Greats"),
    ];

    [Fact]
    public void Bands_are_never_split_names_keep_their_spelling_and_odd_tags_are_left_for_a_look()
    {
        var plan = ArtistResolve.Plan(Owners());
        foreach (var band in new[] { "Diana Ross & the Supremes", "Eric B. & Rakim", "Bob & Earl", "Tom Petty and the Heartbreakers", "The Chemical Brothers feat. Th" })
            Assert.DoesNotContain(plan, c => c.OldArtists[0] == band);
        Assert.Equal(["P!nk", "Nate Ruess"], For(plan, "Just Give Me a Reason", "P!nk feat. Nate Ruess").NewArtists);
        Assert.Equal(["Grandmaster Flash & The Furious Five"], For(plan, "The Message", "Grandmaster Flash &amp; The Furious Five").NewArtists);
        Assert.Equal(["Drake", "WizKid & Kyla"], For(plan, "One Dance", "Drake feat. WizKid (4) & Kyla").NewArtists);   // neither is an artist here: kept as one, like KC And Jojo
        Assert.Equal(["2Pac", "KC And Jojo"], For(plan, "How Do U Want It", "2Pac Featuring KC And Jojo").NewArtists);   // a pair nobody here knows apart stays one name
        Assert.Equal(["Lady Gaga", "Bruno Mars"], For(plan, "Die With A Smile", "Lady Gaga & Bruno Mars").NewArtists);   // both are artists of their own here
        Assert.Equal(["A$AP Rocky", "Rod Stewart", "Miguel", "Mark Ronson"], For(plan, "Everyday", "A$AP Rocky feat. Rod Stewart x Miguel x Mark Ronson").NewArtists);
        Assert.Equal(["Diana Ross & the Supremes", "The Temptations"], For(plan, "I Second That Emotion", "Diana Ross & the Supremes/The Temptations").NewArtists);
    }
}
