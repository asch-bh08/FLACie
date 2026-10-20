package com.ipodemu.player

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The seek bar's shape from a FLAC file's frame sizes (FlacWave, the port of the server's WaveShape.cs): the phone streams straight from Jellyfin,
 * so it works the shape out from the bytes it downloads to play the song, piece by piece as they arrive.
 */
class FlacWaveTest {
    private fun fixture(name: String) = (javaClass.getResourceAsStream("/tags/$name") ?: error("missing fixture $name")).use { it.readBytes() }

    @Test fun aQuietFirstHalfAndALoudSecondHalfShowAsLowThenHighBars() {
        val bars = FlacWave.fromFlac(fixture("quiet-then-loud.flac"))
        assertNotNull(bars); bars!!
        assertEquals(200, bars.size)
        assertTrue(bars.all { it in 0..255 })
        val first = bars.take(90).average(); val second = bars.drop(110).average()
        assertTrue("quiet half $first, loud half $second", second > first + 100)
    }

    /** The server's numbers for the same file (WaveShape.FromFlac, run once and written down here), so the two never drift apart. */
    @Test fun theSameNumbersAsTheServer() {
        val bars = FlacWave.fromFlac(fixture("quiet-then-loud.flac"))!!
        assertArrayEquals(ServerShapes.QUIET_THEN_LOUD, bars)
    }

    @Test fun theTagFixturesHaveAShapeOfTwoHundredBars() {
        for (name in listOf("flac_art.flac", "flac_hires.flac")) {
            val bars = FlacWave.fromFlac(fixture(name))
            assertNotNull(name, bars)
            assertEquals(name, 200, bars!!.size)
            assertTrue(name, bars.all { it in 0..255 })
        }
    }

    @Test fun anythingThatIsNotFlacHasNoShape() {
        assertNull(FlacWave.fromFlac(byteArrayOf('I'.code.toByte(), 'D'.code.toByte(), '3'.code.toByte(), 3, 0, 0, 0, 0, 0, 0)))
        assertNull(FlacWave.fromFlac(ByteArray(0)))
        assertNull(FlacWave.fromFlac(fixture("mp3_v1.mp3")))
        val b = FlacWave.Builder(); b.feed(fixture("mp3_v1.mp3"))
        assertTrue(b.notFlac); assertNull(b.finish())
    }

    @Test fun fedInSmallPiecesTheBuilderGivesExactlyTheWholeFileShape() {
        for (name in listOf("quiet-then-loud.flac", "flac_art.flac", "flac_hires.flac")) {
            val f = fixture(name)
            for (piece in listOf(1, 7, 16, 17, 333, 4096, f.size)) {
                val b = FlacWave.Builder()
                var at = 0
                while (at < f.size) { val n = minOf(piece, f.size - at); b.feed(f, at, n); at += n }
                assertArrayEquals("$name in pieces of $piece", FlacWave.fromFlac(f), b.finish())
            }
        }
    }

    @Test fun halfTheFileShowsTheFirstHalfAndLeavesTheRestUnknown() {
        val f = fixture("quiet-then-loud.flac")
        val b = FlacWave.Builder()
        b.feed(f, 0, f.size * 3 / 4)
        val part = b.partial()
        assertNotNull(part); part!!
        assertEquals(200, part.size)
        val known = part.count { it != FlacWave.UNKNOWN }
        // about three quarters of the song is known (the audio after the metadata), and the rest waits at the resting level
        assertTrue("known $known", known in 120..170)
        assertTrue(part.drop(known).all { it == FlacWave.UNKNOWN })
        // the quiet start is low and the loud part already high
        assertTrue(part.take(80).average() + 100 < part.slice(110 until known).average())
    }

    @Test fun nothingIsShownBeforeTheFirstFramesArrive() {
        val f = fixture("quiet-then-loud.flac")
        val b = FlacWave.Builder()
        b.feed(f, 0, 40)
        assertNull(b.partial())
    }

    @Test fun shapesAreKeptByJellyfinItemIdWhicheverAddressTheSongCameFrom() {
        val id = "0123456789abcdef0123456789ABCDEF"
        assertEquals(WaveCache.idKey("http://jf:8096/Audio/$id/stream?static=true"), WaveCache.idKey("https://flacie.example/Audio/${id.lowercase()}/stream?static=true"))
        val dir = kotlin.io.path.createTempDirectory("waves").toFile()
        try {
            val rough = IntArray(200) { it }
            WaveCache.write(dir, "http://a/Audio/$id/stream", rough, rough = true)
            assertArrayEquals(rough, WaveCache.read(dir, "http://b/Audio/$id/stream?static=true", rough = true))
            assertNull("a rough shape is not taken for a decoded one", WaveCache.read(dir, "http://a/Audio/$id/stream"))
        } finally { dir.deleteRecursively() }
    }

    @Test fun unknownBarsRestAtTheLevelHeight() {
        val shape = IntArray(200) { if (it < 100) it else FlacWave.UNKNOWN }
        val h = WaveBars.heights(shape, 100)
        assertTrue(h.drop(50).all { it == .4f })
        assertTrue(h[49] > h[0])
    }
}
