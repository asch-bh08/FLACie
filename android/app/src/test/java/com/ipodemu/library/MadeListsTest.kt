package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Ready-made playlists from FLACie Web (owner, 2 Nov): what the phone reads from /api/made. */
class MadeListsTest {
    @Test fun searchReadsTheListsAndTheirFirstSong() {
        val l = MadeLists.parseSearch("""{"lists":[
            {"id":"made:decade:2010","name":"2010s","subtitle":"50 songs · made for you","count":50,"artKey":"jfabc","first":{"path":"jf:1","title":"One","artist":"A","album":"LP","durationMs":200000}},
            {"id":"made:artist:prodigy","name":"Best of The Prodigy","subtitle":"8 songs · made for you","count":8,"artKey":null,"first":null},
            {"name":"no id"}]}""")
        assertEquals(2, l.size)
        assertEquals("2010s", l[0].name)
        assertEquals(50, l[0].count)
        assertEquals("jf:1", l[0].first!!.path)
        assertEquals("One", l[0].first!!.title)
        assertFalse(l[0].first!!.isNew)
        assertNull(l[1].first)
        assertEquals("8 songs · made for you", l[1].subtitle)
    }

    @Test fun anOutsideListCarriesItsPictureAndSource() {
        val l = MadeLists.parseSearch("""{"lists":[{"id":"web:spotify:abc","name":"This Is The Prodigy","subtitle":"50 songs · Spotify editors","count":50,"artKey":null,"picture":"https://i.scdn.co/image/x","first":null},
            {"id":"made:genre:rock","name":"Rock","subtitle":"","count":2,"artKey":null,"picture":null,"first":null}]}""")
        assertEquals("https://i.scdn.co/image/x", l[0].picture)
        assertTrue(MadeLists.isOutside(l[0].id))
        assertEquals("Spotify", MadeLists.source(l[0].id))
        assertNull(l[1].picture)
        assertFalse(MadeLists.isOutside(l[1].id))
    }

    @Test fun anOpenedOutsideListNamesTheSongsThePhoneLacks() {
        val f = MadeLists.parseOne("""{"id":"web:deezer:9","name":"90s Rave","count":3,"tracks":[
            {"path":"jf:1","title":"One","artist":"A","missing":false},{"path":null,"title":"Firestarter","artist":"The Prodigy","missing":true}]}""")
        assertEquals(listOf("Firestarter" to "The Prodigy"), MadeLists.missing(f) { it.path != null })
    }

    @Test fun anEmptyAnswerIsNoLists() {
        assertTrue(MadeLists.parseSearch("""{"lists":[]}""").isEmpty())
        assertTrue(MadeLists.parseSearch("{}").isEmpty())
    }

    @Test fun oneListCarriesItsSongs() {
        val f = MadeLists.parseOne("""{"id":"made:genre:rock","name":"Rock","subtitle":"2 songs · made for you","count":2,"tracks":[
            {"path":"jf:1","title":"One","artist":"A","album":"LP","durationMs":1000},{"path":null,"title":"Two","artist":"B","album":"","durationMs":2000}]}""")
        assertEquals("Rock", f.name)
        assertEquals(listOf("One", "Two"), f.tracks.map { it.title })
        assertEquals(1000L, f.tracks[0].durationMs)
        assertNull(f.tracks[1].path)
    }

    @Test fun savingAnswersThePlaylist() {
        val s = MadeLists.parseSaved("""{"id":"abc","name":"Rock 2","count":30}""")
        assertEquals("abc", s.id); assertEquals("Rock 2", s.name); assertEquals(30, s.count)
    }

    @Test fun theIdIsOnePathSegment() {
        assertEquals("/api/made/made%3Aartist%3Athe%20prodigy", MadeLists.path("made:artist:the prodigy"))
        assertEquals("/api/made/made%3Agenre%3Ar%26b", MadeLists.path("made:genre:r&b"))
        assertEquals("/api/made/made%3Agenre%3Aa%2Fb", MadeLists.path("made:genre:a/b"))
    }

    @Test fun onlySearchesOfTwoLettersOrMoreAreAsked() {
        assertFalse(MadeLists.worthAsking(" r "))
        assertTrue(MadeLists.worthAsking("90s"))
    }
}
