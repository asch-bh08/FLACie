package com.ipodemu.playback

import org.json.JSONArray
import org.json.JSONObject

/**
 * Playback problems kept for FLACie Web (/api/diag): stalls, errors, songs that never started, downloads that failed, what the DJ mic heard, how big a car queue
 * really was. Sent in batches, kept while there is no signal, the oldest dropped past [MAX] (owner, 2 Nov: a bad drive should explain itself). Thread-safe.
 */
object Diag {
    const val MAX = 300
    const val BATCH = 50
    private val pending = ArrayDeque<JSONObject>()

    fun add(kind: String, text: String, at: Long = System.currentTimeMillis()) = synchronized(pending) {
        pending.addLast(JSONObject().put("at", at).put("kind", kind).put("text", text.take(300)))
        while (pending.size > MAX) pending.removeFirst()
        // in logcat too, so an emulator run shows the same steps (unit tests have no Log)
        try { android.util.Log.i("FLACieDiag", "$kind: ${text.take(300)}") } catch (_: Throwable) { }
    }

    /** Up to [BATCH] events as a request body, taken out of the queue; null when there are none. */
    fun take(): Pair<String, List<JSONObject>>? = synchronized(pending) {
        if (pending.isEmpty()) return null
        val out = ArrayList<JSONObject>(); while (out.size < BATCH && pending.isNotEmpty()) out += pending.removeFirst()
        JSONObject().put("events", JSONArray(out)).toString() to out
    }

    /** Puts events back at the front when sending them failed. */
    fun giveBack(events: List<JSONObject>) = synchronized(pending) { for (e in events.asReversed()) pending.addFirst(e); while (pending.size > MAX) pending.removeLast() }

    val size: Int get() = synchronized(pending) { pending.size }
    internal fun clear() = synchronized(pending) { pending.clear() }
}
