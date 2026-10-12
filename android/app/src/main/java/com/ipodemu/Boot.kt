package com.ipodemu

import android.os.Process
import android.os.SystemClock
import android.util.Log
import java.util.concurrent.ConcurrentHashMap

/**
 * Launch timing and cover coverage, printed to logcat (`adb logcat -s FLACieBoot FLACieProbe`). Times are milliseconds since the process started,
 * so they cover everything a cold start does before and after Application.onCreate. Cheap enough to stay on in release builds.
 */
object Boot {
    const val TAG = "FLACieBoot"
    private val marks = ConcurrentHashMap<String, Long>()

    fun sinceStart(): Long = SystemClock.elapsedRealtime() - Process.getStartElapsedRealtime()

    fun mark(name: String) { Log.i(TAG, "${sinceStart()} ms  $name") }

    /** Logs the first time only (a screen that recomposes many times). */
    fun once(name: String) { if (marks.putIfAbsent(name, sinceStart()) == null) mark(name) }

    inline fun <T> span(name: String, block: () -> T): T {
        val t0 = SystemClock.elapsedRealtime()
        try { return block() } finally { Log.i(TAG, "${sinceStart()} ms  $name took ${SystemClock.elapsedRealtime() - t0} ms") }
    }
}

/**
 * Which cover slots are on screen right now and which of them already have their picture, so "how many visible rows have a cover after 1 s / 3 s / 10 s"
 * can be read from logcat. A slot is one [com.ipodemu.player.rememberArt] in composition with a cover key.
 */
object CoverProbe {
    private const val TAG = "FLACieProbe"
    private class Slot(val key: String, val at: Long, @Volatile var loadedAt: Long = 0L)
    private val slots = ConcurrentHashMap<Any, Slot>()
    @Volatile private var started = false

    fun enter(token: Any, key: String, have: Boolean) {
        val n = Boot.sinceStart()
        slots[token] = Slot(key, n, if (have) n else 0L)
        if (!started) { started = true; startReports() }
    }
    fun loaded(token: Any) { slots[token]?.let { if (it.loadedAt == 0L) it.loadedAt = Boot.sinceStart() } }
    fun exit(token: Any) { slots.remove(token) }

    fun report(label: String) {
        val all = slots.values.toList()
        val with = all.count { it.loadedAt > 0L }
        val pct = if (all.isEmpty()) 0 else with * 100 / all.size
        val g = try { com.ipodemu.library.JellyfinNet.gate.stats().line() } catch (_: Throwable) { "" }
        Log.i(TAG, "${Boot.sinceStart()} ms  $label visible=${all.size} withCover=$with pct=$pct | gate $g")
    }

    private fun startReports() {
        val h = android.os.Handler(android.os.Looper.getMainLooper())
        // the first cover slot appears when the first screen is drawn; 1 s, 3 s and 10 s are counted from the process start (the tap on the icon)
        for (s in longArrayOf(1000, 2000, 3000, 5000, 10000)) {
            val wait = s - Boot.sinceStart()
            h.postDelayed({ report("t=${s / 1000}s") }, wait.coerceAtLeast(0))
        }
    }
}
