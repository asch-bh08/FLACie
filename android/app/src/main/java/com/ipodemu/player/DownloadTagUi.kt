package com.ipodemu.player

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.text.BasicText
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.sp
import com.ipodemu.library.DownloadTag
import com.ipodemu.library.DownloadTags
import com.ipodemu.library.TagForm
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
private val StreamingInk = Color(0xFF6DB3FF)

/**
 * The status tag under a song's artist: "Downloading · <stage>", "Recently downloaded · 12s ago" (within an hour of the download finishing),
 * "Couldn't download", and for a Cloud song past the hour a quiet "New download". [key] is the song's [matchKey]; [addedMs] is its added time when it
 * is a Cloud track and [cloud] says it is one (the third source of truth after a running job and a finished one, see [DownloadTags.resolve]).
 * Draws nothing for an ordinary library song. One line, never focusable. [hi] = the row is highlighted (pressed or focused), whose background needs
 * light text.
 *
 * Width: [compact] starts in the short wording ("Downloaded · 4m ago") for places known to be narrow; whatever the place, a line that does not fit
 * steps down to the short wording and then to the age alone, so the age is never the part that is cut. [showNew] = false for places that already
 * name the source (Now Playing's pill, a row with its own badge).
 */
@Composable
fun DownloadTagLine(
    key: String, addedMs: Long?, modifier: Modifier = Modifier, hi: Boolean = false, size: Float = 11.5f, align: TextAlign = TextAlign.Start,
    cloud: Boolean = false, compact: Boolean = false, showNew: Boolean = true,
) {
    val store = LocalApp.current.library.tags
    val sc = LocalScheme.current
    store.liveRev   // a new stage message, a start, a finish
    val base = System.currentTimeMillis()
    val first = store.statusOf(key, base, addedMs, cloud)
    if (first is DownloadTag.None || (first is DownloadTag.NewDownload && !showNew)) return
    // the age ticks only for a finished download; a tag that will expire says so once, so its row gives the line back
    val ticking = DownloadTags.ticks(first)
    if (ticking) LaunchedEffect(key) { TagClock.acquire(); try { awaitCancellation() } finally { TagClock.release() } }
    val expiry = DownloadTags.expiresAt(first)
    if (expiry != null) LaunchedEffect(key, expiry) { delay((expiry - System.currentTimeMillis()).coerceAtLeast(0L) + 250L); store.touch() }
    val now = if (ticking) maxOf(TagClock.now, base) else base
    val tag = if (ticking) store.statusOf(key, now, addedMs, cloud) else first
    var form by remember(key) { mutableStateOf(if (compact) TagForm.COMPACT else TagForm.FULL) }
    val text = DownloadTags.text(tag, now, form) ?: return
    val ink = when {
        hi -> Color(0xEEFFFFFF)
        tag is DownloadTag.Failed -> FailedInk
        tag is DownloadTag.Recent -> RecentInk
        tag is DownloadTag.NewDownload -> StreamingInk   // "Streaming": the same fixed blue as the badge and the pill
        else -> sc.onBgDim   // running and "New download": the quiet secondary text colour (the accent changes with the user's setting)
    }
    BasicText(
        text, modifier.fillMaxWidth(),
        style = TextStyle(color = ink, fontSize = size.sp, fontWeight = FontWeight.SemiBold, fontFamily = LocalStyle.current.font, textAlign = align),
        maxLines = 1, overflow = TextOverflow.Ellipsis,
        onTextLayout = { r ->
            // does not fit: the shorter wording, then the age alone (a tag with nothing shorter keeps its ellipsis)
            if (r.hasVisualOverflow) form = when (form) { TagForm.FULL -> TagForm.COMPACT; TagForm.COMPACT -> TagForm.AGE_ONLY; TagForm.AGE_ONLY -> TagForm.AGE_ONLY }
        },
    )
}

@Composable
fun DownloadTagLine(
    t: Track, modifier: Modifier = Modifier, hi: Boolean = false, size: Float = 11.5f, align: TextAlign = TextAlign.Start,
    compact: Boolean = false, showNew: Boolean = true,
) = DownloadTagLine(t.matchKey, DownloadTags.cloudAdded(t), modifier, hi, size, align, DownloadTags.isCloud(t), compact, showNew)

/** Whether a tag line will show for this song right now: rows use it to make room for the extra line (a structural change only: it does not tick). */
@Composable
fun isTagged(key: String, addedMs: Long?, cloud: Boolean = false, showNew: Boolean = true): Boolean {
    val store = LocalApp.current.library.tags
    store.structRev
    val tag = store.statusOf(key, System.currentTimeMillis(), addedMs, cloud)
    return tag !is DownloadTag.None && (showNew || tag !is DownloadTag.NewDownload)
}

@Composable
fun isTagged(t: Track, showNew: Boolean = true): Boolean = isTagged(t.matchKey, DownloadTags.cloudAdded(t), DownloadTags.isCloud(t), showNew)

/** The tag key of a song known by title and artist only (a chart row, a search result). */
fun tagKeyOf(title: String, artist: String): String = matchKey(title, artist)
