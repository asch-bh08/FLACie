package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RatingStoreTest {
    private fun t(title: String = "Song", artist: String = "Art", path: String? = null) = Track(
        path = path ?: "http://jf/Audio/$title/stream", title = title, artist = artist, album = "Al", albumArtist = artist, genre = "", trackNo = 1, discNo = 1,
        durationMs = 1000, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    @Test fun starsMapToJellyfinsTenPointScaleAndBack() {
        assertEquals(8.0, RatingStore.toJellyfin(4), 0.0); assertEquals(10.0, RatingStore.toJellyfin(9), 0.0); assertEquals(0.0, RatingStore.toJellyfin(-3), 0.0)
        assertEquals(4, RatingStore.fromJellyfin(8.0)); assertEquals(4, RatingStore.fromJellyfin(7.4)); assertEquals(0, RatingStore.fromJellyfin(Double.NaN)); assertEquals(5, RatingStore.fromJellyfin(20.0))
        assertEquals("not rated", RatingStore.label(0)); assertEquals("1 star", RatingStore.label(1)); assertEquals("5 stars", RatingStore.label(5))
    }

    @Test fun jellyfinPathsTryTheNewEndpointFirst() {
        assertEquals(listOf("/UserItems/i1/UserData?userId=u1", "/Users/u1/Items/i1/UserData"), RatingStore.jellyfinPaths("u1", "i1"))
    }

    @Test fun aRatingIsKeptAndFoundByPathOrByTitleAndArtist() {
        val s = RatingStore(); val a = t()
        assertEquals(0, s.get(a)); s.set(a, 4, 10); assertEquals(4, s.get(a))
        assertEquals("the same song from another source", 4, s.get(t(path = "/other/file.flac")))
        assertEquals(0, s.get(t(title = "Another")))
        s.set(a, 2, 20); assertEquals(2, s.get(a)); assertEquals(1, s.rated().size)
        s.set(a, 9, 30); assertEquals(5, s.get(a)); s.set(a, 0, 40); assertEquals(0, s.get(a)); assertTrue(s.rated().isEmpty())
    }

    @Test fun ratedListsNewestFirst() {
        val s = RatingStore(); s.set(t("A"), 3, 1); s.set(t("B"), 5, 2)
        assertEquals(listOf("B", "A"), s.rated().map { it.title }); assertEquals(listOf(5, 3), s.rated().map { it.stars })
    }

    @Test fun theNewestChoiceWinsWhenDevicesDisagree() {
        val s = RatingStore(); s.set(t("A"), 3, 100)
        assertFalse("an older rating elsewhere changes nothing", s.merge(listOf(RatingEntry(t("A").path, "A", "Art", 5, 50))))
        assertEquals(3, s.get(t("A")))
        assertTrue(s.merge(listOf(RatingEntry(t("A").path, "A", "Art", 5, 150)))); assertEquals(5, s.get(t("A")))
        assertTrue("a newer clearing wins too", s.merge(listOf(RatingEntry(t("A").path, "A", "Art", 0, 200)))); assertEquals(0, s.get(t("A")))
        assertTrue(s.merge(listOf(RatingEntry("x", "New", "Someone", 4, 5)))); assertEquals(4, s.get(t("New", "Someone", "x")))
        assertFalse(s.merge(emptyList()))
    }

    @Test fun savesAndLoadsAndReadsWhatTheWebWrites() {
        val s = RatingStore(); s.set(t("A"), 3, 100); s.set(t("B"), 5, 200)
        val back = RatingStore(); back.load(s.toJson())
        assertEquals(3, back.get(t("A"))); assertEquals(5, back.get(t("B"))); assertEquals(listOf("B", "A"), back.rated().map { it.title })
        val web = org.json.JSONArray("""[{"p":"/p/x","t":"Web Song","a":"W","r":4,"w":999},{"t":"broken"}]""")
        val parsed = RatingStore().parse(web)
        assertEquals("Web Song", parsed[0].title); assertEquals(4, parsed[0].stars); assertEquals(999L, parsed[0].at)
        back.load(null); assertTrue(back.rated().isEmpty())
    }
}
