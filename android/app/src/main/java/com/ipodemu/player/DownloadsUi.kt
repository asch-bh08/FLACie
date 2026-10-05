package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.library.DownloadRecord
import com.ipodemu.library.FlacieWebClient
import com.ipodemu.library.OpenSourceNames
import com.ipodemu.library.RunningDownload
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** One line of what is running right now (this phone's own downloads and the server's). */
private class RunningLine(val label: String, val kind: String, val startedAt: Long, val message: String, val current: String?, val artKey: String?, val origin: String)

private val SECTIONS = listOf("running" to "Running", "failed" to "Failed", "soulseek" to "Soulseek", "lidarr" to "Lidarr", "open" to "YouTube and open sources")

private fun sectionOf(r: DownloadRecord): String = when {
    !r.done -> "failed"
    r.source == "soulseek" -> "soulseek"
    r.source == "lidarr" -> "lidarr"
    else -> "open"
}

private fun sourceColor(id: String?): Color = when (id) {
    "soulseek" -> Color(0xFF6FE0A0); "lidarr" -> Color(0xFFE6C46A); "ytdl" -> Color(0xFFFF8A8A)
    "archive", "audius", "jamendo" -> Color(0xFF7CC0FF); else -> Color(0xFFFF7A8A)
}

private fun kindText(k: String) = when (k) {
    "Search" -> "You asked for it"; "Autoplay" -> "Fetched ahead for Autoplay"; "Playlist" -> "From a playlist"; "Import" -> "From an import"; "Chart" -> "Daily charts"; else -> k
}

private fun took(ms: Long): String { val s = ms / 1000; return if (ms < 1000) "under a second" else if (s < 90) "$s s" else if (s < 5400) "${s / 60} min" else "${s / 3600} h" }
private fun ago(ms: Long): String { val d = (System.currentTimeMillis() - ms) / 1000; return if (d < 60) "just now" else if (d < 3600) "${d / 60} min ago" else if (d < 86400) "${d / 3600} h ago" else if (d < 172800) "yesterday" else "${d / 86400} days ago" }
private fun clock(ms: Long): String = SimpleDateFormat("d MMM, HH:mm", Locale.getDefault()).format(Date(ms))
private fun clockSec(ms: Long): String = SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date(ms))

/**
 * Downloads: what is running now, and the log of everything fetched for this account, kept across restarts, in sections by where it came from
 * (Failed, Soulseek, Lidarr, YouTube and the open sources). Each entry opens to its story (what every source said) and where the file went.
 * It shows this phone's own downloads and, when FLACie Web is set up, the server's (web searches, Autoplay, charts, imports).
 */
@Composable
fun DownloadsScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val lib = app.library
    val sc = LocalScheme.current
    BackHandler { ui.downloadsOpen = false }

    val web = remember { FlacieWebClient(app.prefs) }
    var server by remember { mutableStateOf<FlacieWebClient.ServerDownloads?>(null) }
    var tick by remember { mutableStateOf(0L) }
    LaunchedEffect(Unit) {
        while (true) {
            if (web.available) server = try { withContext(Dispatchers.IO) { web.downloads() } } catch (_: Exception) { server }
            delay(4000)
        }
    }
    LaunchedEffect(Unit) { while (true) { delay(1000); tick++ } }
    @Suppress("UNUSED_EXPRESSION") tick

    var pick by remember { mutableStateOf("all") }
    val shown = remember { mutableStateMapOf<String, Int>() }
    val open = remember { mutableStateMapOf<String, Boolean>() }

    val running = (lib.runningDownloads.values.map { RunningLine(it.label, it.kind, it.startedAt, it.message, it.current, it.artKey, "phone") } +
        (server?.running ?: emptyList()).map { RunningLine(it.label, it.kind, it.startedAt, it.message, it.current, it.artKey, "server") }).sortedByDescending { it.startedAt }
    val log = (lib.downloadLog.all() + (server?.log ?: emptyList())).sortedByDescending { it.finishedAt }
    val by = log.groupBy { sectionOf(it) }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        LazyColumn(Modifier.widthIn(max = 620.dp).fillMaxSize(), contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            item {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    IconAction(Glyph.BACK, "Back", { ui.downloadsOpen = false }, tint = Color.White)
                    Txt("Downloads", Modifier.padding(start = 4.dp), size = 22f, weight = FontWeight.Bold, color = Color.White)
                }
            }
            item {
                Txt("Everything fetched for you, newest first, kept across restarts. Each download tries Soulseek first, then the free sources and YouTube (if switched on), then Lidarr; tap one to see what each source said." +
                    if (web.available) "" else " Set up FLACie Web to also see the server's downloads here.", size = 13f, color = Color(0xB3FFFFFF), maxLines = 6)
            }
            item {
                Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    GlossPill("All", { pick = "all" }, primary = pick == "all", height = 36.dp)
                    for ((id, name) in SECTIONS) {
                        val n = if (id == "running") running.size else by[id]?.size ?: 0
                        GlossPill("$name $n", { pick = id }, primary = pick == id, height = 36.dp)
                    }
                }
            }
            for ((id, name) in SECTIONS) {
                if (pick != "all" && pick != id) continue
                item(key = "h-$id") {
                    val n = if (id == "running") running.size else by[id]?.size ?: 0
                    Txt("$name  $n", Modifier.padding(top = 10.dp), size = 17f, weight = FontWeight.Bold, color = Color.White)
                }
                if (id == "running") {
                    if (running.isEmpty()) item(key = "e-running") { Txt("Nothing is downloading.", size = 13f, color = Color(0x99FFFFFF)) }
                    items(running) { r -> RunningCard(r) }
                } else {
                    val rows = by[id] ?: emptyList()
                    if (rows.isEmpty()) item(key = "e-$id") {
                        Txt(when (id) { "failed" -> "Nothing failed."; "soulseek" -> "Nothing came from Soulseek yet."; "lidarr" -> "Nothing came from Lidarr yet."; else -> "Nothing came from YouTube, the Internet Archive, Audius or Jamendo yet." }, size = 13f, color = Color(0x99FFFFFF))
                    }
                    val limit = shown[id] ?: 15
                    items(rows.take(limit), key = { "${it.origin}-${it.id}" }) { r ->
                        RecordCard(r, open["${r.origin}-${r.id}"] == true) { open["${r.origin}-${r.id}"] = open["${r.origin}-${r.id}"] != true }
                    }
                    if (rows.size > limit) item(key = "m-$id") { GlossPill("Show more (${rows.size - limit} older)", { shown[id] = limit + 25 }, height = 36.dp) }
                }
            }
            item { Box(Modifier.size(24.dp)) }
        }
    }
}

@Composable
private fun SourceTag(id: String?, failed: Boolean = false) {
    val c = if (failed) Color(0xFFFF7A8A) else sourceColor(id)
    Box(Modifier.clip(RoundedCornerShape(50)).background(c.copy(alpha = 0.16f)).padding(horizontal = 9.dp, vertical = 4.dp)) {
        Txt(if (failed) "Failed" else OpenSourceNames.of(id), size = 11f, weight = FontWeight.SemiBold, color = c)
    }
}

@Composable
private fun RunningCard(r: RunningLine) {
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Palette.surface).padding(12.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        ArtImage(r.artKey, Modifier.size(44.dp), thumb = true, corner = 8.dp)
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Txt(r.label, size = 15f, weight = FontWeight.SemiBold, maxLines = 1)
            Txt(r.message, size = 13f, color = Color(0xCCFFFFFF), maxLines = 2)
            Txt((r.current?.let { "Working on ${OpenSourceNames.of(it)} · " } ?: "") + "${kindText(r.kind)} · ${took(System.currentTimeMillis() - r.startedAt)} so far" + if (r.origin == "server") " · on the server" else "", size = 11.5f, color = Color(0x99FFFFFF), maxLines = 2)
        }
        if (r.current != null) SourceTag(r.current)
    }
}

@Composable
private fun RecordCard(r: DownloadRecord, open: Boolean, toggle: () -> Unit) {
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Palette.surface).clickable { toggle() }.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            ArtImage(r.artKey, Modifier.size(44.dp), thumb = true, corner = 8.dp)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Txt(r.label, size = 15f, weight = FontWeight.SemiBold, maxLines = 1)
                Txt(r.message, size = 13f, color = if (r.done) Color(0xCCFFFFFF) else Color(0xFFFF7A8A), maxLines = 2)
                Txt("${kindText(r.kind)} · ${clock(r.finishedAt)} (${ago(r.finishedAt)}) · took ${took(r.finishedAt - r.startedAt)}" + if (r.origin == "server") " · on the server" else "", size = 11.5f, color = Color(0x99FFFFFF), maxLines = 3)
            }
            SourceTag(r.source, failed = !r.done)
        }
        if (open) {
            Column(Modifier.padding(start = 56.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                for (t in r.trail) {
                    Txt("${clockSec(t.at)}  " + (t.source?.takeIf { !t.text.startsWith(OpenSourceNames.of(it), ignoreCase = true) }?.let { OpenSourceNames.of(it) + ": " } ?: "") + t.text,
                        size = 12.5f, color = if (t.miss) Color(0x80FFFFFF) else Color(0xB3FFFFFF), maxLines = 3)
                }
                if (r.file != null) Txt("Saved as ${r.file}", size = 12f, color = Color(0x99FFFFFF), maxLines = 4)
                else if (r.done) Txt("Filed by Lidarr into your library; it shows up after the next Jellyfin scan.", size = 12f, color = Color(0x99FFFFFF), maxLines = 3)
            }
        }
    }
}
