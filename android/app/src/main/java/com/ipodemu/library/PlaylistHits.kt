package com.ipodemu.library

/**
 * The person's playlists for a search, as on the web (FLACie.Core PlaylistHits): those named like it first, then those holding a song whose title or artist
 * matches it, the most matching songs first (owner, 7 Nov: searching "dreamer", "with me", "eminem" or "aura" should list the playlists the song or artist
 * is in). [holds] says what it holds for its card ("Has “Dreamer”", "2 songs by Eminem"); null when its name matched. Pure.
 */
object PlaylistHits {
    data class Song(val title: String, val artist: String)
    data class Hit<P>(val list: P, val holds: String?)

    fun <P> find(lists: List<P>, name: (P) -> String, songs: (P) -> List<Song>, query: String, max: Int = 12): List<Hit<P>> {
        val words = searchWords(query)
        if (words.isEmpty()) return emptyList()
        val named = ArrayList<Hit<P>>()
        val holding = ArrayList<Pair<Hit<P>, Int>>()
        for (p in lists) {
            if (searchHit(words, name(p))) { named += Hit(p, null); continue }
            // the title alone or the artist alone: "with me" is a song, "eminem" an artist; the two run together would match far too much
            val all = songs(p)
            val byTitle = all.filter { searchHit(words, it.title) }
            val byArtist = all.filter { searchHit(words, it.artist) }
            val hit = (byTitle + byArtist).distinct()
            if (hit.isEmpty()) continue
            holding += Hit(p, label(byTitle, byArtist, hit)) to hit.size
        }
        return (named + holding.sortedByDescending { it.second }.map { it.first }).take(max)
    }

    private fun label(byTitle: List<Song>, byArtist: List<Song>, all: List<Song>): String = when {
        byTitle.isNotEmpty() && byTitle.map { it.title.lowercase() }.distinct().size == 1 && all.size == byTitle.size -> "Has “${byTitle[0].title}”"
        byArtist.size == all.size -> if (all.size == 1) "Has “${all[0].title}”" else "${all.size} songs by ${byArtist[0].artist}"
        else -> "Has ${all.size} of these songs"
    }
}
