package com.ipodemu.library

import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** The kinds of thing the server's listening log keeps, as the numbers it reads (the order of FLACie.Core's ListenKind). */
object ListenKind {
    const val PLAY = 0; const val HEART = 1; const val UNHEART = 2; const val THUMB_DOWN = 3; const val LESS_LIKE_THIS = 4
    const val PLAYLIST_ADD = 5; const val PLAYLIST_REMOVE = 6
}

/** One thing the person did, as POST /api/listen takes it. [id] is made here so a resend is not counted twice; [at] in ms; [heard] and [length] in seconds. */
data class ListenEventJson(val id: String, val kind: Int, val at: Long, val path: String, val title: String, val artist: String,
                           val heard: Double = 0.0, val length: Double = 0.0, val context: String? = null) {
    fun json(): JSONObject = JSONObject().put("id", id).put("kind", kind).put("at", at).put("path", path).put("title", title).put("artist", artist)
        .put("heard", heard).put("length", length).put("context", context ?: JSONObject.NULL)

    companion object {
        fun of(kind: Int, t: Track, heard: Double = 0.0, context: String? = null, now: Long = System.currentTimeMillis()) =
            ListenEventJson(java.util.UUID.randomUUID().toString().replace("-", ""), kind, now, t.path, t.title, t.artist, heard, t.durationMs / 1000.0, context)
        fun from(o: JSONObject) = ListenEventJson(o.getString("id"), o.getInt("kind"), o.getLong("at"), o.optString("path"), o.optString("title"), o.optString("artist"),
            o.optDouble("heard", 0.0), o.optDouble("length", 0.0), if (o.isNull("context")) null else o.optString("context"))
    }
}

/** A skip is under 30 seconds heard; a finish is 90 % or more of the song (the same rules as the server's ListenClass). */
object ListenRules {
    fun isSkip(heard: Double) = heard < 30
    fun isFinish(heard: Double, length: Double) = length > 0 && heard >= length * 0.9
}

/**
 * How much of a song was really heard: the time it was playing, times the speed. A seek moves the place in the song but adds nothing, so
 * jumping to the end is not a finish. Times are passed in (the player gives its clock), so this runs in a unit test.
 */
class PlayMeter {
    private var heardMs = 0.0
    private var since = -1L
    private var rate = 1f

    fun start(now: Long, playing: Boolean, rate: Float = 1f) { heardMs = 0.0; this.rate = rate; since = if (playing) now else -1L }
    fun playing(on: Boolean, now: Long) { if (on) { if (since < 0) since = now } else { bank(now); since = -1L } }
    fun rate(r: Float, now: Long) { bank(now); if (since >= 0) since = now; rate = r }
    /** A seek: the heard time so far stands, nothing is added for the jump. */
    fun seeked() {}
    /** The song is over (or replaced): seconds heard. */
    fun end(now: Long): Double { bank(now); since = -1L; return heardMs / 1000.0 }
    private fun bank(now: Long) { if (since >= 0) { heardMs += (now - since).coerceAtLeast(0L) * rate.toDouble(); since = now } }
}

/**
 * Events waiting to reach the server, kept in `listen-queue-<account>.json` so a phone offline for days loses none. Each account has its own file,
 * so what one person did is never sent under another's sign-in. They leave the file only once the server has taken them; the server ignores an
 * id it already has, so sending a batch twice after a lost reply does no harm.
 */
class ListenQueue(dir: File, owner: String) {
    private val file = File(dir, "listen-queue-" + owner.filter { it.isLetterOrDigit() || it == '-' } + ".json")
    private val tmp = File(file.path + ".tmp")
    private val events = ArrayList<ListenEventJson>()

    init {
        // a crash between taking the old file away and putting the new one in place leaves only the temp file: it is the newest copy
        if (!file.exists() && tmp.exists()) tmp.renameTo(file)
        try { if (file.exists()) JSONArray(file.readText()).let { a -> for (i in 0 until a.length()) events.add(ListenEventJson.from(a.getJSONObject(i))) } }
        catch (e: Exception) {
            // kept aside (not overwritten by the next save), so the events can still be recovered by hand
            events.clear()
            System.err.println("listen queue unreadable, kept as .bad: ${e.message}")
            try { File(file.path + ".bad").delete(); file.renameTo(File(file.path + ".bad")) } catch (_: Exception) { }
        }
    }

    fun add(e: ListenEventJson) = synchronized(events) { events.add(e); if (events.size > MAX) events.subList(0, events.size - MAX).clear(); save() }
    fun pending(): List<ListenEventJson> = synchronized(events) { events.toList() }
    fun accepted(ids: Set<String>) = synchronized(events) { if (events.removeAll { it.id in ids }) save() }

    /** Sends everything waiting, oldest first, a batch at a time; throws (keeping the rest) when a batch fails. */
    fun flush(send: (List<ListenEventJson>) -> Unit) {
        while (true) {
            val batch = synchronized(events) { events.take(BATCH) }
            if (batch.isEmpty()) return
            send(batch)
            accepted(batch.mapTo(HashSet()) { it.id })
        }
    }

    private fun save() {
        val a = JSONArray(); events.forEach { a.put(it.json()) }
        try {
            tmp.writeText(a.toString())
            try { java.nio.file.Files.move(tmp.toPath(), file.toPath(), java.nio.file.StandardCopyOption.ATOMIC_MOVE, java.nio.file.StandardCopyOption.REPLACE_EXISTING) }
            catch (_: Exception) { java.nio.file.Files.move(tmp.toPath(), file.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING) }
        } catch (_: Exception) { }
    }

    companion object {
        const val BATCH = 500
        // a phone offline for months keeps the newest this many; the server takes 2000 per request
        const val MAX = 20_000
        /** Sign-out or another account signing in: every account's waiting events go (the person's own log keeps what already reached it). */
        fun clearAll(dir: File) { dir.listFiles { f -> f.name.startsWith("listen-queue") }?.forEach { it.delete() } }
        fun body(batch: List<ListenEventJson>): String = JSONObject().put("events", JSONArray().also { a -> batch.forEach { a.put(it.json()) } }).toString()
    }
}
