package com.ipodemu.library

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Search's catalogue covers kept on disk (owner, 6 Nov: covers came slowly in search; every search fetched them again). */
class RemoteArtFilesTest {
    private fun dir() = File(System.getProperty("java.io.tmpdir"), "remote-art-" + System.nanoTime()).apply { mkdirs() }

    @Test fun aPictureFetchedOnceIsReadFromDiskAfter() {
        val f = RemoteArtFiles(dir(), cap = 1_000_000)
        assertNull(f.get("https://e-cdns-images.dzcdn.net/a.jpg"))
        f.put("https://e-cdns-images.dzcdn.net/a.jpg", byteArrayOf(1, 2, 3))
        assertArrayEquals(byteArrayOf(1, 2, 3), f.get("https://e-cdns-images.dzcdn.net/a.jpg"))
        assertNotEquals(f.file("https://x/a.jpg").name, f.file("https://x/b.jpg").name)
    }

    @Test fun theOldestGoWhenTheCacheIsFull() {
        val d = dir()
        val f = RemoteArtFiles(d, cap = 25)
        for (i in 0 until 5) { f.put("https://x/$i.jpg", ByteArray(10)); f.file("https://x/$i.jpg").setLastModified(1_000_000L * (i + 1)) }
        f.trim()
        assertTrue(d.listFiles()!!.sumOf { it.length() } <= 25)
        assertNull(f.get("https://x/0.jpg"))
        assertEquals(10, f.get("https://x/4.jpg")!!.size)
    }
}
