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
    class Fact(val codec: String, val rate: Int, val depth: Int, val kbps: Int) {
        val lossless: Boolean get() = codec in LOSSLESS
        /** Lossless and better than CD: more than 16 bits, or above 44.1 kHz. */
        val hiRes: Boolean get() = lossless && (depth > 16 || rate > 44_100)
    }

    private val LOSSLESS = setOf("flac", "alac", "wav", "pcm", "aiff", "ape", "wavpack", "wv", "dsd", "tta", "pcm_s16le", "pcm_s24le", "pcm_s32le", "hi-res")
    @Volatile private var map: Map<String, Fact> = emptyMap()
    /** Songs the server was still reading when it last answered. */
    @Volatile var pending = 0; private set
    /** Changes whenever a new table arrives, so a screen that reads it (a badge, a filter) draws again. */
    var version by mutableIntStateOf(0); private set

    fun of(t: Track): Fact? = t.fileKey?.let { map[it] }
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
