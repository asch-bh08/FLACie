package com.ipodemu.library

/**
 * The Playlists chip's genre and decade choices (6 Nov: the web's Search filters on the phone): the library's genres, most songs first (a tag with
 * several, "Electronic; Dance", counts for each), and its decades, newest first. The server matches the lists by these names.
 */
class ListFilters(val genres: List<String>, val decades: List<String>) {
    companion object {
        const val MAX_GENRES = 12

        fun of(songs: List<Track>): ListFilters {
            val counts = LinkedHashMap<String, Int>(); val names = HashMap<String, String>()
            for (t in songs) for (g in t.genre.split(';', '/', ',').map { it.trim() }.filter { it.isNotEmpty() }) {
                val k = g.lowercase()
                counts[k] = (counts[k] ?: 0) + 1
                names.putIfAbsent(k, g.replaceFirstChar { it.uppercase() })
            }
            val genres = counts.entries.sortedByDescending { it.value }.take(MAX_GENRES).map { names.getValue(it.key) }
            val decades = songs.map { it.year }.filter { it in 1900..2100 }.map { it / 10 * 10 }.distinct().sortedDescending().map { "${it}s" }
            return ListFilters(genres, decades)
        }
    }
}
