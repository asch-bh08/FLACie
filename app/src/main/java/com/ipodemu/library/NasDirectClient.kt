package com.ipodemu.library

import jcifs.smb.SmbFile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Browses an SMB/CIFS network share directly from the phone -- a plain "\\server\share" NAS, no
 * Jellyfin/Plex media server required. There's no metadata API for a raw share, so titles/artists/
 * albums are inferred from the folder layout: files directly under the configured folder get no
 * artist/album, one level down is treated as "Artist/file", two levels down as "Artist/Album/file"
 * (the common layout used by iTunes-style and most NAS music folders). Track numbers are stripped
 * from filenames like "03 - Song.mp3" for a cleaner title.
 */
class NasDirectClient {
    suspend fun testConnection(host: String, share: String, folder: String, username: String, password: String, domain: String): Pair<Boolean, String?> =
        withContext(Dispatchers.IO) {
            try {
                val ctx = NasSmb.context(username, password, domain)
                val root = SmbFile(NasSmb.rootUrl(host, share, folder), ctx)
                if (!root.exists()) return@withContext false to "Not found: $share/$folder"
                if (!root.isDirectory) return@withContext false to "That path isn't a folder"
                true to null
            } catch (e: Exception) {
                false to (e.message ?: "Connection failed")
            }
        }

    suspend fun scanTracks(host: String, share: String, folder: String, username: String, password: String, domain: String): List<Track> =
        withContext(Dispatchers.IO) {
            val ctx = NasSmb.context(username, password, domain)
            val root = SmbFile(NasSmb.rootUrl(host, share, folder), ctx)
            val out = ArrayList<Track>()
            walk(root, out, depth = 0, artist = null, album = null)
            out
        }

    private fun walk(dir: SmbFile, out: MutableList<Track>, depth: Int, artist: String?, album: String?) {
        val children = try { dir.listFiles() } catch (_: Exception) { return }
        for (f in children) {
            try {
                if (f.isDirectory) {
                    val name = f.name.trimEnd('/')
                    val nextArtist = if (depth == 0) name else artist
                    val nextAlbum = if (depth == 1) name else if (depth == 0) null else album
                    walk(f, out, depth + 1, nextArtist, nextAlbum)
                    continue
                }
                if (!f.isFile || !NasSmb.isAudio(f.name)) continue
                val title = f.name.substringBeforeLast('.').replace(Regex("^\\d+[\\s.\\-_]+"), "").ifBlank { f.name }
                out += Track(
                    path = f.canonicalPath,
                    title = title,
                    artist = artist ?: "",
                    album = album ?: "",
                    albumArtist = artist ?: "",
                    genre = "",
                    trackNo = 0,
                    discNo = 0,
                    durationMs = 0L,
                    year = 0,
                    isMusic = true,
                    artKey = null,
                    mtime = f.lastModified(),
                    size = f.length(),
                    source = TrackSource.NAS,
                )
            } catch (_: Exception) { /* one bad entry (permissions, a broken link) shouldn't stop the whole scan */ }
        }
    }
}
