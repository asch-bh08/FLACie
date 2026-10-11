using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The same table is in the Android app's TrackTitlesTest.kt: both platforms must clean a file name the same way.</summary>
public class TrackTitlesTests
{
    // name | path ("@" = /music/Va/Mix/{name}.mp3) | item artist | item album | siblings numbered (0/1) | known artists (; separated) | expected title | expected artist
    const string Table = """
06 - Jason Mraz - Everything Is Sound|@|||0||Everything Is Sound|Jason Mraz
06 - a&e - Goldfrapp|@|||0|goldfrapp|a&e|Goldfrapp
01. Artist - Title|@|||0||Title|Artist
06 - Title|@|||0||Title|
06. Title|@|||0||Title|
06 Title|@|||0||Title|
06 Title|/music/Q/R/06 Title.mp3|Foo|Bar|0||06 Title|Foo
12 Title|@|||1||Title|
12 Title|@|||0||12 Title|
99 Problems|/music/Jay-Z/The Black Album/99 Problems.mp3|Jay-Z|The Black Album|0||99 Problems|Jay-Z
1979|/music/Smashing Pumpkins/Mellon Collie/1979.mp3|Smashing Pumpkins|Mellon Collie|1||1979|Smashing Pumpkins
7 Rings|/music/Ariana Grande/Thank U Next/7 Rings.mp3|Ariana Grande|Thank U Next|0||7 Rings|Ariana Grande
22|/music/Taylor Swift/Red/22.mp3|Taylor Swift|Red|1||22|Taylor Swift
1-800-273-8255|/music/Logic/Everybody/1-800-273-8255.mp3|Logic|Everybody|0||1-800-273-8255|Logic
1-800-273-8255|/music/Logic/Everybody/1-800-273-8255.mp3|Logic|Everybody|1||1-800-273-8255|Logic
06 - Title|/music/Foo/Bar/Real Name.mp3|Foo|Bar|0||06 - Title|Foo
Hotel California|/music/Eagles/Hotel California/01 - Hotel California.mp3|Eagles|Hotel California|1||Hotel California|Eagles
Jason Mraz - We Sing - 06 - Everything Is Sound|@||We Sing|0||Everything Is Sound|Jason Mraz
Jason Mraz - We Sing - 06 - Everything Is Sound|@|||0||Everything Is Sound|Jason Mraz
1-06 Title|@|||0||Title|
(1-06) Title|@|||0||Title|
1.15. Title|@|||0||Title|
06_Jason_Mraz_-_Everything_Is_Sound|@|||0||Everything Is Sound|Jason Mraz
06 - Title [Hi-Res]|@|||0||Title|
06 - Title (www.site.com)|@|||0||Title|
Artist - Title - www.spam.ru|@|||0||Title|Artist
06 - Artist feat. Guest - Title|@|||0||Title|Artist feat. Guest
05 - Björk - Jóga|@|||0||Jóga|Björk
03 - 坂本龍一 - Merry Christmas Mr. Lawrence|@|||0||Merry Christmas Mr. Lawrence|坂本龍一
Artist - Title|@|Unknown||0||Title|Artist
Artist - Title|@|Various Artists||0||Title|Artist
Jason Mraz - I'm Yours|@|Jason Mraz||0||I'm Yours|Jason Mraz
I'm Yours - Jason Mraz|@|Jason Mraz||0||I'm Yours|Jason Mraz
Some - Thing|@|Other||0||Some - Thing|Other
Cinderella Man - Eminem|@|||0|eminem|Cinderella Man|Eminem
06 - JASON MRAZ - I'M YOURS|@|||0||I'M YOURS|JASON MRAZ
06 - jAsOn MrAz - TiTlE|@|||0||TiTlE|jAsOn MrAz
06 - title|/music/Va/Mix/06 - Title.mp3|||0||title|
06 - Title||||0||06 - Title|
06 - 2 Become 1|@|||0||2 Become 1|
06 - 20 Questions|@|||0||20 Questions|
06 - 1979|@|||0||1979|
Artist - 1979|@|||0||1979|Artist
Artist - 22|@|||0||Artist - 22|
Smashing Pumpkins - 22|@|||0|smashing pumpkins|22|Smashing Pumpkins
01 - Intro|@|Foo||0||Intro|Foo
06-Title|@|||0||Title|
Title|@|||0||Title|
06 - Artist - Title [Hi-Res] (www.x.com)|@|||0||Title|Artist
Toto - Toto IV (1982) - 01 - Rosanna|@||Toto IV (1982)|0||Rosanna|Toto
50 Cent - In Da Club|@|||0||In Da Club|50 Cent
50 Cent - In Da Club|@|||1|50 cent|In Da Club|50 Cent
2Pac - Dear Mama|@|||0||Dear Mama|2Pac
06 - Title Part 2 (Live) [Remastered]|@|||0||Title Part 2 (Live) [Remastered]|
Daft Punk - Around the World - Radio Edit|@|||0|daft punk|Around the World - Radio Edit|Daft Punk
Foo - Bar - Baz|@|||0||Bar - Baz|Foo
06 - Title|/music/Va/Mix/06 - Title.FLAC|||0||Title|
06 - Title|D:\Music\Va\Mix\06 - Title.mp3|||0||Title|
The Weeknd - Blinding Lights|@|The Weeknd||0||Blinding Lights|The Weeknd
""";

    [Fact]
    public void CleansEveryCase()
    {
        var lines = Table.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(lines.Length >= 40);
        foreach (var line in lines)
        {
            var f = line.Split('|');
            Assert.True(f.Length == 8, "bad table line: " + line);
            var path = f[1] == "@" ? $"/music/Va/Mix/{f[0]}.mp3" : f[1];
            var known = f[5].Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var r = TrackTitles.Clean(f[0], path, f[2], f[3], f[4] == "1", known);
            Assert.True(f[6] == r.Title, $"title of: {line} -> {r.Title}");
            Assert.True(f[7] == r.Artist, $"artist of: {line} -> {r.Artist}");
        }
    }

    static Track Jf(string title, string path, string artist = "", string album = "") =>
        new($"http://jf/Audio/{Math.Abs(title.GetHashCode())}/stream?static=true", title, artist, album, artist, 0, 0, 0, 0, null, 0, TrackSource.Jellyfin, path);

    [Fact]
    public void LibraryPassUsesFoldersAndKnownArtists()
    {
        var lib = new List<Track>
        {
            Jf("Goldfrapp - Ooh La La", "/m/Goldfrapp/Black Cherry/01 Ooh La La.mp3", "Goldfrapp", "Black Cherry"),
            Jf("12 Alpha", "/m/Mix/12 Alpha.mp3"), Jf("13 Beta", "/m/Mix/13 Beta.mp3"), Jf("14 Gamma", "/m/Mix/14 Gamma.mp3"),
            Jf("99 Problems", "/m/Jay-Z/99 Problems.mp3", "Jay-Z"),
            Jf("06 - a&e - Goldfrapp", "/m/Mix/06 - a&e - Goldfrapp.mp3"),
        };
        var o = TrackTitles.CleanAll(lib).ToDictionary(t => t.FilePath!);
        Assert.Equal("Alpha", o["/m/Mix/12 Alpha.mp3"].Title);
        Assert.Equal("Gamma", o["/m/Mix/14 Gamma.mp3"].Title);
        Assert.Equal("99 Problems", o["/m/Jay-Z/99 Problems.mp3"].Title);
        var ae = o["/m/Mix/06 - a&e - Goldfrapp.mp3"];
        Assert.Equal("a&e", ae.Title); Assert.Equal("Goldfrapp", ae.Artist); Assert.Equal("06 - a&e - Goldfrapp", ae.OrigTitle);
        Assert.Equal(o.Values.Select(t => t.Title), TrackTitles.CleanAll(o.Values.ToList()).Select(t => t.Title));
    }

    [Fact]
    public void CleanedSongMatchesTheNasCopyAndKeepsItsFileKey()
    {
        var j = TrackTitles.CleanAll([Jf("06 - Jason Mraz - Everything Is Sound", "/media/music/Jason Mraz/06 - Jason Mraz - Everything Is Sound.mp3")])[0];
        Assert.Equal(Matching.MatchKey("Everything Is Sound", "Jason Mraz"), Matching.MatchKey(j));
        Assert.EndsWith("jason mraz/06 - jason mraz - everything is sound.mp3", Matching.FileKey(j));
    }
}
