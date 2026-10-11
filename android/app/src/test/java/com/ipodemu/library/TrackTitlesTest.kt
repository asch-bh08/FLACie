package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

/** The same table is in ipodsync/tests/FLACie.Tests/TrackTitlesTests.cs: both platforms must clean a file name the same way. */
class TrackTitlesTest {
    // name | path ("@" = /music/Va/Mix/{name}.mp3) | item artist | item album | siblings numbered (0/1) | known artists (; separated) | expected title | expected artist
    private val table = """
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
""".trim().lines()

    @Test fun table() {
        assertNotEquals(0, table.size)
        for (line in table) {
            val f = line.split('|')
            assertEquals("bad table line: $line", 8, f.size)
            val path = if (f[1] == "@") "/music/Va/Mix/${f[0]}.mp3" else f[1]
            val known = f[5].split(';').filter { it.isNotEmpty() }.toSet()
            val r = TrackTitles.clean(f[0], path, f[2], f[3], f[4] == "1", known)
            assertEquals("title of: $line", f[6], r.title)
            assertEquals("artist of: $line", f[7], r.artist)
        }
    }

    @Test fun tableHasAtLeastFortyCases() = assertEquals(true, table.size >= 40)

    private fun jf(title: String, path: String, artist: String = "", album: String = "") = Track(
        path = "http://jf/Audio/${title.hashCode()}/stream?static=true", title = title, artist = artist, album = album, albumArtist = artist, genre = "",
        trackNo = 0, discNo = 0, durationMs = 0, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN, filePath = path)

    @Test fun libraryPassUsesFoldersAndKnownArtists() {
        val lib = listOf(
            jf("Goldfrapp - Ooh La La", "/m/Goldfrapp/Black Cherry/01 Ooh La La.mp3", "Goldfrapp", "Black Cherry"), // tagged: teaches the artist
            jf("12 Alpha", "/m/Mix/12 Alpha.mp3"), jf("13 Beta", "/m/Mix/13 Beta.mp3"), jf("14 Gamma", "/m/Mix/14 Gamma.mp3"),
            jf("99 Problems", "/m/Jay-Z/99 Problems.mp3", "Jay-Z"),
            jf("06 - a&e - Goldfrapp", "/m/Mix/06 - a&e - Goldfrapp.mp3"),
        )
        val out = TrackTitles.cleanAll(lib).associateBy { it.filePath }
        assertEquals("Alpha", out["/m/Mix/12 Alpha.mp3"]!!.title)
        assertEquals("Gamma", out["/m/Mix/14 Gamma.mp3"]!!.title)
        assertEquals("99 Problems", out["/m/Jay-Z/99 Problems.mp3"]!!.title)
        val ae = out["/m/Mix/06 - a&e - Goldfrapp.mp3"]!!
        assertEquals("a&e", ae.title); assertEquals("Goldfrapp", ae.artist); assertEquals("06 - a&e - Goldfrapp", ae.origTitle)
        // idempotent: a cleaned song is not touched again
        assertEquals(out.values.map { it.title }, TrackTitles.cleanAll(out.values.toList()).map { it.title })
    }

    @Test fun cleanedSongMatchesTheNasCopyAndKeepsItsFileKey() {
        val j = TrackTitles.cleanAll(listOf(jf("06 - Jason Mraz - Everything Is Sound", "/media/music/Jason Mraz/06 - Jason Mraz - Everything Is Sound.mp3"))).single()
        assertEquals(matchKey("Everything Is Sound", "Jason Mraz"), j.matchKey)
        assertEquals("jason mraz/06 - jason mraz - everything is sound.mp3", j.fileKey!!.substringAfter("music/"))
    }
}
