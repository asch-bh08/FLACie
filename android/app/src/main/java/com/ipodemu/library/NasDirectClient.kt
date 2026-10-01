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
            // artist folders at the top level tell which side of a loose "X - Y" file name is the artist
            knownArtists = try { root.listFiles().filter { it.isDirectory }.mapTo(HashSet()) { primaryArtist(it.name.trimEnd('/')) } } catch (_: Exception) { emptySet() }
            walk(root, out, depth = 0, artist = null, album = null)
            out
        }

    @Volatile private var knownArtists: Set<String> = emptySet()

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
                val cleaned = cleanTitle(f.name, artist, album)
                // a loose "Artist - Title" file straight inside a top-level folder: that folder is a playlist-style collection
                // ("aura(LAC)"), not the artist, so the credit comes from the file name
                val split = (if (album == null && " - " in cleaned) cleaned.split(" - ", limit = 2).map { it.trim() }.takeIf { it.all { p -> p.isNotEmpty() } } else null)
                    // "Cinderella Man - Eminem": the known artist is on the right, so it is "Title - Artist"
                    ?.let { s -> if (primaryArtist(s[1]) in knownArtists && primaryArtist(s[0]) !in knownArtists) listOf(s[1], s[0]) else s }
                val title = split?.get(1) ?: cleaned
                val credit = split?.get(0) ?: artist
                out += Track(
                    path = f.canonicalPath,
                    title = title,
                    artist = credit ?: "",
                    album = album ?: "",
                    albumArtist = credit ?: "",
                    genre = "",
                    trackNo = 0,
                    discNo = 0,
                    durationMs = 0L,
                    year = 0,
                    isMusic = true,
                    // the folder's own cover first (one key per album folder), then an online lookup by these names
                    artKey = NasCover.key(f.parent, credit ?: "", album ?: "", if (album != null) "" else title),
                    mtime = f.lastModified(),
                    size = f.length(),
                    source = TrackSource.NAS,
                )
            } catch (_: Exception) { /* one bad entry (permissions, a broken link) shouldn't stop the whole scan */ }
        }
    }

    /** Strips redundant "Artist - ", "Album - ", and leading track-number prefixes from a filename,
     * in that order -- matching the common "Artist - Album - 01 - Title.ext" convention (Lidarr's
     * own, and this app's file-mover) as well as plain "01 - Title.ext" rips. Without this, a NAS
     * scan (no embedded-tag reading, filename only) reads the *entire* filename as the title, so the
     * same song organized two different ways looks like two different songs -- confirmed live, a
     * Lidarr-imported "Artist - Album - 01 - Title.flac" and this app's own "Artist - Title.ext"
     * download of the same track never matched each other's dedup key. The track-number strip
     * repeats (not just once) to also handle "1.15. Title.ext"-style disc.track prefixes.
     *
     * The album match strips a trailing "(1982)"-style year first: the *folder* name (which becomes
     * `album`) commonly carries the release year, but Lidarr's own filenames embed just the bare
     * album title without it -- confirmed live, a folder "Toto IV (1982)" holding a file named
     * "Toto - Toto IV - 01 - Rosanna.flac" was silently falling through this strip (the exact-prefix
     * match "Toto IV (1982) - " never matched the file's "Toto IV - "), leaving the track-number
     * regex nothing to grab since the remainder still started with a letter, not a digit. */
    private fun cleanTitle(filename: String, artist: String?, album: String?): String {
        var t = filename.substringBeforeLast('.')
        val albumCore = album?.trim()?.replace(Regex("\\s*\\(\\d{4}\\)\\s*$"), "")
        if (!artist.isNullOrBlank() && t.startsWith("$artist - ", ignoreCase = true)) t = t.substring(artist.length + 3)
        if (!albumCore.isNullOrBlank() && t.startsWith("$albumCore - ", ignoreCase = true)) t = t.substring(albumCore.length + 3)
        t = t.replace(Regex("^(\\d+[\\s.\\-_]+)+"), "")
        if (!artist.isNullOrBlank() && t.startsWith("$artist - ", ignoreCase = true)) t = t.substring(artist.length + 3)
        return t.trim().ifBlank { filename.substringBeforeLast('.') }
    }
}
