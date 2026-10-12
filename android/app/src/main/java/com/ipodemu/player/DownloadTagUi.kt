package com.ipodemu.player

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import com.ipodemu.library.DownloadTag
import com.ipodemu.library.DownloadTags
import com.ipodemu.library.TickerRefs
import com.ipodemu.library.Track
import com.ipodemu.library.matchKey
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * The one clock behind every "Recently downloaded · 12s ago" tag. It ticks once a second, and only while at least one tag with a live age is
 * composed (a tagged row scrolled out of view releases it; with none on screen no coroutine runs at all).
 */
object TagClock {
    private val refs = TickerRefs()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var job: Job? = null
    var now by mutableLongStateOf(System.currentTimeMillis()); private set

    fun acquire() {
        if (!refs.acquire()) return
        now = System.currentTimeMillis()
        job = scope.launch { while (true) { delay(1000); now = System.currentTimeMillis() } }
    }

    fun release() {
        if (!refs.release()) return
        job?.cancel(); job = null
    }
}

private val RecentInk = Color(0xFF4EE0A1)
private val FailedInk = Color(0xFFFF9C9C)

/**
 * The status tag under a song's artist: "Downloading · <stage>", "Recently downloaded · 12s ago" (within an hour of the download finishing) or
 * "Couldn't download". [key] is the song's [matchKey]; [addedMs] is its added time when it is a Cloud track (the third source of truth after a
 * running job and a finished one, see [DownloadTags.resolve]). Draws nothing for an ordinary library song. One line, truncated, never focusable.
 * [hi] = the row is highlighted (pressed or focused), whose background needs light text.
 */
@Composable
fun DownloadTagLine(key: String, addedMs: Long?, modifier: Modifier = Modifier, hi: Boolean = false, size: Float = 11.5f, align: TextAlign = TextAlign.Start) {
    val store = LocalApp.current.library.tags
    val sc = LocalScheme.current
    store.liveRev   // a new stage message, a start, a finish
    val base = System.currentTimeMillis()
    val first = store.statusOf(key, base, addedMs)
    if (first is DownloadTag.None) return
    // the age ticks only for a finished download; a tag that will expire says so once, so its row gives the line back
    val ticking = DownloadTags.ticks(first)
    if (ticking) LaunchedEffect(key) { TagClock.acquire(); try { awaitCancellation() } finally { TagClock.release() } }
    val expiry = DownloadTags.expiresAt(first)
    if (expiry != null) LaunchedEffect(key, expiry) { delay((expiry - System.currentTimeMillis()).coerceAtLeast(0L) + 250L); store.touch() }
    val now = if (ticking) maxOf(TagClock.now, base) else base
    val tag = if (ticking) store.statusOf(key, now, addedMs) else first
    val text = DownloadTags.text(tag, now) ?: return
    val ink = when {
        hi -> Color(0xEEFFFFFF)
        tag is DownloadTag.Failed -> FailedInk
        tag is DownloadTag.Downloading -> sc.accent
        else -> RecentInk
    }
    Txt(text, modifier.fillMaxWidth(), size = size, weight = FontWeight.SemiBold, color = ink, maxLines = 1, align = align)
}

@Composable
fun DownloadTagLine(t: Track, modifier: Modifier = Modifier, hi: Boolean = false, size: Float = 11.5f, align: TextAlign = TextAlign.Start) =
    DownloadTagLine(t.matchKey, DownloadTags.cloudAdded(t), modifier, hi, size, align)

/** Whether a tag line will show for this song right now: rows use it to make room for the extra line (a structural change only: it does not tick). */
@Composable
fun isTagged(key: String, addedMs: Long?): Boolean {
    val store = LocalApp.current.library.tags
    store.structRev
    return store.statusOf(key, System.currentTimeMillis(), addedMs) !is DownloadTag.None
}

@Composable
fun isTagged(t: Track): Boolean = isTagged(t.matchKey, DownloadTags.cloudAdded(t))

/** The tag key of a song known by title and artist only (a chart row, a search result). */
fun tagKeyOf(title: String, artist: String): String = matchKey(title, artist)
