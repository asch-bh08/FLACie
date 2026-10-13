package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.library.offline.OfflineEntry
import com.ipodemu.library.offline.OfflineMath
import com.ipodemu.library.offline.OfflineState

private val LIMITS = listOf(1, 2, 5, 10, 20, 50, 0)

/** Settings > Offline and storage: what is kept on the phone, what is on its way, the switches, and a way to let it all go. */
@Composable
fun OfflineScreen(nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val prefs = app.prefs
    val m = app.offline
    // read here so the screen follows every change of the downloads
    val entries = m.entries
    val used = m.usedBytes
    var tick by androidx.compose.runtime.remember { androidx.compose.runtime.mutableIntStateOf(0) }
    @Suppress("UNUSED_EXPRESSION") tick

    Column(Modifier.fillMaxSize().background(Palette.bg)) {
        TopBar("Offline and storage", nav, showBack = true)
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
            LazyColumn(Modifier.widthIn(max = 620.dp).fillMaxSize(), contentPadding = PaddingValues(bottom = 30.dp)) {
                item { SectionHeader("On this phone") }
                item {
                    Card {
                        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Txt("${m.doneCount} songs, ${OfflineMath.formatBytes(used)}", size = 17f, weight = FontWeight.SemiBold)
                            Txt("${OfflineMath.formatBytes(m.freeBytes)} free on the phone", size = 13f, color = sc.onBgDim)
                            val pending = m.pendingCount; val failed = m.failedCount
                            if (pending > 0) Txt(m.statusLine.ifEmpty { "$pending waiting to download" }, size = 13f, color = sc.accent)
                            if (failed > 0) Txt("$failed failed", size = 13f, color = sc.onBgDim)
                        }
                    }
                }
                item { SectionHeader("Settings") }
                item {
                    Card {
                        OfflineRow("Wi-Fi only", toggle = prefs.offlineWifiOnly) { prefs.offlineWifiOnly = !prefs.offlineWifiOnly; tick++; m.kick() }
                        OfflineRow("Keep favourites offline", toggle = prefs.offlineAutoFavorites) { prefs.offlineAutoFavorites = !prefs.offlineAutoFavorites; tick++; app.offlineSyncSoon() }
                        OfflineRow("Favourites limit", value = if (prefs.offlineLimitGb == 0) "No limit" else "${prefs.offlineLimitGb} GB") {
                            prefs.offlineLimitGb = LIMITS[(LIMITS.indexOf(prefs.offlineLimitGb).coerceAtLeast(0) + 1) % LIMITS.size]; tick++; app.offlineSyncSoon()
                        }
                    }
                }
                if (m.failedCount > 0) item { Card { OfflineRow("Try the failed ones again") { m.retryFailed() } } }
                item { SectionHeader("Downloads") }
                if (entries.isEmpty()) item { Box(Modifier.fillMaxWidth().padding(24.dp)) { Txt("Nothing yet. Open a song's menu and choose Download for offline, or switch on Keep favourites offline.", size = 14f, color = sc.onBgDim) } }
                items(entries.sortedWith(compareBy({ it.state != OfflineState.DONE }, { -it.addedMs })), key = { it.key }) { e ->
                    OfflineEntryRow(e, onPlay = { play(app, entries, e) }, onRemove = { m.remove(e.path) })
                }
                if (entries.isNotEmpty()) item { Card { OfflineRow("Remove all downloads") { nav.sheet = SheetSpec("Remove all downloads?", "${m.doneCount} songs, ${OfflineMath.formatBytes(used)}", listOf(
                    SheetItem("Yes, remove them all", Glyph.CLOSE) { m.removeAll() }, SheetItem("Keep them", Glyph.CHECK) { })) } } }
            }
        }
    }
}

private fun play(app: com.ipodemu.App, entries: List<OfflineEntry>, e: OfflineEntry) {
    val done = entries.filter { it.state == OfflineState.DONE }
    val i = done.indexOfFirst { it.key == e.key }
    if (i < 0) return
    app.player.play(done.map { it.toTrack() }, i, context = "Downloads")
}

@Composable
private fun OfflineRow(title: String, value: String? = null, toggle: Boolean? = null, onClick: () -> Unit) {
    IpodRow(onClick, height = 58.dp, trailing = {
        if (toggle != null) SwitchPill(toggle) else if (value != null) Txt(value, size = 15f, color = rowDim(), maxLines = 1)
    }) { hi -> Txt(title, size = 16f, weight = FontWeight.Medium, color = if (hi) androidx.compose.ui.graphics.Color.White else LocalScheme.current.onBg) }
}

@Composable
private fun OfflineEntryRow(e: OfflineEntry, onPlay: () -> Unit, onRemove: () -> Unit) {
    val sc = LocalScheme.current
    IpodRow(if (e.state == OfflineState.DONE) onPlay else onRemove, onLong = onRemove, height = 64.dp,
        leading = { ArtImage(e.artKey, Modifier.size(44.dp), thumb = true, corner = 6.dp) },
        trailing = {
            val label = when (e.state) {
                OfflineState.DONE -> OfflineMath.formatBytes(e.bytes)
                OfflineState.DOWNLOADING -> "${(e.progress * 100).toInt()} %"
                OfflineState.QUEUED -> "Waiting"
                OfflineState.FAILED -> "Failed"
            }
            Txt(label, size = 13f, color = if (e.state == OfflineState.FAILED) sc.accent else rowDim(), maxLines = 1)
        }) { hi ->
        Column {
            Txt(e.title, size = 15f, weight = FontWeight.SemiBold, color = if (hi) androidx.compose.ui.graphics.Color.White else sc.onBg)
            Txt(e.artist.ifEmpty { e.album } + if (e.state == OfflineState.DONE) "" else (e.error?.let { "  -  $it" } ?: ""), size = 12.5f, color = rowDim())
        }
    }
}
