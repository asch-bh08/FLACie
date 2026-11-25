package com.ipodemu.playback

/**
 * One spoken request at a time, answered once and at once (owner, 25 Nov: "sometimes it plays the song 2 mins later than you asked for"). Each request
 * gets a ticket; an answer is carried out only for the latest ticket, only the first time and only within [STALE_MS]. The phone stops waiting for the
 * server at [ASK_DEADLINE_MS] and answers from the phone itself ([OfflineAsk]); the server's answer that turns up after that is dropped, not played a
 * minute later. The asks used to queue behind the DJ's own song lines on one thread (each up to 15 s on a weak signal), so a request could wait for
 * several of them, and whatever came back was played however late. Pure, unit-tested; [PlayerController.askDj] keeps one.
 */
class DjAskGate(private val clock: () -> Long = { android.os.SystemClock.elapsedRealtime() }) {
    enum class Landed { PLAY_NOW, UP_NEXT, DROP }

    @Volatile private var seq = 0
    @Volatile private var answered = -1
    @Volatile private var startedAt = 0L

    /** A new request: any earlier one still waiting is superseded. */
    fun begin(): Int { seq++; startedAt = clock(); return seq }

    /** The person moved on (pressed play on something, pressed the mic again): an answer still on its way is dropped. */
    fun cancel() { seq++ }

    /** Whether [ticket] is still the request being waited for (not superseded, not answered yet). */
    fun waiting(ticket: Int) = ticket == seq && answered != ticket

    /** An answer for [ticket] arrived: true when it is to be carried out (latest, first, in time). */
    fun answer(ticket: Int): Boolean {
        if (!waiting(ticket) || clock() - startedAt > STALE_MS) return false
        answered = ticket
        return true
    }

    /** The deadline passed for [ticket]: true when it is still unanswered (the phone answers it itself now; the server's answer will be dropped). */
    fun timedOut(ticket: Int): Boolean {
        if (!waiting(ticket)) return false
        answered = ticket
        return true
    }

    /** A song fetched for [ticket] has landed: played at once when it came soon, put up next when it came later (it never cuts into what is playing
     *  minutes after the ask), and not at all once something else was asked for or started. */
    fun landed(ticket: Int): Landed = when {
        ticket != seq -> Landed.DROP
        clock() - startedAt <= LANDED_NOW_MS -> Landed.PLAY_NOW
        else -> Landed.UP_NEXT
    }

    /** Ms since [ticket] was asked, or null when it is not the latest. */
    fun age(ticket: Int): Long? = if (ticket == seq) clock() - startedAt else null

    companion object {
        /** How long the phone waits for the server's answer before it answers from the phone (a car on a weak signal must not sit in silence). */
        const val ASK_DEADLINE_MS = 6_000L
        /** The connection's own limit, inside the deadline. */
        const val CONNECT_MS = 3_000L
        /** An answer older than this is never carried out. */
        const val STALE_MS = 15_000L
        /** A fetched song that lands within this of the ask plays at once; later it goes up next. */
        const val LANDED_NOW_MS = 45_000L
    }
}
