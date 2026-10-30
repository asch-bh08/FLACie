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
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.library.Track
import com.ipodemu.library.offline.KeepStatus
import com.ipodemu.library.offline.OfflineEntry
import com.ipodemu.library.offline.OfflineMath
import com.ipodemu.library.offline.OfflineReason
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
                            Txt("${OfflineMath.songs(m.doneCount)}, ${OfflineMath.formatBytes(used)}", size = 17f, weight = FontWeight.SemiBold)
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
                        OfflineRow("Pause downloads", toggle = prefs.offlinePaused) { prefs.offlinePaused = !prefs.offlinePaused; tick++; if (!prefs.offlinePaused) m.kick() }
                        OfflineRow("Wi-Fi only", toggle = prefs.offlineWifiOnly) { prefs.offlineWifiOnly = !prefs.offlineWifiOnly; tick++; m.kick() }
                        OfflineRow("Keep favourites offline", toggle = prefs.offlineAutoFavorites) { prefs.offlineAutoFavorites = !prefs.offlineAutoFavorites; tick++; app.offlineSyncSoon() }
                        OfflineRow("Favourites limit", value = if (prefs.offlineLimitGb == 0) "No limit" else "${prefs.offlineLimitGb} GB") {
                            prefs.offlineLimitGb = LIMITS[(LIMITS.indexOf(prefs.offlineLimitGb).coerceAtLeast(0) + 1) % LIMITS.size]; tick++; app.offlineSyncSoon()
                        }
                    }
                }
                if (m.failedCount > 0) item { Card { OfflineRow("Try the failed ones again") { m.retryFailed() } } }
                // what each album takes, biggest first: a tap offers to remove that album's downloads
                val albums = OfflineMath.byAlbum(entries)
                if (albums.size > 1) {
                    item { SectionHeader("By album") }
                    item {
                        Card {
                            albums.take(12).forEach { a ->
                                OfflineRow("${a.album} · ${a.artist}", value = "${OfflineMath.songs(a.songs)}, ${OfflineMath.formatBytes(a.bytes)}") {
                                    nav.sheet = SheetSpec("Remove ${a.album}?", "${OfflineMath.songs(a.songs)}, ${OfflineMath.formatBytes(a.bytes)}", listOf(
                                        SheetItem("Yes, remove its downloads", Glyph.CLOSE) { a.paths.forEach { m.remove(it) } }, SheetItem("Keep it", Glyph.CHECK) { }))
                                }
                            }
                        }
                    }
                }
                item { SectionHeader("Downloads") }
                if (entries.isEmpty()) item { Box(Modifier.fillMaxWidth().padding(24.dp)) { Txt("Nothing yet. Open a song's menu and choose Download for offline, or switch on Keep favourites offline.", size = 14f, color = sc.onBgDim) } }
                items(entries.sortedWith(compareBy({ it.state != OfflineState.DONE }, { -it.addedMs })), key = { it.key }) { e ->
                    OfflineEntryRow(e, onPlay = { play(app, entries, e) }, onRemove = { m.remove(e.path) })
                }
                if (entries.isNotEmpty()) item { Card { OfflineRow("Remove all downloads") { nav.sheet = SheetSpec("Remove all downloads?", "${OfflineMath.songs(m.doneCount)}, ${OfflineMath.formatBytes(used)}", listOf(
                    SheetItem("Yes, remove them all", Glyph.CLOSE) { m.removeAll() }, SheetItem("Keep them", Glyph.CHECK) { })) } } }
            }
        }
    }
}

/**
 * "On this phone": the songs kept for listening without signal, one tap from Home (Settings > Offline and storage is only the switches and the clean-up). Songs that
 * are still on their way show how far they are; Songs / Albums changes how they are listed; the playlists kept up to date are listed so it is clear what feeds this.
 */
@Composable
fun OnPhoneScreen(nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val m = app.offline
    // read here so the screen follows every change of the downloads
    val entries = m.entries
    val ud = app.userData
    var byAlbum by remember { mutableStateOf(false) }
    val done = entries.filter { it.state == OfflineState.DONE }
    val ordered = entries.sortedWith(compareBy({ it.state != OfflineState.DONE }, { -it.addedMs }))
    val kept = m.keptPlaylists.mapNotNull { id ->
        if (id.startsWith("folder:")) id.removePrefix("folder:") to Screen.Detail(DetailKind.FOLDER, id.removePrefix("folder:"))
        else ud.playlists.firstOrNull { it.id == id }?.let { it.name to Screen.Detail(DetailKind.USER, id) }
    }
    Column(Modifier.fillMaxSize().background(Palette.bg)) {
        TopBar("On this phone", nav, showBack = true) { TopAction(Glyph.GEAR, "Offline settings") { nav.push(Screen.Offline) } }
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
            LazyColumn(Modifier.widthIn(max = 620.dp).fillMaxSize(), contentPadding = PaddingValues(bottom = 30.dp)) {
                item {
                    Card {
                        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Txt("${OfflineMath.songs(m.doneCount)}, ${OfflineMath.formatBytes(m.usedBytes)}", size = 17f, weight = FontWeight.SemiBold)
                            Txt("These play with no signal. ${OfflineMath.formatBytes(m.freeBytes)} free on the phone", size = 13f, color = sc.onBgDim)
                            val pending = m.pendingCount; val failed = m.failedCount
                            if (pending > 0) Txt(m.statusLine.ifEmpty { "$pending waiting to download" }, size = 13f, color = sc.accent)
                            if (failed > 0) Txt("$failed failed", size = 13f, color = sc.onBgDim)
                        }
                    }
                }
                if (entries.isEmpty()) item { Box(Modifier.fillMaxWidth().padding(24.dp)) { Txt("Nothing on this phone yet. Open an album or a playlist and tap Download, or use a song's menu and choose Download for offline.", size = 14f, color = sc.onBgDim) } }
                else item {
                    Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        if (done.isNotEmpty()) {
                            DesignChip("Play", false, { app.player.play(done.map { it.toTrack() }, 0, context = "On this phone") }, icon = Glyph.PLAY)
                            DesignChip("Shuffle", false, { app.player.play(done.map { it.toTrack() }, done.indices.random(), true, "On this phone") }, icon = Glyph.SHUFFLE)
                        }
                        DesignChip("Songs", !byAlbum, { byAlbum = false })
                        DesignChip("Albums", byAlbum, { byAlbum = true })
                        if (m.failedCount > 0) DesignChip("Try the failed ones again", false, { m.retryFailed() })
                    }
                }
                if (kept.isNotEmpty()) {
                    item { SectionHeader("Kept up to date") }
                    item {
                        Card {
                            kept.forEach { (name, to) -> OfflineRow(name, value = "Playlist") { nav.push(to) } }
                        }
                    }
                }
                if (entries.isNotEmpty()) item { SectionHeader(if (byAlbum) "Albums" else "Songs") }
                if (!byAlbum) items(ordered, key = { it.key }) { e -> OfflineEntryRow(e, onPlay = { play(app, entries, e, "On this phone") }, onRemove = { m.remove(e.path) }) }
                else {
                    // each album with its songs under it (an album still downloading lists what has arrived and what is on the way)
                    val groups = ordered.groupBy { it.albumArtist.ifEmpty { it.artist }.lowercase() + "|" + it.album.lowercase() }.values.sortedBy { it[0].album.lowercase() }
                    groups.forEach { g ->
                        val n = g.count { it.state == OfflineState.DONE }
                        item(key = "g" + g[0].key) {
                            OfflineRow("${g[0].album.ifEmpty { "Unknown album" }} · ${g[0].albumArtist.ifEmpty { g[0].artist }}", value = "$n of ${g.size}") {
                                nav.sheet = SheetSpec("Remove ${g[0].album.ifEmpty { "these songs" }}?", OfflineMath.songs(g.size), listOf(
                                    SheetItem("Yes, remove its downloads", Glyph.CLOSE) { g.forEach { m.remove(it.path) } }, SheetItem("Keep it", Glyph.CHECK) { }))
                            }
                        }
                        items(g, key = { it.key }) { e -> OfflineEntryRow(e, onPlay = { play(app, entries, e, "On this phone") }, onRemove = { m.remove(e.path) }) }
                    }
                }
            }
        }
    }
}

private fun play(app: com.ipodemu.App, entries: List<OfflineEntry>, e: OfflineEntry, context: String = "Downloads") {
    val done = entries.filter { it.state == OfflineState.DONE }
    val i = done.indexOfFirst { it.key == e.key }
    if (i < 0) return
    app.player.play(done.map { it.toTrack() }, i, context = context)
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

/**
 * The Download button of an album or a playlist page: says Download, then Downloading 3 of 12, then Downloaded. A tap while it is on its way or done offers to remove it.
 * On a playlist ([listId] set) it also keeps the playlist up to date: songs added to it later download too, and songs taken out go again.
 */
@Composable
fun KeepOfflineChip(tracks: List<Track>, nav: PlayerNav, listId: String?, title: String) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val m = app.offline
    val st = m.keepStatus(tracks)   // reads the downloads' revision, so the label follows their progress
    if (st.kind == KeepStatus.Kind.UNAVAILABLE) return
    val kept = listId != null && m.isKept(listId)
    val reason = if (listId != null) OfflineReason.PLAYLIST else OfflineReason.MANUAL
    fun start() {
        if (listId != null) m.setKept(listId, true)
        val n = m.keep(tracks, reason)
        app.ui.say(if (n > 0) "Downloading ${OfflineMath.songs(n)}" + (if (listId != null) ". New songs added to \"$title\" download too" else "") else "Already on this phone")
        if (listId != null) app.offlineSyncSoon()
    }
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        DesignChip(st.label, st.kind == KeepStatus.Kind.DONE, {
            when (st.kind) {
                KeepStatus.Kind.NONE, KeepStatus.Kind.FAILED -> start()
                else -> nav.sheet = SheetSpec(if (st.kind == KeepStatus.Kind.BUSY) "Stop downloading \"$title\"?" else "Remove the download of \"$title\"?", OfflineMath.songs(st.total) + (if (kept) ". Kept up to date" else ""), listOf(
                    SheetItem(if (st.kind == KeepStatus.Kind.BUSY) "Yes, stop and remove" else "Yes, remove the downloads", Glyph.CLOSE) {
                        if (listId != null) m.setKept(listId, false)
                        m.forget(tracks)
                    },
                    SheetItem("Keep it", Glyph.CHECK) { },
                ))
            }
        }, icon = if (st.kind == KeepStatus.Kind.DONE) Glyph.CHECK else Glyph.DOWN)
        if (kept) Txt("Kept on this phone: new songs added to this playlist download too", size = 12f, color = sc.onBgDim)
    }
}
