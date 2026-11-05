package com.ipodemu.library

import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** F3: the phone opens from its saved library before any network, and the saved cover tiles stay under a size cap. */
@OptIn(ExperimentalCoroutinesApi::class)
class LibraryStoreTest {
    @Test fun theSavedLibraryIsShownBeforeTheNetworkIsAsked() = runTest {
        val log = ArrayList<String>()
        LibraryOpen.run(
            loadSaved = { log += "load saved" },
            shown = { log += "shown" },
            refresh = { log += "network" },
        )
        assertEquals(listOf("load saved", "shown", "network"), log)
    }

    @Test fun aNetworkThatFailsStillLeavesTheSavedLibraryShown() = runTest {
        var shown = false
        try {
            LibraryOpen.run(loadSaved = {}, shown = { shown = true }, refresh = { throw java.io.IOException("offline") })
        } catch (_: java.io.IOException) {}
        assertTrue("with the network off the saved library is already up", shown)
    }

    private fun tile(dir: File, name: String, bytes: Int, age: Long): File =
        File(dir, name).also { it.writeBytes(ByteArray(bytes)); it.setLastModified(1_000_000L - age) }

    @Test fun tilesOverTheCapAreRemovedOldestFirst() {
        val dir = createTempDir()
        try {
            val old = tile(dir, "a.r", 40, 300); val mid = tile(dir, "b.r", 40, 200); val fresh = tile(dir, "c.r", 40, 100)
            val gone = TileCap.trim(listOf(fresh, old, mid), 90)
            assertEquals(listOf(old), gone)
            assertFalse(old.exists()); assertTrue(mid.exists()); assertTrue(fresh.exists())
        } finally { dir.deleteRecursively() }
    }

    @Test fun tilesUnderTheCapAreLeftAlone() {
        val dir = createTempDir()
        try {
            val a = tile(dir, "a.r", 40, 300); val b = tile(dir, "b.r", 40, 200)
            assertTrue(TileCap.trim(listOf(a, b), 100).isEmpty()); assertTrue(a.exists() && b.exists())
        } finally { dir.deleteRecursively() }
    }

    @Test fun theDefaultCapIs100Megabytes() = assertEquals(100L * 1024 * 1024, TileCap.DEFAULT_BYTES)
}
