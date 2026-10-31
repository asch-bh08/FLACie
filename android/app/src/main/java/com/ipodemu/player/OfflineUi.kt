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
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.clickable
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.runtime.derivedStateOf
import com.ipodemu.library.offline.DownloadMark
import com.ipodemu.library.offline.DownloadMarks
import com.ipodemu.library.offline.DownloadedList
import com.ipodemu.library.offline.DownloadsOverview
import com.ipodemu.library.offline.ListSpec
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
                item { SectionHeader("Downloaded") }
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

/** A downloaded playlist or album as a row of the Downloads section: where a tap goes, the songs it plays, and (for a playlist kept up to date) the id that is kept. */
private class Shelf(val list: DownloadedList, val to: Screen, val paths: List<String>, val keptId: String?)

/**
 * The little mark on a song that is on the phone (Spotify's green arrow, in the app's accent), or a dim one while it is on its way. It asks the in-memory list only, through a
 * derived state that follows the downloads' state revision (it ignores progress ticks): a row redraws when its own mark changes, not on every progress tick, and a list with no downloads at all skips it.
 */
@Composable
fun DownloadedMark(path: String, size: Dp = 16.dp) {
    val m = LocalApp.current.offline
    val mark by remember(path, m) { derivedStateOf { m.stateRev; if (m.hasAny) DownloadMarks.ofSong(m.stateOf(path)) else DownloadMark.NONE } }
    MarkIcon(mark, size)
}

/** The same mark for a playlist or an album, from its songs' paths: Downloaded only when every song is on the phone; partly done shows nothing (calm). */
@Composable
fun DownloadedListMark(paths: List<String>, size: Dp = 16.dp) {
    val m = LocalApp.current.offline
    val mark by remember(paths, m) { derivedStateOf { m.stateRev; if (m.hasAny) DownloadMarks.ofList(paths) { m.stateOf(it) } else DownloadMark.NONE } }
    MarkIcon(mark, size)
}

@Composable
private fun MarkIcon(mark: DownloadMark, size: Dp) {
    if (mark == DownloadMark.NONE) return
    val sc = LocalScheme.current
    val done = mark == DownloadMark.DOWNLOADED
    // a finished song is the full accent; one on its way is the same shape, quieter, so a list does not flicker
    GlyphIcon(Glyph.DOWNLOADED, Modifier.size(size).semantics { contentDescription = if (done) "Downloaded" else "Downloading" }, if (done) sc.accent else sc.onBgDim.copy(alpha = .7f))
}

/**
 * Downloads: everything kept on the phone for listening without signal, like Spotify's Downloaded view. At the top the total and, while songs are on their way, how far
 * they are (with Try again for the ones that failed); then the downloaded playlists and albums (a tap opens them, a long press offers Play, Shuffle and Remove download), then
 * every downloaded song. Settings > Offline and storage is only the switches and the clean-up.
 */
@Composable
fun DownloadsScreen(nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val m = app.offline
    val ud = app.userData
    val libRev = LocalLibRev.current
    ud.rev
    // read here so the screen follows every change of the downloads
    val entries = m.entries
    val doneCount = m.doneCount; val pending = m.pendingCount; val failed = m.failedCount
    val songs = DownloadsOverview.songs(entries)
    val trouble = entries.filter { it.state != OfflineState.DONE }.sortedBy { if (it.state == OfflineState.FAILED) 0 else 1 }
    // worked out again only when songs finish or the lists change, not on every progress tick; only lists that hold a downloaded song are looked at
    val shelves = remember(doneCount, ud.rev, libRev) {
        fun hasDone(paths: Collection<String>) = paths.any { m.peek(it)?.state == OfflineState.DONE }
        fun art(paths: Collection<String>) = paths.firstNotNullOfOrNull { m.peek(it)?.takeIf { e -> e.state == OfflineState.DONE }?.artKey }
        val out = ArrayList<Shelf>()
        for (p in ud.playlists.toList()) {
            if (!hasDone(p.paths)) continue
            DownloadsOverview.lists(listOf(ListSpec(p.id, p.name, "Playlist", art(p.paths), p.paths.toList()))) { m.peek(it) }
                .forEach { out += Shelf(it, Screen.Detail(DetailKind.USER, p.id), p.paths.toList(), p.id) }
        }
        for (g in shownFolderPlaylists(app)) {
            val paths = g.tracks.map { it.path }
            if (!hasDone(paths)) continue
            DownloadsOverview.lists(listOf(ListSpec("folder:" + g.name, g.name, "Playlist", art(paths) ?: g.artKey, paths))) { m.peek(it) }
                .forEach { out += Shelf(it, Screen.Detail(DetailKind.FOLDER, g.name), paths, "folder:" + g.name) }
        }
        val playlists = out.sortedBy { it.list.name.lowercase() }
        val albums = ArrayList<Shelf>()
        for (g in app.library.albums()) {
            val paths = g.tracks.map { it.path }
            if (!hasDone(paths)) continue
            val key = g.tracks.firstOrNull()?.albumKey ?: continue
            DownloadsOverview.lists(listOf(ListSpec(key, g.name, g.tracks.firstOrNull()?.albumArtist ?: "", art(paths) ?: g.artKey, paths))) { m.peek(it) }
                .forEach { albums += Shelf(it, Screen.Detail(DetailKind.ALBUM, key), paths, null) }
        }
        playlists to albums.sortedBy { it.list.name.lowercase() }
    }
    fun tracksOf(s: Shelf) = s.paths.mapNotNull { m.peek(it) }.filter { it.state == OfflineState.DONE }.distinctBy { it.key }.map { it.toTrack() }
    fun rowSheet(s: Shelf) {
        nav.sheet = SheetSpec(s.list.name, s.list.detail, listOf(
            SheetItem("Play", Glyph.PLAY) { val t = tracksOf(s); if (t.isNotEmpty()) app.player.play(t, 0, context = s.list.name) },
            SheetItem("Shuffle", Glyph.SHUFFLE) { val t = tracksOf(s); if (t.isNotEmpty()) app.player.play(t, t.indices.random(), true, s.list.name) },
            SheetItem("Remove download", Glyph.CLOSE) {
                nav.sheet = SheetSpec("Remove the download of \"${s.list.name}\"?", s.list.detail, listOf(
                    SheetItem("Yes, remove the downloads", Glyph.CLOSE) { s.keptId?.let { m.setKept(it, false) }; s.paths.forEach { m.remove(it) } }, SheetItem("Keep it", Glyph.CHECK) { }))
            },
        ))
    }
    @Composable fun shelfRow(s: Shelf) {
        IpodRow({ nav.push(s.to) }, onLong = { rowSheet(s) }, height = 64.dp,
            leading = { ArtImage(s.list.artKey, Modifier.size(48.dp), thumb = true, corner = 8.dp) },
            trailing = { Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                GlyphIcon(Glyph.DOWNLOADED, Modifier.size(16.dp), sc.accent)
                Box(Modifier.size(44.dp).clip(androidx.compose.foundation.shape.RoundedCornerShape(50)).semantics { contentDescription = "More for ${s.list.name}" }.clickable { rowSheet(s) }, contentAlignment = Alignment.Center) { GlyphIcon(Glyph.MORE, Modifier.size(20.dp), rowDim()) }
            } }) { hi ->
            Column {
                Txt(s.list.name, size = 16f, weight = FontWeight.SemiBold, color = if (hi) androidx.compose.ui.graphics.Color.White else sc.onBg)
                Txt(s.list.detail, size = 12.5f, color = rowDim())
            }
        }
    }

    Column(Modifier.fillMaxSize().background(Palette.bg)) {
        TopBar("Downloads", nav, showBack = true) { TopAction(Glyph.GEAR, "Download settings") { nav.push(Screen.Offline) } }
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
            LazyColumn(Modifier.widthIn(max = 620.dp).fillMaxSize(), contentPadding = PaddingValues(bottom = 30.dp)) {
                item {
                    Card {
                        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Txt(DownloadsOverview.storageLine(doneCount, m.usedBytes), size = 17f, weight = FontWeight.SemiBold)
                            Txt("These play with no signal. ${OfflineMath.formatBytes(m.freeBytes)} free on the phone", size = 13f, color = sc.onBgDim)
                            DownloadsOverview.progressLine(doneCount, pending, m.waitReason())?.let { Txt(it, size = 13f, color = sc.accent) }
                            DownloadsOverview.failedLine(failed)?.let { Txt(it, size = 13f, color = sc.onBgDim) }
                        }
                    }
                }
                if (entries.isEmpty()) item { Box(Modifier.fillMaxWidth().padding(24.dp)) { Txt("Open an album or a playlist and tap Download, or use a song's menu and choose Download.", size = 14f, color = sc.onBgDim, maxLines = 4) } }
                else item {
                    Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        if (songs.isNotEmpty()) {
                            DesignChip("Play", false, { app.player.play(songs.map { it.toTrack() }, 0, context = "Downloads") }, icon = Glyph.PLAY)
                            DesignChip("Shuffle", false, { app.player.play(songs.map { it.toTrack() }, songs.indices.random(), true, "Downloads") }, icon = Glyph.SHUFFLE)
                        }
                        if (failed > 0) DesignChip("Try again", false, { m.retryFailed() })
                    }
                }
                if (shelves.first.isNotEmpty()) { item { SectionHeader("Playlists") }; items(shelves.first, key = { "p" + it.list.id }) { shelfRow(it) } }
                if (shelves.second.isNotEmpty()) { item { SectionHeader("Albums") }; items(shelves.second, key = { "a" + it.list.id }) { shelfRow(it) } }
                // songs still to come or that failed, at most a screenful (the top card says how many there are in all)
                if (trouble.isNotEmpty()) {
                    item { SectionHeader(if (trouble.any { it.state == OfflineState.FAILED }) "Not finished" else "On their way") }
                    items(trouble.take(20), key = { "t" + it.key }) { e -> OfflineEntryRow(e, onPlay = { }, onRemove = { m.remove(e.path) }) }
                    if (trouble.size > 20) item { Box(Modifier.fillMaxWidth().padding(16.dp)) { Txt("and ${trouble.size - 20} more", size = 13f, color = sc.onBgDim) } }
                }
                if (songs.isNotEmpty()) item { SectionHeader("Songs") }
                items(songs, key = { "s" + it.key }) { e -> OfflineEntryRow(e, onPlay = { play(app, songs, e, "Downloads") }, onRemove = { m.remove(e.path) }) }
                if (entries.isNotEmpty()) item { Card { OfflineRow("Remove all downloads") { nav.sheet = SheetSpec("Remove all downloads?", DownloadsOverview.storageLine(doneCount, m.usedBytes), listOf(
                    SheetItem("Yes, remove them all", Glyph.CLOSE) { m.removeAll() }, SheetItem("Keep them", Glyph.CHECK) { })) } } }
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
        }, icon = if (st.kind == KeepStatus.Kind.DONE) Glyph.DOWNLOADED else Glyph.DOWN)
        if (kept) Txt("Kept on this phone: new songs added to this playlist download too", size = 12f, color = sc.onBgDim)
    }
}
