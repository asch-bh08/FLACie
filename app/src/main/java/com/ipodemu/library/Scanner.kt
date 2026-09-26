package com.ipodemu.library

import android.content.Context
import android.media.MediaMetadataRetriever
import android.os.storage.StorageManager
import android.provider.MediaStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.ConcurrentLinkedQueue
import java.util.concurrent.atomic.AtomicInteger

class ScanResult(val tracks: List<Track>, val playlists: Map<String, List<String>>)

class Scanner(private val ctx: Context, private val art: ArtCache) {

    suspend fun scan(
        existing: Map<String, Track>,
        onProgress: (found: Int, partial: List<Track>) -> Unit,
    ): ScanResult = withContext(Dispatchers.IO) {
        val audio = ArrayList<File>()
        val lists = ArrayList<File>()
        val roots = roots()
        for (root in roots) walk(root, roots, audio, lists)
        if (audio.isEmpty()) audio += mediaStorePaths()

        val out = ConcurrentLinkedQueue<Track>()
        val done = AtomicInteger()
        for (chunk in audio.chunked(64)) {
            coroutineScope {
                chunk.chunked(16).map { part ->
                    async {
                        for (f in part) {
                            val old = existing[f.path]
                            val t = if (old != null && old.mtime == f.lastModified() && old.size == f.length()) old
                            else read(f)
                            if (t != null) out.add(t)
                            done.incrementAndGet()
                        }
                    }
                }.awaitAll()
            }
            onProgress(done.get(), out.toList())
        }
        val known = out.mapTo(HashSet()) { it.path }
        ScanResult(out.toList(), lists.mapNotNull { parsePlaylist(it, known) }.toMap())
    }

    private fun roots(): List<File> {
        val sm = ctx.getSystemService(Context.STORAGE_SERVICE) as StorageManager
        return sm.storageVolumes.mapNotNull { it.directory }.filter { it.canRead() }
    }

    private fun walk(dir: File, roots: List<File>, audio: MutableList<File>, lists: MutableList<File>) {
        val kids = dir.listFiles() ?: return
        if (kids.any { it.name == ".nomedia" }) return
        for (k in kids) {
            val n = k.name
            if (n.startsWith(".")) continue
            if (k.isDirectory) {
                if (n == "Android" && dir in roots) continue
                walk(k, roots, audio, lists)
            } else {
                val ext = n.substringAfterLast('.', "").lowercase()
                when {
                    ext in AUDIO_EXT && k.length() > 2048 -> audio += k
                    ext == "m3u" || ext == "m3u8" -> lists += k
                }
            }
        }
    }

    private fun mediaStorePaths(): List<File> = try {
        val out = ArrayList<File>()
        @Suppress("DEPRECATION")
        ctx.contentResolver.query(
            MediaStore.Audio.Media.EXTERNAL_CONTENT_URI, arrayOf(MediaStore.Audio.Media.DATA), null, null, null
        )?.use { c -> while (c.moveToNext()) c.getString(0)?.let { out += File(it) } }
        out.filter { it.exists() }
    } catch (_: Exception) { emptyList() }

    private fun read(f: File): Track? {
        val mmr = MediaMetadataRetriever()
        try {
            mmr.setDataSource(f.path)
            fun tag(k: Int) = mmr.extractMetadata(k)?.trim()?.takeIf { it.isNotEmpty() }
            val artist = tag(MediaMetadataRetriever.METADATA_KEY_ARTIST)
            val album = tag(MediaMetadataRetriever.METADATA_KEY_ALBUM)
            val title = tag(MediaMetadataRetriever.METADATA_KEY_TITLE)
            val lowerPath = f.parent.orEmpty().lowercase()
            val memoPath = MEMO_HINTS.any { lowerPath.contains(it) }
            val music = (artist != null || album != null) && !memoPath
            val albumArtist = tag(MediaMetadataRetriever.METADATA_KEY_ALBUMARTIST) ?: artist ?: ""
            val key = if (music) sha1(if (album != null) "$albumArtist|$album" else f.path) else null
            if (key != null && !art.has(key)) {
                val bytes = mmr.embeddedPicture ?: folderArt(f)
                if (bytes != null) try { art.save(key, bytes) } catch (_: Exception) {}
            }
            return Track(
                path = f.path,
                title = title ?: f.nameWithoutExtension,
                artist = artist ?: (if (music) "Unknown Artist" else ""),
                album = album ?: (if (music) "Unknown Album" else ""),
                albumArtist = albumArtist,
                genre = tag(MediaMetadataRetriever.METADATA_KEY_GENRE) ?: "",
                trackNo = num(tag(MediaMetadataRetriever.METADATA_KEY_CD_TRACK_NUMBER)),
                discNo = num(tag(MediaMetadataRetriever.METADATA_KEY_DISC_NUMBER)),
                durationMs = tag(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull() ?: 0L,
                year = num(tag(MediaMetadataRetriever.METADATA_KEY_YEAR)),
                isMusic = music,
                artKey = key,
                mtime = f.lastModified(),
                size = f.length(),
            )
        } catch (_: Exception) {
            return null
        } finally {
            try { mmr.release() } catch (_: Exception) {}
        }
    }

    private fun folderArt(f: File): ByteArray? {
        val dir = f.parentFile ?: return null
        for (n in ART_NAMES) for (e in arrayOf("jpg", "jpeg", "png")) {
            val a = File(dir, "$n.$e")
            if (a.isFile && a.length() < 8_000_000) return a.readBytes()
        }
        return null
    }

    private fun parsePlaylist(f: File, known: Set<String>): Pair<String, List<String>>? = try {
        val base = f.parentFile
        val paths = f.readLines().map { it.trim() }.filter { it.isNotEmpty() && !it.startsWith("#") }
            .map { p -> if (p.startsWith("/")) File(p) else File(base, p) }
            .map { it.path }.filter { it in known }
        if (paths.isEmpty()) null else f.nameWithoutExtension to paths
    } catch (_: Exception) { null }

    private fun num(s: String?) = s?.substringBefore('/')?.trim()?.toIntOrNull() ?: 0

    private fun sha1(s: String) = MessageDigest.getInstance("SHA-1").digest(s.toByteArray())
        .joinToString("") { "%02x".format(it) }.take(20)

    companion object {
        val AUDIO_EXT = setOf("mp3", "m4a", "aac", "flac", "ogg", "oga", "opus", "wav", "m4b", "amr", "3gp", "mka")
        val MEMO_HINTS = listOf("recording", "voice", "memo", "dictaphone", "sound recorder", "soundrecorder")
        val ART_NAMES = listOf("cover", "folder", "front", "album", "albumart")
    }
}
