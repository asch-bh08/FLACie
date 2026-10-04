package com.ipodemu.library

import android.os.Handler
import android.os.Looper
import com.ipodemu.App
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * What plays when the queue runs out, like FLACie Web. When the queue is nearly over it adds songs that go with the last one: the artist's other
 * well-known songs and those of similar artists (looked up online) that the library has, then the library's own idea of what fits. It never
 * adds another release of a song already queued or played lately (same title and lead artist is the same song). Songs the library lacks are
 * requested in the background, a few ahead (the server does the downloading; the phone only asks), and join the queue as they arrive.
 */
class Autoplay(private val app: App) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val main = Handler(Looper.getMainLooper())
    private val related = HashMap<String, Pair<Long, List<WebCatalog.Song>>>()
    private val waiting = LinkedHashMap<String, WebCatalog.Song>() // requested songs, by matchKey, to join the queue when they land
    @Volatile private var working = false

    /** The player calls this as a song starts when little is left after it. */
    fun queueLow() {
        if (!app.prefs.autoplay || working || app.player.interceptor != null) return
        val seed = app.player.current ?: return
        working = true
        scope.launch {
            try { refill(seed) } catch (_: Exception) { } finally { working = false }
        }
    }

    private suspend fun refill(seed: Track) {
        val lib = app.library
        val queued = withContext(Dispatchers.Main) { app.player.queueTracks() }
        val seen = HashSet<String>()
        queued.forEach { seen.add(it.matchKey) }
        seen.add(seed.matchKey)
        val by = lib.byPath()
        app.userData.recents.take(40).forEach { p -> by[p]?.let { seen.add(it.matchKey) } }

        val key = seed.matchKey
        val suggestions = synchronized(related) { related[key]?.takeIf { System.currentTimeMillis() - it.first < 30 * 60_000L }?.second }
            ?: try { WebCatalog.related(seed.artist, seed.title) } catch (_: Exception) { emptyList() }.also { synchronized(related) { related[key] = System.currentTimeMillis() to it } }

        val pick = ArrayList<Track>(); val taken = HashSet<String>()
        fun take(src: Sequence<Track>, max: Int) {
            var n = 0
            for (t in src) {
                if (n >= max || pick.size >= 6) return
                if (!t.isMusic || (t.durationMs in 1..40_000) || t.matchKey in seen || !taken.add(t.matchKey)) continue
                pick.add(t); n++
            }
        }
        take(suggestions.asSequence().mapNotNull { lib.findSong(it.title, it.artist) }, 4)
        val artist = primaryArtist(seed.artist)
        val songs = lib.songs()
        take(songs.asSequence().filter { primaryArtist(it.artist) == artist }.shuffled(), 2)
        // songs that share a playlist with the one that played, then the same genre, then anything
        val mates = app.userData.playlists.filter { pl -> pl.paths.any { by[it]?.matchKey == key } }.flatMap { pl -> pl.paths.mapNotNull { by[it] } }
        take(mates.shuffled().asSequence(), 3)
        if (seed.genre.isNotEmpty()) take(songs.asSequence().filter { it.genre.equals(seed.genre, true) }.shuffled(), 3)
        take(songs.asSequence().shuffled(), 6)
        if (pick.isNotEmpty()) withContext(Dispatchers.Main) { app.player.appendAutoplay(pick) }

        // songs the library lacks: ask for the next few now so they are there in time
        if (app.prefs.autoplayFetch && lib.downloadsConfigured()) {
            suggestions.asSequence().filter { lib.findSong(it.title, it.artist) == null && it.matchKey !in seen }.take(5).forEach { s ->
                val k = matchKey(s.title, s.artist)
                synchronized(waiting) { if (waiting.containsKey(k)) return@forEach; waiting[k] = s }
                lib.requestDownload(s.artist, s.title, s.album, s.durationMs)
            }
        }
    }

    /** A download finished: if Autoplay asked for it, it joins the end of the queue. */
    fun downloaded(e: DownloadEntry) {
        val k = matchKey(e.title, e.artist)
        val was = synchronized(waiting) { waiting.remove(k) } ?: return
        if (!app.prefs.autoplay) return
        main.postDelayed({
            val t = app.library.findSong(was.title, was.artist) ?: return@postDelayed
            if (t.matchKey !in app.player.queueTracks().map { it.matchKey }) app.player.appendAutoplay(listOf(t))
        }, 1500)
    }

    private val WebCatalog.Song.matchKey get() = matchKey(title, artist)
}
