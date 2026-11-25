using FLACie.Core;

namespace FLACie.Tests;

/// <summary>
/// A made-up library and listening history shaped like the owner's (owner, 25 Nov: "I play Heads Will Roll and the next 10-20 songs are by Yeah Yeah
/// Yeahs"), for measuring recommendations offline. Genre tags are as messy as real ones (Indie Rock, Dance-Punk, Alternative, Electroclash, New Rave,
/// Post-Punk for one scene), about a third of the songs have no vibe yet, the person plays Yeah Yeah Yeahs most of all, hearts a few pop bangers that
/// feel the same as indie ones, and has remixes, live takes and second vocals of some songs.
/// </summary>
public sealed class RecFixture
{
    public static readonly DateTime Now = new(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc);
    public static long Ago(double days) => new DateTimeOffset(Now.AddDays(-days)).ToUnixTimeMilliseconds();

    public readonly List<Track> Songs = [];
    public readonly Dictionary<Track, Vibe> Vibes = [];
    public readonly List<ListenEvent> Events = [];
    public readonly Dictionary<string, List<CatalogSong>> Related = new(StringComparer.Ordinal);
    public Library Lib { get; }
    public TasteProfile Taste { get; }

    /// <summary>The scenes, by lead artist, for judging whether a pick stays in the right territory.</summary>
    public static readonly HashSet<string> Indie = new[]
    {
        "Yeah Yeah Yeahs", "LCD Soundsystem", "The Rapture", "Franz Ferdinand", "Bloc Party", "MGMT", "Hot Chip", "Metronomy", "Le Tigre", "Klaxons", "The Killers",
        "Arctic Monkeys", "Phoenix", "Two Door Cinema Club", "Justice", "Interpol", "The Strokes", "Santigold", "CSS", "Cut Copy", "Friendly Fires",
    }.Select(Matching.PrimaryArtist).ToHashSet();
    public static readonly HashSet<string> Nineties = new[]
    {
        "Corona", "Livin' Joy", "Haddaway", "2 Unlimited", "Snap!", "Culture Beat", "Robin S", "Alice Deejay", "La Bouche", "Real McCoy", "Ace of Base", "Black Box",
        "The Prodigy", "Faithless", "Underworld", "Mr. President", "Vengaboys", "Eiffel 65", "Gala", "N-Trance",
    }.Select(Matching.PrimaryArtist).ToHashSet();
    public static readonly HashSet<string> PopScene = new[]
    {
        "Katy Perry", "Lady Gaga", "Miley Cyrus", "Dua Lipa", "Taylor Swift", "David Guetta", "Calvin Harris", "Avicii", "Rihanna", "Little Mix", "Ariana Grande",
    }.Select(Matching.PrimaryArtist).ToHashSet();

    enum Feel { Indie, DancePunk, Euro, Pop, Ballad, Rap, Rock }

    static Vibe VibeOf(Feel f, int i) => f switch
    {
        Feel.Indie => new(0.72 + i % 3 * 0.03, 0.8, 125 + i % 5, 0.6),
        Feel.DancePunk => new(0.8 + i % 2 * 0.03, 0.82, 122 + i % 4, 0.75),
        Feel.Euro => new(0.86 + i % 3 * 0.02, 0.88, 128 + i % 6, 0.85),
        Feel.Pop => new(0.78 + i % 3 * 0.03, 0.85, 120 + i % 7, 0.72),
        Feel.Ballad => new(0.25 + i % 3 * 0.03, 0.45, 72 + i % 5, 0.15),
        Feel.Rap => new(0.6, 0.8, 92 + i % 5, 0.7),
        _ => new(0.7, 0.85, 120, 0.5),
    };

    int n;
    Track Add(string title, string artist, string genre, int year, Feel feel, string album = "")
    {
        var t = new Track($"jf:{artist}:{title}", title, artist, album.Length > 0 ? album : artist + " album " + year, artist, 1 + n % 12, 1, 210_000, year, null, 0,
            TrackSource.Jellyfin, JellyfinId: $"id{n}", Genre: genre);
        // about a third of the library has not been analysed yet
        if (n % 3 != 2) Vibes[t] = VibeOf(feel, n);
        n++;
        Songs.Add(t);
        return t;
    }

    public Track this[string title] => Songs.First(t => t.Title == title);

    public RecFixture()
    {
        // ---- indie, dance-punk, electroclash: one scene under many tags ----
        string[] yyy = ["Heads Will Roll", "Maps", "Zero", "Date with the Night", "Gold Lion", "Y Control", "Pin", "Sacrilege", "Despair", "Cheated Hearts", "Soft Shock",
            "Dull Life", "Skeletons", "Turn Into", "Phenomena", "Black Tongue", "Mosquito", "Under the Earth", "Spitting Off the Edge of the World", "Wolf"];
        for (var i = 0; i < yyy.Length; i++) Add(yyy[i], "Yeah Yeah Yeahs", "Indie Rock", 2003 + i % 11, i == 12 ? Feel.Ballad : Feel.Indie);
        Add("Heads Will Roll (A-Trak Remix)", "Yeah Yeah Yeahs", "Electro", 2009, Feel.DancePunk);
        Add("Maps (Live)", "Yeah Yeah Yeahs", "Indie Rock", 2004, Feel.Indie);
        foreach (var (t, y) in new[] { ("Daft Punk Is Playing at My House", 2005), ("All My Friends", 2007), ("Dance Yrself Clean", 2010), ("North American Scum", 2007), ("Movement", 2004) })
            Add(t, "LCD Soundsystem", "Dance-Punk", y, Feel.DancePunk);
        Add("House of Jealous Lovers", "The Rapture", "Dance-Punk", 2002, Feel.DancePunk); Add("Get Myself Into It", "The Rapture", "Dance-Punk", 2006, Feel.DancePunk);
        foreach (var (t, y) in new[] { ("Take Me Out", 2004), ("Do You Want To", 2005), ("No You Girls", 2009), ("The Dark of the Matinée", 2004) }) Add(t, "Franz Ferdinand", "Indie Rock", y, Feel.Indie);
        foreach (var (t, y) in new[] { ("Banquet", 2005), ("Helicopter", 2005), ("Flux", 2007) }) Add(t, "Bloc Party", "Indie", y, Feel.Indie);
        foreach (var (t, y) in new[] { ("Kids", 2007), ("Electric Feel", 2007), ("Time to Pretend", 2007) }) Add(t, "MGMT", "Alternative", y, Feel.Indie);
        Add("Over and Over", "Hot Chip", "Electronic", 2006, Feel.DancePunk); Add("Ready for the Floor", "Hot Chip", "Electronic", 2008, Feel.DancePunk);
        Add("The Look", "Metronomy", "Indie Pop", 2011, Feel.Indie); Add("Heartbreaker", "Metronomy", "Indie Pop", 2008, Feel.Indie);
        Add("Deceptacon", "Le Tigre", "Electroclash", 1999, Feel.DancePunk);
        Add("Golden Skans", "Klaxons", "New Rave", 2007, Feel.DancePunk); Add("Gravity's Rainbow", "Klaxons", "New Rave", 2006, Feel.DancePunk);
        Add("Mr. Brightside", "The Killers", "Alternative Rock", 2004, Feel.Indie); Add("Somebody Told Me", "The Killers", "Alternative Rock", 2004, Feel.Indie);
        Add("When You Were Young", "The Killers", "Alternative Rock", 2006, Feel.Indie);
        Add("Mr. Brightside (Jacques Lu Cont Remix)", "The Killers", "Electronic", 2004, Feel.DancePunk);
        Add("I Bet You Look Good on the Dancefloor", "Arctic Monkeys", "Indie Rock", 2005, Feel.Indie); Add("Brianstorm", "Arctic Monkeys", "Indie Rock", 2007, Feel.Indie);
        Add("Do I Wanna Know?", "Arctic Monkeys", "Indie Rock", 2013, Feel.Indie);
        Add("1901", "Phoenix", "Indie Pop", 2009, Feel.Indie); Add("Lisztomania", "Phoenix", "Indie Pop", 2009, Feel.Indie);
        Add("What You Know", "Two Door Cinema Club", "Indie", 2010, Feel.Indie); Add("Undercover Martyn", "Two Door Cinema Club", "Indie", 2010, Feel.Indie);
        Add("D.A.N.C.E.", "Justice", "Electro", 2007, Feel.DancePunk);
        Add("Evil", "Interpol", "Post-Punk", 2004, Feel.Indie); Add("Slow Hands", "Interpol", "Post-Punk", 2004, Feel.Indie);
        Add("Last Nite", "The Strokes", "Garage Rock", 2001, Feel.Indie); Add("Reptilia", "The Strokes", "Garage Rock", 2003, Feel.Indie);
        Add("L.E.S. Artistes", "Santigold", "Alternative", 2008, Feel.Indie);
        Add("Let's Make Love and Listen to Death from Above", "CSS", "Electroclash", 2005, Feel.DancePunk);
        Add("Hearts on Fire", "Cut Copy", "Synthpop", 2008, Feel.DancePunk);
        Add("Paris", "Friendly Fires", "Indie", 2008, Feel.DancePunk);

        // ---- 90s dance ----
        Add("The Rhythm of the Night", "Corona", "Eurodance", 1993, Feel.Euro); Add("Baby Baby", "Corona", "Eurodance", 1994, Feel.Euro); Add("Try Me Out", "Corona", "Eurodance", 1995, Feel.Euro);
        Add("Dreamer", "Livin' Joy", "House", 1995, Feel.Euro); Add("Dreamer (Janice Robinson Vocal)", "Livin' Joy", "House", 1994, Feel.Euro);
        Add("Don't Stop Movin'", "Livin' Joy", "House", 1996, Feel.Euro);
        Add("What Is Love", "Haddaway", "Eurodance", 1993, Feel.Euro); Add("Life", "Haddaway", "Eurodance", 1993, Feel.Euro);
        Add("No Limit", "2 Unlimited", "Eurodance", 1993, Feel.Euro); Add("Get Ready for This", "2 Unlimited", "Eurodance", 1991, Feel.Euro); Add("Twilight Zone", "2 Unlimited", "Eurodance", 1992, Feel.Euro);
        Add("Rhythm Is a Dancer", "Snap!", "Dance", 1992, Feel.Euro); Add("The Power", "Snap!", "Dance", 1990, Feel.Euro);
        Add("Mr. Vain", "Culture Beat", "Eurodance", 1993, Feel.Euro);
        Add("Show Me Love", "Robin S", "House", 1993, Feel.Euro);
        Add("Better Off Alone", "Alice Deejay", "Trance", 1999, Feel.Euro);
        Add("Be My Lover", "La Bouche", "Eurodance", 1995, Feel.Euro); Add("Sweet Dreams", "La Bouche", "Eurodance", 1994, Feel.Euro);
        Add("Another Night", "Real McCoy", "Eurodance", 1994, Feel.Euro);
        Add("All That She Wants", "Ace of Base", "Pop", 1992, Feel.Euro); Add("The Sign", "Ace of Base", "Pop", 1993, Feel.Euro);
        Add("Ride on Time", "Black Box", "House", 1989, Feel.Euro);
        Add("Out of Space", "The Prodigy", "Big Beat", 1992, Feel.Euro); Add("Firestarter", "The Prodigy", "Big Beat", 1996, Feel.Euro);
        Add("Breathe", "The Prodigy", "Big Beat", 1996, Feel.Euro); Add("Voodoo People", "The Prodigy", "Big Beat", 1994, Feel.Euro);
        Add("Out of Space (Live)", "The Prodigy", "Big Beat", 1997, Feel.Euro);
        Add("Insomnia", "Faithless", "Dance", 1995, Feel.Euro); Add("God Is a DJ", "Faithless", "Dance", 1998, Feel.Euro);
        Add("Insomnia (Armand Van Helden Remix)", "Faithless", "House", 1996, Feel.Euro);
        Add("Born Slippy .NUXX", "Underworld", "Electronic", 1996, Feel.Euro);
        Add("Coco Jamboo", "Mr. President", "Eurodance", 1996, Feel.Euro);
        Add("We Like to Party", "Vengaboys", "Eurodance", 1998, Feel.Euro); Add("Boom, Boom, Boom, Boom!!", "Vengaboys", "Eurodance", 1998, Feel.Euro);
        Add("Blue (Da Ba Dee)", "Eiffel 65", "Eurodance", 1998, Feel.Euro);
        Add("Freed from Desire", "Gala", "Eurodance", 1996, Feel.Euro);
        Add("Set You Free", "N-Trance", "Dance", 1994, Feel.Euro);

        // ---- 2010s dance and pop ----
        Add("Titanium", "David Guetta", "Dance", 2011, Feel.Pop); Add("Sexy Bitch", "David Guetta", "Dance", 2009, Feel.Pop); Add("Without You", "David Guetta", "Dance", 2011, Feel.Pop);
        Add("Titanium (Alesso Remix)", "David Guetta", "Dance", 2011, Feel.Pop);
        Add("Feel So Close", "Calvin Harris", "Dance", 2011, Feel.Pop); Add("Summer", "Calvin Harris", "Dance", 2014, Feel.Pop);
        Add("Levels", "Avicii", "Dance", 2011, Feel.Pop); Add("Wake Me Up", "Avicii", "Dance", 2013, Feel.Pop); Add("Levels (Skrillex Remix)", "Avicii", "Dance", 2011, Feel.Pop);
        Add("Firework", "Katy Perry", "Pop", 2010, Feel.Pop); Add("Roar", "Katy Perry", "Pop", 2013, Feel.Pop); Add("Teenage Dream", "Katy Perry", "Pop", 2010, Feel.Pop);
        Add("Bad Romance", "Lady Gaga", "Pop", 2009, Feel.Pop); Add("Just Dance", "Lady Gaga", "Pop", 2008, Feel.Pop); Add("Poker Face", "Lady Gaga", "Pop", 2008, Feel.Pop);
        Add("Party in the U.S.A.", "Miley Cyrus", "Pop", 2009, Feel.Pop);
        Add("Levitating", "Dua Lipa", "Pop", 2020, Feel.Pop); Add("Don't Start Now", "Dua Lipa", "Pop", 2019, Feel.Pop);
        Add("Shake It Off", "Taylor Swift", "Pop", 2014, Feel.Pop); Add("Blank Space", "Taylor Swift", "Pop", 2014, Feel.Pop);
        Add("Only Girl (In the World)", "Rihanna", "Pop", 2010, Feel.Pop); Add("We Found Love", "Rihanna", "Pop", 2011, Feel.Pop);
        Add("Shout Out to My Ex", "Little Mix", "Pop", 2016, Feel.Pop);
        Add("7 rings", "Ariana Grande", "Pop", 2019, Feel.Pop);

        // ---- the rest of a real library ----
        Add("Lose Yourself", "Eminem", "Hip-Hop", 2002, Feel.Rap); Add("Without Me", "Eminem", "Hip-Hop", 2002, Feel.Rap);
        Add("HUMBLE.", "Kendrick Lamar", "Hip-Hop", 2017, Feel.Rap); Add("Hotline Bling", "Drake", "Hip-Hop", 2015, Feel.Rap);
        Add("Someone Like You", "Adele", "Soul", 2011, Feel.Ballad); Add("Hello", "Adele", "Soul", 2015, Feel.Ballad);
        Add("Perfect", "Ed Sheeran", "Pop", 2017, Feel.Ballad); Add("Someone You Loved", "Lewis Capaldi", "Pop", 2018, Feel.Ballad);
        Add("Fix You", "Coldplay", "Alternative", 2005, Feel.Ballad); Add("The Scientist", "Coldplay", "Alternative", 2002, Feel.Ballad);
        Add("Bohemian Rhapsody", "Queen", "Rock", 1975, Feel.Rock); Add("Don't Stop Me Now", "Queen", "Rock", 1978, Feel.Rock);
        Add("Whole Lotta Love", "Led Zeppelin", "Rock", 1969, Feel.Rock);
        Add("Smells Like Teen Spirit", "Nirvana", "Grunge", 1991, Feel.Rock); Add("Everlong", "Foo Fighters", "Rock", 1997, Feel.Rock);

        Lib = new Library(Songs);

        // ---- what the person did ----
        var rnd = new Random(25);
        var day = 60.0;
        void Session(params string[] titles)
        {
            // a session of plays three and a half minutes apart, heard to the end
            var at = Ago(day);
            foreach (var title in titles)
            {
                var t = this[title];
                Events.Add(new($"e{Events.Count}", ListenKind.Play, at, t.Path, t.Title, t.Artist, 210, 210));
                at += 210_000;
            }
            day -= 0.5 + rnd.NextDouble();
        }
        void Did(ListenKind k, string title, double days = 3) { var t = this[title]; Events.Add(new($"e{Events.Count}", k, Ago(days), t.Path, t.Title, t.Artist)); }
        void Skipped(string title, double days) { var t = this[title]; Events.Add(new($"e{Events.Count}", ListenKind.Play, Ago(days), t.Path, t.Title, t.Artist, 8, 210)); }

        // Yeah Yeah Yeahs, most of all, and the indie nights they come in
        for (var r = 0; r < 4; r++)
        {
            Session("Heads Will Roll", "Daft Punk Is Playing at My House", "Take Me Out", "Maps", "Kids", "Mr. Brightside", "Zero", "Banquet");
            Session("Zero", "Gold Lion", "Date with the Night", "Y Control", "Pin", "Cheated Hearts", "Soft Shock", "Despair", "Sacrilege");
            Session("Heads Will Roll", "House of Jealous Lovers", "I Bet You Look Good on the Dancefloor", "Electric Feel", "Lisztomania", "Golden Skans", "Last Nite");
            Session("Maps", "Phenomena", "Turn Into", "Black Tongue", "Mosquito", "Dull Life", "Wolf", "Under the Earth");
            // 90s dance
            Session("The Rhythm of the Night", "What Is Love", "No Limit", "Mr. Vain", "Show Me Love", "Rhythm Is a Dancer", "Be My Lover", "Insomnia", "Out of Space");
            Session("Dreamer", "Baby Baby", "The Sign", "Ride on Time", "Better Off Alone", "Freed from Desire", "Firestarter", "Get Ready for This");
            // pop
            Session("Teenage Dream", "Bad Romance", "Firework", "Party in the U.S.A.", "Levitating", "Shake It Off", "Titanium", "We Found Love");
        }
        Session("All My Friends", "Over and Over", "Time to Pretend", "1901", "What You Know", "Somebody Told Me", "Flux", "Ready for the Floor");
        Session("Dance Yrself Clean", "Helicopter", "Brianstorm", "The Look", "When You Were Young", "Do You Want To", "Reptilia");
        Did(ListenKind.Heart, "Heads Will Roll"); Did(ListenKind.Heart, "Maps"); Did(ListenKind.Heart, "Zero");
        Did(ListenKind.Heart, "Teenage Dream"); Did(ListenKind.Heart, "Bad Romance"); Did(ListenKind.Heart, "Party in the U.S.A.");
        Did(ListenKind.Heart, "The Rhythm of the Night"); Did(ListenKind.Heart, "Insomnia");
        Did(ListenKind.ThumbDown, "We Like to Party");
        foreach (var d in new[] { 2.0, 5, 9, 14 }) { Skipped("Fix You", d); Skipped("Perfect", d + 1); }

        // what fans of Heads Will Roll play (a public catalog's list): mostly the band's own songs, then their scene, two not in the library yet
        Related[Matching.MatchKey("Heads Will Roll", "Yeah Yeah Yeahs")] =
        [
            .. new[] { "Maps", "Zero", "Gold Lion", "Date with the Night", "Y Control", "Sacrilege", "Despair", "Cheated Hearts" }.Select(t => Cat(t, "Yeah Yeah Yeahs")),
            Cat("Heads Will Roll (A-Trak Remix)", "Yeah Yeah Yeahs"), Cat("Daft Punk Is Playing at My House", "LCD Soundsystem"), Cat("Kids", "MGMT"),
            Cat("Golden Skans", "Klaxons"), Cat("Deceptacon", "Le Tigre"), Cat("Lisztomania", "Phoenix"), Cat("Ready for the Floor", "Hot Chip"),
            Cat("Hollywood", "Marina and the Diamonds"), Cat("Walking on a Dream", "Empire of the Sun"),
        ];
        Related[Matching.MatchKey("The Rhythm of the Night", "Corona")] =
        [
            Cat("Baby Baby", "Corona"), Cat("Try Me Out", "Corona"), Cat("What Is Love", "Haddaway"), Cat("Mr. Vain", "Culture Beat"), Cat("Another Night", "Real McCoy"),
            Cat("Coco Jamboo", "Mr. President"), Cat("Scatman (Ski-Ba-Bop-Ba-Dop-Bop)", "Scatman John"),
        ];

        Taste = new TasteProfile(Lib, [], [], [], Now, events: Events);
    }

    static CatalogSong Cat(string title, string artist) => new(artist, title, "", null, 210_000, 1, 1);

    public IReadOnlyList<CatalogSong> RelatedOf(Track t) => Related.GetValueOrDefault(Matching.MatchKey(t)) ?? [];
}
