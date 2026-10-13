package com.ipodemu.playback

import com.ipodemu.library.Group
import com.ipodemu.library.Track

/** What the car (Android Auto), a Bluetooth head unit and voice search browse. The lists come from the library; this file only shapes them. */
interface BrowseSource {
    fun recent(): List<Track>
    fun favorites(): List<Track>
    fun downloaded(): List<Track>
    /** id to name. */
    fun playlists(): List<Pair<String, String>>
    fun playlistTracks(id: String): List<Track>
    fun albums(): List<Group>
    fun artists(): List<Group>
    fun search(query: String): List<Track>
}

/** One row of the browse tree: a folder you can open and/or a song or set of songs you can play. */
class BrowseNode(val id: String, val title: String, val subtitle: String?, val browsable: Boolean, val playable: Boolean, val artKey: String?, val path: String? = null)

/** A play request: the songs of the list the person picked from, and where to start. */
class Resolved(val tracks: List<Track>, val index: Int)

/**
 * The browse tree, pure and unit-tested: Home (the root) holds Recently played, Favourites, Playlists, Albums, Artists and Downloaded. A song's id carries the list it was
 * picked from, so playing it queues that list (what a person expects in a car), not the one song. Lists are paged and capped: a car screen shows a handful, and the
 * answer to one call must stay small.
 */
object BrowseTree {
    const val ROOT = "root"
    const val RECENT = "recent"; const val FAVORITES = "favorites"; const val PLAYLISTS = "playlists"; const val ALBUMS = "albums"; const val ARTISTS = "artists"; const val DOWNLOADED = "downloaded"
    private const val SEP = '\u001F'
    const val MAX_PAGE = 100
    /** A play request never queues more than this many songs (a 10,000-song list would be a bad thing to hand to anything). */
    const val MAX_QUEUE = 300

    fun songId(parent: String, path: String) = "s:$parent$SEP$path"
    fun playlistId(id: String) = "pl:$id"
    fun albumId(key: String) = "al:$key"
    fun artistId(name: String) = "ar:$name"

    private fun folder(id: String, title: String) = BrowseNode(id, title, null, browsable = true, playable = false, artKey = null)

    fun root(): List<BrowseNode> = listOf(
        folder(RECENT, "Recently played"), folder(FAVORITES, "Favourites"), folder(PLAYLISTS, "Playlists"), folder(ALBUMS, "Albums"), folder(ARTISTS, "Artists"), folder(DOWNLOADED, "Downloaded"),
    )

    private fun songNode(parent: String, t: Track) = BrowseNode(songId(parent, t.path), t.title, t.artist.ifEmpty { t.album }.ifEmpty { null }, browsable = false, playable = true, artKey = t.artKey, path = t.path)

    private fun sorted(g: List<Group>) = g.filter { it.name.isNotBlank() }.sortedBy { it.name.lowercase() }

    /** The children of [parent], page [page] (0-based) of up to [pageSize] (at most [MAX_PAGE]); empty for a song or an id it does not know. */
    fun children(parent: String, page: Int, pageSize: Int, src: BrowseSource): List<BrowseNode> {
        val all: List<BrowseNode> = when {
            parent == ROOT -> root()
            parent == RECENT -> src.recent().map { songNode(parent, it) }
            parent == FAVORITES -> src.favorites().map { songNode(parent, it) }
            parent == DOWNLOADED -> src.downloaded().map { songNode(parent, it) }
            parent == PLAYLISTS -> src.playlists().map { (id, name) -> BrowseNode(playlistId(id), name, null, browsable = true, playable = true, artKey = null) }
            parent == ALBUMS -> sorted(src.albums()).map { g -> BrowseNode(albumId(g.tracks.firstOrNull()?.albumKey ?: g.name), g.name, g.tracks.firstOrNull()?.albumArtist?.ifEmpty { null }, browsable = true, playable = true, artKey = g.artKey) }
            parent == ARTISTS -> sorted(src.artists()).map { g -> BrowseNode(artistId(g.name), g.name, null, browsable = true, playable = true, artKey = g.artKey) }
            parent.startsWith("pl:") -> src.playlistTracks(parent.removePrefix("pl:")).map { songNode(parent, it) }
            parent.startsWith("al:") -> albumTracks(parent, src).map { songNode(parent, it) }
            parent.startsWith("ar:") -> artistTracks(parent, src).map { songNode(parent, it) }
            else -> emptyList()
        }
        val size = pageSize.coerceIn(1, MAX_PAGE)
        val from = (page.coerceAtLeast(0).toLong() * size).coerceAtMost(all.size.toLong()).toInt()
        return all.subList(from, minOf(all.size, from + size))
    }

    private fun albumTracks(id: String, src: BrowseSource): List<Track> {
        val key = id.removePrefix("al:")
        return src.albums().firstOrNull { g -> g.tracks.firstOrNull()?.albumKey == key || g.name == key }?.tracks?.sortedWith(compareBy({ it.discNo }, { it.trackNo })) ?: emptyList()
    }

    private fun artistTracks(id: String, src: BrowseSource): List<Track> =
        src.artists().firstOrNull { it.name == id.removePrefix("ar:") }?.tracks?.sortedWith(compareBy({ it.album.lowercase() }, { it.discNo }, { it.trackNo })) ?: emptyList()

    /** The songs behind any id: a song (with its list around it), or a whole playlist, album or artist from its first song. Null for an id that is not playable. */
    fun resolve(mediaId: String, src: BrowseSource): Resolved? {
        if (mediaId.startsWith("s:")) {
            val body = mediaId.removePrefix("s:"); val cut = body.indexOf(SEP)
            if (cut <= 0) return null
            val parent = body.substring(0, cut); val path = body.substring(cut + 1)
            val list = listOf(parent).flatMap { songsOf(it, src) }
            val i = list.indexOfFirst { it.path == path }
            if (i < 0) return null
            return window(list, i)
        }
        val list = songsOf(mediaId, src)
        return if (list.isEmpty()) null else window(list, 0)
    }

    private fun songsOf(id: String, src: BrowseSource): List<Track> = when {
        id == RECENT -> src.recent(); id == FAVORITES -> src.favorites(); id == DOWNLOADED -> src.downloaded()
        id.startsWith("pl:") -> src.playlistTracks(id.removePrefix("pl:")); id.startsWith("al:") -> albumTracks(id, src); id.startsWith("ar:") -> artistTracks(id, src)
        id.startsWith("search:") -> src.search(id.removePrefix("search:"))
        else -> emptyList()
    }

    /** At most [MAX_QUEUE] songs, with the chosen one inside them. */
    private fun window(list: List<Track>, index: Int): Resolved {
        if (list.size <= MAX_QUEUE) return Resolved(list, index)
        val from = (index - 20).coerceAtLeast(0).coerceAtMost(list.size - MAX_QUEUE)
        return Resolved(list.subList(from, from + MAX_QUEUE), index - from)
    }

    /** A voice or text search: songs, capped; a play request for them queues the results. */
    fun searchNodes(query: String, page: Int, pageSize: Int, src: BrowseSource): List<BrowseNode> {
        val all = src.search(query).map { songNode("search:" + query, it) }
        val size = pageSize.coerceIn(1, MAX_PAGE); val from = (page.coerceAtLeast(0).toLong() * size).coerceAtMost(all.size.toLong()).toInt()
        return all.subList(from, minOf(all.size, from + size))
    }

    /** The songs of a search for playing (the first hit onwards), used when a voice command says "play X". */
    fun searchPlay(query: String, src: BrowseSource): Resolved? = src.search(query).takeIf { it.isNotEmpty() }?.let { window(it, 0) }
}
