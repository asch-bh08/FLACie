package com.ipodemu.library

/**
 * F6.3: the artist a search means, for the top result (the same rule as FLACie Web's SearchRankTop). Only artists named like the search count
 * ("prodigy" is The Prodigy or Prodigy, never Prodigy of Mobb Deep); among them the one played most, then the one with the most songs, then the exact name.
 * [plays] is the number of plays per song match key (UserData.plays).
 */
object SearchRankTop {
    fun artist(artists: List<Group>, query: String, plays: Map<String, Int>): Group? {
        val q = key(query)
        if (q.isEmpty()) return null
        fun played(g: Group) = g.tracks.sumOf { plays[it.matchKey] ?: 0 }
        val exact = SearchRank.words(query).joinToString(" ")
        return artists.filter { key(it.name) == q }
            .sortedWith(compareByDescending<Group> { played(it) }.thenByDescending { it.tracks.size }.thenByDescending { SearchRank.words(it.name).joinToString(" ") == exact })
            .firstOrNull()
    }

    /** The name's words without a leading "the" ("" when that is all there is). */
    private fun key(s: String): String {
        val w = SearchRank.words(s).joinToString(" ")
        return if (w.startsWith("the ")) w.substring(4) else if (w == "the") "" else w
    }
}

/**
 * What the phone's search shows for an artist (the same split as FLACie Web's ArtistPage): their top 5 songs under the Top result, then Popular (the next
 * most played), Albums and Singles & EPs (each newest first). A release of 6 songs or fewer, or one named EP or Single, is a single.
 */
class ArtistPage(val top: List<Track>, val popular: List<Track>, val albums: List<Group>, val singles: List<Group>) {
    companion object {
        const val TOP = 5
        const val POPULAR = 10
        const val SINGLE_MAX = 6
        private val named = Regex("""[(\[\s-](?:ep|single)[)\]]?\s*$""", RegexOption.IGNORE_CASE)

        fun of(artist: Group, albums: List<Group>, plays: Map<String, Int>): ArtistPage {
            val songs = artist.tracks.sortedWith(compareByDescending<Track> { plays[it.matchKey] ?: 0 }.thenByDescending { it.year }.thenBy { it.title.lowercase() })
                .distinctBy { it.matchKey }
            val paths = artist.tracks.map { it.path }.toHashSet()
            val theirs = albums.filter { g -> g.name.isNotEmpty() && g.tracks.any { it.path in paths } }
                .sortedWith(compareByDescending<Group> { g -> g.tracks.maxOf { it.year } }.thenBy { it.name.lowercase() })
            return ArtistPage(songs.take(TOP), songs.drop(TOP).take(POPULAR), theirs.filter { !isSingle(it) }, theirs.filter { isSingle(it) })
        }

        fun isSingle(album: Group) = album.tracks.size <= SINGLE_MAX || named.containsMatchIn(album.name)
        /** "Single" for one song, "EP" for a few, else "Album". */
        fun kind(album: Group) = if (!isSingle(album)) "Album" else if (album.tracks.size == 1) "Single" else "EP"
    }
}
