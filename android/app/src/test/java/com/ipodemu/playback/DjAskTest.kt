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

    /** A player that logs every step, with the F9 ones too. */
    private class Steps : DjAskPlayer<String> {
        val log = mutableListOf<String>()
        override fun find(s: DjAskSong) = s.path.takeIf { it.startsWith("/") }
        override fun play(list: List<String>, title: String, shuffle: Boolean) { log += "play $title $list $shuffle" }
        override fun upNext(list: List<String>, replace: Boolean) { log += "upnext $list" }
        override fun next() { log += "next" }
        override fun pause() { log += "pause" }
        override fun resume() { log += "resume" }
        override fun nudgeVolume(delta: Float) { log += "volume $delta" }
        override fun setDj(on: Boolean) { log += "dj $on" }
        override fun addToPlaylist(name: String, list: List<String>) { log += "add $name $list" }
        override fun removeFromPlaylist(name: String, list: List<String>) { log += "remove $name $list" }
        override fun createPlaylist(name: String, list: List<String>) { log += "create $name $list" }
        override fun renamePlaylist(name: String, to: String) { log += "rename $name $to" }
        override fun ask(question: String) { log += "ask $question" }
        override fun setShuffle(on: Boolean) { log += "shuffle $on" }
    }

    @Test fun stepsRunInOrder() {
        val p = Steps()
        val a = DjAskAnswer.parse("""{"do":"none","say":"Done.","steps":[
            {"do":"addtoplaylist","playlist":"Pop","songs":[{"path":"/a"},{"path":"/b"}]},
            {"do":"upnext","songs":[{"path":"/a"}],"replace":true},
            {"do":"shuffle","shuffle":true},{"do":"next"},{"do":"pause"},{"do":"resume"},{"do":"volumeup"},{"do":"volumedown"},{"do":"djon"},{"do":"djoff"},
            {"do":"removefromplaylist","playlist":"Pop","songs":[{"path":"/b"}]},
            {"do":"createplaylist","playlist":"Backroads","songs":[{"path":"/a"}]},
            {"do":"play","title":"Backroads","songs":[{"path":"/a"}]},
            {"do":"renameplaylist","playlist":"Backroads","title":"Country Roads"},
            {"do":"ask","say":"Are you sure?"}]}""")
        assertTrue(DjAskRunner.run(p, a))
        assertEquals(listOf("add Pop [/a, /b]", "upnext [/a]", "shuffle true", "next", "pause", "resume", "volume 0.15", "volume -0.15", "dj true", "dj false",
            "remove Pop [/b]", "create Backroads [/a]", "play Backroads [/a] false", "rename Backroads Country Roads", "ask Are you sure?"), p.log)
        assertTrue("an ask step is a question back", a.asked)
        assertEquals(15, a.allSteps.size)
    }

    @Test fun noStepsIsTheOldAnswerAsItsOneStep() {
        val p = Steps()
        val a = DjAskAnswer.parse("""{"do":"play","say":"x","paths":["/a"],"title":"Pop","shuffle":true}""")
        assertEquals(1, a.allSteps.size)
        DjAskRunner.run(p, a)
        assertEquals(listOf("play Pop [/a] true"), p.log)
        assertTrue(DjAskAnswer.parse("""{"do":"none","say":"x"}""").allSteps.isEmpty())
    }

    @Test fun stepsThatCannotRunAreSkippedAndTheRestGoOn() {
        val p = Steps()
        val a = DjAskAnswer.parse("""{"do":"none","say":"x","steps":[{"do":"addtoplaylist","playlist":"Pop","songs":[{"path":"gone"}]},{"do":"addtoplaylist","songs":[{"path":"/a"}]},
            {"do":"renameplaylist","playlist":"Pop"},{"do":"ask"},{"do":"createplaylist","playlist":"Empty"},{"do":"next"}]}""")
        assertTrue(DjAskRunner.run(p, a))
        assertEquals(listOf("create Empty []", "next"), p.log)
        assertFalse(DjAskAnswer.parse("""{"do":"none","say":"x","steps":[{"do":"ask"}]}""").asked)
    }

    @Test fun anAnswerOfStepsLeavesTheConversationToTheMainStep() {
        val a = DjAskAnswer.parse("""{"do":"none","say":"Done","steps":[{"do":"createplaylist","playlist":"B"},{"do":"play","songs":[{"path":"/a"}]}]}""")
        assertEquals(DjAskDo.PLAY, a.mainDo)
        assertEquals(DjTurn.END_MUSIC_CHOSEN, DjConversation.after(a.mainDo, a.asked, 1, a.intent))
        val q = DjAskAnswer.parse("""{"do":"none","say":"Are you sure?","intent":"Clarify","steps":[{"do":"ask","say":"Are you sure?"}]}""")
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(q.mainDo, q.asked, 1, q.intent))
        // a question back wins over a song that starts: the mic still opens for the reply
        val both = DjAskAnswer.parse("""{"do":"none","say":"Made Backroads. Call it something else?","intent":"Clarify","steps":[{"do":"play","songs":[{"path":"/a"}]},{"do":"ask","say":"x"}]}""")
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(both.mainDo, both.asked, 1, both.intent))
    }
}
