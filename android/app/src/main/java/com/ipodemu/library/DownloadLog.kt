package com.ipodemu.library

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** How a download source is named for people (the log, the Downloads screen). */
object OpenSourceNames {
    fun of(id: String?): String = when (id) {
        "soulseek" -> "Soulseek"; "lidarr" -> "Lidarr"; "ytdl" -> "YouTube"; "archive" -> "Internet Archive"; "audius" -> "Audius"; "jamendo" -> "Jamendo"
        null, "" -> ""; else -> id
    }
}

/** One step of a download's story ("Soulseek had nothing it could finish", "Looking on YouTube..."). */
data class TrailStep(val at: Long, val source: String?, val text: String, val miss: Boolean)

/** A finished download in the log, or a running one. [origin] is "phone" (started here) or "server" (FLACie Web: searches, Autoplay, charts, imports). */
data class DownloadRecord(
    val id: String, val label: String, val kind: String, val startedAt: Long, val finishedAt: Long, val done: Boolean, val source: String?, val message: String,
    val file: String?, val artKey: String?, val trail: List<TrailStep>, val origin: String = "phone",
)

/** A download in progress on this phone; the story builds up as statuses arrive. */
class RunningDownload(val id: String, val label: String, val kind: String, val artKey: String?, val startedAt: Long = System.currentTimeMillis()) {
    @Volatile var message = "Requested"
    @Volatile var current: String? = null
    val trail = java.util.Collections.synchronizedList(mutableListOf<TrailStep>())
}

/** Every download this phone ran, kept across restarts (newest 2000, one JSON line each in files/downloads.jsonl). */
class DownloadLog(ctx: Context) {
    private val file = File(ctx.filesDir, "downloads.jsonl")
    private val items = mutableListOf<DownloadRecord>()   // newest first
    private val keep = 2000

    init {
        try {
            if (file.exists()) file.readLines().asReversed().take(keep).forEach { line -> try { items += fromJson(JSONObject(line)) } catch (_: Exception) { } }
        } catch (_: Exception) { }
    }

    @Synchronized fun add(r: DownloadRecord) {
        items.add(0, r); while (items.size > keep) items.removeAt(items.lastIndex)
        try { file.appendText(toJson(r).toString() + "\n") } catch (_: Exception) { }
    }

    @Synchronized fun all(): List<DownloadRecord> = items.toList()

    companion object {
        fun toJson(r: DownloadRecord) = JSONObject().put("id", r.id).put("label", r.label).put("kind", r.kind).put("startedAt", r.startedAt).put("finishedAt", r.finishedAt)
            .put("done", r.done).put("source", r.source ?: JSONObject.NULL).put("message", r.message).put("file", r.file ?: JSONObject.NULL).put("artKey", r.artKey ?: JSONObject.NULL)
            .put("trail", JSONArray().also { a -> r.trail.forEach { t -> a.put(JSONObject().put("at", t.at).put("source", t.source ?: JSONObject.NULL).put("text", t.text).put("miss", t.miss)) } })

        fun fromJson(o: JSONObject, origin: String = "phone") = DownloadRecord(
            o.optString("id"), o.optString("label"), o.optString("kind", "Search"), o.optLong("startedAt"), o.optLong("finishedAt"), o.optBoolean("done"),
            o.optString("source").takeIf { it.isNotEmpty() && !o.isNull("source") }, o.optString("message"), o.optString("file").takeIf { it.isNotEmpty() && !o.isNull("file") },
            o.optString("artKey").takeIf { it.isNotEmpty() && !o.isNull("artKey") },
            o.optJSONArray("trail")?.let { a -> List(a.length()) { i -> a.getJSONObject(i).let { t -> TrailStep(t.optLong("at"), t.optString("source").takeIf { it.isNotEmpty() && !t.isNull("source") }, t.optString("text"), t.optBoolean("miss")) } } } ?: emptyList(),
            origin,
        )
    }
}
