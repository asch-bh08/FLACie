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
    /** How many songs a playlist has and its first song's cover, without reading every song (a car asks for all the playlists at once). */
    fun playlistSummary(id: String): Pair<Int, String?> = playlistTracks(id).let { it.size to it.firstOrNull()?.artKey }
    fun albums(): List<Group>
    fun artists(): List<Group>
    fun search(query: String): List<Track>
    /** Every song, for Shuffle all. */
    fun allSongs(): List<Track>
}

/**
 * One row of the browse tree: a folder you can open and/or a song or set of songs you can play. [grid]: the car shows its children as big covers (albums,
 * playlists, artists) instead of a list. [group]: the heading it sits under in a list (Home's sections). [icon]: a drawable's name for a row with no cover (the tabs,
 * Favourites, Shuffle all). [offline]: the song is on the phone, so the car marks it as downloaded.
 */
class BrowseNode(
    val id: String, val title: String, val subtitle: String?, val browsable: Boolean, val playable: Boolean, val artKey: String?, val path: String? = null,
    val grid: Boolean = false, val group: String? = null, val icon: String? = null, val offline: Boolean = false,
)

/** A play request: the songs of the list the person picked from, and where to start. */
class Resolved(val tracks: List<Track>, val index: Int)

/**
 * The browse tree, pure and unit-tested. The car shows the root as four tabs: Home (Shuffle all, Favourites and Downloaded, then the songs played lately),
 * Playlists, Albums and Artists, the last three as grids of covers. A song's id carries the list it was picked from, so playing it queues that list (what a person
 * expects in a car), not the one song. Lists are paged and capped: a car screen shows a handful, and the answer to one call must stay small.
 */
object BrowseTree {
    const val ROOT = "root"
    const val HOME = "home"; const val SHUFFLE = "shuffle"
    const val RECENT = "recent"; const val FAVORITES = "favorites"; const val PLAYLISTS = "playlists"; const val ALBUMS = "albums"; const val ARTISTS = "artists"; const val DOWNLOADED = "downloaded"
    private const val SEP = '\u001F'
    const val MAX_PAGE = 100
    /** A play request never queues more than this many songs (a 10,000-song list would be a bad thing to hand to anything). */
    const val MAX_QUEUE = 300
    /** How many recent songs Home shows under its heading (all of them are in Recently played's own list). */
    const val HOME_RECENT = 12

    /** The lists the car is told to reload when the library or the account's lists change (PlaybackService): it keeps what it was first told, and
     * a car that connected before FLACie had loaded anything showed "No items" in every tab until it was restarted. */
    val LIVE = listOf(HOME, RECENT, FAVORITES, PLAYLISTS, ALBUMS, ARTISTS, DOWNLOADED)

    fun songId(parent: String, path: String) = "s:$parent$SEP$path"
    fun playlistId(id: String) = "pl:$id"
    fun albumId(key: String) = "al:$key"
    fun artistId(name: String) = "ar:$name"

    private fun folder(id: String, title: String, icon: String, grid: Boolean = false, group: String? = null) =
        BrowseNode(id, title, null, browsable = true, playable = false, artKey = null, grid = grid, group = group, icon = icon)

    fun root(): List<BrowseNode> = listOf(
        folder(HOME, "Home", "ic_car_home"), folder(PLAYLISTS, "Playlists", "ic_car_playlists", grid = true),
        folder(ALBUMS, "Albums", "ic_car_albums", grid = true), folder(ARTISTS, "Artists", "ic_car_artists", grid = true),
    )

    private fun home(src: BrowseSource): List<BrowseNode> {
        val mine = "Your music"; val lately = "Recently played"
        return listOf(BrowseNode(SHUFFLE, "Shuffle all", "Your whole library", browsable = false, playable = true, artKey = null, icon = "ic_car_shuffle")) +
            folder(FAVORITES, "Favourites", "ic_car_favourites", group = mine) + folder(DOWNLOADED, "Downloaded", "ic_car_downloaded", group = mine) +
            src.recent().take(HOME_RECENT).map { songNode(RECENT, it, lately) }
    }

    private fun songNode(parent: String, t: Track, group: String? = null) = BrowseNode(
        songId(parent, t.path), t.title, listOf(t.artist, t.album).filter { it.isNotBlank() }.joinToString(" · ").ifEmpty { null },
        browsable = false, playable = true, artKey = t.artKey, path = t.path, group = group, offline = parent == DOWNLOADED,
    )

    // by the words: "\"...And Crazy\"" files under A, not before everything (the car has an A-Z jump that goes by this order)
    private fun sorted(g: List<Group>) = g.filter { it.name.isNotBlank() }.sortedBy { it.name.trimStart { c -> !c.isLetterOrDigit() }.lowercase() }
    private fun songs(n: Int) = if (n == 1) "1 song" else "$n songs"

    /** The children of [parent], page [page] (0-based) of up to [pageSize] (at most [MAX_PAGE]); empty for a song or an id it does not know. */
    fun children(parent: String, page: Int, pageSize: Int, src: BrowseSource): List<BrowseNode> {
        val all: List<BrowseNode> = when {
            parent == ROOT -> root()
            parent == HOME -> home(src)
            parent == RECENT -> src.recent().map { songNode(parent, it) }
            parent == FAVORITES -> src.favorites().map { songNode(parent, it) }
            parent == DOWNLOADED -> src.downloaded().map { songNode(parent, it) }
            parent == PLAYLISTS -> src.playlists().map { (id, name) -> val (n, art) = src.playlistSummary(id); BrowseNode(playlistId(id), name, songs(n), browsable = true, playable = true, artKey = art, grid = true) }
            parent == ALBUMS -> sorted(src.albums()).map { g -> BrowseNode(albumId(g.tracks.firstOrNull()?.albumKey ?: g.name), g.name, g.tracks.firstOrNull()?.albumArtist?.ifEmpty { null }, browsable = true, playable = true, artKey = g.artKey, grid = true) }
            parent == ARTISTS -> sorted(src.artists()).map { g -> BrowseNode(artistId(g.name), g.name, songs(g.tracks.size), browsable = true, playable = true, artKey = g.artKey, grid = true) }
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
        id == SHUFFLE -> src.allSongs().shuffled().take(MAX_QUEUE)
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
