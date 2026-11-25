package com.ipodemu.playback

import com.ipodemu.library.Track
import com.ipodemu.library.primaryArtist
import java.text.Normalizer

/**
 * "Play X" understood on the phone when FLACie Web can't be reached (owner, 25 Nov: "The DJ on Android Auto doesn't understand play X when there's no
 * signal even if that song is downloaded offline"). The same few rules as the server's VoiceParser (FLACie.Server/Alexa/VoiceMatcher.cs): play a song,
 * a song by an artist, an album, a playlist, an artist, shuffle the favourites or everything, and the plain controls (next, pause, carry on, louder,
 * quieter). With no signal only what is downloaded can play, so only that is looked in (and a match that is not downloaded is said so); with a signal
 * but no server, the whole library. The answer is a [DjAskAnswer] carried out like the server's, by paths on this phone, and always says why the
 * server was not asked. Pure, unit-tested.
 */
object OfflineAsk {
    /** Why the phone answered: no network at all, or the network is there but FLACie Web did not answer in time. */
    enum class Why { NO_SIGNAL, NO_SERVER }

    /** The server's "intent" of an answer made here: the conversation ends after it (a dead line is not listened on again). */
    const val INTENT = "Offline"

    /** How many songs a request queues at most. */
    private const val MAX = 300
    /** A misheard title ("heads will role") still counts from this likeness up (0 to 1). */
    private const val LIKE = 0.75

    interface Lib {
        fun songs(): List<Track>
        /** Name to songs. */
        fun playlists(): List<Pair<String, List<Track>>>
        fun favourites(): List<Track>
        fun isDownloaded(path: String): Boolean
    }

    private val lead = Regex("^(?:(?:hey |ok |okay )?dj |please |can you |could you |would you |will you |i want to hear |i want to listen to |i would like to hear |i'd like to hear |id like to hear |put on |listen to |start playing |play )")
    private val playLed = Regex("^(?:(?:hey |ok |okay )?dj )?(?:please |can you |could you |would you |will you )*(?:play|put on|listen to|start playing|shuffle|i want to hear|i'd like to hear)\\b")
    private val next = Regex("^(?:skip|next|skip (?:this|it)|(?:skip )?(?:the )?(?:this )?next(?: song| track| one)?|skip (?:this )?(?:song|track|one))$")
    private val pause = Regex("^(?:pause|stop)(?: (?:the )?(?:music|song|it|this))?$")
    private val resume = Regex("^(?:|resume|carry on|keep (?:playing|going)|continue|unpause|go on)(?: (?:the )?(?:music|song|it))?$")
    private val louder = Regex("\\b(?:louder|volume up|turn (?:it |the music |the volume )?up)\\b")
    private val quieter = Regex("\\b(?:quieter|softer|volume down|turn (?:it |the music |the volume )?down)\\b")
    private val favourites = Regex("^(?:my |the )?(?:favou?rites?|liked songs|loved songs|songs i (?:like|love)|hearted songs)$")
    private val downloads = Regex("^(?:my |the )?(?:downloads|downloaded (?:songs|music)|offline music|music on (?:my|the) phone)$")
    private val everything = Regex("^(?:(?:some |my |the )?(?:library|music|everything|all|songs|whole library)|anything|something)$")
    private val playlist = Regex("^(?:my |the )?(.+?)\\s+playlist$|^(?:my |the )?playlist\\s+(.+)$")
    private val album = Regex("^(?:the\\s+)?album\\s+(.+?)(?:\\s+by\\s+(.+))?$|^(.+?)\\s+album(?:\\s+by\\s+(.+))?$")
    private val song = Regex("^(?:the\\s+)?song\\s+(.+?)(?:\\s+by\\s+(.+))?$")
    private val byArtist = Regex("^(?:songs?|music|tracks?|something|anything|stuff)\\s+by\\s+(.+)$")
    private val titleBy = Regex("^(.+?)\\s+by\\s+(.+)$")

    fun answer(words: String, why: Why, lib: Lib): DjAskAnswer {
        var s = words.trim().lowercase().replace('’', '\'').replace(Regex("[.,!?;:\"]+"), " ").replace(Regex("\\s+"), " ").trim()
        val asked = playLed.containsMatchIn(s)
        s = s.removeSuffix(" please").trim()
        while (true) { val t = lead.replace(s, "").trim(); if (t == s) break; s = t }
        var shuffle = false
        if (s == "shuffle" || s.startsWith("shuffle ")) { shuffle = true; s = s.removePrefix("shuffle").trim().removePrefix("up ").trim() }

        // the plain controls first: they need nothing but the player
        if (!shuffle) {
            when {
                next.matches(s) -> return said(DjAskDo.NEXT, "")
                pause.matches(s) -> return said(DjAskDo.PAUSE, "")
                resume.matches(s) -> return said(DjAskDo.RESUME, "")
                s.split(' ').size <= 6 && louder.containsMatchIn(s) -> return said(DjAskDo.VOLUMEUP, "")
                s.split(' ').size <= 6 && quieter.containsMatchIn(s) -> return said(DjAskDo.VOLUMEDOWN, "")
            }
        }

        val offline = why == Why.NO_SIGNAL
        val all = lib.songs()
        val pool = if (offline) all.filter { lib.isDownloaded(it.path) } else all
        fun can(list: List<Track>) = if (offline) list.filter { lib.isDownloaded(it.path) } else list

        // the favourites, everything, the downloads
        if (favourites.matches(s)) {
            val f = can(lib.favourites())
            return if (f.isEmpty()) said(DjAskDo.NONE, if (offline) "No signal, and none of your favourites are downloaded." else "I couldn't reach the server, and you have no favourites on this phone.")
            else play(f.shuffled(), "Favourites", true, if (offline) "No signal: shuffling your favourites from your downloads." else "I couldn't reach the server: shuffling your favourites.")
        }
        if (downloads.matches(s) || (shuffle && s.isEmpty()) || everything.matches(s)) {
            val list = if (downloads.matches(s)) all.filter { lib.isDownloaded(it.path) } else pool
            return if (list.isEmpty()) said(DjAskDo.NONE, if (offline) "No signal, and nothing is downloaded on this phone." else "I couldn't reach the server, and there's nothing on this phone to play.")
            else play(list.shuffled(), if (offline || downloads.matches(s)) "Downloads" else "Your library", true,
                if (offline) "No signal: shuffling your downloads." else "I couldn't reach the server: shuffling your library.")
        }
        if (s.isEmpty()) return generic(why)

        // what kind of thing was named, when the words say ("my pop playlist", "the album X", "the song X by Y", "songs by X", "X by Y"); else a bare
        // name: a playlist or artist called exactly that, a song with that title (before an album of the same name: a single is "Heads Will Roll" too),
        // an album, then a near name. The first that has something to play wins; one with nothing on the phone is kept to say so
        val tries: List<() -> Found?> = listOf(
            { playlist.matchEntire(s)?.let { m -> findPlaylist((m.groupValues[1].ifEmpty { m.groupValues[2] }), lib, exact = false) } },
            { album.matchEntire(s)?.let { m -> if (m.groupValues[1].isNotEmpty()) findAlbum(m.groupValues[1], m.groupValues[2], all) else findAlbum(m.groupValues[3], m.groupValues[4], all) } },
            { song.matchEntire(s)?.let { m -> findSong(m.groupValues[1], m.groupValues[2], all, offline, lib) } },
            { byArtist.matchEntire(s)?.let { m -> findArtist(m.groupValues[1], all, exact = false) } },
            { findPlaylist(s, lib, exact = true) }, { findArtist(s, all, exact = true) },
            { titleBy.matchEntire(s)?.let { m -> findSong(m.groupValues[1], m.groupValues[2], all, offline, lib) } },
            { findSong(s, "", all, offline, lib) }, { findAlbum(s, "", all, exact = true) },
            { findPlaylist(s, lib, exact = false) }, { findArtist(s, all, exact = false) }, { findAlbum(s, "", all, exact = false) },
        )
        var found: Found? = null
        for (f in tries) { val x = f() ?: continue; if (found == null) found = x; if (can(x.tracks).isNotEmpty()) { found = x; break } }

        if (found == null) return if (asked) said(DjAskDo.NONE, if (offline) "No signal, and I can't find $s in your downloads." else "I couldn't reach the server, and I can't find $s on this phone.")
            else generic(why)
        val playable = can(found.tracks)
        if (playable.isEmpty()) return said(DjAskDo.NONE, "No signal, and ${found.name} isn't downloaded.")
        val from = if (offline) " from your downloads." else "."
        val opening = if (offline) "No signal: " else "I couldn't reach the server: "
        return when (found.kind) {
            Kind.SONG -> {
                // the song, then the rest of what is on the phone shuffled, so the music doesn't stop in a dead zone
                val first = playable.first()
                val rest = all.filter { lib.isDownloaded(it.path) && it.path != first.path }.shuffled()
                play(listOf(first) + rest, first.title, false, "${opening}playing ${first.title}$from")
            }
            Kind.ARTIST -> play(playable.shuffled(), found.name, true, "${opening}shuffling ${found.name}$from")
            Kind.ALBUM, Kind.PLAYLIST -> play(playable, found.name, shuffle, "${opening}playing ${found.name}$from")
        }
    }

    private enum class Kind { SONG, ALBUM, PLAYLIST, ARTIST }
    private class Found(val kind: Kind, val name: String, val tracks: List<Track>)

    private fun said(d: DjAskDo, say: String) = DjAskAnswer(d, say, intent = INTENT)
    private fun generic(why: Why) = said(DjAskDo.NONE, if (why == Why.NO_SIGNAL) "No signal. I can play your downloads: say play and a song, album, artist or playlist." else "I couldn't reach the server.")
    private fun play(list: List<Track>, title: String, shuffle: Boolean, say: String) = DjAskAnswer(DjAskDo.PLAY, say,
        list.take(MAX).map { DjAskSong(it.path, it.filePath, it.title, it.artist, it.album) }, title, shuffle, intent = INTENT)

    /** Lower case, no accents, "&" is "and", no punctuation, no leading "the": how a name is compared with what was heard. */
    fun fold(s: String): String {
        var f = Normalizer.normalize(s.lowercase(), Normalizer.Form.NFD).replace(Regex("\\p{Mn}+"), "")
        f = f.replace("&", " and ").replace("+", " and ").replace("'", "").replace("’", "").replace(Regex("[^\\p{L}\\p{N}]+"), " ").trim().replace(Regex(" +"), " ")
        return if (f.startsWith("the ") && f.length > 4) f.substring(4) else f
    }

    /** The title without what is in brackets or after " - " ("Heads Will Roll (A-Trak Remix)" is "heads will roll"). */
    private fun core(title: String) = fold(title.replace(Regex("\\s*[(\\[].*?[)\\]]"), "").substringBefore(" - "))

    /** How alike two folded names are, 0 to 1 (letter pairs in common). */
    fun like(a: String, b: String): Double {
        if (a == b) return 1.0
        if (a.length < 2 || b.length < 2) return 0.0
        val pa = a.windowed(2).groupingBy { it }.eachCount().toMutableMap()
        var common = 0
        for (p in b.windowed(2)) { val n = pa[p] ?: 0; if (n > 0) { common++; pa[p] = n - 1 } }
        return 2.0 * common / (a.length - 1 + b.length - 1)
    }

    private fun artistLike(want: String, t: Track): Double = maxOf(like(fold(want), fold(primaryArtist(t.artist))), like(fold(want), fold(t.artist)))

    private fun findSong(title: String, artist: String, all: List<Track>, offline: Boolean, lib: Lib): Found? {
        val q = fold(title); if (q.isEmpty()) return null
        var best: Track? = null; var bestScore = 0.0
        for (t in all) {
            val full = fold(t.title); val c = core(t.title)
            var s = maxOf(like(q, full), like(q, c))
            if (s < LIKE) continue
            // the main version over a remix or a live one, unless those words were said
            if (full != c && full != q) s -= 0.05
            if (artist.isNotBlank()) { val a = artistLike(artist, t); if (a < 0.6) continue; s = s * 0.6 + a * 0.4 }
            // with no signal, the copy on the phone
            if (offline && lib.isDownloaded(t.path)) s += 0.1
            if (s > bestScore) { best = t; bestScore = s }
        }
        return best?.let { Found(Kind.SONG, it.title, listOf(it)) }
    }

    private fun findArtist(name: String, all: List<Track>, exact: Boolean): Found? {
        val q = fold(name); if (q.isEmpty()) return null
        val by = all.groupBy { fold(primaryArtist(it.artist)) }
        val key = by.keys.firstOrNull { it == q } ?: if (exact) null else by.keys.maxByOrNull { like(q, it) }?.takeIf { like(q, it) >= 0.8 }
        val list = key?.let { by[it] } ?: return null
        return Found(Kind.ARTIST, list.groupingBy { primaryArtistName(it.artist) }.eachCount().maxBy { it.value }.key, list)
    }

    private fun primaryArtistName(a: String) = a.split(Regex("\\s*(;|,|&|/|\\s+(feat\\.?|ft\\.?|featuring)\\s+)\\s*", RegexOption.IGNORE_CASE)).firstOrNull { it.isNotBlank() }?.trim() ?: a

    private fun findAlbum(name: String, artist: String, all: List<Track>, exact: Boolean = false): Found? {
        val q = fold(name); if (q.isEmpty()) return null
        val by = all.filter { it.album.isNotBlank() && (artist.isBlank() || artistLike(artist, it) >= 0.6 || like(fold(artist), fold(it.albumArtist)) >= 0.6) }.groupBy { fold(it.album) }
        val key = by.keys.firstOrNull { it == q } ?: if (exact) null else by.keys.maxByOrNull { like(q, it) }?.takeIf { like(q, it) >= 0.8 }
        val list = key?.let { by[it] } ?: return null
        return Found(Kind.ALBUM, list.first().album, list.sortedWith(compareBy({ it.discNo }, { it.trackNo })))
    }

    private fun findPlaylist(name: String, lib: Lib, exact: Boolean): Found? {
        val q = fold(name); if (q.isEmpty()) return null
        val lists = lib.playlists()
        val p = lists.firstOrNull { fold(it.first) == q } ?: if (exact) null else lists.maxByOrNull { like(q, fold(it.first)) }?.takeIf { like(q, fold(it.first)) >= 0.8 }
        return p?.let { Found(Kind.PLAYLIST, it.first, it.second) }
    }
}
