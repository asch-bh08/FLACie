package com.ipodemu.library

import android.util.Base64

/**
 * Covers for NAS songs ("nf" keys): the image already in the album folder (cover.jpg, folder.jpg, front.jpg...), else
 * an online lookup by artist and album (see [CoverLookup]). Songs of one album folder share one key, so the folder is
 * read once.
 */
object NasCover {
    private val names = listOf("cover", "folder", "front", "album", "albumart", "artwork")
    private val exts = setOf("jpg", "jpeg", "png", "webp")

    fun key(folderUrl: String, artist: String, album: String, title: String): String =
        "nf" + Base64.encodeToString("$folderUrl\n$artist\n$album\n$title".toByteArray(), Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)

    fun fetch(key: String, user: String, pass: String, domain: String): ByteArray? {
        val parts = String(Base64.decode(key.removePrefix("nf"), Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)).split('\n')
        if (parts.size < 4) return null
        val (folder, artist, album, title) = parts
        folderImage(folder, user, pass, domain)?.let { return it }
        return CoverLookup.key(artist, album, title)?.let { CoverLookup.fetch(it) }
    }

    private fun folderImage(folderUrl: String, user: String, pass: String, domain: String): ByteArray? = try {
        val dir = jcifs.smb.SmbFile(folderUrl, NasSmb.context(user, pass, domain))
        val images = dir.listFiles().filter { it.isFile && it.name.substringAfterLast('.', "").lowercase() in exts }
        // a named cover first, else the largest image (a scan of the front is usually the biggest)
        val pick = images.firstOrNull { it.name.substringBeforeLast('.').lowercase() in names } ?: images.maxByOrNull { it.length() }
        pick?.takeIf { it.length() in 1..15_000_000 }?.inputStream?.use { it.readBytes() }
    } catch (_: Exception) { null }
}
