package com.ipodemu.library

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** What Jellyfin's song listing says (real response shapes) becomes tracks, stream facts and cover identities. */
class JellyfinListingTest {
    private val base = "http://jf:8096"
    private val SONG = "a".repeat(31) + "1"; private val SONG2 = "a".repeat(31) + "2"; private val ALBUM = "b".repeat(31) + "1"
    private fun item(json: String) = JSONObject(json)
    private fun list(vararg json: String) = JSONArray("[" + json.joinToString(",") + "]")

    private val flac24 = """{"Name":"Song One","Id":"${SONG}","AlbumId":"${ALBUM}","Album":"Album","AlbumArtist":"Artist","Artists":["Artist"],
        "RunTimeTicks":2400000000,"DateCreated":"2026-09-28T17:58:38.7227291Z","DateLastSaved":"2026-10-01T12:00:00.1234567Z","Path":"/music/Artist/Album/01.flac",
        "ImageTags":{"Primary":"songtag1"},"AlbumPrimaryImageTag":"albumtag1","Type":"Audio",
        "MediaSources":[{"Protocol":"File","Container":"flac","Size":123456789,"Bitrate":2300000,"MediaStreams":[
            {"Type":"Audio","Codec":"flac","SampleRate":96000,"BitDepth":24,"BitRate":2200000,"Channels":2,"Index":0}]}]}"""

    // ---- stream facts -------------------------------------------------------------------------------------------

    @Test fun flac24bit96kHzIsHiRes() {
        val p = JellyfinFacts.parse(item(flac24))
        val f = p.fact!!
        assertEquals("flac", f.codec); assertEquals(96000, f.rate); assertEquals(24, f.depth); assertEquals(2200, f.kbps)
        assertTrue(f.lossless); assertTrue(f.hiRes)
        assertEquals(123456789L, p.size)
    }

    @Test fun cdFlacIsNotHiResEvenAtAHighBitRate() {
        val o = item("""{"Id":"x","MediaSources":[{"Size":50000000,"MediaStreams":[{"Type":"Audio","Codec":"FLAC","SampleRate":44100,"BitDepth":16,"BitRate":1900000}]}]}""")
        val f = JellyfinFacts.parse(o).fact!!
        assertEquals("flac", f.codec)   // case-folded
        assertFalse(f.hiRes); assertTrue(f.lossless)
    }

    @Test fun aMissingBitDepthIsUnknownNotGuessed() {
        // Jellyfin leaves BitDepth out for some files: 0 = unknown, and unknown never makes a file Hi-Res
        val o = item("""{"Id":"x","MediaSources":[{"MediaStreams":[{"Type":"Audio","Codec":"flac","SampleRate":44100,"BitRate":1200000}]}]}""")
        val f = JellyfinFacts.parse(o).fact!!
        assertEquals(0, f.depth); assertEquals(44100, f.rate)
        assertFalse(f.hiRes)
        // ... but a rate above 44.1 kHz alone is enough (the one rule)
        val hi = JellyfinFacts.parse(item("""{"Id":"x","MediaSources":[{"MediaStreams":[{"Type":"Audio","Codec":"flac","SampleRate":48000}]}]}""")).fact!!
        assertTrue(hi.hiRes)
    }

    @Test fun mp3IsLossyWhateverItsRate() {
        val o = item("""{"Id":"x","MediaSources":[{"Container":"mp3","Size":9000000,"Bitrate":320000,"MediaStreams":[
            {"Type":"Audio","Codec":"mp3","SampleRate":48000,"BitRate":320000,"Channels":2}]}]}""")
        val f = JellyfinFacts.parse(o).fact!!
        assertEquals("mp3", f.codec); assertEquals(320, f.kbps); assertEquals(0, f.depth)
        assertFalse(f.lossless); assertFalse(f.hiRes)
    }

    @Test fun alacInAnM4aIsLossless() {
        val o = item("""{"Id":"x","MediaSources":[{"Container":"m4a","Size":30000000,"MediaStreams":[
            {"Type":"Video","Codec":"mjpeg","Width":600,"Height":600},
            {"Type":"Audio","Codec":"alac","SampleRate":96000,"BitDepth":24,"BitRate":2500000}]}]}""")
        val f = JellyfinFacts.parse(o).fact!!
        assertEquals("alac", f.codec)
        assertTrue("the audio stream is found after a cover-art video stream", f.rate == 96000)
        assertTrue(f.lossless); assertTrue(f.hiRes)
    }

    @Test fun streamsAtTheTopOfTheItemAreReadToo() {
        // Fields=MediaStreams puts them on the item itself
        val o = item("""{"Id":"x","MediaSources":[{"Size":1000,"Bitrate":900000}],"MediaStreams":[{"Type":"Audio","Codec":"flac","SampleRate":44100,"BitDepth":16}]}""")
        val p = JellyfinFacts.parse(o)
        assertEquals("flac", p.fact!!.codec)
        assertEquals("the bit rate falls back to the source's", 900, p.fact!!.kbps)
    }

    @Test fun anItemWithoutStreamsHasNoFactsAndNeverAHiResBadge() {
        val none = JellyfinFacts.parse(item("""{"Id":"x","MediaSources":[{"Size":1000}]}"""))
        assertNull(none.fact); assertEquals(1000L, none.size)
        assertNull(JellyfinFacts.parse(item("""{"Id":"x"}""")).fact)
        assertNull(JellyfinFacts.parse(item("""{"Id":"x","MediaSources":[{"MediaStreams":[{"Type":"Audio"}]}]}""")).fact)
    }

    @Test fun theListingAsksForTheStreams() {
        val f = JellyfinFacts.FIELDS.split(',')
        assertTrue("MediaSources" in f); assertTrue("MediaStreams" in f); assertTrue("DateLastSaved" in f)
        val u = JellyfinDirectClient.pageUrl("http://jf:8096/", "uid", 500, 250, "2026-10-01T12:00:00.1234567Z")
        assertTrue(u.startsWith("http://jf:8096/Users/uid/Items?IncludeItemTypes=Audio&Recursive=true"))
        assertTrue(u.contains("Fields=Path,DateCreated,Genres,MediaSources,MediaStreams,DateLastSaved"))
        assertTrue(u.contains("StartIndex=500&Limit=250"))
        assertTrue("the cursor is URL-encoded", u.endsWith("&MinDateLastSaved=2026-10-01T12%3A00%3A00.1234567Z"))
        assertFalse(JellyfinDirectClient.pageUrl("http://jf", "uid", 0, 500).contains("MinDateLastSaved"))
        assertFalse("no image sizes in a listing", u.contains("maxWidth", ignoreCase = true))
    }

    // ---- tracks -------------------------------------------------------------------------------------------------

    @Test fun aTrackStreamsDirectlyAndKeepsItsFacts() {
        val it = JellyfinDirectClient.itemsOf(base, list(flac24)).single()
        assertEquals("$base/Audio/${SONG}/stream?static=true", it.track.path)
        assertEquals("Song One", it.track.title); assertEquals("Artist", it.track.artist); assertEquals(240_000L, it.track.durationMs)
        assertEquals(TrackSource.JELLYFIN, it.track.source)
        assertEquals("/music/Artist/Album/01.flac", it.track.filePath)
        assertEquals(96000, it.fact!!.rate)
        assertEquals("the delta cursor is normalised so dates compare as strings", "2026-10-01T12:00:00.1234567Z", it.savedAt)
    }

    @Test fun normalisedDatesCompareAsText() {
        assertEquals("2026-10-01T12:00:00.1200000Z", normIso("2026-10-01T12:00:00.12Z"))
        assertEquals("2026-10-01T12:00:00.0000000Z", normIso("2026-10-01T12:00:00Z"))
        assertEquals("2026-10-01T12:00:00.1234567Z", normIso("2026-10-01T12:00:00.123456789Z"))
        assertEquals("", normIso(""))
        assertTrue(normIso("2026-10-01T12:00:00.5Z") > normIso("2026-10-01T12:00:00.25Z"))
    }

    // ---- cover identity -----------------------------------------------------------------------------------------

    @Test fun songsOfOneAlbumShareOneCover() {
        val second = flac24.replace("${SONG}", "${SONG2}").replace("songtag1", "songtag2").replace("Song One", "Song Two")
        val items = JellyfinDirectClient.itemsOf(base, list(flac24, second))
        assertEquals("jfbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb1", items[0].track.artKey)
        assertEquals(items[0].track.artKey, items[1].track.artKey)
        assertEquals("albumtag1", items[0].coverTag); assertEquals("albumtag1", items[1].coverTag)
        assertEquals("one request, one file", 1, items.map { it.coverKey to it.coverTag }.toSet().size)
    }

    @Test fun aCompilationKeepsEachSongsOwnPicture() {
        for (name in listOf("Various Artists", "various", "VA", "V.A.", "Compilations")) {
            val va = flac24.replace("\"AlbumArtist\":\"Artist\"", "\"AlbumArtist\":\"$name\"")
            val it = JellyfinDirectClient.itemsOf(base, list(va)).single()
            assertEquals(name, "jfaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1", it.track.artKey)
            assertEquals("songtag1", it.coverTag)
        }
    }

    @Test fun withoutAnAlbumPictureTheSongsOwnIsUsed() {
        val o = flac24.replace(",\"AlbumPrimaryImageTag\":\"albumtag1\"", "")
        val it = JellyfinDirectClient.itemsOf(base, list(o)).single()
        assertEquals("jfaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1", it.track.artKey)
        assertEquals("songtag1", it.coverTag)
    }

    @Test fun aSongWithNoPictureAnywhereStillPointsAtItsAlbumWithoutATag() {
        val o = flac24.replace(",\"AlbumPrimaryImageTag\":\"albumtag1\"", "").replace("\"ImageTags\":{\"Primary\":\"songtag1\"},", "\"ImageTags\":{},")
        val it = JellyfinDirectClient.itemsOf(base, list(o)).single()
        assertEquals("jfbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb1", it.track.artKey)
        assertNull(it.coverTag)
        val bare = item("""{"Id":"cc","Name":"X"}""")
        assertNull(artKeyOf(bare)); assertNull(coverTagOf(bare))
    }

    @Test fun aSessionsNowPlayingItemUsesTheSameRule() {
        // the same file is shared between the library and the remote screen
        assertNotNull(artKeyOf(item(flac24)))
        assertEquals(JellyfinDirectClient.itemsOf(base, list(flac24)).single().track.artKey, artKeyOf(item(flac24)))
    }
}
