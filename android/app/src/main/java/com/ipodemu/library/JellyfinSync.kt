package com.ipodemu.library

import kotlinx.coroutines.CancellationException
import java.util.concurrent.atomic.AtomicBoolean

/** What the sync needs from the server (the real one is [JellyfinDirectClient]; tests use a fake). */
interface JfApi {
    suspend fun page(startIndex: Int, limit: Int, minSaved: String?): JellyfinDirectClient.Page
    suspend fun count(): Int
    suspend fun allIds(): Set<String>
    suspend fun byIds(ids: List<String>): List<JellyfinDirectClient.Item>
}

/**
 * The phone's copy of the Jellyfin song list, by item id, plus the delta cursor: the newest DateLastSaved seen. After the first full pass
 * only items saved since that moment are asked for.
 */
class JellyfinIndex {
    private val byId = LinkedHashMap<String, Track>()
    @Volatile private var snapshot: List<Track>? = null
    /** Newest DateLastSaved seen (normalised, see [normIso]); "" = no full sync has finished yet. */
    var cursor: String = ""

    val size: Int get() = synchronized(this) { byId.size }
    fun tracks(): List<Track> = synchronized(this) { snapshot ?: ArrayList(byId.values).also { snapshot = it } }
    fun has(id: String) = synchronized(this) { byId.containsKey(id.replace("-", "").lowercase()) }

    fun idOf(t: Track): String? = ID_RE.find(t.path)?.groupValues?.get(1)?.lowercase()

    fun replaceAll(tracks: List<Track>) = synchronized(this) {
        byId.clear(); for (t in tracks) idOf(t)?.let { byId[it] = t }; snapshot = null
    }

    /** Inserts or replaces; returns how many were new. */
    fun upsert(items: List<Pair<String, Track>>): Int = synchronized(this) {
        var added = 0
        for ((id, t) in items) { if (byId.put(id.replace("-", "").lowercase(), t) == null) added++ }
        snapshot = null; added
    }

    fun remove(ids: Collection<String>): List<Track> = synchronized(this) {
        val gone = ids.mapNotNull { byId.remove(it.replace("-", "").lowercase()) }
        if (gone.isNotEmpty()) snapshot = null
        gone
    }

    /** Keeps only [serverIds]; returns what was dropped (deleted on the server). */
    fun retainOnly(serverIds: Set<String>): List<Track> = synchronized(this) {
        val gone = ArrayList<Track>()
        val it = byId.entries.iterator()
        while (it.hasNext()) { val e = it.next(); if (e.key !in serverIds) { gone += e.value; it.remove() } }
        if (gone.isNotEmpty()) snapshot = null
        gone
    }

    fun missingFrom(serverIds: Set<String>): List<String> = synchronized(this) { serverIds.filter { it !in byId } }

    /** Raises the cursor to [iso] if it is newer. */
    fun advance(iso: String) { if (iso.isNotEmpty() && iso > cursor) cursor = iso }

    fun clear() = synchronized(this) { byId.clear(); snapshot = null; cursor = "" }

    companion object { val ID_RE = Regex("/Audio/([0-9a-fA-F]{32})/") }
}

/** Where a sync reports to (the Library, the fact table, the cover store). All called from the sync's coroutine. */
interface JfSink {
    /** Title clean-up for songs without tags (needs the whole list for the artist names it learns from). */
    fun clean(tracks: List<Track>, wholeLibrary: Boolean): List<Track>
    fun publish(tracks: List<Track>, complete: Boolean)
    fun facts(add: Map<String, AudioFacts.Fact>, removedKeys: List<String>)
    fun coverTags(tags: Map<String, String>)
    fun cursorChanged(cursor: String)
    fun persist()
}

/**
 * Keeps the Jellyfin song list current with as little traffic as possible:
 *  - the first pass pages through the catalog (songs are usable as soon as the first page lands; each page also brings the stream facts,
 *    the cover tags and the save dates) and records the delta cursor;
 *  - every later pass asks only for what was saved since the cursor, then checks the server's count: if it differs, ids are compared and
 *    what was deleted is dropped (and anything the phone missed is fetched);
 *  - a LibraryChanged message from the live socket can remove songs at once ([removed]) and starts a pass instead of polling.
 */
class JellyfinSync(
    private val api: JfApi,
    val index: JellyfinIndex,
    private val sink: JfSink,
    private val pageSize: Int = 500,
    private val clock: () -> Long = { System.currentTimeMillis() },
    private val publishGapMs: Long = 1500,
    /** The server's song count is compared with ours at most this often (a deletion is also pushed by the live socket). */
    private val countEveryMs: Long = 30 * 60_000L,
) {
    private val running = AtomicBoolean(false)
    @Volatile private var again = false
    @Volatile var progress: String = ""; private set
    @Volatile var lastError: String? = null; private set
    var fullPasses = 0; private set
    var deltaPasses = 0; private set
    /** When the count was last compared (epoch ms; saved with the cursor so a quick restart does not repeat it). */
    @Volatile var lastCountAt = 0L
    @Volatile private var countWanted = false
    /** A library change message arrived: the next pass also compares the count. */
    fun wantCount() { countWanted = true }

    /** Runs a pass (full when no pass has finished yet, else a delta). A call while one runs makes it run once more afterwards. */
    suspend fun sync() {
        if (!running.compareAndSet(false, true)) { again = true; return }
        try {
            do {
                again = false
                try {
                    if (index.cursor.isEmpty()) full() else delta()
                    lastError = null
                } catch (e: CancellationException) { throw e }
                catch (e: Exception) { lastError = e.message ?: e.javaClass.simpleName; throw e }
            } while (again)
        } finally { running.set(false); progress = "" }
    }

    /** Songs the server deleted (a LibraryChanged message): gone from the phone at once, no request needed. */
    fun removed(ids: Collection<String>) {
        val gone = index.remove(ids)
        if (gone.isEmpty()) return
        sink.facts(emptyMap(), gone.mapNotNull { it.fileKey })
        sink.publish(index.tracks(), true); sink.persist()
    }

    private fun report(items: List<JellyfinDirectClient.Item>) {
        val facts = HashMap<String, AudioFacts.Fact>(); val tags = HashMap<String, String>()
        for (it in items) {
            val k = it.track.fileKey
            if (it.fact != null && k != null) facts[k] = it.fact
            if (it.coverKey != null && it.coverTag != null) tags[it.coverKey] = it.coverTag
        }
        sink.facts(facts, emptyList()); sink.coverTags(tags)
    }

    private suspend fun full() {
        val keepShowing = index.size > 0   // an older list (before this version) stays on screen until the new one is complete
        val seen = LinkedHashMap<String, JellyfinDirectClient.Item>()
        var start = 0; var maxSaved = ""; var lastPublish = 0L; var pages = 0
        while (true) {
            val p = api.page(start, pageSize, null)
            pages++
            for (it in p.items) { seen[it.id.lowercase()] = it; if (it.savedAt > maxSaved) maxSaved = it.savedAt }
            report(p.items)
            start += p.items.size
            progress = "$start of ${p.total}"
            val done = p.items.isEmpty() || start >= p.total
            if (!done && !keepShowing && (pages <= 2 || clock() - lastPublish >= publishGapMs)) {
                lastPublish = clock()
                sink.publish(sink.clean(seen.values.map { it.track }, true), false)
            }
            if (done) break
        }
        val cleaned = sink.clean(seen.values.map { it.track }, true)
        index.replaceAll(cleaned)
        index.cursor = ""; index.advance(maxSaved)
        if (index.cursor.isEmpty()) index.cursor = "0001-01-01T00:00:00.0000000Z"   // an empty library is still a finished pass
        fullPasses++
        sink.publish(index.tracks(), true); sink.persist(); sink.cursorChanged(index.cursor)
        // the pass itself is done and saved; a failing count check only means it is tried again with the next pass
        try { checkCount(); sink.cursorChanged(index.cursor) } catch (e: CancellationException) { throw e } catch (_: Exception) { }
    }

    private suspend fun delta() {
        val since = index.cursor
        val changed = LinkedHashMap<String, JellyfinDirectClient.Item>()
        var start = 0; var maxSaved = ""
        while (true) {
            val p = api.page(start, pageSize, since)
            // MinDateLastSaved is inclusive: the newest song of the last pass comes back every time; it is a change only if it is newer than the cursor or new to us
            for (it in p.items) {
                if (it.savedAt <= since && index.has(it.id)) continue
                changed[it.id.lowercase()] = it; if (it.savedAt > maxSaved) maxSaved = it.savedAt
            }
            start += p.items.size
            if (p.items.isEmpty() || start >= p.total) break
        }
        deltaPasses++
        if (changed.isNotEmpty()) {
            report(changed.values.toList())
            val cleaned = sink.clean(changed.values.map { it.track }, false)
            val ids = changed.keys.toList()
            index.upsert(ids.indices.map { ids[it] to cleaned[it] })
            index.advance(maxSaved)
            sink.publish(index.tracks(), true); sink.persist()
            if (maxSaved.isNotEmpty()) sink.cursorChanged(index.cursor)
        }
        // the count costs one more (tiny) request: only when something changed, a change message came, or it has not been compared for a while
        if (changed.isNotEmpty() || countWanted || clock() - lastCountAt >= countEveryMs) {
            checkCount()
            sink.cursorChanged(index.cursor)   // saves lastCountAt with the cursor
        }
    }

    /** The server's count differs from ours: something was deleted (or missed). Compare ids. */
    private suspend fun checkCount() {
        countWanted = false
        val n = api.count()
        lastCountAt = clock()
        if (n < 0 || n == index.size) return
        val ids = api.allIds()
        val gone = index.retainOnly(ids)
        val missing = index.missingFrom(ids)
        var fetched = emptyList<JellyfinDirectClient.Item>()
        if (missing.isNotEmpty()) {
            fetched = api.byIds(missing)
            report(fetched)
            val cleaned = sink.clean(fetched.map { it.track }, false)
            index.upsert(fetched.indices.map { fetched[it].id to cleaned[it] })
            for (it in fetched) index.advance(it.savedAt)
        }
        if (gone.isNotEmpty()) sink.facts(emptyMap(), gone.mapNotNull { it.fileKey })
        if (gone.isNotEmpty() || fetched.isNotEmpty()) { sink.publish(index.tracks(), true); sink.persist(); sink.cursorChanged(index.cursor) }
    }
}

/** What a message from Jellyfin's live socket means for the song list. */
internal class ServerChange(
    /** Ids the server says it removed: dropped from the phone at once. */
    val removed: List<String>,
    /** Start one (debounced) delta pass. */
    val sync: Boolean,
    /** The next pass also compares the server's song count. */
    val wantCount: Boolean,
    /** Only if the phone has not synced for a while ("SocketOpen": the socket was down, changes may have been missed). */
    val onlyIfStale: Boolean = false,
)

/** Pure: the reaction to one socket message, or null when it does not concern the list (play counts, favourites, sessions...). */
internal fun serverChange(type: String, data: org.json.JSONObject?): ServerChange? = when (type) {
    "LibraryChanged" -> ServerChange(
        removed = data?.optJSONArray("ItemsRemoved")?.let { a -> (0 until a.length()).map { a.optString(it) }.filter { it.isNotBlank() } } ?: emptyList(),
        sync = true, wantCount = true,
    )
    "SocketOpen" -> ServerChange(emptyList(), sync = true, wantCount = false, onlyIfStale = true)
    else -> null   // UserDataChanged and the rest: the song list does not depend on them
}
