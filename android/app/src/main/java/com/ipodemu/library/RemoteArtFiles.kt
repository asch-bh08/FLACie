package com.ipodemu.library

import java.io.File

/**
 * Catalogue covers (Deezer's, iTunes', a playlist's picture) kept on disk by their address, so a search shows them at once the next time, after a
 * restart too (owner, 6 Nov: covers came slowly in search). Up to [cap] bytes; the least recently used go first.
 */
class RemoteArtFiles(private val dir: File, private val cap: Long = 40L * 1024 * 1024) {
    fun file(url: String): File {
        val md = java.security.MessageDigest.getInstance("SHA-1").digest(url.toByteArray())
        return File(dir, md.joinToString("") { "%02x".format(it) } + ".img")
    }

    fun get(url: String): ByteArray? {
        val f = file(url)
        if (!f.exists()) return null
        return try { f.readBytes().also { f.setLastModified(System.currentTimeMillis()) } } catch (_: Exception) { null }
    }

    private var sinceTrim = 0

    fun put(url: String, bytes: ByteArray) {
        dir.mkdirs()
        try { CoverFiles.saveAtomically(file(url), bytes) } catch (_: Exception) { return }
        if (++sinceTrim >= 50) { sinceTrim = 0; trim() }
    }

    fun trim() {
        val all = dir.listFiles()?.filter { it.isFile } ?: return
        var total = all.sumOf { it.length() }
        if (total <= cap) return
        for (f in all.sortedBy { it.lastModified() }) {
            if (total <= cap) break
            total -= f.length(); f.delete()
        }
    }
}
