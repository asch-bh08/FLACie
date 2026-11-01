package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** The legal files the build copies into assets/legal (app/build.gradle.kts, legalFiles: the ones at the top of the repository), as Settings > About shows them. */
object LegalDocs {
    /** Licences: the third-party notices, FLACie's own MIT licence (with its "as is" clause), and the full texts of the licences the app's libraries use. */
    val LICENCES = listOf(
        "Third-party software" to "legal/THIRD-PARTY-NOTICES.md", "FLACie's licence" to "legal/LICENSE.txt",
        "GNU LGPL 2.1 (jcifs-ng)" to "legal/licenses/LGPL-2.1.txt", "Apache License 2.0" to "legal/licenses/Apache-2.0.txt",
    )
    /** Privacy: what a FLACie server keeps and sends, and what the app keeps on the phone. */
    val PRIVACY = listOf("Privacy" to "legal/PRIVACY.md")

    /** A long text in paragraphs (split at blank lines), so a list draws only what is on screen. */
    fun paragraphs(text: String): List<String> =
        text.replace("\r\n", "\n").split(Regex("""\n\s*\n""")).map { it.trim('\n') }.filter { it.isNotBlank() }
            .ifEmpty { listOf("This text isn't in this build of the app. It is in the FLACie source repository.") }
}

/** Settings > About > Licences or Privacy: the files, one after another, as plain text that scrolls. */
@Composable
fun LegalScreen(nav: PlayerNav, title: String, docs: List<Pair<String, String>>) {
    val ctx = LocalContext.current
    val sc = LocalScheme.current
    val parts = remember(docs) {
        docs.map { (name, file) -> name to LegalDocs.paragraphs(try { ctx.assets.open(file).bufferedReader().use { it.readText() } } catch (_: Exception) { "" }) }
    }
    Column(Modifier.fillMaxSize().background(Palette.bg)) {
        TopBar(title, nav, showBack = true)
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
            LazyColumn(Modifier.widthIn(max = 720.dp).fillMaxSize(), contentPadding = PaddingValues(start = 18.dp, end = 18.dp, bottom = 40.dp)) {
                for ((name, paras) in parts) {
                    item { SectionHeader(name) }
                    items(paras) { p -> Txt(p, Modifier.fillMaxWidth().padding(bottom = 12.dp), size = 13f, color = sc.onBg, maxLines = Int.MAX_VALUE) }
                }
                item { Txt("FLACie is an independent project, not affiliated with Apple, Sony, Google, Amazon, Jellyfin or Plex.", Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.SemiBold, maxLines = Int.MAX_VALUE) }
            }
        }
    }
}
