package com.ipodemu.library

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import org.json.JSONObject
import java.io.File

/**
 * What each song's file really is (codec, sample rate, bit depth, bit rate), as FLACie Web read it with ffprobe / Jellyfin: nothing here is guessed
 * from a file extension. The phone fetches the whole table in one request (GET /api/audiofacts), keeps a copy in its files, and looks songs up
 * by the same file key the library merge uses. Songs the server has not read yet simply have no entry.
 */
object AudioFacts {
    /** [gainDb]: the gain Jellyfin measured to bring the song to a common loudness (NaN when the server gave none). */
    class Fact(val codec: String, val rate: Int, val depth: Int, val kbps: Int, val gainDb: Float = Float.NaN) {
        val lossless: Boolean get() = QualityRule.isLossless(codec)
        /** Lossless and better than CD: more than 16 bits, or above 44.1 kHz (the one rule, [QualityRule]). */
        val hiRes: Boolean get() = QualityRule.isHiRes(lossless, rate, depth)
    }

    @Volatile private var map: Map<String, Fact> = emptyMap()
    /** Songs the server was still reading when it last answered. */
    @Volatile var pending = 0; private set
    /** Changes whenever a new table arrives, so a screen that reads it (a badge, a filter) draws again. */
    var version by mutableIntStateOf(0); private set

    /** Facts the phone read from the files' own tags (NAS songs), used where the server has none. */
    private val local = java.util.concurrent.ConcurrentHashMap<String, Fact>()

    /** Facts Jellyfin itself gave with the song listing (see [JellyfinFacts]); needs no other server. */
    private val jf = java.util.concurrent.ConcurrentHashMap<String, Fact>()

    /** Order: FLACie Web's table (when it has the file), then Jellyfin's own, then what the phone read from the NAS file's tags. */
    fun of(t: Track): Fact? = t.fileKey?.let { map[it] ?: jf[it] ?: local[it] }

    fun putJellyfin(facts: Map<String, Fact>) { if (facts.isEmpty()) return; jf.putAll(facts); version++ }
    fun removeJellyfin(keys: Collection<String>) { if (keys.isEmpty()) return; keys.forEach { jf.remove(it) }; version++ }
    fun clearJellyfin() { jf.clear(); version++ }
    val jellyfinSize: Int get() = jf.size

    fun saveJellyfin(file: File) {
        try {
            val tmp = File(file.path + ".tmp")
            android.util.JsonWriter(tmp.bufferedWriter(Charsets.UTF_8, 1 shl 16)).use { w ->
                w.beginObject()
                for ((k, f) in jf) { w.name(k).beginArray().value(f.codec).value(f.rate.toLong()).value(f.depth.toLong()).value(f.kbps.toLong()).also { if (!f.gainDb.isNaN()) w.value(f.gainDb.toDouble()) }.endArray() }
                w.endObject()
            }
            file.delete(); tmp.renameTo(file)
        } catch (_: Exception) { }
    }

    fun loadJellyfin(file: File) {
        try {
            if (!file.exists()) return
            val out = HashMap<String, Fact>()
            android.util.JsonReader(file.bufferedReader(Charsets.UTF_8, 1 shl 16)).use { r ->
                r.beginObject()
                while (r.hasNext()) { val k = r.nextName(); r.beginArray(); val codec = r.nextString(); val rate = r.nextInt(); val depth = r.nextInt(); val kbps = r.nextInt(); val gain = if (r.hasNext()) r.nextDouble().toFloat() else Float.NaN; out[k] = Fact(codec, rate, depth, kbps, gain); r.endArray() }
                r.endObject()
            }
            jf.putAll(out); version++
        } catch (_: Exception) { }
    }

    /** The tag reader's findings for NAS files; the server's table, when it has the file, still wins. */
    fun putLocal(facts: Map<String, Fact>) {
        local.putAll(facts)
        version++
    }
    val size: Int get() = map.size

    fun load(file: File) {
        try { if (file.exists()) parse(file.readText()) } catch (_: Exception) { }
    }

    /** Asks the server for the table and keeps it; quietly does nothing when there is no server or it is unreachable. */
    fun refresh(web: FlacieWebClient, file: File) {
        try {
            if (!web.available) return
            val raw = web.audioFactsRaw()
            parse(raw)
            file.writeText(raw)
        } catch (_: Exception) { }
    }

    private fun parse(raw: String) {
        val o = JSONObject(raw); val f = o.getJSONObject("facts")
        val out = HashMap<String, Fact>(f.length() * 2)
        for (k in f.keys()) { val a = f.getJSONArray(k); out[k] = Fact(a.optString(0), a.optInt(1), a.optInt(2), a.optInt(3)) }
        pending = o.optInt("pending")
        map = out; version++
    }
}
