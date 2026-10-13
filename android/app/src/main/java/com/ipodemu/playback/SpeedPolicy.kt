package com.ipodemu.playback

/** Playback speed rules, apart from the player: the allowed steps, which items count as spoken word, and the per-item memory of speed and place. */
object SpeedPolicy {
    const val MIN = 0.5f
    const val MAX = 3.0f
    val STEPS = listOf(0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f)
    /** Items at least this long remember where you stopped. */
    const val LONG_ITEM_MS = 20 * 60_000L
    /** An item this close to its end counts as finished: no resume point is kept. */
    const val FINISHED_MS = 15_000L
    /** A resume point earlier than this is not worth restoring. */
    const val MIN_RESUME_MS = 5_000L

    fun clamp(s: Float): Float = if (s.isNaN()) 1f else s.coerceIn(MIN, MAX)

    /** The step after [s] (wraps from the fastest back to the slowest) or before it. */
    fun step(s: Float, up: Boolean): Float {
        val c = clamp(s)
        return if (up) STEPS.firstOrNull { it > c + 0.001f } ?: STEPS.last() else STEPS.lastOrNull { it < c - 0.001f } ?: STEPS.first()
    }

    /** "1x", "1.25x", "0.5x". */
    fun label(s: Float): String {
        val c = clamp(s)
        val t = if (Math.abs(c - Math.round(c)) < 0.001f) Math.round(c).toString() else "%.2f".format(java.util.Locale.ROOT, c).trimEnd('0').trimEnd('.')
        return t + "x"
    }

    private val spokenWords = listOf("audiobook", "audio book", "audio-book", "podcast", "spoken", "speech", "lecture", "sermon", "radio play", "radio drama", "hörbuch", "hoerbuch", "story", "stories", "comedy", "talk", "interview", "news")

    /** Spoken word by genre. Music genres never match; a long file with no genre is not guessed to be spoken (see [isLong]). */
    fun isSpoken(genre: String): Boolean {
        val g = genre.lowercase()
        return g.isNotBlank() && spokenWords.any { g.contains(it) }
    }

    fun isLong(durationMs: Long): Boolean = durationMs >= LONG_ITEM_MS

    /** The speed controls (skip 10 back / 30 forward, speed chip first) are shown for spoken or long items, or whenever the speed is not 1x. */
    fun showLongItemControls(genre: String, durationMs: Long, speed: Float): Boolean = isSpoken(genre) || isLong(durationMs) || Math.abs(speed - 1f) > 0.001f

    /** A key that stays the same for the same item across servers' URL changes: the 32-hex Jellyfin item id when the path has one, else the path. */
    fun keyOf(path: String): String = ITEM_ID.find(path)?.groupValues?.get(1)?.lowercase() ?: path
    private val ITEM_ID = Regex("/Audio/([0-9a-fA-F]{32})/")

    /** Where to start an item: its remembered place if it is long enough to remember, not finished and not trivially early; else the start. */
    fun resumeAt(savedMs: Long?, durationMs: Long): Long {
        if (savedMs == null || !isLong(durationMs)) return 0L
        if (savedMs < MIN_RESUME_MS) return 0L
        if (durationMs > 0L && savedMs >= durationMs - FINISHED_MS) return 0L
        return savedMs
    }

    /** True when a place should be stored (long item, not within the last seconds, past the first seconds). */
    fun shouldSavePlace(positionMs: Long, durationMs: Long): Boolean = isLong(durationMs) && positionMs >= MIN_RESUME_MS && positionMs < durationMs - FINISHED_MS
}

/**
 * Remembers one number per item, newest kept when over [cap]; saved as text "key=value" lines so it can live in one preference. Used for the speed of an item
 * (1x is the default and is not stored) and for the place in a long item.
 */
class ItemMemory(private val cap: Int = 400, private val defaultValue: Double? = null) {
    private val map = LinkedHashMap<String, Double>()

    val size: Int get() = map.size
    fun get(key: String): Double? = map[key]

    fun set(key: String, value: Double?) {
        map.remove(key)
        if (value == null || (defaultValue != null && Math.abs(value - defaultValue) < 0.0005)) return
        map[key] = value
        while (map.size > cap) map.remove(map.keys.first())
    }

    fun encode(): String = map.entries.joinToString("\n") { it.key.replace("\n", " ").replace("=", "%3D") + "=" + it.value }

    companion object {
        fun decode(text: String?, cap: Int = 400, defaultValue: Double? = null): ItemMemory {
            val m = ItemMemory(cap, defaultValue)
            if (text.isNullOrBlank()) return m
            for (line in text.lineSequence()) {
                val i = line.lastIndexOf('='); if (i <= 0) continue
                val v = line.substring(i + 1).toDoubleOrNull() ?: continue
                m.set(line.substring(0, i).replace("%3D", "="), v)
            }
            return m
        }
    }
}
