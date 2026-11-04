package com.ipodemu.library

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test

/** Pasting a Spotify or Deezer link: which links the phone accepts (as the server does), what it sends, and how it reads the answer. */
class LinkImportTest {
    @Test fun aSpotifyPlaylistLinkIsKeptWithoutItsTrackingQuery() =
        assertEquals("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", LinkImport.recognise("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=abc123"))

    @Test fun anInternationalSpotifyAlbumAndAnEmbedLinkAreAccepted() {
        assertEquals("https://open.spotify.com/album/4aawyAB9vmqN3uQ7FjRGTy", LinkImport.recognise("https://open.spotify.com/intl-de/album/4aawyAB9vmqN3uQ7FjRGTy"))
        assertEquals("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", LinkImport.recognise("open.spotify.com/embed/playlist/37i9dQZF1DXcBWIGoYBM5M"))
    }

    @Test fun aSpotifyUriBecomesItsWebAddress() =
        assertEquals("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", LinkImport.recognise("spotify:playlist:37i9dQZF1DXcBWIGoYBM5M"))

    @Test fun deezerLinksWithAndWithoutALanguageAreAccepted() {
        assertEquals("https://www.deezer.com/playlist/3155776842", LinkImport.recognise("https://www.deezer.com/en/playlist/3155776842"))
        assertEquals("https://www.deezer.com/album/302127", LinkImport.recognise("deezer.com/album/302127?utm_source=x"))
    }

    @Test fun aLinkInsideASharedMessageIsFound() =
        assertEquals("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", LinkImport.recognise("Listen to my mix\nhttps://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=z thanks"))

    @Test fun otherTextIsNotALink() {
        assertNull(LinkImport.recognise(null))
        assertNull(LinkImport.recognise(""))
        assertNull(LinkImport.recognise("not a link"))
        assertNull(LinkImport.recognise("https://music.apple.com/gb/playlist/x/pl.123"))
        assertNull(LinkImport.recognise("https://open.spotify.com/track/37i9dQZF1DXcBWIGoYBM5M"))
        assertNull(LinkImport.recognise("https://www.youtube.com/playlist?list=PL123"))
    }

    @Test fun theRequestBodyCarriesTheLinkAndReplace() {
        val o = JSONObject(LinkImport.body("https://www.deezer.com/playlist/12345"))
        assertEquals("https://www.deezer.com/playlist/12345", o.getString("url"))
        assertFalse(o.getBoolean("replace"))
    }

    @Test fun theJobAnswerIsRead() {
        val j = LinkImport.parseJob("""{"id":"abc","file":"Road","state":"running","total":40,"done":3,"failed":1,"note":null,"created":"2026-11-02T20:00:00Z","lists":[]}""")
        assertEquals("abc", j.id); assertEquals("Road", j.file); assertEquals("running", j.state)
        assertEquals(40, j.total); assertEquals(3, j.done); assertEquals(1, j.failed); assertNull(j.note)
    }

    @Test fun theServersWordsAreShownAsTheyCame() {
        assertEquals("No songs found at that link. Is the playlist public?", LinkImport.errorText(400, """{"error":"No songs found at that link. Is the playlist public?"}"""))
        assertEquals("Couldn't reach Spotify. Try again in a minute.", LinkImport.errorText(400, """{"error":"Couldn't reach Spotify. Try again in a minute."}"""))
    }

    @Test fun withoutTheServersWordsThereIsAPlainLine() {
        assertEquals("FLACie Web doesn't know this account. Open FLACie Web in a browser once, signed in to the same Jellyfin.", LinkImport.errorText(401, ""))
        assertEquals("Too many requests just now. Try again in a minute.", LinkImport.errorText(429, "<html>"))
        assertEquals("FLACie Web had a problem answering. Try again in a moment.", LinkImport.errorText(502, ""))
        assertEquals("Couldn't import that link (HTTP 418).", LinkImport.errorText(418, ""))
    }
}
