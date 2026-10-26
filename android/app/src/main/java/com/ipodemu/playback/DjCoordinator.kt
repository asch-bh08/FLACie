package com.ipodemu.playback

import com.ipodemu.library.Track
import org.json.JSONObject
import kotlin.math.abs
import kotlin.math.roundToLong

/** A song as the DJ needs it (`DjSong` in DjContracts.cs). The phone sends what it has; blank fields are never spoken. */
data class DjSong(
    val title: String, val artist: String, val album: String = "", val albumArtist: String = "", val year: Int = 0, val genre: String = "",
    val durationMs: Long = 0, val path: String = "", val jellyfinId: String? = null, val trackNo: Int = 0, val discNo: Int = 0,
) {
    fun toJson(): JSONObject = JSONObject().put("title", title).put("artist", artist).put("album", album).put("albumArtist", albumArtist).put("year", year)
        .put("genre", genre).put("durationMs", durationMs).put("path", path).put("jellyfinId", jellyfinId ?: JSONObject.NULL).put("trackNo", trackNo).put("discNo", discNo)

    companion object {
        private val jfId = Regex("/Audio/([0-9a-fA-F]{32})/")
        /** A Jellyfin song goes as its file's path on the server (what FLACie Web knows it by) and its Jellyfin id; anything else by the app's own path. */
        fun of(t: Track) = DjSong(t.title, t.artist, t.album, t.albumArtist, t.year, t.genre, t.durationMs, t.filePath.ifEmpty { t.path },
            jfId.find(t.path)?.groupValues?.get(1), t.trackNo, t.discNo)
    }
}

/**
 * The phone's side of the AI DJ, with no player in it (so it can be tested; PlayerController applies it). When a song starts, the player asks the server
 * about the song after it ([request], `POST /api/dj/next`); the answer is held as the cue for that song ([accept]), keyed by its path, and said only when
 * the player moves to that very song by itself ([changed] with [Change.AUTO]): a skip, a new queue or another song says nothing. It also keeps the Now
 * Playing "DJ for this queue" switch ([queueDj], reset by every new queue) and remembers recent quick skips (the server stays quiet after one).
 */
class DjCoordinator {
    /** How the player got to a new song. */
    enum class Change { AUTO, SKIP, NEW_QUEUE, REPEAT }

    /** The Now Playing switch, for this queue only: null = untouched (the settings and the playlist decide). */
    var queueDj: Boolean? = null; private set

    /** The last two changes of song: true = a song was skipped within [QUICK_SKIP_MS] of hearing it. */
    private val recent = ArrayDeque<Boolean>()
    val skippedRecently: Boolean get() = recent.any { it }

    /** The cue waiting for the song with path [pendingPath]. */
    var pending: DjCue? = null; private set
    var pendingPath: String? = null; private set
    private var askedKey: String? = null
    private var ticket = 0

    /** Now Playing's DJ button: the opposite of what is in force now, for this queue. Any cue already fetched is dropped (it is asked again if on). */
    fun flip(effectiveNow: Boolean): Boolean { val on = !effectiveNow; queueDj = on; forget(); return on }

    /** Something new started (play, a hand-over, the queue of last time, Clear): the switch goes back to untouched and no cue is kept. */
    fun newQueue() { queueDj = null; forget() }

    /** Drops the cue and lets the next [ask] go out again (the DJ was switched, the settings changed). */
    fun forget() { pending = null; pendingPath = null; askedKey = null; ticket++ }

    /** Clear: everything goes, the skip memory too. */
    fun reset() { newQueue(); recent.clear() }

    /**
     * The player moved to the song with path [newPath]; for a skip, [leftAtMs] is how far into the song it left the skip came (-1 when not known).
     * Returns the cue to say now: only after a song ended by itself and only when the cue was fetched for this very song. The cue is used up either way.
     */
    fun changed(change: Change, newPath: String?, leftAtMs: Long = -1L): DjCue? {
        when (change) {
            Change.AUTO -> remember(false)
            Change.SKIP -> remember(leftAtMs in 0 until QUICK_SKIP_MS)
            Change.NEW_QUEUE, Change.REPEAT -> {}
        }
        val cue = if (change == Change.AUTO && newPath != null && newPath == pendingPath) pending else null
        pending = null; pendingPath = null; askedKey = null
        return cue
    }

    private fun remember(quickSkip: Boolean) { recent.addLast(quickSkip); while (recent.size > 2) recent.removeFirst() }

    /**
     * Whether to ask the server now. [key] says what would be asked (the next song, the one before it, where): asking again about the same thing is
     * pointless (the server gives the same answer). Returns the ticket to hand to [accept], or -1 when it was already asked. A different question drops
     * the cue of the old one (it was for a song that no longer comes next).
     */
    fun ask(key: String): Int {
        if (key == askedKey) return -1
        askedKey = key; pending = null; pendingPath = null
        return ++ticket
    }

    /** The server's answer to ask [ticket] about the song with path [nextPath]; null = nothing to say. Returns false for an answer that came too late. */
    fun accept(ticket: Int, nextPath: String, cue: DjCue?): Boolean {
        if (ticket != this.ticket) return false
        pending = cue; pendingPath = if (cue != null) nextPath else null
        return true
    }

    /** The body of `POST /api/dj/next` (camelCase of `DjRequest`): asked as [prev] starts, about [next]; so [prev] is expected to play out. */
    fun request(surface: DjSurface, next: DjSong, prev: DjSong?, playlistId: String?, localHour: Int, weekday: Int): JSONObject =
        JSONObject().put("surface", surface.wire).put("next", next.toJson()).put("prev", prev?.toJson() ?: JSONObject.NULL)
            .put("prevPlayedOut", true).put("skippedRecently", skippedRecently).put("playlistId", playlistId?.takeIf { it.isNotEmpty() } ?: JSONObject.NULL)
            .put("queueDj", queueDj ?: JSONObject.NULL).put("localHour", localHour).put("weekday", weekday).put("why", JSONObject.NULL)

    companion object {
        /** A song left within this long of starting counts as skipped. */
        const val QUICK_SKIP_MS = 10_000L
        /** .NET's DayOfWeek, which the server reads: 0 = Sunday ... 6 = Saturday. */
        fun weekday(d: java.time.DayOfWeek): Int = d.value % 7
    }
}

/**
 * The music's volume while the DJ speaks: down to [low] over [downMs], back to full over [upMs], straight lines. A change of direction halfway starts
 * from where the level is (no jump), and takes the share of the time that the distance left needs. Pure, unit-tested; the player ticks it.
 */
class DuckRamp(val low: Float = LOW, private val downMs: Long = DOWN_MS, private val upMs: Long = UP_MS) {
    private var from = 1f
    private var to = 1f
    private var startedAt = 0L
    private var span = 0L

    fun duck(now: Long) = go(low, now)
    fun release(now: Long) = go(1f, now)
    /** Full volume at once (the player stopped, nothing to ramp back from). */
    fun reset() { from = 1f; to = 1f; span = 0L }

    private fun go(target: Float, now: Long) {
        val cur = level(now)
        from = cur; to = target; startedAt = now
        span = ((abs(target - cur) / (1f - low)) * (if (target < cur) downMs else upMs)).roundToLong()
    }

    /** The multiplier for the music at time [now]: 1 = untouched. */
    fun level(now: Long): Float {
        if (span <= 0L || now >= startedAt + span) return to
        if (now <= startedAt) return from
        return from + (to - from) * ((now - startedAt).toFloat() / span)
    }

    /** The ramp has arrived (no more ticks needed). */
    fun settled(now: Long): Boolean = span <= 0L || now >= startedAt + span
    /** Heading down or held down. */
    val ducking: Boolean get() = to < 1f

    companion object {
        const val LOW = 0.3f
        const val DOWN_MS = 400L
        const val UP_MS = 600L
    }
}
