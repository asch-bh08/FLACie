package com.ipodemu.playback

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** "Play X" with no signal: understood on the phone and played from its downloads (owner, 25 Nov: the DJ in the car "doesn't understand play X when
 *  there's no signal even if that song is downloaded offline"). */
class OfflineAskTest {
    private fun t(title: String, artist: String, album: String = "Album", path: String = "jf:$artist/$title") =
        Track(path, title, artist, album, artist, "Rock", 1, 1, 200_000, 2009, true, null, 0, 0, TrackSource.JELLYFIN)

    private val heads = t("Heads Will Roll", "Yeah Yeah Yeahs", "It's Blitz!")
    private val headsRemix = t("Heads Will Roll (A-Trak Remix)", "Yeah Yeah Yeahs", "Heads Will Roll")
    private val zero = t("Zero", "Yeah Yeah Yeahs", "It's Blitz!")
    private val maps = t("Maps", "Yeah Yeah Yeahs", "Fever to Tell")
    private val firestarter = t("Firestarter", "The Prodigy", "The Fat of the Land")
    private val breathe = t("Breathe", "The Prodigy", "The Fat of the Land")
    private val breatheOther = t("Breathe", "Faith Hill", "Breathe")
    private val dancing = t("Dancing On My Own", "Robyn", "Body Talk")
    private val all = listOf(headsRemix, heads, zero, maps, firestarter, breathe, breatheOther, dancing)

    private class Lib(val songs: List<Track>, val downloads: Set<String>, val lists: List<Pair<String, List<Track>>> = emptyList(), val favs: List<Track> = emptyList()) : OfflineAsk.Lib {
        override fun songs() = songs
        override fun playlists() = lists
        override fun favourites() = favs
        override fun isDownloaded(path: String) = path in downloads
    }

    private fun lib(vararg downloaded: Track, lists: List<Pair<String, List<Track>>> = emptyList(), favs: List<Track> = emptyList()) =
        Lib(all, downloaded.mapTo(HashSet()) { it.path }, lists, favs)

    private fun firstTitle(a: DjAskAnswer) = a.songs.firstOrNull()?.title

    @Test fun playASongThatIsDownloaded() {
        val a = OfflineAsk.answer("play heads will roll", OfflineAsk.Why.NO_SIGNAL, lib(heads, maps))
        assertEquals(DjAskDo.PLAY, a.doThis)
        assertEquals("Heads Will Roll", firstTitle(a))
        assertEquals(heads.path, a.songs[0].path)
        assertEquals("No signal: playing Heads Will Roll from your downloads.", a.say)
    }

    @Test fun theMainVersionBeforeARemix() {
        val a = OfflineAsk.answer("play heads will roll", OfflineAsk.Why.NO_SERVER, lib())
        assertEquals(heads.path, a.songs[0].path)
    }

    @Test fun noSignalPlaysTheDownloadedCopyNotOneThatNeedsStreaming() {
        // only the remix is on the phone: with no signal that is what can play
        val a = OfflineAsk.answer("play heads will roll", OfflineAsk.Why.NO_SIGNAL, lib(headsRemix))
        assertEquals(headsRemix.path, a.songs[0].path)
    }

    @Test fun theMusicCarriesOnWithTheOtherDownloadsAfterTheSong() {
        val a = OfflineAsk.answer("play heads will roll", OfflineAsk.Why.NO_SIGNAL, lib(heads, maps, dancing))
        assertEquals(heads.path, a.songs[0].path)
        assertEquals(setOf(heads.path, maps.path, dancing.path), a.songs.map { it.path }.toSet())
    }

    @Test fun aSongWithItsArtist() {
        val a = OfflineAsk.answer("play breathe by faith hill", OfflineAsk.Why.NO_SERVER, lib())
        assertEquals(breatheOther.path, a.songs[0].path)
        val b = OfflineAsk.answer("play breathe by the prodigy", OfflineAsk.Why.NO_SERVER, lib())
        assertEquals(breathe.path, b.songs[0].path)
    }

    @Test fun aSlightlyMisheardTitleIsStillFound() {
        val a = OfflineAsk.answer("play heads will role", OfflineAsk.Why.NO_SIGNAL, lib(heads))
        assertEquals(heads.path, a.songs[0].path)
    }

    @Test fun aSongNotDownloadedSaysSoWithNoSignal() {
        val a = OfflineAsk.answer("play firestarter", OfflineAsk.Why.NO_SIGNAL, lib(heads))
        assertEquals(DjAskDo.NONE, a.doThis)
        assertEquals("No signal, and Firestarter isn't downloaded.", a.say)
    }

    @Test fun somethingNotOnThePhoneAtAll() {
        val a = OfflineAsk.answer("play bohemian rhapsody", OfflineAsk.Why.NO_SIGNAL, lib(heads))
        assertEquals(DjAskDo.NONE, a.doThis)
        assertEquals("No signal, and I can't find bohemian rhapsody in your downloads.", a.say)
        val b = OfflineAsk.answer("play bohemian rhapsody", OfflineAsk.Why.NO_SERVER, lib(heads))
        assertEquals("I couldn't reach the server, and I can't find bohemian rhapsody on this phone.", b.say)
    }

    @Test fun anAlbum() {
        val a = OfflineAsk.answer("play the album it's blitz", OfflineAsk.Why.NO_SIGNAL, lib(heads, zero))
        assertEquals(DjAskDo.PLAY, a.doThis)
        assertEquals(setOf(heads.path, zero.path), a.songs.map { it.path }.toSet())
        assertEquals("It's Blitz!", a.title)
        assertFalse(a.shuffle)
    }

    @Test fun aPlaylistByName() {
        val lists = listOf("Pop" to listOf(dancing, maps), "Running" to listOf(firestarter))
        val a = OfflineAsk.answer("play my pop playlist", OfflineAsk.Why.NO_SIGNAL, lib(dancing, maps, lists = lists))
        assertEquals("Pop", a.title)
        assertEquals(listOf(dancing.path, maps.path), a.songs.map { it.path })
        assertEquals("No signal: playing Pop from your downloads.", a.say)
    }

    @Test fun aPlaylistWithNoSignalPlaysOnlyItsDownloadedSongs() {
        val lists = listOf("Pop" to listOf(dancing, maps))
        val a = OfflineAsk.answer("play pop playlist", OfflineAsk.Why.NO_SIGNAL, lib(maps, lists = lists))
        assertEquals(listOf(maps.path), a.songs.map { it.path })
    }

    @Test fun aBarePlaylistNameBeatsASongTitle() {
        val lists = listOf("Breathe" to listOf(dancing))
        val a = OfflineAsk.answer("play breathe", OfflineAsk.Why.NO_SERVER, lib(lists = lists))
        assertEquals("Breathe", a.title); assertEquals(dancing.path, a.songs[0].path)
    }

    @Test fun anArtist() {
        val a = OfflineAsk.answer("play the prodigy", OfflineAsk.Why.NO_SIGNAL, lib(firestarter, breathe, heads))
        assertEquals(setOf(firestarter.path, breathe.path), a.songs.map { it.path }.toSet())
        assertTrue(a.shuffle)
        val b = OfflineAsk.answer("play songs by prodigy", OfflineAsk.Why.NO_SIGNAL, lib(firestarter, breathe))
        assertEquals(2, b.songs.size)
    }

    @Test fun shuffleMyFavourites() {
        val a = OfflineAsk.answer("shuffle my favourites", OfflineAsk.Why.NO_SIGNAL, lib(heads, maps, favs = listOf(heads, maps, firestarter)))
        assertEquals(DjAskDo.PLAY, a.doThis); assertTrue(a.shuffle)
        assertEquals(setOf(heads.path, maps.path), a.songs.map { it.path }.toSet())
        assertEquals("No signal: shuffling your favourites from your downloads.", a.say)
    }

    @Test fun shuffleEverythingOnThePhone() {
        val a = OfflineAsk.answer("shuffle", OfflineAsk.Why.NO_SIGNAL, lib(heads, maps))
        assertTrue(a.shuffle); assertEquals(2, a.songs.size)
        val b = OfflineAsk.answer("play my downloads", OfflineAsk.Why.NO_SIGNAL, lib(heads, maps))
        assertEquals(2, b.songs.size)
    }

    @Test fun nothingDownloadedSaysSo() {
        val a = OfflineAsk.answer("shuffle my favourites", OfflineAsk.Why.NO_SIGNAL, lib(favs = listOf(heads)))
        assertEquals(DjAskDo.NONE, a.doThis)
        assertEquals("No signal, and none of your favourites are downloaded.", a.say)
    }

    @Test fun plainControlsWorkWithoutTheServer() {
        assertEquals(DjAskDo.NEXT, OfflineAsk.answer("skip this song", OfflineAsk.Why.NO_SIGNAL, lib()).doThis)
        assertEquals(DjAskDo.NEXT, OfflineAsk.answer("next", OfflineAsk.Why.NO_SIGNAL, lib()).doThis)
        assertEquals(DjAskDo.PAUSE, OfflineAsk.answer("pause", OfflineAsk.Why.NO_SIGNAL, lib()).doThis)
        assertEquals(DjAskDo.RESUME, OfflineAsk.answer("carry on", OfflineAsk.Why.NO_SIGNAL, lib()).doThis)
        assertEquals(DjAskDo.VOLUMEUP, OfflineAsk.answer("turn it up", OfflineAsk.Why.NO_SIGNAL, lib()).doThis)
        assertEquals(DjAskDo.VOLUMEDOWN, OfflineAsk.answer("quieter please", OfflineAsk.Why.NO_SIGNAL, lib()).doThis)
    }

    @Test fun aQuestionTheServerIsNeededForSaysWhy() {
        val a = OfflineAsk.answer("what is this song", OfflineAsk.Why.NO_SIGNAL, lib(heads))
        assertEquals(DjAskDo.NONE, a.doThis)
        assertEquals("No signal. I can play your downloads: say play and a song, album, artist or playlist.", a.say)
        assertEquals("I couldn't reach the server.", OfflineAsk.answer("what is this song", OfflineAsk.Why.NO_SERVER, lib()).say)
    }

    @Test fun politeWordsAndTheDjAreIgnored() {
        val a = OfflineAsk.answer("Hey DJ, could you please play Maps?", OfflineAsk.Why.NO_SIGNAL, lib(maps))
        assertEquals(maps.path, a.songs[0].path)
    }

    @Test fun theAnswerIsMarkedAsThePhonesOwn() {
        assertEquals(OfflineAsk.INTENT, OfflineAsk.answer("play maps", OfflineAsk.Why.NO_SIGNAL, lib(maps)).intent)
        // and the conversation ends after it (no follow-up turn on a dead line)
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.NONE, false, 1, OfflineAsk.INTENT))
    }
}
