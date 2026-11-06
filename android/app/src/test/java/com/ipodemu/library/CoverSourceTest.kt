package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** F2: with FLACie Web connected every cover comes from its /art; when it can't be reached the phone looks the cover up itself. */
class CoverSourceTest {
    private val sent = ArrayList<Pair<String, Map<String, String>>>()
    private var answer: GateResponse? = GateResponse(200, byteArrayOf(1, 2, 3), mapOf("content-type" to "image/jpeg"))
    private var clock = 1_000L
    private var url = "http://192.0.2.10:8080/"
    private fun web() = WebCovers({ url }, { mapOf("X-Emby-Token" to "t") }, { u, h -> sent += u to h; answer }, { clock })

    @Test fun aCatalogKeyIsAskedFromTheWebWithTheRulesMark() {
        assertEquals("/art/itABC~r2?fmt=png", WebCovers.path("itABC", null, false))
        assertEquals("/art/itABC~r2?fmt=png", WebCovers.path("itABC~r2", null, false))
        assertEquals("/art/itABC~r2?fmt=png&s=full", WebCovers.path("itABC", null, true))
    }

    @Test fun aJellyfinKeyCarriesItsPictureTag() {
        assertEquals("/art/jfabc~tag1?fmt=png", WebCovers.path("jfabc", "tag1", false))
        assertEquals("/art/jfabc?fmt=png", WebCovers.path("jfabc", null, false))
        assertEquals("/art/jfabc~tag1?fmt=png&s=full", WebCovers.path("jfabc", "tag1", true))
    }

    @Test fun withTheWebConnectedTheCoverComesFromItsArtRouteSignedLikeOtherWebCalls() {
        val r = web().fetch("itABC", null, false, null)
        assertNotNull(r); assertEquals(3, r!!.body.size)
        assertTrue(sent.single().first.startsWith("http://192.0.2.10:8080/art/itABC~r2"))
        assertEquals("t", sent.single().second["X-Emby-Token"])
    }

    @Test fun theSavedCopysEtagIsSentSoANotModifiedAnswerPassesThrough() {
        answer = GateResponse(304, ByteArray(0))
        val r = web().fetch("jfabc", "x", false, "\"e1\"")
        assertEquals(304, r!!.code)
        assertEquals("\"e1\"", sent.single().second["If-None-Match"])
    }

    @Test fun anUnreachableWebFallsBackAndIsNotAskedAgainForAWhile() {
        answer = null
        val w = web()
        assertNull(w.fetch("itA", null, false, null))
        assertNull(w.fetch("itB", null, false, null)); assertEquals("a dead server costs one timeout, not one per cover", 1, sent.size)
        clock += 31_000
        w.fetch("itC", null, false, null); assertEquals(2, sent.size)
    }

    @Test fun aWebThatAnswersOnceMoreIsUsedAgain() {
        answer = null
        val w = web(); w.fetch("itA", null, false, null)
        clock += 31_000; answer = GateResponse(200, byteArrayOf(9), mapOf("content-type" to "image/png"))
        assertNotNull(w.fetch("itB", null, false, null))
    }

    @Test fun aMadeSvgCoverIsNotUsedAsAPictureAndTheWebStaysInUse() {
        answer = GateResponse(200, "<svg/>".toByteArray(), mapOf("content-type" to "image/svg+xml"))
        val w = web()
        assertNull(w.fetch("itA", null, false, null))
        assertNull(w.fetch("itB", null, false, null)); assertEquals("an svg answer is not an outage", 2, sent.size)
    }

    @Test fun aPageThatIsNotAPictureIsNeverUsedAsACover() {
        // b108: the web sent the phone to its sign-in page, and the page's HTML was kept as every cover
        answer = GateResponse(200, "<!DOCTYPE html><html>".toByteArray(), mapOf("content-type" to "text/html; charset=utf-8"))
        assertNull(web().fetch("itA", null, false, null))
        answer = GateResponse(200, byteArrayOf(1, 2, 3), emptyMap())
        assertNull("no type, no picture", web().fetch("itA", null, false, null))
    }

    @Test fun anErrorAnswerFallsBackWithoutMarkingTheWebDown() {
        answer = GateResponse(500, ByteArray(0))
        val w = web()
        assertNull(w.fetch("itA", null, false, null)); assertNull(w.fetch("itB", null, false, null)); assertEquals(2, sent.size)
    }

    @Test fun notConnectedMeansNoRequestAtAll() {
        url = ""
        assertNull(web().fetch("itA", null, false, null)); assertTrue(sent.isEmpty())
    }
}
