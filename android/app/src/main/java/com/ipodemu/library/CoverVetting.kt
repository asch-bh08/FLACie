package com.ipodemu.library

/**
 * Which of a Jellyfin song's cover keys can be trusted. A song's own picture ("jf" + its own item id) is always right. A song without one points at its album's
 * picture (the AlbumId item), and on a real Jellyfin AlbumId is not reliable: files of many different albums (or with no album at all) end up under one AlbumId, and
 * all of them then show that one album's picture. A fallback is therefore kept only for songs that really belong to the album the picture is of, which the library
 * itself can tell: among the songs that point at one album picture there must be exactly one album name (not blank, not "Unknown"), and only the songs with that name keep
 * it. Every other song shows no cover rather than another album's.
 */
object CoverVetting {
    private val itemId = Regex("/Audio/([0-9a-fA-F]{32})/")

    private val editionTail = Regex("""\s*[(\[].*?[)\]]""")
    private val notAlphanumeric = Regex("""[^\p{L}\p{N}]+""")
    private fun albumName(t: Track): String = t.album.lowercase().replace(editionTail, "").replace(notAlphanumeric, " ").trim()
    private fun blank(n: String) = n.isEmpty() || n == "unknown" || n == "unknown album" || n == "no title" || n == "untitled"

    /** The same songs with the doubtful album fallbacks removed (the list itself when nothing changes). */
    fun vet(tracks: List<Track>): List<Track> {
        // key -> album names of the songs that point at it as a fallback
        var groups: HashMap<String, HashSet<String>>? = null
        for (t in tracks) {
            val k = t.artKey ?: continue
            if (t.source != TrackSource.JELLYFIN || !k.startsWith("jf") || isOwn(t, k)) continue
            (groups ?: HashMap<String, HashSet<String>>().also { groups = it }).getOrPut(k) { HashSet() }.add(albumName(t))
        }
        val g = groups ?: return tracks
        // the album every fallback picture is of: the one non-blank name its songs share, or none when they disagree
        val owner = HashMap<String, String>()
        for ((k, names) in g) { val real = names.filterNot { blank(it) }; if (real.size == 1) owner[k] = real[0] }
        var changed = false
        val out = ArrayList<Track>(tracks.size)
        for (t in tracks) {
            val k = t.artKey
            if (k != null && t.source == TrackSource.JELLYFIN && k.startsWith("jf") && !isOwn(t, k) && owner[k] != albumName(t)) { out += t.copy(artKey = catalogKey(t)); changed = true }
            else out += t
        }
        return if (changed) out else tracks
    }

    /** A catalog lookup for a song the server has no usable picture for: one per album (its songs share the key and the one download), by title when there is no album name. */
    fun catalogKey(t: Track): String? = CoverLookup.key(t.albumArtist.ifBlank { t.artist }, if (blank(albumName(t))) "" else t.album, if (blank(albumName(t))) t.title else "")

    /** True when the key is this song's own item (its own picture). */
    fun isOwn(t: Track, key: String): Boolean = itemId.find(t.path)?.groupValues?.get(1)?.equals(key.substring(2), true) == true
}
