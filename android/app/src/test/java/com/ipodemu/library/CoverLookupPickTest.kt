package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner, 5 Nov: catalog covers blank or wrong. The phone picks the way the server does, and Deezer's quota answer is "ask again soon", not "no cover". */
class CoverLookupPickTest {
    private fun track(artist: String, title: String, album: String, cover: String) =
        """{"title":"$title","artist":{"name":"$artist"},"album":{"title":"$album","cover_big":"$cover"}}"""
    private fun page(vararg rows: String) = "{\"data\":[${rows.joinToString(",")}]}"

    @Test fun aSongFoundByItsTitleGetsItsOwnAlbumNotTheCompilation() {
        val body = page(
            track("Nickelback", "How You Remind Me", "Now That's What I Call Music! #1's", "https://e/now.jpg"),
            track("Nickelback", "How You Remind Me", "Silver Side Up", "https://e/ssu.jpg"))
        assertEquals("https://e/ssu.jpg", CoverLookup.pickDeezer(body, "search", "Nickelback", "", "How You Remind Me"))
    }

    @Test fun aPlainTitleMatchIsAcceptedEvenWhenOnlyACompilationHasIt() {
        val body = page(track("Eminem", "Lose Yourself", "8 Mile (Music From And Inspired By The Motion Picture)", "https://e/8m.jpg"))
        assertEquals("https://e/8m.jpg", CoverLookup.pickDeezer(body, "search", "Eminem", "", "Lose Yourself"))
    }

    @Test fun anotherSongByTheArtistIsNoCoverRatherThanAWrongOne() {
        val body = page(track("Eminem", "Without Me", "The Eminem Show", "https://e/show.jpg"))
        assertNull(CoverLookup.pickDeezer(body, "search", "Eminem", "", "Lose Yourself"))
    }

    @Test fun anAlbumSearchMustFindThatAlbum() {
        val body = """{"data":[{"title":"Dark Horse","artist":{"name":"Nickelback"},"cover_big":"https://e/dh.jpg"},{"title":"Silver Side Up","artist":{"name":"Nickelback"},"cover_big":"https://e/ssu.jpg"}]}"""
        assertEquals("https://e/ssu.jpg", CoverLookup.pickDeezer(body, "search/album", "Nickelback", "Silver Side Up (Deluxe)", "How You Remind Me"))
        assertNull(CoverLookup.pickDeezer(body, "search/album", "Nickelback", "Now That's What I Call Music! #1's", "How You Remind Me"))
    }

    @Test fun deezersQuotaAnswerIsAskAgainLaterNotNoCover() {
        val quota = """{"error":{"type":"Exception","message":"Quota limit exceeded","code":4}}"""
        val thrown = runCatching { CoverLookup.pickDeezer(quota, "search", "Nickelback", "", "How You Remind Me") }.exceptionOrNull()
        assertTrue(thrown is CoverLookup.CatalogBusy)
    }

    @Test fun aBusyCatalogIsAskedAgainSoonerThanAMiss() {
        assertTrue(ArtCache.retryAfterMs(busy = true) < ArtCache.retryAfterMs(busy = false))
        assertEquals(30_000L, ArtCache.retryAfterMs(busy = true))
    }
}
