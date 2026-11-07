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

    // owner, 7 Nov: "make the rules fix all them": the Goldfrapp singles are tagged with the song as the artist and "Goldfrapp" as the title
    static Track Swapped(string song, int no) =>
        new($"/m/{++n}", "Goldfrapp", song, "", song, 0, 0, 200_000, 0, null, 0, TrackSource.Jellyfin, FilePath: $"/media/music/Goldfrapp - The Singles/{no:00} - {song} - Goldfrapp .mp3", JellyfinId: "id" + n);

    [Fact]
    public void A_title_and_artist_swapped_are_swapped_back_with_the_song_in_capitals()
    {
        var plan = ArtistResolve.Plan([.. Library(), Swapped("ooh la la", 1), Swapped("a&e", 6), Swapped("strict machine", 3)]);
        var c = plan.Single(x => x.OldArtists[0] == "ooh la la");
        Assert.Equal(["Goldfrapp"], c.NewArtists); Assert.Equal("Goldfrapp", c.NewAlbumArtist); Assert.Equal("Ooh La La", c.NewTitle);
        Assert.Contains("swapped", c.Why);
        Assert.Equal("A&E", plan.Single(x => x.OldArtists[0] == "a&e").NewTitle);
        // three songs called "Intro" by three artists, filed under their own names, are not swapped
        Assert.DoesNotContain(ArtistResolve.Plan([T("Intro", "Drake", ""), T("Intro", "The xx", ""), T("Intro", "Alt-J", "")]), x => x.NewTitle is not null);
    }

    [Fact]
    public void Various_is_not_an_artist_and_a_soundtrack_label_is_not_one_of_the_artists()
    {
        Assert.Null(ArtistResolve.Split("various", _ => false, Library()));
        Assert.Null(ArtistResolve.Split("Various Artists", _ => false, Library()));
        Assert.Equal(["Aqualung", "Lucy Schwartz"], ArtistResolve.Split("Aqualung/Lucy Schwartz/Original Soundtrack", _ => false, Library()));
    }

    [Fact]
    public void After_feat_two_names_joined_by_and_are_two_artists_unless_they_are_an_act_here()
    {
        Assert.Equal(["Noname", "Raury", "Cam O'Bi"], ArtistResolve.Split("Noname feat. Raury & Cam O'Bi", _ => false, Library()));
        Assert.Equal(["Eminem", "Simon & Garfunkel"], ArtistResolve.Split("Eminem feat. Simon & Garfunkel", _ => true, Library()));
    }

    sealed class Jellyfin : IArtistWriter
    {
        public Dictionary<string, (IReadOnlyList<string> Artists, string AlbumArtist)> Items = [];
        public Dictionary<string, string> Titles = [];
        public List<string> Writes = [];
        public Task<(IReadOnlyList<string> Artists, string AlbumArtist, string Title)?> ReadAsync(string id, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<string>, string, string)?>(Items.TryGetValue(id, out var v) ? (v.Artists, v.AlbumArtist, Titles.GetValueOrDefault(id, "")) : null);
        public Task<bool> WriteAsync(string id, IReadOnlyList<string> artists, string albumArtist, string? title, CancellationToken ct)
        {
            Writes.Add(id); Items[id] = (artists, albumArtist); if (title is not null) Titles[id] = title; return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task A_swap_writes_the_title_too_and_undo_puts_it_back()
    {
        var song = Swapped("ooh la la", 1); List<Track> lib = [song, Swapped("number 1", 2), Swapped("utopia", 5)];
        var jf = new Jellyfin();
        foreach (var t in lib) { jf.Items[t.JellyfinId!] = ([t.Artist], t.AlbumArtist); jf.Titles[t.JellyfinId!] = t.Title; }
        var svc = new ArtistResolveService(Path.Combine(dir, "swap.json"), () => DateTime.UtcNow);
        await svc.PlanAsync(lib, (_, _, _) => Task.FromResult<bool?>(false), default);
        Assert.Equal(3, await svc.ApproveAsync(jf, default));
        Assert.Equal("Ooh La La", jf.Titles[song.JellyfinId!]); Assert.Equal(["Goldfrapp"], jf.Items[song.JellyfinId!].Artists);
        await svc.UndoAsync(jf, song.JellyfinId, default);
        Assert.Equal("Goldfrapp", jf.Titles[song.JellyfinId!]); Assert.Equal(["ooh la la"], jf.Items[song.JellyfinId!].Artists);
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

    [Fact]
    public async Task A_song_put_back_by_hand_is_never_fixed_again_by_itself()
    {
        var lib = Library(); var jf = new Jellyfin();
        foreach (var t in lib) jf.Items[t.JellyfinId!] = ([t.Artist], t.AlbumArtist);
        var svc = new ArtistResolveService(null, () => DateTime.UtcNow);
        Func<string, string, CancellationToken, Task<bool?>> no = (_, _, _) => Task.FromResult<bool?>(false);
        await svc.PlanAsync(lib, no, default); await svc.ApproveAsync(jf, default);
        var breathe = lib.First(t => t.Title == "Breathe").JellyfinId!;
        await svc.UndoAsync(jf, breathe, default);
        var writes = jf.Writes.Count;
        Assert.Equal(0, await svc.AfterSyncAsync(lib, jf, no, default));
        Assert.Equal(writes, jf.Writes.Count);
        Assert.DoesNotContain(await svc.PlanAsync(lib, no, default), c => c.ItemId == breathe);
    }

    [Fact]
    public async Task Leave_as_it_is_drops_that_kind_of_change_now_and_for_songs_that_come_later()
    {
        // owner, 6 Nov: "Eagles" is the band's own name; the list wanted The Eagles
        var lib = new List<Track> { T("Hotel California", "Eagles", "Hotel California"), T("New Kid in Town", "The Eagles", "Hotel California"), T("Take It Easy", "The Eagles", "Eagles") };
        var jf = new Jellyfin(); foreach (var t in lib) jf.Items[t.JellyfinId!] = ([t.Artist], t.AlbumArtist);
        var svc = new ArtistResolveService(Path.Combine(dir, "leave.json"), () => DateTime.UtcNow);
        Func<string, string, CancellationToken, Task<bool?>> no = (_, _, _) => Task.FromResult<bool?>(false);
        var c = Assert.Single(await svc.PlanAsync(lib, no, default));
        svc.Leave(c);
        Assert.Empty(svc.Pending);
        await svc.ApproveAsync(jf, default);
        var later = T("Life in the Fast Lane", "Eagles", "Hotel California"); jf.Items[later.JellyfinId!] = (["Eagles"], "Eagles");
        Assert.Equal(0, await new ArtistResolveService(Path.Combine(dir, "leave.json"), () => DateTime.UtcNow).AfterSyncAsync([.. lib, later], jf, no, default));
        Assert.Empty(jf.Writes);
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
        Assert.Equal("Breathe", r.Title);
        Assert.True(await w.WriteAsync("id9", ["The Prodigy"], "The Prodigy", null, default));
        Assert.Equal("/Items/id9", h.PostedTo);
        var o = System.Text.Json.Nodes.JsonNode.Parse(h.Posted!)!;
        Assert.Equal("Breathe", (string?)o["Name"]);                                 // no new title: the title stays
        Assert.True(await w.WriteAsync("id9", ["Goldfrapp"], "Goldfrapp", "Ooh La La", default));
        Assert.Equal("Ooh La La", (string?)System.Text.Json.Nodes.JsonNode.Parse(h.Posted!)!["Name"]);
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

    // ---- the owner's review of the list (6 Nov) ----

    static List<Track> Reviewed() =>
    [
        T("Baby Love", "Diana Ross & the Supremes", "40 Golden Motown Greats"), T("Upside Down", "Diana Ross", "Diana"), T("Stop! In the Name of Love", "The Supremes", "Where Did Our Love Go"),
        T("Golden", "HUNTR/X, EJAE, AUDREY NUNA, REI AMI & KPop Demon Hunters Cast", "KPop Demon Hunters"),
        T("Back in Black", "AC/DC", "Back in Black"),
        T("Another One Bites the Dust", "Free; Prakazrel \"Pras\" Michel; Queen; Wyclef Jean", "Greatest Hits III", "Queen"), T("Bohemian Rhapsody", "Queen", "A Night at the Opera"),
        T("Crazy In Love", "Beyoncé; JAŸ-Z; JAY-Z", "Crazy In Love"),
        T("Use Somebody", "Kings of Leon", "Only by the Night"), T("Arizona", "Kings Of Leon", "Because Of The Times"), T("Charmer", "Kings Of Leon", "Because Of The Times"),
        T("Mad World", "Tears For Fears", "The Hurting"), T("Shout", "Tears for Fears", "Songs from the Big Chair"),
        T("Americano", "Lady GaGa", "Born This Way"), T("Poker Face", "Lady Gaga", "The Fame"), T("Just Dance", "Lady Gaga", "The Fame"),
        T("Better Off Alone", "Alice Deejay", "Who Needs Guitars Anyway?"), T("Back In My Life", "Alice DeeJay", "Who Needs Guitars Anyway?"), T("Will I Ever", "Alice DeeJay", "x"),
        T("96 Fuckries", "JME", "Integrity>"), T("Man Don't Care", "Jme", "Integrity>"), T("Taking Over", "Jme", "x"),
        T("Hungriges Herz", "MIA.", "Stille Post"), T("Tanz der Moleküle", "Mia.", "Stille Post"), T("Mia 2", "Mia.", "x"),
        T("Drops of Jupiter", "train", "Drops of Jupiter"), T("Hey, Soul Sister", "Train", "Save Me, San Francisco"),
    ];

    [Fact]
    public void A_group_named_with_and_the_stays_whole()
    {
        var plan = ArtistResolve.Plan(Reviewed());
        Assert.DoesNotContain(plan, c => c.Title == "Baby Love");
    }

    [Fact]
    public void A_slash_inside_a_name_is_not_a_split()
    {
        var plan = ArtistResolve.Plan(Reviewed());
        var golden = plan.FirstOrDefault(c => c.Title == "Golden");
        if (golden is not null) Assert.Contains("HUNTR/X", golden.NewArtists);
        Assert.DoesNotContain(plan, c => c.Title == "Back in Black");
    }

    [Fact]
    public void The_album_artist_leads_a_list_that_names_them()
    {
        var c = For(ArtistResolve.Plan(Reviewed()), "Another One Bites the Dust", "Free; Prakazrel \"Pras\" Michel; Queen; Wyclef Jean");
        Assert.Equal("Queen", c.NewArtists[0]);
        Assert.Equal(4, c.NewArtists.Count);
        Assert.Contains("lists 4 artists", c.Why);
        Assert.DoesNotContain("featured", c.Why);
    }

    [Fact]
    public void One_artist_written_two_ways_is_kept_once()
    {
        var c = For(ArtistResolve.Plan(Reviewed()), "Crazy In Love", "Beyoncé; JAŸ-Z; JAY-Z");
        Assert.Equal(2, c.NewArtists.Count);
    }

    // how the catalog writes these names (Deezer), as the server asks it once per name
    static readonly Dictionary<string, string> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Lady Gaga"] = "Lady Gaga", ["Alice Deejay"] = "Alice Deejay", ["Train"] = "Train", ["Paul McCartney"] = "Paul McCartney", ["OutKast"] = "OutKast",
        ["DJ BoBo"] = "DJ BoBo", ["a-ha"] = "a-ha", ["JME"] = "JME",
    };
    static string? Spelling(string n) => Catalog.GetValueOrDefault(n);

    [Fact]
    public void Capitals_change_only_for_the_better()
    {
        var plan = ArtistResolve.Plan(Reviewed(), spelling: Spelling);
        Assert.DoesNotContain(plan, c => c.Title == "Use Somebody");                                  // "of" stays small
        Assert.Equal(["Kings of Leon"], For(plan, "Arizona", "Kings Of Leon").NewArtists);
        Assert.Equal(["Tears for Fears"], For(plan, "Mad World", "Tears For Fears").NewArtists);
        Assert.Equal(["Lady Gaga"], For(plan, "Americano", "Lady GaGa").NewArtists);
        Assert.DoesNotContain(plan, c => c.Title == "Better Off Alone");                              // never "DeeJay"
        Assert.Equal(["Alice Deejay"], For(plan, "Back In My Life", "Alice DeeJay").NewArtists);
        Assert.DoesNotContain(plan, c => c.Title == "96 Fuckries");                                   // JME is how he writes it
        Assert.DoesNotContain(plan, c => c.Title == "Hungriges Herz");
        Assert.Equal(["Train"], For(plan, "Drops of Jupiter", "train").NewArtists);
    }

    // ---- the owner's second look (6 Nov) ----

    static List<Track> SecondLook() =>
    [
        T("Angry", "Paul McCartney", "Press To Play"), T("Hope Of Deliverance", "Paul McCartney", "Hope Of Deliverance"), T("Mull of Kintyre", "Paul Mccartney", "x"),
        T("All Day And Night", "Jax Jones, Martin Solveig & Madison Beer", "Now 103"), T("Breathe", "Jax Jones", "x"), T("Intoxicated", "Martin Solveig", "x"), T("Selfish", "Madison Beer", "x"),
        T("I Ain't No Joke", "Eric B & Rakim", "Paid In Full", "Eric B & Rakim"), T("Paid in Full", "Eric B & Rakim", "Paid In Full", "Eric B & Rakim"), T("Eric B. Is President", "Eric B & Rakim", "Paid In Full", "Eric B & Rakim"), T("x1", "Eric B", "x"), T("x2", "Rakim", "y"),
        T("Danger", "Panda Bear and Sonic Boom", "Reset", "Panda Bear and Sonic Boom"), T("Go On", "Panda Bear and Sonic Boom", "Reset", "Panda Bear and Sonic Boom"), T("Everyday", "Panda Bear and Sonic Boom", "Reset", "Panda Bear and Sonic Boom"), T("Comfy in Nautica", "Panda Bear", "Person Pitch"), T("Big Echo", "Sonic Boom", "All Things Being Equal"),
    ];

    [Fact]
    public void Mc_and_Mac_names_keep_their_capitals()
    {
        var plan = ArtistResolve.Plan(SecondLook(), spelling: Spelling);
        Assert.DoesNotContain(plan, c => c.Title is "Angry" or "Hope Of Deliverance");
        Assert.Equal(["Paul McCartney"], For(plan, "Mull of Kintyre", "Paul Mccartney").NewArtists);
    }

    [Fact]
    public void The_last_and_of_a_comma_list_is_part_of_the_list()
    {
        var c = For(ArtistResolve.Plan(SecondLook()), "All Day And Night", "Jax Jones, Martin Solveig & Madison Beer");
        Assert.Equal(["Jax Jones", "Martin Solveig", "Madison Beer"], c.NewArtists);
    }

    [Fact]
    public void An_act_its_album_is_credited_to_stays_whole()
    {
        var plan = ArtistResolve.Plan(SecondLook());
        Assert.DoesNotContain(plan, c => c.OldArtists[0] is "Eric B & Rakim" or "Panda Bear and Sonic Boom");
    }

    // ---- the owner's third look: the whole library (6 Nov) ----

    [Fact]
    public void Capitals_only_the_catalog_can_settle_are_left_alone_without_it()
    {
        var lib = new List<Track>
        {
            T("Ms. Jackson", "OutKast", "Stankonia"), T("Hey Ya!", "Outkast", "Speakerboxxx"),
            T("Let the Dream Come True", "DJ BoBo", "There Is a Party"), T("Chihuahua", "DJ Bobo", "x"),
            T("Take On Me", "a-ha", "Hunting High and Low"),
            T("Shake It Out", "Florence And The Machine", "Ceremonials"), T("Dog Days", "Florence and the machine", "Lungs"),
        };
        var without = ArtistResolve.Plan(lib);
        Assert.DoesNotContain(without, c => c.OldArtists[0] is "OutKast" or "DJ BoBo" or "a-ha");
        Assert.DoesNotContain(without, c => c.Title == "Shake It Out");   // never "machine", and without the right spelling anywhere it is left alone
        var with = ArtistResolve.Plan(lib, spelling: Spelling);
        Assert.Equal(["OutKast"], For(with, "Hey Ya!", "Outkast").NewArtists);
        Assert.Equal(["DJ BoBo"], For(with, "Chihuahua", "DJ Bobo").NewArtists);
        Assert.DoesNotContain(with, c => c.OldArtists[0] is "OutKast" or "DJ BoBo" or "a-ha");
    }

    [Fact]
    public void A_duo_at_the_end_of_a_list_stays_whole_and_one_person_is_kept_once()
    {
        var lib = new List<Track>
        {
            T("Shine", "Gary Pine, Dollarman, Bob Sinclar, Sly & Robbie", "Made in Jamaica", "Bob Sinclar"), T("x", "Gary Pine", "x"), T("Taxi", "Sly & Robbie", "Taxi"),
            T("If I Ruled the World", "Nas; Ms. Lauryn Hill; Lauryn Hill", "It Was Written", "Nas"),
            T("Telephone", "Lady Gaga Feat. Beyonc�", "The Fame Monster"),
        };
        var plan = ArtistResolve.Plan(lib);
        Assert.Equal(["Bob Sinclar", "Gary Pine", "Dollarman", "Sly & Robbie"], For(plan, "Shine", "Gary Pine, Dollarman, Bob Sinclar, Sly & Robbie").NewArtists);
        Assert.Equal(2, For(plan, "If I Ruled the World", "Nas; Ms. Lauryn Hill; Lauryn Hill").NewArtists.Count);
        Assert.DoesNotContain(plan, c => c.Title == "Telephone");
    }

    [Fact]
    public void The_names_to_ask_the_catalog_about()
    {
        var q = ArtistResolve.SpellingQuestions([T("a", "Lady GaGa", "x"), T("b", "train", "x"), T("c", "OutKast", "x"), T("d", "Outkast", "x"), T("e", "Kings Of Leon", "x"), T("f", "Madonna; Charli xcx", "x")]);
        Assert.Contains("Lady GaGa", q); Assert.Contains("train", q); Assert.Contains("OutKast", q); Assert.Contains("Outkast", q);
        Assert.Contains("Kings Of Leon", q); Assert.DoesNotContain("Madonna", q);
    }

    // how MusicBrainz writes these names (the reference for artist names; Deezer's search said "Bt", "Omi", "24kgoldn")
    static readonly Dictionary<string, string> Mb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BT"] = "BT", ["OMI"] = "OMI", ["24kGoldn"] = "24kGoldn", ["Mac DeMarco"] = "Mac DeMarco", ["Lady Gaga"] = "Lady Gaga", ["OutKast"] = "OutKast",
        ["twenty one pilots"] = "twenty one pilots", ["MIA."] = "MIA.", ["a-ha"] = "a-ha", ["JME"] = "JME",
    };

    [Fact]
    public void MusicBrainz_settles_capitals()
    {
        // owner, 6 Nov: "the ones to leave as they are should be decided for me as part of the check"
        var lib = new List<Track>
        {
            T("Flaming June", "BT", "Flaming June"), T("Cheerleader", "OMI", "Me 4 U"), T("Mood", "24kGoldn", "El Dorado"), T("Chamber of Reflection", "Mac DeMarco", "Salad Days"),
            T("Americano", "Lady GaGa", "Born This Way"), T("Hey Ya!", "Outkast", "Speakerboxxx"), T("Ms. Jackson", "OutKast", "Stankonia"),
            T("Heathens", "Twenty One Pilots", "Suicide Squad"), T("Stressed Out", "twenty one pilots", "Blurryface"), T("Hungriges Herz", "Mia.", "Stille Post"),
            T("Analogue", "A-Ha", "Analogue"), T("96 Fuckries", "JME", "Integrity>"),
            T("Destination Calabria", "no artist", "no title"),
        };
        var plan = ArtistResolve.Plan(lib, spelling: n => Mb.GetValueOrDefault(n));
        Assert.Equal(["Lady Gaga"], For(plan, "Americano", "Lady GaGa").NewArtists);
        Assert.Equal(["OutKast"], For(plan, "Hey Ya!", "Outkast").NewArtists);
        Assert.Equal(["twenty one pilots"], For(plan, "Heathens", "Twenty One Pilots").NewArtists);
        Assert.Equal(["MIA."], For(plan, "Hungriges Herz", "Mia.").NewArtists);
        Assert.Equal(["a-ha"], For(plan, "Analogue", "A-Ha").NewArtists);
        Assert.DoesNotContain(plan, c => c.OldArtists[0] is "BT" or "OMI" or "24kGoldn" or "Mac DeMarco" or "OutKast" or "twenty one pilots" or "JME" or "no artist");
    }

    [Fact]
    public void An_album_MusicBrainz_credits_to_X_keeps_X_and_not_The_X()
    {
        // owner, 6 Nov: Hotel California is Eagles' (the list wanted The Eagles); The Fat of the Land is The Prodigy's
        var lib = new List<Track>
        {
            T("Hotel California", "Eagles", "Hotel California"), T("New Kid in Town", "The Eagles", "Hotel California"),
            T("Breathe", "Prodigy", "The Fat of the Land"), T("Firestarter", "The Prodigy", "The Fat of the Land"),
        };
        string? credited(string album, IReadOnlyList<string> names) => album == "Hotel California" ? "Eagles" : album == "The Fat of the Land" ? "The Prodigy" : null;
        var plan = ArtistResolve.Plan(lib, albumBy: credited);
        Assert.DoesNotContain(plan, c => c.Title == "Hotel California");
        Assert.Equal(["The Prodigy"], For(plan, "Breathe", "Prodigy").NewArtists);
        Assert.Contains(ArtistResolve.AlbumQuestions(lib), q => q.Album == "Hotel California");
    }

    [Fact]
    public void Swapped_title_and_artist_tags_are_left_alone()
    {
        // owner, 6 Nov: Goldfrapp songs tagged with the song as the artist ("ooh la la", "rocket") had their capitals "fixed" by MusicBrainz artists of those names
        var lib = new List<Track> { T("Ooh La La", "Goldfrapp", "Supernature"), T("Number 1", "Goldfrapp", "Supernature"), T("Rocket", "Goldfrapp", "Head First"),
            T("Goldfrapp", "rocket", ""), T("Goldfrapp", "ooh la la", "") };
        var plan = ArtistResolve.Plan(lib, spelling: n => n.ToLowerInvariant() switch { "rocket" => "ROCKET", "ooh la la" => "Ooh La La", _ => null });
        Assert.DoesNotContain(plan, c => c.Title == "Goldfrapp");
    }
}
