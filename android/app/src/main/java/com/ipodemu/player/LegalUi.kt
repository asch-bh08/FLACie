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

    /** One paragraph of a legal file as the phone shows it: plain words, a heading drawn bold. */
    data class Para(val text: String, val heading: Boolean = false)

    private val LINK = Regex("""\[([^\]]+)]\([^)]*\)""")
    private val TABLE_RULE = Regex("""^\|[\s:|-]+\|$""")
    // a link reads as its words, code and bold as plain text
    private fun inline(s: String) = LINK.replace(s) { it.groupValues[1] }.replace("**", "").replace("`", "")

    /**
     * A legal file in plain paragraphs, so a list draws only what is on screen. The files are Markdown (and hard-wrapped licence texts): wrapped lines join,
     * "#" headings become bold headings, a table's rows become "a · b · c" lines (its rule line goes), "- " items become "• " lines.
     */
    fun paragraphs(text: String): List<Para> {
        val out = mutableListOf<Para>()
        for (block in text.replace("\r\n", "\n").split(Regex("""\n\s*\n"""))) {
            val sb = StringBuilder()
            fun flush() { if (sb.isNotBlank()) out += Para(sb.toString().trim()); sb.clear() }
            fun newLine() { if (sb.isNotEmpty()) sb.append('\n') }
            for (raw in block.split('\n')) {
                val l = raw.trim()
                when {
                    l.isEmpty() || TABLE_RULE.matches(l) -> {}
                    l.startsWith("#") -> { flush(); out += Para(inline(l.trimStart('#').trim()), heading = true) }
                    l.startsWith("|") -> { newLine(); sb.append(l.trim('|').split('|').joinToString(" · ") { inline(it.trim()) }) }
                    l.startsWith("- ") || l.startsWith("* ") -> { newLine(); sb.append("• ").append(inline(l.drop(2))) }
                    else -> { if (sb.isNotEmpty() && !sb.endsWith('\n')) sb.append(' '); sb.append(inline(l)) }
                }
            }
            flush()
        }
        return out.ifEmpty { listOf(Para("This text isn't in this build of the app. It is in the FLACie source repository.")) }
    }
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
                    // the file's own title says the same as the section's: shown once
                    items(paras.dropWhile { it.heading && it.text.equals(name, ignoreCase = true) }) { p ->
                        if (p.heading) Txt(p.text, Modifier.fillMaxWidth().padding(top = 6.dp, bottom = 8.dp), size = 15f, weight = FontWeight.Bold, color = sc.onBg, maxLines = Int.MAX_VALUE)
                        else Txt(p.text, Modifier.fillMaxWidth().padding(bottom = 12.dp), size = 13f, color = sc.onBg, maxLines = Int.MAX_VALUE)
                    }
                }
                item { Txt("FLACie is an independent project, not affiliated with Apple, Sony, Google, Amazon, Jellyfin or Plex.", Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.SemiBold, maxLines = Int.MAX_VALUE) }
            }
        }
    }
}
