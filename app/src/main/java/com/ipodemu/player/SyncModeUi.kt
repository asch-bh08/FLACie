package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ipodemu.library.ApplyResult
import com.ipodemu.library.IpodTrack
import com.ipodemu.library.SyncDevice
import com.ipodemu.library.SyncEditClient
import com.ipodemu.library.SyncStaging
import kotlinx.coroutines.launch

/**
 * Sync mode: browse and edit a real iPod plugged into the PC running ipodsync (IpodSync.Web). Nothing touches the
 * device while you edit -- changes are staged here, then Review runs them through ipodsync as a dry run (every
 * check the real write would do), and only an explicit "Write to iPod" + confirmation commits them. ipodsync backs
 * the iPod's database up first and restores it automatically if any post-write check fails.
 */
@Composable
fun SyncModeScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val sc = LocalScheme.current
    val scope = rememberCoroutineScope()
    val client = remember { SyncEditClient() }
    var host by remember { mutableStateOf(app.prefs.syncHost) }
    var devices by remember { mutableStateOf<List<SyncDevice>?>(null) }
    var device by remember { mutableStateOf<SyncDevice?>(null) }
    var staging by remember { mutableStateOf<SyncStaging?>(null) }
    var rev by remember { mutableIntStateOf(0) }   // bumped on every staged edit
    var status by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    var tab by remember { mutableIntStateOf(0) }
    var editTrack by remember { mutableStateOf<IpodTrack?>(null) }
    var openPlaylist by remember { mutableStateOf<SyncStaging.WorkPlaylist?>(null) }
    var reviewing by remember { mutableStateOf(false) }

    val ctx = androidx.compose.ui.platform.LocalContext.current
    /** Opens the ipodsync app (it serves a USB-connected iPod on this device over a loopback API). */
    fun openIpodsync(): Boolean {
        val i = ctx.packageManager.getLaunchIntentForPackage(IPODSYNC_PACKAGE) ?: return false
        ctx.startActivity(i.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)); return true
    }
    fun findDevices(launchIfLocal: Boolean = false) {
        if (host.isBlank()) return
        app.prefs.syncHost = host.trim()
        val local = host.trim() == LOCAL_HOST
        busy = true; status = if (local) "Looking for an iPod plugged into this device..." else "Looking for iPods on ${host.trim()}..."
        scope.launch {
            try {
                devices = try { com.ipodemu.library.SyncClient().devices(host.trim()) } catch (e: Exception) {
                    // on this device the API only exists while the ipodsync app is running: start it once and retry
                    if (!(local && launchIfLocal && openIpodsync())) throw e
                    status = "Starting ipodsync..."
                    kotlinx.coroutines.delay(5000)
                    com.ipodemu.library.SyncClient().devices(host.trim())
                }
                status = if (devices.isNullOrEmpty()) (if (local) "No iPod plugged in" else "No iPod connected to that PC") else null
            } catch (e: Exception) {
                status = if (local) "ipodsync isn't running on this device. Install it (ipodsync v0.4+), open it once, then try again." else "Can't reach ipodsync at ${host.trim()}: ${e.message}"
            } finally { busy = false }
        }
    }
    fun load(d: SyncDevice) {
        busy = true; status = "Reading ${d.volumeLabel ?: d.rootPath}..."
        scope.launch {
            try { staging = SyncStaging(client.load(host.trim(), d.rootPath)); device = d; status = null; rev++ }
            catch (e: Exception) { status = "Couldn't read the iPod: ${e.message}" }
            finally { busy = false }
        }
    }
    LaunchedEffect(Unit) { if (host.isNotBlank()) findDevices() }

    val pending = remember(staging, rev) { staging?.ops()?.length() ?: 0 }
    BackHandler(enabled = editTrack != null || openPlaylist != null || reviewing) { editTrack = null; if (reviewing) reviewing = false else openPlaylist = null }

    Box(Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize()) {
            ModernTopBar(openPlaylist?.name ?: "Sync mode", openPlaylist?.let { { openPlaylist = null } }) {
                GlossPill("Exit", {
                    if (pending == 0) ui.changeTheme(0) else status = "You have $pending unsaved change(s) -- write or discard them first"
                }, height = 36.dp)
            }
            val st = staging
            if (st == null || device == null) {
                // ---- connect ------------------------------------------------------------------------------
                Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).imePadding().padding(horizontal = 20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    Txt("Edit an iPod plugged into this device (needs the ipodsync app) or into a PC running ipodsync: rename songs, rate them, and build or reorder playlists. Changes are only written after you review and confirm them, and the iPod's database is backed up first.", size = 14f, color = sc.onBgDim, maxLines = 6)
                    // no PC, no Wi-Fi: the iPod on this device's USB port, through the ipodsync app's loopback API
                    GlossPill("iPod plugged into this device (USB)", { host = LOCAL_HOST; findDevices(launchIfLocal = true) }, icon = Glyph.IPOD, primary = true)
                    Txt("Or an iPod plugged into a PC running ipodsync:", size = 13f, color = sc.onBgDim)
                    SyncField("ipodsync PC (host:port)", host, { host = it }, "192.168.1.50:5070", uri = true)
                    GlossPill(if (busy) "Looking..." else "Find iPods", { findDevices() })
                    devices?.forEach { d ->
                        // ipodsync reports a placeholder when it can't see the iPod as USB storage yet (not plugged in, or it
                        // still needs "All files access"): send the user to ipodsync to sort that out
                        IpodRow({ if (d.needsUserAction) openIpodsync() else load(d) }, height = if (d.needsUserAction) 84.dp else 60.dp, leading = { IconTile(Glyph.IPOD, size = 40.dp) }, trailing = { GlyphIcon(Glyph.CHEVRON, Modifier.size(18.dp), rowDim()) }) {
                            if (d.needsUserAction) Column { Txt("Open ipodsync", size = 16f, weight = FontWeight.SemiBold); Txt(d.volumeLabel ?: "", size = 12f, color = rowDim(), maxLines = 3) }
                            else Column { Txt(d.volumeLabel ?: "iPod", size = 16f, weight = FontWeight.SemiBold); Txt(d.rootPath + if (d.hasDatabase) "" else "  (no database)", size = 13f, color = rowDim()) }
                        }
                    }
                    // for when detection misses the iPod (or to open a copy of one): its drive or folder on that PC
                    var manual by remember { mutableStateOf("") }
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Box(Modifier.weight(1f)) { SyncField("Or the iPod's drive on that PC", manual, { manual = it }, "G:\\", uri = true) }
                        GlossPill("Open", { if (manual.isNotBlank()) load(SyncDevice(manual.trim(), null, true)) }, height = 44.dp)
                    }
                    status?.let { Txt(it, size = 14f, color = sc.onBgDim, maxLines = 3) }
                }
                return@Column
            }
            val pl = openPlaylist
            if (pl != null) Box(Modifier.weight(1f)) {
                PlaylistEditor(st, pl, onChange = { rev++ }, onClose = { openPlaylist = null })
            } else Column(Modifier.weight(1f)) {
                Row(Modifier.padding(horizontal = 16.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    GlossPill("Songs (${st.tracks.size})", { tab = 0 }, height = 36.dp, primary = tab == 0)
                    GlossPill("Playlists (${st.playlists.count { !it.deleted }})", { tab = 1 }, height = 36.dp, primary = tab == 1)
                    Box(Modifier.weight(1f))
                    Txt(device?.volumeLabel ?: device?.rootPath ?: "", size = 13f, color = sc.onBgDim)
                }
                if (tab == 0) SongsTab(st, rev, onEdit = { editTrack = it }, onRate = { t, s -> st.tracks[t.id] = t.copy(stars = s); rev++ })
                else PlaylistsTab(st, rev, onOpen = { openPlaylist = it }, onCreate = { name -> val p = SyncStaging.WorkPlaylist(null, name, ArrayList(), false, false); st.playlists.add(0, p); rev++; openPlaylist = p })
            }
            status?.let { Txt(it, Modifier.padding(horizontal = 20.dp, vertical = 4.dp), size = 13f, color = sc.onBgDim, maxLines = 2) }
            if (pending > 0) Row(
                Modifier.fillMaxWidth().padding(12.dp).clip(RoundedCornerShape(14.dp)).background(Color(0x22FFFFFF)).padding(horizontal = 16.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Txt("$pending change${if (pending == 1) "" else "s"} staged -- nothing written yet", Modifier.weight(1f), size = 14f, maxLines = 2)
                GlossPill("Discard", { staging = SyncStaging(st.base); openPlaylist = null; rev++ }, height = 38.dp)
                GlossPill("Review", { reviewing = true }, height = 38.dp, primary = true)
            }
        }
        editTrack?.let { t -> TrackEditDialog(t, onDone = { edited -> staging?.tracks?.set(t.id, edited); rev++; editTrack = null }, onDismiss = { editTrack = null }) }
        if (reviewing && staging != null && device != null) ReviewScreen(client, host.trim(), device!!, staging!!, onClose = { reviewing = false }, onWritten = {
            reviewing = false; openPlaylist = null; load(device!!); status = it
        })
    }
}

@Composable
private fun SongsTab(st: SyncStaging, rev: Int, onEdit: (IpodTrack) -> Unit, onRate: (IpodTrack, Int) -> Unit) {
    var q by remember { mutableStateOf("") }
    val list = remember(rev, q) { st.tracks.values.filter { q.isBlank() || it.title.contains(q, true) || it.artist.contains(q, true) || it.album.contains(q, true) }.sortedBy { sortKey(it.title) } }
    Column(Modifier.fillMaxSize()) {
        Box(Modifier.padding(horizontal = 16.dp, vertical = 6.dp)) { SyncField(null, q, { q = it }, "Search songs on the iPod") }
        LazyColumn(Modifier.weight(1f), contentPadding = PaddingValues(bottom = 12.dp)) {
            items(list, key = { it.id }) { t ->
                val changed = st.base.tracks.firstOrNull { it.id == t.id } != t
                IpodRow({ onEdit(t) }, height = 64.dp, trailing = { Stars(t.stars) { onRate(t, it) } }) {
                    Column {
                        Txt((if (changed) "● " else "") + t.title, size = 16f, weight = FontWeight.Medium)
                        Txt(listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString(" - "), size = 13f, color = rowDim())
                    }
                }
            }
        }
    }
}

private fun sortKey(s: String) = com.ipodemu.library.sortKey(s)

/** Five tappable stars; tapping the current rating clears it. */
@Composable
private fun Stars(stars: Int, onSet: (Int) -> Unit) {
    val sc = LocalScheme.current
    Row {
        for (i in 1..5) Box(Modifier.size(28.dp).clickable { onSet(if (stars == i) 0 else i) }, contentAlignment = Alignment.Center) {
            GlyphIcon(Glyph.STAR, Modifier.size(18.dp), if (i <= stars) sc.accent else sc.onBg.copy(alpha = .22f))
        }
    }
}

@Composable
private fun PlaylistsTab(st: SyncStaging, rev: Int, onOpen: (SyncStaging.WorkPlaylist) -> Unit, onCreate: (String) -> Unit) {
    var newName by remember { mutableStateOf("") }
    val lists = remember(rev) { st.playlists.filter { !it.deleted } }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 12.dp)) {
        item {
            Row(Modifier.padding(horizontal = 16.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Box(Modifier.weight(1f)) { SyncField(null, newName, { newName = it }, "New playlist name") }
                GlossPill("Create", { if (newName.isNotBlank()) { onCreate(newName.trim()); newName = "" } }, height = 44.dp, primary = true)
            }
        }
        items(lists, key = { System.identityHashCode(it) }) { p ->
            IpodRow({ onOpen(p) }, height = 60.dp, leading = { IconTile(Glyph.LIST, size = 40.dp) }, trailing = { CountChevron(p.ids.size) }) {
                Column {
                    Txt(p.name, size = 16f, weight = FontWeight.Medium)
                    val tag = when { p.isNew -> "New"; p.smart -> "Smart playlist (read-only)"; p.podcast -> "Podcasts (read-only)"; p.name != p.origName -> "Renamed from ${p.origName}"; else -> null }
                    if (tag != null) Txt(tag, size = 12f, color = rowDim())
                }
            }
        }
    }
}

@Composable
private fun PlaylistEditor(st: SyncStaging, p: SyncStaging.WorkPlaylist, onChange: () -> Unit, onClose: () -> Unit) {
    val sc = LocalScheme.current
    var name by remember(p) { mutableStateOf(p.name) }
    var adding by remember { mutableStateOf(false) }
    var local by remember { mutableIntStateOf(0) }
    @Suppress("UNUSED_EXPRESSION") local
    fun changed() { local++; onChange() }
    if (!p.editable) {
        Txt("Smart and podcast playlists are managed by their rules on the iPod, so they can't be edited here.", Modifier.padding(20.dp), size = 14f, color = sc.onBgDim, maxLines = 3)
    }
    Column(Modifier.fillMaxSize()) {
        if (p.editable) Row(Modifier.padding(horizontal = 16.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Box(Modifier.weight(1f)) { SyncField(null, name, { name = it; if (it.isNotBlank()) { p.name = it.trim(); onChange() } }, "Playlist name") }
            GlossPill("Add songs", { adding = true }, height = 44.dp, primary = true)
            GlossPill("Delete", { p.deleted = true; onChange(); onClose() }, height = 44.dp)
        }
        LazyColumn(Modifier.weight(1f), contentPadding = PaddingValues(bottom = 12.dp)) {
            itemsIndexed(p.ids.toList(), key = { i, id -> "$i-$id" }) { i, id ->
                val t = st.tracks[id]
                IpodRow({}, height = 58.dp, leading = { Txt("${i + 1}", Modifier.widthIn(min = 24.dp), size = 13f, color = rowDim()) }, trailing = {
                    if (p.editable) Row {
                        Box(Modifier.size(40.dp).clickable { if (i > 0) { p.ids.add(i - 1, p.ids.removeAt(i)); changed() } }, contentAlignment = Alignment.Center) {
                            GlyphIcon(Glyph.DOWN, Modifier.size(20.dp).graphicsLayer { rotationZ = 180f }, if (i > 0) sc.onBg else sc.onBg.copy(alpha = .2f))
                        }
                        Box(Modifier.size(40.dp).clickable { if (i < p.ids.lastIndex) { p.ids.add(i + 1, p.ids.removeAt(i)); changed() } }, contentAlignment = Alignment.Center) {
                            GlyphIcon(Glyph.DOWN, Modifier.size(20.dp), if (i < p.ids.lastIndex) sc.onBg else sc.onBg.copy(alpha = .2f))
                        }
                        IconAction(Glyph.CLOSE, "Remove", { p.ids.removeAt(i); changed() }, size = 40.dp)
                    }
                }) {
                    Column { Txt(t?.title ?: "#$id", size = 15f); Txt(t?.artist ?: "", size = 12f, color = rowDim()) }
                }
            }
        }
    }
    if (adding) AddSongsDialog(st, p, onAdd = { p.ids.add(it); changed() }, onDismiss = { adding = false })
}

@Composable
private fun AddSongsDialog(st: SyncStaging, p: SyncStaging.WorkPlaylist, onAdd: (Long) -> Unit, onDismiss: () -> Unit) {
    var q by remember { mutableStateOf("") }
    val list = remember(q) { st.tracks.values.filter { q.isBlank() || it.title.contains(q, true) || it.artist.contains(q, true) }.sortedBy { sortKey(it.title) }.take(300) }
    DialogFrame("Add songs to ${p.name}", onDismiss) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Box(Modifier.weight(1f)) { SyncField(null, q, { q = it }, "Search songs on the iPod") }
            GlossPill("Done", onDismiss, height = 44.dp, primary = true)
        }
        LazyColumn(Modifier.fillMaxWidth().heightIn(max = 240.dp)) {   // capped so Done stays on screen on short (square) screens
            items(list, key = { it.id }) { t ->
                val inList = t.id in p.ids
                IpodRow({ if (!inList) onAdd(t.id) }, height = 56.dp, trailing = { GlyphIcon(if (inList) Glyph.CHECK else Glyph.PLUS, Modifier.size(20.dp), rowDim()) }) {
                    Column { Txt(t.title, size = 15f); Txt(t.artist, size = 12f, color = rowDim()) }
                }
            }
        }
    }
}

@Composable
private fun TrackEditDialog(t: IpodTrack, onDone: (IpodTrack) -> Unit, onDismiss: () -> Unit) {
    var title by remember { mutableStateOf(t.title) }
    var artist by remember { mutableStateOf(t.artist) }
    var album by remember { mutableStateOf(t.album) }
    var stars by remember { mutableIntStateOf(t.stars) }
    DialogFrame("Edit song", onDismiss) {
        SyncField("Title", title, { title = it }, "")
        SyncField("Artist", artist, { artist = it }, "")
        SyncField("Album", album, { album = it }, "")
        Stars(stars) { stars = it }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            GlossPill("Cancel", onDismiss)
            GlossPill("Stage change", { onDone(t.copy(title = title.trim().ifEmpty { t.title }, artist = artist.trim(), album = album.trim(), stars = stars)) }, primary = true)
        }
    }
}

/** Dry run -> results -> explicit confirmation -> write. Nothing reaches the iPod before the last step. */
@Composable
private fun ReviewScreen(client: SyncEditClient, host: String, device: SyncDevice, st: SyncStaging, onClose: () -> Unit, onWritten: (String) -> Unit) {
    val sc = LocalScheme.current
    val scope = rememberCoroutineScope()
    val changeSet = remember { st.changeSet() }
    var dry by remember { mutableStateOf<ApplyResult?>(null) }
    var result by remember { mutableStateOf<ApplyResult?>(null) }
    var confirm by remember { mutableStateOf(false) }
    var writing by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) {
        dry = try { client.apply(host, device.rootPath, changeSet, commit = false, confirmToken = null) }
        catch (e: Exception) { ApplyResult(true, false, false, false, null, emptyList(), emptyList(), emptyList(), null, e.message ?: "Couldn't reach ipodsync") }
    }
    Box(Modifier.fillMaxSize().background(Color(0xFF0B0B0D)).pointerInput(Unit) { detectTapGestures { } }) {
        Column(Modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.BACK, "Back", onClose)
                Txt("Review changes", size = 22f, weight = FontWeight.Bold)
            }
            val r = result ?: dry
            when {
                r == null -> Txt("Checking every change against the iPod's database (dry run, nothing is written)...", size = 14f, color = sc.onBgDim, maxLines = 3)
                else -> {
                    val headline = when {
                        result?.written == true -> "Written to the iPod and verified. Backup: ${result?.backupDir ?: "-"}"
                        result?.restored == true -> "The write failed a check, so the iPod's database was restored from the backup. Nothing changed."
                        result != null -> "Not written: ${result?.error ?: "a check failed"}"
                        r.ok -> "Dry run passed. The iPod's database will be backed up before writing."
                        else -> "Dry run did not pass${r.error?.let { ": $it" } ?: ""} -- nothing can be written."
                    }
                    Txt(headline, size = 15f, weight = FontWeight.SemiBold, color = if (result?.written == true || (result == null && r.ok)) Color(0xFF7CE0A0) else Color(0xFFFFB0B0), maxLines = 4)
                    LazyColumn(Modifier.weight(1f)) {
                        items(r.ops) { o -> Txt((if (o.ok) "✓ " else "✗ ") + o.detail, Modifier.padding(vertical = 4.dp), size = 14f, color = if (o.ok) sc.onBg else Color(0xFFFFB0B0), maxLines = 3) }
                        items(r.problems) { Txt("! $it", Modifier.padding(vertical = 4.dp), size = 13f, color = Color(0xFFFFB0B0), maxLines = 4) }
                        if (!r.ok || result != null) items(r.log.takeLast(12)) { Txt(it, size = 11f, color = sc.onBgDim, maxLines = 3) }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        if (result?.written == true) GlossPill("Done", { onWritten("Wrote ${r.ops.size} change(s) to the iPod") }, primary = true)
                        else {
                            GlossPill("Back to editing", onClose)
                            if (result == null && r.ok && r.confirmToken != null) GlossPill(if (writing) "Writing..." else "Write to iPod", { if (!writing) confirm = true }, primary = true)
                        }
                    }
                }
            }
        }
        if (confirm) DialogFrame("Write to ${device.volumeLabel ?: device.rootPath}?", { confirm = false }) {
            Txt("${changeSet.getJSONArray("ops").length()} change(s) will be written to the iPod. ipodsync backs up its database first, verifies everything after writing, and puts the backup back automatically if any check fails.", size = 14f, maxLines = 6)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                GlossPill("Cancel", { confirm = false })
                GlossPill("Write", {
                    confirm = false; writing = true
                    scope.launch {
                        result = try { client.apply(host, device.rootPath, changeSet, commit = true, confirmToken = dry?.confirmToken) }
                        catch (e: Exception) { ApplyResult(false, false, false, false, null, emptyList(), emptyList(), emptyList(), null, e.message) }
                        writing = false
                    }
                }, primary = true)
            }
        }
    }
}

@Composable
private fun DialogFrame(title: String, onDismiss: () -> Unit, content: @Composable () -> Unit) {
    Box(Modifier.fillMaxSize().background(Color(0xAA000000)).imePadding().pointerInput(Unit) { detectTapGestures { onDismiss() } }, contentAlignment = Alignment.Center) {
        Column(
            Modifier.padding(20.dp).widthIn(max = 520.dp).fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(Color(0xFF1C1C22))
                .pointerInput(Unit) { detectTapGestures { } }.padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Txt(title, size = 19f, weight = FontWeight.Bold, maxLines = 2)
            content()
        }
    }
}

@Composable
private fun SyncField(label: String?, value: String, onChange: (String) -> Unit, hint: String, uri: Boolean = false) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        if (label != null) Txt(label, size = 12f, color = Color(0x99FFFFFF))
        Box(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(horizontal = 14.dp, vertical = 12.dp)) {
            if (value.isEmpty() && hint.isNotEmpty()) Txt(hint, size = 15f, color = Color(0x66FFFFFF))
            BasicTextField(value, onChange, singleLine = true, cursorBrush = SolidColor(Color.White), textStyle = TextStyle(color = Color.White, fontSize = 15.sp),
                keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(autoCorrect = false, keyboardType = if (uri) androidx.compose.ui.text.input.KeyboardType.Uri else androidx.compose.ui.text.input.KeyboardType.Text), modifier = Modifier.fillMaxWidth())   // hosts, drive paths and song titles must not be "corrected"
        }
    }
}

/** ipodsync's Android app serves the iPod on this device's USB port here (LocalApiServer.cs in ipodsync). */
private const val LOCAL_HOST = "127.0.0.1:5071"
private const val IPODSYNC_PACKAGE = "dev.ashley.ipodsync"
