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
    /** The engine's Quick picks, the same songs the phone's Home shows (only ones this phone has: a song still to be fetched can't be started from the car). */
    fun quickPicks(): List<Track> = emptyList()
    /** Songs the online catalog has for a search (as the phone's Search shows under More music); none when offline or without downloads. */
    fun catalog(query: String): List<BrowseTree.CatalogSong> = emptyList()
    /** The library has this song already (so the catalog's copy is not offered). */
    fun owns(title: String, artist: String): Boolean = false
}

/**
 * One row of the browse tree: a folder you can open and/or a song or set of songs you can play. [grid]: the car shows its children as big covers (albums,
 * playlists, artists) instead of a list. [group]: the heading it sits under in a list (Home's sections). [icon]: a drawable's name for a row with no cover (the tabs,
 * Favourites, Shuffle all). [offline]: the song is on the phone, so the car marks it as downloaded. [cover]: this one row is drawn as a big cover in
 * a list (Home's recent songs and playlists).
 */
class BrowseNode(
    val id: String, val title: String, val subtitle: String?, val browsable: Boolean, val playable: Boolean, val artKey: String?, val path: String? = null,
    val grid: Boolean = false, val group: String? = null, val icon: String? = null, val offline: Boolean = false, val cover: Boolean = false,
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
    /** Carry on with the queue of last time (play pressed on the car's card before anything was picked). */
    const val RESUME = "resume"
    const val QUICK = "quick"; const val RECENT = "recent"; const val FAVORITES = "favorites"; const val PLAYLISTS = "playlists"; const val ALBUMS = "albums"; const val ARTISTS = "artists"; const val DOWNLOADED = "downloaded"
    private const val SEP = '\u001F'
    const val MAX_PAGE = 100
    /** A play request never queues more than this many songs (a 10,000-song list would be a bad thing to hand to anything). */
    const val MAX_QUEUE = 300
    /** How many recent songs Home shows under its heading (all of them are in Recently played's own list). */
    const val HOME_RECENT = 12
    /** How many playlists Home shows as covers: all of a usual collection (it stopped at 6 and "pop", the 7th, was missing); the Playlists tab has every one. */
    const val HOME_PLAYLISTS = 30
    /** How many of the engine's Quick picks Home shows. */
    const val HOME_QUICK = 12

    /** The lists the car is told to reload when the library or the account's lists change (PlaybackService): it keeps what it was first told, and
     * a car that connected before FLACie had loaded anything showed "No items" in every tab until it was restarted. */
    val LIVE = listOf(HOME, RECENT, FAVORITES, PLAYLISTS, ALBUMS, ARTISTS, DOWNLOADED)

    fun songId(parent: String, path: String) = "s:$parent$SEP$path"
    /** An id the car or a voice command asks to play, which the player resolves through [resolve]. Shuffle all, Play all and Shuffle were missing here
     * and did nothing when pressed in the car. */
    fun isPlayId(id: String) = id == SHUFFLE || listOf("s:", "pl:", "al:", "ar:", "all:", "shuf:", "dl:").any { id.startsWith(it) }

    /** A song from the online catalog that a car's search offers: picking it downloads it, and it plays when it lands (the player does that, see playBrowse). */
    data class CatalogSong(val artist: String, val title: String, val album: String, val durationMs: Long)
    /** How many catalog songs a car search offers after the library's own. */
    const val CATALOG_IN_SEARCH = 5
    fun downloadId(c: CatalogSong) = "dl:" + listOf(c.artist, c.title, c.album, c.durationMs.toString()).joinToString(SEP.toString())
    fun parseDownload(id: String): CatalogSong? {
        if (!id.startsWith("dl:")) return null
        val p = id.removePrefix("dl:").split(SEP); if (p.size != 4) return null
        return CatalogSong(p[0], p[1], p[2], p[3].toLongOrNull() ?: 0)
    }

    /** The id a voice command plays: the first song of what [searchPlay] found, with the rest of it queued after. */
    fun voiceId(query: String, path: String) = songId("voice:$query", path)
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
        val mine = "Your music"; val lately = "Recently played"; val lists = "Your playlists"; val picks = "Quick picks"
        return listOf(BrowseNode(SHUFFLE, "Shuffle all", "Your whole library", browsable = false, playable = true, artKey = null, icon = "ic_car_shuffle")) +
            // the playlists first: they are what a person reaches for in the car
            playlistNodes(src).take(HOME_PLAYLISTS).map { BrowseNode(it.id, it.title, it.subtitle, true, true, it.artKey, group = lists, icon = "ic_car_playlists", cover = true) } +
            folder(FAVORITES, "Favourites", "ic_car_favourites", group = mine) + folder(DOWNLOADED, "Downloaded", "ic_car_downloaded", group = mine) +
            src.quickPicks().take(HOME_QUICK).map { songNode(QUICK, it, picks).let { n -> BrowseNode(n.id, n.title, n.subtitle, false, true, n.artKey, n.path, group = picks, cover = true) } } +
            src.recent().take(HOME_RECENT).map { songNode(RECENT, it, lately).let { n -> BrowseNode(n.id, n.title, n.subtitle, false, true, n.artKey, n.path, group = lately, cover = true) } }
    }

    private fun playlistNodes(src: BrowseSource) =
        src.playlists().map { (id, name) -> val (n, art) = src.playlistSummary(id); BrowseNode(playlistId(id), name, songs(n), browsable = true, playable = true, artKey = art, grid = true, icon = "ic_car_playlists") }

    /** The two buttons at the top of an album, playlist or artist: its songs in order, or shuffled. */
    private fun buttons(parent: String) = listOf(
        BrowseNode("all:$parent", "Play all", null, browsable = false, playable = true, artKey = null, icon = "ic_car_play"),
        BrowseNode("shuf:$parent", "Shuffle", null, browsable = false, playable = true, artKey = null, icon = "ic_car_shuffle"),
    )

    private val credits = Regex("\\s*(;|,|&|/|\\s+(feat\\.?|ft\\.?|featuring)\\s+)\\s*", RegexOption.IGNORE_CASE)

    /** Artists merged by their main credit however it is spelt ("2Pac", "2PAC", "2Pac; Dr. Dre"), named as their biggest spelling's main credit. */
    private fun mergedArtists(src: BrowseSource): List<Group> = src.artists().filter { it.name.isNotBlank() }
        .groupBy { com.ipodemu.library.primaryArtist(it.name).ifEmpty { it.name.lowercase() } }.values.map { gs ->
            val big = gs.maxBy { it.tracks.size }
            val name = big.name.split(credits).firstOrNull { it.isNotBlank() }?.trim() ?: big.name
            Group(name, gs.flatMap { it.tracks }.distinctBy { it.path }, big.artKey)
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
            parent == QUICK -> src.quickPicks().map { songNode(parent, it) }
            parent == RECENT -> src.recent().map { songNode(parent, it) }
            parent == FAVORITES -> src.favorites().map { songNode(parent, it) }
            parent == DOWNLOADED -> src.downloaded().map { songNode(parent, it) }
            parent == PLAYLISTS -> playlistNodes(src)
            parent == ALBUMS -> sorted(src.albums()).map { g -> BrowseNode(albumId(g.tracks.firstOrNull()?.albumKey ?: g.name), g.name, g.tracks.firstOrNull()?.albumArtist?.ifEmpty { null }, browsable = true, playable = true, artKey = g.artKey, grid = true, icon = "ic_car_albums") }
            parent == ARTISTS -> sorted(mergedArtists(src)).map { g -> BrowseNode(artistId(g.name), g.name, songs(g.tracks.size), browsable = true, playable = true, artKey = g.artKey, grid = true, icon = "ic_car_artists") }
            parent.startsWith("pl:") || parent.startsWith("al:") || parent.startsWith("ar:") ->
                songsOf(parent, src).let { t -> if (t.isEmpty()) emptyList() else buttons(parent) + t.map { songNode(parent, it) } }
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
        mergedArtists(src).firstOrNull { it.name == id.removePrefix("ar:") }?.tracks?.sortedWith(compareBy({ it.album.lowercase() }, { it.discNo }, { it.trackNo })) ?: emptyList()

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
        if (mediaId.startsWith("all:")) return songsOf(mediaId.removePrefix("all:"), src).takeIf { it.isNotEmpty() }?.let { window(it, 0) }
        if (mediaId.startsWith("shuf:")) return songsOf(mediaId.removePrefix("shuf:"), src).takeIf { it.isNotEmpty() }?.let { window(it.shuffled(), 0) }
        val list = songsOf(mediaId, src)
        return if (list.isEmpty()) null else window(list, 0)
    }

    private fun songsOf(id: String, src: BrowseSource): List<Track> = when {
        id == QUICK -> src.quickPicks(); id == RECENT -> src.recent(); id == FAVORITES -> src.favorites(); id == DOWNLOADED -> src.downloaded()
        id == SHUFFLE -> src.allSongs().shuffled().take(MAX_QUEUE)
        id.startsWith("pl:") -> src.playlistTracks(id.removePrefix("pl:")); id.startsWith("al:") -> albumTracks(id, src); id.startsWith("ar:") -> artistTracks(id, src)
        id.startsWith("search:") -> src.search(id.removePrefix("search:"))
        // a voice command's songs: the playlist, artist or album it named, or the songs it found (searchPlay)
        id.startsWith("voice:") -> searchPlay(id.removePrefix("voice:"), src)?.tracks ?: emptyList()
        else -> emptyList()
    }

    /** At most [MAX_QUEUE] songs, with the chosen one inside them. */
    private fun window(list: List<Track>, index: Int): Resolved {
        if (list.size <= MAX_QUEUE) return Resolved(list, index)
        val from = (index - 20).coerceAtLeast(0).coerceAtMost(list.size - MAX_QUEUE)
        return Resolved(list.subList(from, from + MAX_QUEUE), index - from)
    }

    /** A voice or text search: songs, capped; a play request for them queues the results. */
    /** The car's search: the playlists, artists and albums whose names hold the words (as covers), then the songs. */
    fun searchNodes(query: String, page: Int, pageSize: Int, src: BrowseSource): List<BrowseNode> {
        val q = query.trim().lowercase()
        val lists = if (q.isEmpty()) emptyList() else
            playlistNodes(src).filter { it.title.lowercase().contains(q) } +
            sorted(mergedArtists(src)).filter { it.name.lowercase().contains(q) }.take(12).map { g -> BrowseNode(artistId(g.name), g.name, songs(g.tracks.size), true, true, g.artKey, icon = "ic_car_artists", cover = true) } +
            sorted(src.albums()).filter { it.name.lowercase().contains(q) }.take(12).map { g -> BrowseNode(albumId(g.tracks.firstOrNull()?.albumKey ?: g.name), g.name, g.tracks.firstOrNull()?.albumArtist?.ifEmpty { null }, true, true, g.artKey, icon = "ic_car_albums", cover = true) }
        val all = lists.map { if (it.cover) it else BrowseNode(it.id, it.title, it.subtitle, true, true, it.artKey, icon = it.icon, cover = true) } +
            src.search(query).map { songNode("search:" + query, it) } +
            // songs the library lacks, from the online catalog: picked, they download and then play (as the phone's More music)
            (if (q.isEmpty()) emptyList() else src.catalog(query).filter { !src.owns(it.title, it.artist) }.take(CATALOG_IN_SEARCH).map { c ->
                BrowseNode(downloadId(c), c.title, "${c.artist} · Download and play", browsable = false, playable = true, artKey = null, icon = "ic_car_download")
            })
        val size = pageSize.coerceIn(1, MAX_PAGE); val from = (page.coerceAtLeast(0).toLong() * size).coerceAtMost(all.size.toLong()).toInt()
        return all.subList(from, minOf(all.size, from + size))
    }

    /**
     * What a voice command ("play pop on FLACie") plays: a playlist, artist or album with exactly that name (so "pop" is the playlist, not Icona Pop's
     * songs), then the matching songs, then a playlist, artist or album whose name holds the words.
     */
    fun searchPlay(query: String, src: BrowseSource): Resolved? {
        val q = query.trim().lowercase(); if (q.isEmpty()) return null
        fun play(id: String) = songsOf(id, src).takeIf { it.isNotEmpty() }?.let { window(it, 0) }
        src.playlists().firstOrNull { it.second.trim().lowercase() == q }?.let { play(playlistId(it.first))?.let { r -> return r } }
        mergedArtists(src).firstOrNull { it.name.trim().lowercase() == q }?.let { play(artistId(it.name))?.let { r -> return r } }
        src.albums().firstOrNull { it.name.trim().lowercase() == q }?.let { g -> play(albumId(g.tracks.firstOrNull()?.albumKey ?: g.name))?.let { r -> return r } }
        src.search(query).takeIf { it.isNotEmpty() }?.let { return window(it, 0) }
        src.playlists().firstOrNull { it.second.lowercase().contains(q) }?.let { play(playlistId(it.first))?.let { r -> return r } }
        mergedArtists(src).firstOrNull { it.name.lowercase().contains(q) }?.let { play(artistId(it.name))?.let { r -> return r } }
        return src.albums().firstOrNull { it.name.lowercase().contains(q) }?.let { g -> play(albumId(g.tracks.firstOrNull()?.albumKey ?: g.name)) }
    }
}
