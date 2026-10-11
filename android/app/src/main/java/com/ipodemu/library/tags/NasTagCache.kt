package com.ipodemu.library.tags

import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.File

/** What was learned about one NAS file, kept on disk so a rescan only reads new or changed files. No image bytes (those live in ArtCache). */
class CachedTags(
    val size: Long,
    val mtime: Long,
    /** False when the file was read but nothing usable was in it (not read again until it changes). */
    val ok: Boolean,
    val codec: String = "",
    val title: String = "",
    val artist: String = "",
    val albumArtist: String = "",
    val album: String = "",
    val genre: String = "",
    val trackNo: Int = 0,
    val discNo: Int = 0,
    val year: Int = 0,
    val durationMs: Long = 0,
    val sampleRate: Int = 0,
    val bitDepth: Int = 0,
    val bitrateKbps: Int = 0,
    val channels: Int = 0,
    /** The file has an embedded cover (stored under the album's art key by the scan). */
    val hasArt: Boolean = false,
) {
    companion object {
        fun of(size: Long, mtime: Long, t: AudioTags?, hasArt: Boolean) =
            if (t == null) CachedTags(size, mtime, false)
            else CachedTags(size, mtime, true, t.codec, t.title, t.artist, t.albumArtist, t.album, t.genre, t.trackNo, t.discNo, t.year,
                t.durationMs, t.sampleRate, t.bitDepth, t.bitrateKbps, t.channels, hasArt)
    }
}

/** Tags by file path, valid while the file's size and modified time are unchanged. Thread safe; saved atomically. */
class NasTagCache(private val file: File?) {
    private val map = HashMap<String, CachedTags>()
    @Volatile private var dirty = false

    @Synchronized fun load() {
        map.clear()
        val f = file ?: return
        try {
            if (!f.exists()) return
            DataInputStream(BufferedInputStream(f.inputStream(), 1 shl 16)).use { d ->
                if (d.readInt() != MAGIC || d.readInt() != VERSION) return
                val n = d.readInt()
                for (i in 0 until n) {
                    val path = d.readUTF()
                    val size = d.readLong(); val mtime = d.readLong()
                    val ok = d.readBoolean()
                    val codec = d.readUTF(); val title = d.readUTF(); val artist = d.readUTF(); val albumArtist = d.readUTF()
                    val album = d.readUTF(); val genre = d.readUTF()
                    val trackNo = d.readInt(); val discNo = d.readInt(); val year = d.readInt()
                    val dur = d.readLong()
                    val sr = d.readInt(); val bd = d.readInt(); val br = d.readInt(); val ch = d.readInt()
                    val art = d.readBoolean()
                    map[path] = CachedTags(size, mtime, ok, codec, title, artist, albumArtist, album, genre, trackNo, discNo, year, dur, sr, bd, br, ch, art)
                }
            }
        } catch (_: Exception) { map.clear() }
    }

    @Synchronized fun get(path: String, size: Long, mtime: Long): CachedTags? =
        map[path]?.takeIf { it.size == size && it.mtime == mtime }

    @Synchronized fun put(path: String, tags: CachedTags) { map[path] = tags; dirty = true }

    @Synchronized val count: Int get() = map.size

    /** Drops entries for files that are gone (called with the paths of a complete listing). */
    @Synchronized fun retainOnly(paths: Set<String>) {
        if (map.keys.retainAll(paths)) dirty = true
    }

    @Synchronized fun save() {
        val f = file ?: return
        if (!dirty) return
        try {
            f.parentFile?.mkdirs()
            val tmp = File(f.path + ".tmp")
            DataOutputStream(BufferedOutputStream(tmp.outputStream(), 1 shl 16)).use { d ->
                d.writeInt(MAGIC); d.writeInt(VERSION); d.writeInt(map.size)
                for ((p, c) in map) {
                    d.writeUTF(clip(p))
                    d.writeLong(c.size); d.writeLong(c.mtime); d.writeBoolean(c.ok)
                    d.writeUTF(clip(c.codec)); d.writeUTF(clip(c.title)); d.writeUTF(clip(c.artist)); d.writeUTF(clip(c.albumArtist))
                    d.writeUTF(clip(c.album)); d.writeUTF(clip(c.genre))
                    d.writeInt(c.trackNo); d.writeInt(c.discNo); d.writeInt(c.year)
                    d.writeLong(c.durationMs)
                    d.writeInt(c.sampleRate); d.writeInt(c.bitDepth); d.writeInt(c.bitrateKbps); d.writeInt(c.channels)
                    d.writeBoolean(c.hasArt)
                }
            }
            if (f.exists()) f.delete()
            if (tmp.renameTo(f)) dirty = false
        } catch (_: Exception) {}
    }

    private fun clip(s: String) = if (s.length > 4000) s.substring(0, 4000) else s

    companion object {
        private const val MAGIC = 0x4E544147   // "NTAG"
        /** Bump when the reader's output changes, so every file is read again once. */
        const val VERSION = 1
    }
}
