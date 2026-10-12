package com.ipodemu.library

import android.os.Handler
import android.os.Looper
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
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
    /** Paths of the songs Autoplay itself put in the queue (Up next draws a divider above the first of them). */
    private val added = HashSet<String>()
    /** Changes whenever the set of requested suggestions changes (Up next redraws). */
    var rev by androidx.compose.runtime.mutableIntStateOf(0); private set

    fun isAutoplay(t: Track): Boolean = synchronized(added) { t.path in added }
    private fun mark(list: List<Track>) { synchronized(added) { list.forEach { added.add(it.path) }; if (added.size > 400) { val keep = added.toList().takeLast(200); added.clear(); added.addAll(keep) } } }

    /** The suggestions asked for and not yet in the queue, in the order they were chosen (= the order they will play); Up next shows them, display only. */
    fun pending(): List<UpNextMerge.Pending> = if (!app.prefs.autoplay) emptyList() else synchronized(waiting) {
        waiting.map { (k, s) -> UpNextMerge.Pending(k, s.title, s.artist, s.album, s.durationMs, s.artUrl) }
    }

    /** The player calls this as a song starts when little is left after it. */
    fun queueLow() {
        if (!app.prefs.autoplay || working || app.player.interceptor != null) return
        val seed = app.player.current ?: return
        working = true
        scope.launch {
            try { refill(seed) } catch (e: Exception) { android.util.Log.w("FLACie", "autoplay failed: ${e.message}", e) } finally { working = false }
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
            ?: try { WebCatalog.related(seed.artist, seed.title) } catch (e: Exception) { android.util.Log.w("FLACie", "related failed: $e", e); emptyList() }.also { synchronized(related) { related[key] = System.currentTimeMillis() to it } }

        android.util.Log.d("FLACie", "autoplay: ${suggestions.size} suggestions for ${seed.title}")
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
        if (pick.isNotEmpty()) withContext(Dispatchers.Main) { mark(pick); app.player.appendAutoplay(pick) }

        // songs the library lacks: ask for the next few now so they are there in time
        if (app.prefs.autoplayFetch && lib.downloadsConfigured()) {
            suggestions.asSequence().filter { lib.findSong(it.title, it.artist) == null && it.matchKey !in seen }.take(5).forEach { s ->
                val k = matchKey(s.title, s.artist)
                synchronized(waiting) { if (waiting.containsKey(k)) return@forEach; waiting[k] = s }
                // the tag (and the Up next row) is there at once, before the job's first status arrives
                lib.requestDownload(s.artist, s.title, s.album, s.durationMs)
                main.post { rev++ }
            }
        }
    }

    /**
     * A download finished: if Autoplay asked for it, it joins the end of the queue. Its suggestion row stays in Up next until the song is really in
     * the queue (the library needs a moment to list it), so there is never a gap or a duplicate at the switch.
     */
    fun downloaded(e: DownloadEntry) {
        val k = matchKey(e.title, e.artist)
        val was = synchronized(waiting) { waiting[k] } ?: return
        if (!app.prefs.autoplay) { synchronized(waiting) { waiting.remove(k) }; rev++; return }
        fun attempt(left: Int) {
            val t = app.library.findSong(was.title, was.artist)
            if (t != null) {
                if (t.matchKey !in app.player.queueTracks().map { it.matchKey }) { mark(listOf(t)); app.player.appendAutoplay(listOf(t)) }
                synchronized(waiting) { waiting.remove(k) }
                rev++
            } else if (left > 0) main.postDelayed({ attempt(left - 1) }, 1500) else { synchronized(waiting) { waiting.remove(k) }; rev++ }
        }
        main.postDelayed({ attempt(6) }, 1500)
    }

    private val WebCatalog.Song.matchKey get() = matchKey(title, artist)
}
