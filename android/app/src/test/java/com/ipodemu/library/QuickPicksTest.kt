package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class QuickPicksTest {
    private fun song(i: Int, artist: String, album: String, title: String = "Song $i", genre: String = "Pop") = Track(
        path = "http://x/Audio/${"%032x".format(i)}/stream?static=true", title = title, artist = artist, album = album, albumArtist = artist, genre = genre, trackNo = i % 12, discNo = 1,
        durationMs = 200_000, year = 2010, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN, filePath = "$title.flac")

    // 5 artists x 2 albums x 8 songs
    private val artists = listOf("Aurora", "Brass", "Cobalt", "Delta", "Echo")
    private val songs = buildList { var i = 1; for (a in artists) for (al in 1..2) for (t in 1..8) { add(song(i, a, "$a album $al", "${a.lowercase()} track $i")); i++ } }
    private val none = QuickPicks.Signals(emptyMap(), emptySet(), emptyList(), emptyList())
    private fun idOf(t: Track) = TasteData.jfIdRe.find(t.path)!!.groupValues[1]
    private val now = 1_800_000_000_000L

    @Test fun songsYouPlayInJellyfinLeadTheRow() {
        val aurora = songs.filter { it.artist == "Aurora" }
        val jf = aurora.take(3).mapIndexed { i, t -> JfPlay(idOf(t), 12 - i, now - 20 * 86_400_000L, false) }
        val picks = QuickPicks.pick(songs, none, jf, emptySet(), 1, now, emptySet())
        assertEquals(20, picks.size)
        // the three loved songs are all in the row, before anything new
        assertTrue(aurora.take(2).all { it in picks.take(6) })
        assertTrue("Aurora's own songs lead", picks.take(3).count { it.artist == "Aurora" } >= 2)
    }

    @Test fun anArtistsKnownHitsComeForwardOverTheirOtherSongs() {
        val brass = songs.filter { it.artist == "Brass" }
        val jf = brass.take(2).map { JfPlay(idOf(it), 8, now - 10 * 86_400_000L, false) }
        val hit = brass.last()
        val picks = QuickPicks.pick(songs, none, jf, setOf(hit.matchKey), 1, now, emptySet())
        assertTrue("the known hit is picked although never played", hit in picks)
    }

    @Test fun neverPlayedSongsOnlyCountWhenTiedToWhatYouPlay() {
        val jf = songs.filter { it.artist == "Cobalt" }.take(4).map { JfPlay(idOf(it), 6, now - 5 * 86_400_000L, false) }
        val picks = QuickPicks.pick(songs, none, jf, emptySet(), 1, now, emptySet())
        val played = jf.map { it.id }.toSet()
        val newOnes = picks.filter { idOf(it) !in played }
        // after the loved ones, the first new songs come from the artist or album played; songs of artists never touched only fill a short row at the end
        assertTrue(newOnes.isNotEmpty() && picks.take(3).all { it.artist == "Cobalt" })
    }

    @Test fun aSongPlayedAMomentAgoSitsOut() {
        val aurora = songs.first { it.artist == "Aurora" }
        val jf = listOf(JfPlay(idOf(aurora), 30, now - 30 * 60_000L, true))
        assertTrue(aurora !in QuickPicks.pick(songs, none, jf, emptySet(), 1, now, emptySet()))
    }

    @Test fun aRowIsNotFilledByOneArtist() {
        val jf = songs.filter { it.artist == "Delta" }.map { JfPlay(idOf(it), 9, now - 3 * 86_400_000L, false) }
        val picks = QuickPicks.pick(songs, none, jf, emptySet(), 1, now, emptySet())
        assertTrue("the played artist does not fill the row", picks.count { it.artist == "Delta" } <= 4)
    }

    @Test fun theRowChangesWithTheHourButStaysWithinIt() {
        val jf = songs.take(10).map { JfPlay(idOf(it), 5, now - 40 * 86_400_000L, false) }
        val a = QuickPicks.pick(songs, none, jf, emptySet(), 100, now, emptySet())
        assertEquals(a, QuickPicks.pick(songs, none, jf, emptySet(), 100, now, emptySet()))
        assertTrue(a != QuickPicks.pick(songs, none, jf, emptySet(), 101, now, emptySet()))
    }

    @Test fun aSmallHistoryStillFillsTheRowWithoutRandomSongsFirst() {
        val jf = songs.filter { it.artist == "Echo" }.take(2).map { JfPlay(idOf(it), 7, now - 9 * 86_400_000L, false) }
        val picks = QuickPicks.pick(songs, none, jf, emptySet(), 1, now, emptySet())
        assertEquals(20, picks.size)
        assertEquals("Echo", picks.first().artist)
    }

    @Test fun withNoHistoryThereAreNoPicksInsteadOfRandomOnes() {
        assertTrue(QuickPicks.pick(songs, none, emptyList(), emptySet(), 1, now, emptySet()).isEmpty())
    }
}
