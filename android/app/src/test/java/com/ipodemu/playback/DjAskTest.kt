package com.ipodemu.playback

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** "Talk to the DJ" on the phone: the server's answer read, carried out on a fake player, and its songs found in this library. */
class DjAskTest {
    private class Fake : DjAskPlayer<String> {
        val log = mutableListOf<String>(); var volume = 0.5f; var dj: Boolean? = null
        val have = setOf("/a", "/b")
        override fun find(s: DjAskSong) = s.path.takeIf { it in have }
        override fun play(list: List<String>, title: String, shuffle: Boolean) { log += "play $title ${list.size} $shuffle" }
        override fun upNext(list: List<String>, replace: Boolean) { log += "upnext ${list.size} $replace" }
        override fun next() { log += "next" }
        override fun pause() { log += "pause" }
        override fun resume() { log += "resume" }
        override fun nudgeVolume(delta: Float) { volume = (volume + delta).coerceIn(0f, 1f) }
        override fun setDj(on: Boolean) { dj = on }
    }

    @Test fun answersAreReadAndCarriedOut() {
        val p = Fake()
        val play = DjAskAnswer.parse("""{"do":"play","say":"Here's your Pop playlist.","paths":["/a","/b","/zzz"],"title":"Pop","shuffle":true,"clip":"/api/dj/clip/x.mp3"}""")
        assertEquals(DjAskDo.PLAY, play.doThis); assertEquals("/api/dj/clip/x.mp3", play.clip)
        assertTrue(DjAskRunner.run(p, play)); assertEquals("play Pop 2 true", p.log.last())
        for ((json, want) in listOf("next" to "next", "pause" to "pause", "resume" to "resume")) { DjAskRunner.run(p, DjAskAnswer.parse("""{"do":"$json","say":""}""")); assertEquals(want, p.log.last()) }
        DjAskRunner.run(p, DjAskAnswer.parse("""{"do":"volumeup","say":""}""")); assertEquals(0.65f, p.volume, 0.001f)
        DjAskRunner.run(p, DjAskAnswer.parse("""{"do":"djoff","say":"Okay, I'll keep quiet."}""")); assertEquals(false, p.dj)
        DjAskRunner.run(p, DjAskAnswer.parse("""{"do":"djon","say":""}""")); assertEquals(true, p.dj)
        DjAskRunner.run(p, DjAskAnswer.parse("""{"do":"upnext","say":"","paths":["/b"],"replace":true}""")); assertEquals("upnext 1 true", p.log.last())
    }

    @Test fun anAnswerOrADownloadChangesNothing() {
        val p = Fake()
        val wait = DjAskAnswer.parse("""{"do":"none","say":"Can't find that one, getting it now.","clip":null,"wait":{"artist":"Akon","title":"Smack That"}}""")
        assertFalse(DjAskRunner.run(p, wait)); assertEquals("Akon", wait.waitArtist); assertNull(wait.clip)
        assertFalse(DjAskRunner.run(p, DjAskAnswer.parse("""{"do":"play","say":"x","paths":["/nothere"]}""")))
        assertTrue(p.log.isEmpty())
    }

    @Test fun songsAreFoundByPathThenFileThenTitle() {
        val s = DjAskSong("/server/a.flac", "/media/a.flac", "Smack That", "Akon")
        assertEquals("p", DjAskRunner.resolve(s, { if (it == "/server/a.flac") "p" else null }, { "f" }, { _, _ -> "t" }))
        assertEquals("f", DjAskRunner.resolve(s, { null }, { if (it == "/media/a.flac") "f" else null }, { _, _ -> "t" }))
        assertEquals("t", DjAskRunner.resolve(s, { null }, { null }, { t, a -> if (t == "Smack That" && a == "Akon") "t" else null }))
        assertNull(DjAskRunner.resolve(DjAskSong("/x"), { null }, { "f" }, { _, _ -> "t" }))
    }

    @Test fun theBodyCarriesOnlyTheWords() {
        val b = JSONObject(DjAskAnswer.body("play my pop playlist", "Creep", "Radiohead", DjSurface.PHONE, 21))
        assertEquals("play my pop playlist", b.getString("text"))
        assertEquals("Creep", b.getJSONObject("currentSong").getString("title"))
        assertEquals("PHONE", b.getString("surface")); assertEquals(21, b.getInt("hour"))
        assertEquals(setOf("text", "currentSong", "surface", "hour"), b.keys().asSequence().toSet())
        assertEquals(300, JSONObject(DjAskAnswer.body("x".repeat(500), null, null, DjSurface.AUTO, 0)).getString("text").length)
    }
}

/** The car's voice search goes to the DJ's request reader. */
class DjAskCarTest {
    @Test fun aCarRequestRoundTripsAndReadsAsARequest() {
        assertEquals("my pop playlist", BrowseTree.parseAsk(BrowseTree.askId("my pop playlist")))
        assertNull(BrowseTree.parseAsk("song:abc"))
        assertEquals("play my pop playlist", BrowseTree.askWords("my pop playlist"))
        assertEquals("play Smack That", BrowseTree.askWords(" Smack That "))
        assertEquals("shuffle my library", BrowseTree.askWords("shuffle my library"))
        assertEquals("next", BrowseTree.askWords("next"))
        assertEquals("play nextdoor", BrowseTree.askWords("nextdoor"))
    }
}

/** The DJ button's settings link leaves Now Playing first (it opened Settings underneath the player on the web). */
class DjSettingsLinkTest {
    @Test fun settingsOpenOverNowPlayingClosed() {
        val nav = com.ipodemu.player.PlayerNav()
        nav.nowPlaying = true
        com.ipodemu.player.openDjSettings(nav)
        assertFalse(nav.nowPlaying)
        assertEquals("dj", nav.settingsSection)
        assertEquals(com.ipodemu.player.Screen.Settings, nav.top)
    }
}
