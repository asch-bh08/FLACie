package com.ipodemu.library

import org.json.JSONArray
import org.json.JSONObject

/**
 * Devices, "Where you were" (owner, 25 Nov: "a way to pick where the phone was like 1 hour ago"): FLACie Web keeps each device's play states for a day
 * (GET /api/where); this reads them, and says them in the same words as the web page (the server's DeviceLabels).
 */
object WhereYouWere {
    class Moment(
        val id: String, val at: Long, val title: String, val artist: String, val album: String?, val artKey: String?, val itemId: String?, val path: String?,
        val positionMs: Long, val durationMs: Long, val paused: Boolean, val context: String?, val queueLength: Int,
    )
    class Device(val deviceId: String, val device: String, val kind: String, val lastSeen: Long, val moments: List<Moment>)
    /** One moment with its queue, ready to carry on (GET /api/where/{id}). */
    class Full(val moment: Moment, val queue: List<String>, val index: Int)

    private fun text(o: JSONObject, k: String): String? = if (o.isNull(k)) null else o.optString(k).takeIf { it.isNotBlank() }

    private fun moment(o: JSONObject) = Moment(
        o.optString("id"), o.optLong("at"), o.optString("title"), o.optString("artist"), text(o, "album"), text(o, "artKey"), text(o, "itemId"), text(o, "path"),
        o.optLong("positionMs"), o.optLong("durationMs"), o.optBoolean("paused"), text(o, "context"), o.optInt("queueLength"),
    )

    /** The devices, the one seen last first, each with its moments newest first (as the server sends them; sorted again so a reorder can't confuse it). */
    fun parse(json: String): List<Device> {
        val arr = JSONObject(json).optJSONArray("devices") ?: return emptyList()
        return (0 until arr.length()).map { arr.getJSONObject(it) }.map { d ->
            val ms = d.optJSONArray("states") ?: JSONArray()
            Device(d.optString("deviceId"), d.optString("device"), d.optString("kind").ifBlank { kind(d.optString("device")) }, d.optLong("lastSeen"),
                (0 until ms.length()).map { moment(ms.getJSONObject(it)) }.sortedByDescending { it.at })
        }.filter { it.moments.isNotEmpty() }.sortedByDescending { it.lastSeen }
    }

    fun parseOne(json: String): Full {
        val o = JSONObject(json)
        val q = o.optJSONArray("queue") ?: JSONArray()
        return Full(moment(o), (0 until q.length()).map { q.getString(it) }, o.optInt("index"))
    }

    /** What the phone sends about itself (POST /api/where). [queue]: Jellyfin ids, or a path for a song Jellyfin doesn't have. */
    fun report(deviceId: String, device: String, t: Track, itemId: String?, positionMs: Long, paused: Boolean, queue: List<String>, index: Int, context: String?): String =
        JSONObject().put("deviceId", deviceId).put("device", device).put("kind", "Phone").put("title", t.title).put("artist", t.artist).put("album", t.album)
            .put("artKey", t.artKey).put("itemId", itemId).put("path", if (itemId == null) t.path else null).put("positionMs", positionMs).put("durationMs", t.durationMs)
            .put("paused", paused).put("queue", JSONArray(queue)).put("index", index).put("context", context).toString()

    /** Should the phone tell the server now? A new song or a pause/play at once, else once a minute while playing (the server keeps only what is new). */
    fun shouldSend(lastKey: String?, lastPaused: Boolean?, lastAt: Long, key: String, paused: Boolean, now: Long): Boolean =
        key != lastKey || paused != lastPaused || (!paused && now - lastAt >= 60_000)

    // ---- words, the same as the web page's ----

    /** Jellyfin only gives a device's name, so the kind is guessed from it. */
    fun kind(name: String): String {
        val n = name.lowercase()
        return when {
            n.contains("flacie for windows") || n.contains("desktop") || n.contains("windows pc") || n.contains("laptop") -> "Computer"
            n.contains("flacie web") || n.contains("browser") || n.contains("chrome") || n.contains("firefox") || n.contains("edge") || n.contains("safari") -> "Browser"
            n.startsWith("sm-") || n.startsWith("sdk_") || n.contains("pixel") || n.contains("phone") || n.contains("galaxy") || n.contains("iphone") -> "Phone"
            n.contains("echo") || n.contains("alexa") -> "Speaker"
            else -> "Player"
        }
    }

    /** "just now", "5 min ago", "1 h ago", "yesterday". */
    fun ago(at: Long, now: Long): String {
        val mins = ((now - at) / 60_000).coerceAtLeast(0)
        return when {
            mins < 1 -> "just now"
            mins < 60 -> "$mins min ago"
            mins / 60 < 24 -> "${mins / 60} h ago"
            else -> "yesterday"
        }
    }

    /** "2:13", "1:02:05". */
    fun clock(ms: Long): String {
        val s = (ms / 1000).coerceAtLeast(0)
        return if (s >= 3600) String.format(java.util.Locale.ROOT, "%d:%02d:%02d", s / 3600, s % 3600 / 60, s % 60) else String.format(java.util.Locale.ROOT, "%d:%02d", s / 60, s % 60)
    }

    /** "at 2:13 in Road trip", or "at 2:13". */
    fun where(positionMs: Long, context: String?): String = "at " + clock(positionMs) + if (context.isNullOrBlank()) "" else " in $context"
}
