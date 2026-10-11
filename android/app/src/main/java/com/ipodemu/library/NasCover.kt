package com.ipodemu.library

import com.ipodemu.library.tags.NasArtKeys
import com.ipodemu.library.tags.TagReader

/**
 * Covers for NAS songs. "nf" keys: the image already in the album folder (cover.jpg, folder.jpg, front.jpg...), else
 * an online lookup by artist and album (see [CoverLookup]). "ne" keys ([NasArtKeys]): the picture embedded in the
 * album's files, normally saved at scan time; this reads it again if the saved thumbnail was cleared. Songs of one
 * album folder share one key, so the folder is read once.
 */
object NasCover {
    private val names = listOf("cover", "folder", "front", "album", "albumart", "artwork")
    private val exts = setOf("jpg", "jpeg", "png", "webp")
    private val enc = java.util.Base64.getUrlEncoder().withoutPadding()
    private val dec = java.util.Base64.getUrlDecoder()

    fun isImage(fileName: String) = fileName.substringAfterLast('.', "").lowercase() in exts

    fun key(folderUrl: String, artist: String, album: String, title: String): String =
        "nf" + enc.encodeToString("$folderUrl\n$artist\n$album\n$title".toByteArray())

    fun fetch(key: String, user: String, pass: String, domain: String): ByteArray? {
        if (key.startsWith("ne")) return fetchEmbedded(key, user, pass, domain)
        val parts = String(dec.decode(key.removePrefix("nf"))).split('\n')
        if (parts.size < 4) return null
        val (folder, artist, album, title) = parts
        folderImage(folder, user, pass, domain)?.let { return it }
        return CoverLookup.key(artist, album, title)?.let { CoverLookup.fetch(it) }
    }

    /** The picture embedded in the album's files again (the saved thumbnail is gone): the named file, else the first few of the folder. */
    private fun fetchEmbedded(key: String, user: String, pass: String, domain: String): ByteArray? {
        val (folder, album, name) = NasArtKeys.parse(key) ?: return null
        return try {
        val ctx = NasSmb.context(user, pass, domain)
        val dir = jcifs.smb.SmbFile(folder, ctx)
        val files = dir.listFiles().filter { it.isFile && NasSmb.isAudio(it.name) }
            .let { all -> if (name.isNotEmpty()) all.filter { it.name == name } else all }
        var found: ByteArray? = null
        for (f in files.take(12)) {
            val access = NasSmbAccess(f)
            val tags = try { TagReader.read(access, f.name) } finally { access.close() }
            if (tags?.art != null && (name.isNotEmpty() || tags.album.trim().lowercase() == album)) { found = tags.art; break }
        }
        found
        } catch (_: Exception) { null }
    }

    private fun folderImage(folderUrl: String, user: String, pass: String, domain: String): ByteArray? = try {
        val dir = jcifs.smb.SmbFile(folderUrl, NasSmb.context(user, pass, domain))
        val images = dir.listFiles().filter { it.isFile && it.name.substringAfterLast('.', "").lowercase() in exts }
        // a named cover first, else the largest image (a scan of the front is usually the biggest)
        val pick = images.firstOrNull { it.name.substringBeforeLast('.').lowercase() in names } ?: images.maxByOrNull { it.length() }
        pick?.takeIf { it.length() in 1..15_000_000 }?.inputStream?.use { it.readBytes() }
    } catch (_: Exception) { null }
}
