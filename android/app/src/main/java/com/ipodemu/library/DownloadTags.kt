package com.ipodemu.library

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import java.util.concurrent.ConcurrentHashMap

/**
 * The wording shared with FLACie Web (beta 54): the source of a song that was just downloaded and is played from the file mover.
 * Pure strings, so the badge, the Info panel and Now Playing cannot drift apart.
 */
object SourceLabel {
    /** The small badge in song lists (it used to say "Streaming"). */
    const val NEW_DOWNLOAD = "New download"
    /** The source line of Now Playing and Info. */
    const val STREAMING_FROM_NEW_DOWNLOAD = "Streaming from new download"

    private fun plain(s: TrackSource) = s.name.lowercase().replaceFirstChar { it.uppercase() }

    /** The list badge: "New download" for a Cloud song, "NAS", else the capitalised source name. */
    fun badge(s: TrackSource): String = when (s) { TrackSource.CLOUD -> NEW_DOWNLOAD; TrackSource.NAS -> "NAS"; else -> plain(s) }
    /** The source line in Now Playing and Info. */
    fun line(s: TrackSource): String = if (s == TrackSource.CLOUD) STREAMING_FROM_NEW_DOWNLOAD else plain(s)
}

/** What the status tag under a song's artist says; [None] when the song is an ordinary library song (no tag). */
sealed class DownloadTag {
    object None : DownloadTag()
    /** A download of this song is running: [stage] is the live stage message ("Searching Soulseek...", "Downloading 40%..."). */
    data class Downloading(val stage: String) : DownloadTag()
    /** Finished at [finishedMs] (epoch ms), not longer ago than [DownloadTags.WINDOW_MS]. */
    data class Recent(val finishedMs: Long) : DownloadTag()
    /** A fetch failed at [atMs], within the window. */
    data class Failed(val atMs: Long) : DownloadTag()
}

/** The pure rules of the download tags (the same contract as FLACie Web's `DownloadStatusOf` / `AgeLabel`). */
object DownloadTags {
    /** "Recent" means recent: 60 minutes after the download finished, then the tag is gone and the song is a normal library song. */
    const val WINDOW_MS = 60L * 60_000L
    /** A finish time later than now by more than this is not believed (a wrong clock must not make a tag last for ever). */
    const val FUTURE_SLACK_MS = 60_000L

    /**
     * The age text of a download that finished at [finishedMs]: "just now" under 5 s, "Ns ago" under a minute, "N min ago" under an hour,
     * null when it finished a window ago or more, or lies in the future by more than a minute.
     */
    fun ageLabel(nowMs: Long, finishedMs: Long): String? {
        val age = nowMs - finishedMs
        if (age < -FUTURE_SLACK_MS || age >= WINDOW_MS) return null
        return when {
            age < 5_000L -> "just now"
            age < 60_000L -> "${age / 1000}s ago"
            else -> "${age / 60_000L} min ago"
        }
    }

    /** True while [atMs] is a believable time within the window. */
    fun within(nowMs: Long, atMs: Long?): Boolean = atMs != null && atMs > 0 && ageLabel(nowMs, atMs) != null

    /** A Cloud song's own added time (the file mover's copy, stamped when it was filed); null for every other source. */
    fun cloudAdded(t: Track): Long? = if (t.source == TrackSource.CLOUD && t.mtime > 0) t.mtime else null

    /** The stage text of a running job: the request itself reads as "Starting...", the rest is the job's own message. */
    fun stageText(stage: DownloadStage, message: String): String =
        if (stage == DownloadStage.REQUESTED || message.isBlank()) "Starting..." else message

    /**
     * ONE lookup, in this order: a running job > a finished (or failed) one within the window > a Cloud song's own added time within the window.
     * A failure only wins over a success when it is the later of the two. [cloudAddedMs] is the song's added time when it is a Cloud track, else null.
     */
    fun resolve(nowMs: Long, runningStage: String?, finishedMs: Long?, failedMs: Long?, cloudAddedMs: Long?): DownloadTag {
        if (runningStage != null) return DownloadTag.Downloading(runningStage)
        val fin = finishedMs?.takeIf { within(nowMs, it) }
        val fail = failedMs?.takeIf { within(nowMs, it) }
        if (fail != null && (fin == null || fail > fin)) return DownloadTag.Failed(fail)
        if (fin != null) return DownloadTag.Recent(fin)
        if (within(nowMs, cloudAddedMs)) return DownloadTag.Recent(cloudAddedMs!!)
        return DownloadTag.None
    }

    /** The text of a tag at [nowMs]: "Recently downloaded · 1s ago", "Downloading · Searching Soulseek...", "Couldn't download", or null (no tag). */
    fun text(tag: DownloadTag, nowMs: Long): String? = when (tag) {
        DownloadTag.None -> null
        is DownloadTag.Downloading -> if (tag.stage.isBlank()) "Downloading" else "Downloading · ${tag.stage}"
        is DownloadTag.Recent -> ageLabel(nowMs, tag.finishedMs)?.let { "Recently downloaded · $it" }
        is DownloadTag.Failed -> if (within(nowMs, tag.atMs)) "Couldn't download" else null
    }

    /** When a Recent tag stops existing (epoch ms): the moment its row should drop the tag and give the line back. */
    fun expiresAt(tag: DownloadTag): Long? = when (tag) { is DownloadTag.Recent -> tag.finishedMs + WINDOW_MS; is DownloadTag.Failed -> tag.atMs + WINDOW_MS; else -> null }

    /** Whether the tag's text changes with time (only a Recent age does); the ticker only runs while such a tag is on screen. */
    fun ticks(tag: DownloadTag): Boolean = tag is DownloadTag.Recent
}

/**
 * What this phone knows about downloads, keyed by [matchKey] (title + lead artist, the same key the whole app uses to call two files one song):
 * running jobs with their live stage, finished ones with their time, failed ones. Fed by [Library] as statuses arrive and seeded from the
 * persisted download history, so a restart inside the hour keeps the tag. Thread safe.
 *
 * [structRev] changes when a song starts, finishes, fails or expires (rows decide their height from it); [liveRev] also changes with every new
 * stage message (only the tag text itself reads it).
 */
class DownloadTagStore {
    private val running = ConcurrentHashMap<String, String>()
    private val finished = ConcurrentHashMap<String, Long>()
    private val failed = ConcurrentHashMap<String, Long>()
    var structRev by mutableIntStateOf(0); private set
    var liveRev by mutableIntStateOf(0); private set

    /** Nudges the rows (a tag expired: its row gives the line back). */
    fun touch() { structRev++; liveRev++ }

    /** A download status for a song ([key] null for an album: its songs arrive with DONE). [nowMs] stamps a finish or a failure. */
    fun onStatus(key: String?, status: DownloadStatus, nowMs: Long) {
        when (status.stage) {
            DownloadStage.DONE -> {
                val keys = HashSet<String>()
                key?.let { keys += it }
                status.newTracks.forEach { keys += matchKey(it.title, it.artist) }
                keys.forEach { k -> running.remove(k); failed.remove(k); finished[k] = nowMs }
                key?.let { running.remove(it) }
                prune(nowMs)
                structRev++; liveRev++
            }
            DownloadStage.FAILED -> {
                if (key != null) { running.remove(key); failed[key] = nowMs; prune(nowMs); structRev++; liveRev++ }
            }
            else -> {
                if (key == null) return
                // a "had nothing" note belongs to the story, not to the stage: the tag keeps the stage it had
                if (status.miss && running.containsKey(key)) return
                val text = DownloadTags.stageText(status.stage, status.message)
                val before = running.put(key, text)
                if (before == null) structRev++
                if (before != text) liveRev++
            }
        }
    }

    /** Finished downloads remembered across restarts (title/artist/time of each); only what is still inside the window is kept. */
    fun seed(entries: List<Triple<String, String, Long>>, nowMs: Long) {
        var any = false
        for ((title, artist, at) in entries) {
            if (!DownloadTags.within(nowMs, at)) continue
            val k = matchKey(title, artist)
            finished.merge(k, at) { a, b -> maxOf(a, b) }
            any = true
        }
        if (any) { structRev++; liveRev++ }
    }

    /** The tag of the song [key] at [nowMs]; [cloudAddedMs] is its added time when it is a Cloud track. */
    fun statusOf(key: String, nowMs: Long, cloudAddedMs: Long? = null): DownloadTag =
        DownloadTags.resolve(nowMs, running[key], finished[key], failed[key], cloudAddedMs)

    private fun prune(nowMs: Long) {
        if (finished.size > 256) finished.entries.removeIf { !DownloadTags.within(nowMs, it.value) }
        if (failed.size > 256) failed.entries.removeIf { !DownloadTags.within(nowMs, it.value) }
    }
}

/** Reference counting for the one ticker: it runs while at least one tagged row with a live age is on screen, and not at all otherwise. */
class TickerRefs {
    private var count = 0
    /** A tagged row appeared: true when this is the first one (start the ticker). */
    @Synchronized fun acquire(): Boolean { count++; return count == 1 }
    /** A tagged row left: true when it was the last one (stop the ticker). Never goes below zero. */
    @Synchronized fun release(): Boolean { if (count == 0) return false; count--; return count == 0 }
    @get:Synchronized val users: Int get() = count
}
