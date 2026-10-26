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

    // ---- one voice, the DJ's volume and the music under it, the choice per song (docs/dj-voice-volume-report.md) ----

    @Test fun deviceVoiceVolumeDuckAndSongsAreReadWrittenAndClamped() {
        val d = DjSettings()
        assertFalse("only the server's voice unless the person picks the phone's", d.deviceVoice)
        assertEquals(90, d.volume); assertEquals(30, d.duck); assertTrue(d.songs.isEmpty())
        val s = DjSettings(deviceVoice = true, volume = 60, duck = 15, songs = mapOf("yellow|coldplay" to true, "intro|xx" to false))
        assertEquals(s, DjSettings.parse(s.toJson().toString()))
        assertEquals("always", s.toJson().getJSONObject("songs").getString("yellow|coldplay"))
        val odd = DjSettings.parse("""{ "volume": 400, "duck": -3, "songs": { "a|b": "never", "c|d": "maybe", "e|f": true } }""")
        assertEquals(100, odd.volume); assertEquals(0, odd.duck); assertEquals(mapOf("a|b" to false), odd.songs)
        assertEquals(0.6f, s.voiceLevel, 0.0001f); assertEquals(0.15f, s.duckLevel, 0.0001f)
    }

    @Test fun theSongChoiceIsKeyedByTitleAndArtistAndOffersThreeChoicesWithTheCurrentOneMarked() {
        val key = DjRules.songKey("Yellow", "Coldplay")
        assertEquals(com.ipodemu.library.matchKey("Yellow", "Coldplay"), key)
        assertEquals("", DjRules.songKey(" ", "Coldplay"))
        var s = DjRules.withSong(DjSettings(), "Yellow", "Coldplay", true)
        assertEquals(true, DjRules.songChoice(s, "yellow", "COLDPLAY"))
        assertEquals(listOf("DJ: always introduce this song", "DJ: never talk over this song", "DJ: default"), DjRules.songMenu(s, "Yellow", "Coldplay").map { it.first })
        assertEquals(listOf(true, false, false), DjRules.songMenu(s, "Yellow", "Coldplay").map { it.third })
        s = DjRules.withSong(s, "Yellow", "Coldplay", false)
        assertEquals(listOf(false, true, false), DjRules.songMenu(s, "Yellow", "Coldplay").map { it.third })
        s = DjRules.withSong(s, "Yellow", "Coldplay", null)
        assertNull(DjRules.songChoice(s, "Yellow", "Coldplay"))
        assertEquals(listOf(false, false, true), DjRules.songMenu(s, "Yellow", "Coldplay").map { it.third })
        val p = DjRules.songPatch("Yellow", "Coldplay", false).getJSONObject("song")
        assertEquals("Yellow", p.getString("title")); assertEquals("Coldplay", p.getString("artist")); assertEquals("never", p.getString("choice"))
        assertTrue(DjRules.songPatch("Yellow", "Coldplay", null).getJSONObject("song").isNull("choice"))
    }

    @Test fun theMusicDucksToThePersonsLevel() {
        val r = DuckRamp()
        r.low = 0.15f
        r.duck(0L)
        assertEquals(0.15f, r.level(10_000L), 0.0001f)
        r.release(10_000L)
        assertEquals(1f, r.level(20_000L), 0.0001f)
        r.low = 1f   // "Music under the DJ" at 100 %: nothing ducks, and the ramp still ends
        r.duck(30_000L)
        assertEquals(1f, r.level(40_000L), 0.0001f)
    }

    @Test fun aCueWithAClipIsNeverMarkedForThePhonesVoiceUnlessItHasNoClip() {
        // the server sends backup = true only when the person chose the device voice, and then with no clip
        val device = DjCue.parse("""{ "speak": true, "text": "Here's Yellow.", "caption": "Here's Yellow.", "clip": null, "backup": true }""")
        assertNotNull(device); assertNull(device!!.clip); assertTrue(device.backup)
        assertNull("no clip and not the device voice: nothing to say", DjCue.parse("""{ "speak": true, "text": "Here's Yellow.", "caption": "", "clip": null, "backup": false }"""))
    }
}
