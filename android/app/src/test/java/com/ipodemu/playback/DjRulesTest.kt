package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The Kotlin DjRules must behave like DjRules.EffectiveOn in DjContracts.cs (the server, the web and Windows use that one). */
class DjRulesTest {
    private fun on(s: DjSettings, surface: DjSurface = DjSurface.PHONE, playlist: String? = null, queue: Boolean? = null) = DjRules.effectiveOn(s, surface, playlist, queue)

    @Test fun theQueueSwitchWinsOverEverything() {
        val off = DjSettings(enabled = false, playlists = mapOf("p" to false))
        assertTrue(on(off, playlist = "p", queue = true))
        val onEverywhere = DjSettings(enabled = true, playlists = mapOf("p" to true))
        assertFalse(on(onEverywhere, playlist = "p", queue = false))
        assertFalse("even where the Where tick is on", on(DjSettings(enabled = true), DjSurface.AUTO, queue = false))
    }

    @Test fun thePlaylistSwitchWinsOverOnOffAndWhere() {
        assertTrue("just this playlist: DJ off, the playlist on", on(DjSettings(enabled = false, playlists = mapOf("p" to true)), playlist = "p"))
        assertFalse("a quiet bedtime list", on(DjSettings(enabled = true, playlists = mapOf("p" to false)), playlist = "p"))
        assertTrue("even with this device's tick off", on(DjSettings(enabled = true, phone = false, playlists = mapOf("p" to true)), DjSurface.PHONE, "p"))
    }

    @Test fun noPlaylistEntryFollowsOnOffWithTheWhereTick() {
        val s = DjSettings(enabled = true, playlists = mapOf("other" to false))
        assertTrue(on(s, playlist = "p")); assertTrue(on(s, playlist = null)); assertTrue("an empty id is no playlist", on(s, playlist = ""))
        assertFalse("DJ off", on(DjSettings(enabled = false), playlist = "p"))
        assertFalse("an empty id never matches an entry", on(DjSettings(enabled = false, playlists = mapOf("" to true)), playlist = ""))
    }

    @Test fun onlyInTheCarIsOnWithOnlyAndroidAutoTicked() {
        val car = DjSettings(enabled = true, phone = false, auto = true, web = false, windows = false)
        assertTrue(on(car, DjSurface.AUTO)); assertFalse(on(car, DjSurface.PHONE)); assertFalse(on(car, DjSurface.WEB)); assertFalse(on(car, DjSurface.WINDOWS))
        assertFalse("ticks alone do nothing while the DJ is off", on(car.copy(enabled = false), DjSurface.AUTO))
    }

    @Test fun defaultsMatchTheContract() {
        val d = DjSettings()
        assertFalse(d.enabled); assertEquals(DjChatty.OFTEN, d.chatty); assertTrue(d.backup); assertFalse(d.voiceReady)
        for (s in DjSurface.values()) assertTrue(d.on(s))
        assertFalse("the DJ is off until it is switched on", on(d))
    }

    @Test fun settingsReadTheServersShape() {
        val s = DjSettings.parse("""{"enabled":true,"chatty":"rare","phone":false,"auto":true,"web":false,"windows":true,"backup":false,"playlists":{"jf1":true,"local2":false,"bad":null},"voiceReady":true}""")
        assertEquals(DjSettings(true, DjChatty.RARE, false, true, false, true, false, mapOf("jf1" to true, "local2" to false), true), s)
        assertEquals("a field left out keeps its default", DjSettings(enabled = true), DjSettings.parse("""{"enabled":true}"""))
        assertEquals("written and read back the same", s, DjSettings.parse(s.toJson().toString()))
        assertEquals("nothing cached: the defaults", DjSettings(), DjSettings.parseOrDefault(""))
        assertEquals("a broken cache: the defaults", DjSettings(), DjSettings.parseOrDefault("{not json"))
        assertEquals("often", DjSettings().toJson().getString("chatty"))
        assertTrue("Talk after skips is on unless the server says off", DjSettings().talkAfterSkips)
        val quiet = DjSettings.parse("""{"enabled":true,"talkAfterSkips":false}""")
        assertFalse(quiet.talkAfterSkips)
        assertEquals("written and read back the same", quiet, DjSettings.parse(quiet.toJson().toString()))
    }

    @Test fun theStyleIsClassicUnlessTheServerSaysOtherwise() {
        assertEquals(DjStyle.CLASSIC, DjSettings().style)
        assertEquals(DjStyle.CLASSIC, DjSettings.parse("""{"enabled":true}""").style)
        assertEquals(DjStyle.CHILL, DjSettings.parse("""{"style":"chill"}""").style)
        val hype = DjSettings.parse("""{"style":"HYPE"}""")
        assertEquals(DjStyle.HYPE, hype.style)
        assertEquals("hype", hype.toJson().getString("style"))
        assertEquals(DjStyle.CLASSIC, DjSettings.parse("""{"style":"loud"}""").style)
        assertEquals("a tap moves on, round to the start", listOf(DjStyle.CHILL, DjStyle.HYPE, DjStyle.CLASSIC), DjStyle.entries.map { it.next() })
    }

    @Test fun aCueIsReadOnlyWhenThereIsSomethingToSay() {
        assertNull(DjCue.parse("""{"speak":false}"""))
        val clip = DjCue.parse("""{"speak":true,"text":"Next, nineteen eighty-four","caption":"Next, 1984","clip":"/api/dj/clip/ab12.mp3","backup":false}""")
        assertEquals(DjCue("Next, nineteen eighty-four", "Next, 1984", "/api/dj/clip/ab12.mp3", false), clip)
        val backup = DjCue.parse("""{"speak":true,"text":"Here's Adele","caption":"Here's Adele","clip":null,"backup":true}""")
        assertNotNull(backup); assertNull("no clip", backup!!.clip); assertTrue(backup.backup)
        assertNull("no clip and no backup voice: nothing to say", DjCue.parse("""{"speak":true,"text":"Hi","caption":"Hi","clip":null,"backup":false}"""))
        assertNull("not JSON", DjCue.parse("<html>502</html>"))
        assertNull(DjCue.parse(""))
    }
}
