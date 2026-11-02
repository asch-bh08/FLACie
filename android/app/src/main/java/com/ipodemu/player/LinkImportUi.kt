package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ipodemu.library.FlacieWebClient
import com.ipodemu.library.LinkImport
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** The clipboard's text when it holds a Spotify or Deezer playlist or album link, else null. */
private fun clipboardLink(ctx: android.content.Context): String? = try {
    val cm = ctx.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
    LinkImport.recognise(cm.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(ctx)?.toString())
} catch (_: Exception) { null }

/** How a running import reads in a row (as Settings > Background downloads shows it). */
internal fun importProgress(j: FlacieWebClient.Job) = when (j.state) { "done" -> "Done ${j.total - j.failed}/${j.total}"; "waiting" -> "Waiting for space"; "cancelled" -> "Stopped"; else -> "${j.done}/${j.total}" }

/**
 * "Paste a Spotify or Deezer link": a field, Paste and Import. FLACie Web reads the link and imports it like a playlist file; the words match the web's Import
 * page. The field starts with the clipboard's link when it holds one, or with [startWith] (a link shared to FLACie). [showProgress]: follow the job here
 * (Settings already lists its imports); [onStarted] runs after the server has taken the link.
 */
@Composable
fun LinkImportBox(startWith: String? = null, showProgress: Boolean = true, onStarted: (FlacieWebClient.Job) -> Unit = {}) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val scope = rememberCoroutineScope()
    val web = remember { FlacieWebClient(app.prefs) }
    var text by remember { mutableStateOf(startWith ?: "") }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var job by remember { mutableStateOf<FlacieWebClient.Job?>(null) }
    LaunchedEffect(Unit) { if (text.isBlank()) clipboardLink(ctx)?.let { text = it } }

    fun go() {
        if (busy) return
        if (!web.available) { error = LinkImport.NEEDS_WEB; return }
        val url = LinkImport.recognise(text)
        if (url == null) { error = LinkImport.NOT_A_LINK; return }
        busy = true; error = null
        scope.launch(Dispatchers.IO) {
            val r = try { Result.success(LinkImport.start(app.prefs, url)) } catch (e: Exception) { Result.failure(e) }
            withContext(Dispatchers.Main) {
                busy = false
                r.onSuccess { j -> text = ""; job = j; app.ui.say("Sent ${j.file}: ${j.total} songs. The server is fetching them."); onStarted(j) }
                r.onFailure { e -> error = (e as? java.io.IOException)?.message ?: "Couldn't reach FLACie Web" }
            }
        }
    }
    // follow the job the way Settings does: ask the job list every few seconds until it is over
    LaunchedEffect(job?.id) {
        val id = job?.id ?: return@LaunchedEffect
        if (!showProgress) return@LaunchedEffect
        while (true) {
            delay(3000)
            val now = try { withContext(Dispatchers.IO) { web.jobs() }.firstOrNull { it.id == id } } catch (_: Exception) { null }
            if (now != null) job = now
            if (now != null && now.state !in setOf("running", "waiting")) break
        }
    }

    Column(Modifier.padding(horizontal = 16.dp, vertical = 4.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Txt("Paste a Spotify or Deezer link", size = 15f, weight = FontWeight.SemiBold)
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            BasicTextField(
                text, { text = it; error = null }, singleLine = true,
                textStyle = TextStyle(color = sc.onBg, fontSize = 16.sp, fontFamily = DesignFonts.sans),
                cursorBrush = SolidColor(sc.accent),
                keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = androidx.compose.ui.text.input.KeyboardType.Uri, imeAction = androidx.compose.ui.text.input.ImeAction.Go),
                keyboardActions = androidx.compose.foundation.text.KeyboardActions(onGo = { go() }),
                modifier = Modifier.weight(1f).height(52.dp).semantics { contentDescription = "Playlist link" },
                decorationBox = { inner ->
                    Box(Modifier.fillMaxSize().clip(RoundedCornerShape(14.dp)).background(Palette.surface).border(1.dp, Palette.line, RoundedCornerShape(14.dp)).padding(horizontal = 16.dp), contentAlignment = Alignment.CenterStart) {
                        if (text.isEmpty()) Txt("https://open.spotify.com/playlist/...", size = 16f, color = sc.onBgDim)
                        inner()
                    }
                },
            )
            DesignChip("Paste", false, {
                val c = try { (ctx.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager).primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(ctx)?.toString() } catch (_: Exception) { null }
                if (!c.isNullOrBlank()) { text = c.trim(); error = null }
            })
        }
        val ready = text.isNotBlank() && !busy
        Box(
            Modifier.height(48.dp).clip(RoundedCornerShape(14.dp)).background(if (ready) sc.accent else sc.accent.copy(alpha = .35f))
                .semantics { contentDescription = "Import" }.clickable(enabled = ready) { go() }.padding(horizontal = 22.dp),
            contentAlignment = Alignment.Center,
        ) { Txt(if (busy) "Reading…" else "Import", size = 15f, weight = FontWeight.Bold, color = if (ready) sc.accent.readableInk() else sc.onBg.copy(alpha = .5f)) }
        error?.let { Txt(it, size = 13f, color = Palette.pink, maxLines = 4) }
        job?.takeIf { showProgress }?.let { j -> Txt("${j.file}: ${importProgress(j)}", size = 13f, color = sc.onBgDim, maxLines = 2) }
        Txt("A Spotify or Deezer playlist or album that anyone can open (Share, then Copy link). Spotify gives its first 100 songs.", size = 12f, color = sc.onBgDim, maxLines = 3)
    }
}
