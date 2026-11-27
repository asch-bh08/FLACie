package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/**
 * The words people read are one list (docs/plans/one-app-words.md) on the web and the phone. This scans the string literals of the screens
 * (the player folder) and of the files that make user-facing messages (download progress, sign-in) and fails on the spellings the list bans:
 * "Favorite", "..." (three dots), straight double quotes around a name, and "for offline". Comments, identifiers, routes and log lines are not looked at.
 */
class WordsTest {
    private val main = File("src/main/java/com/ipodemu")

    /** A string literal of the source: its text as written (escapes kept) and the line it starts on. */
    class Lit(val text: String, val line: Int, val raw: Boolean)

    /** The string literals of Kotlin source, comments skipped; the code inside ${...} is walked too, for the literals it holds. */
    fun literals(src: String): List<Lit> {
        val out = ArrayList<Lit>()
        var i = 0; var line = 1; val n = src.length
        fun str(code: (Boolean) -> Unit) {
            val startLine = line
            val raw = src.startsWith("\"\"\"", i)
            i += if (raw) 3 else 1
            val buf = StringBuilder()
            while (i < n) {
                val c = src[i]
                if (raw) { if (src.startsWith("\"\"\"", i)) { i += 3; while (i < n && src[i] == '"') { buf.append('"'); i++ }; break } }
                else if (c == '"') { i++; break }
                else if (c == '\\') { buf.append(src, i, minOf(n, i + 2)); i += 2; continue }
                if (c == '\n') line++
                if (c == '$' && i + 1 < n && src[i + 1] == '{') { i += 2; buf.append("\${}"); code(true); continue }
                buf.append(c); i++
            }
            out.add(Lit(buf.toString(), startLine, raw))
        }
        fun code(inBraces: Boolean) {
            var depth = 0
            while (i < n) {
                val c = src[i]
                if (c == '\n') { line++; i++; continue }
                if (c == '/' && i + 1 < n && src[i + 1] == '/') { while (i < n && src[i] != '\n') i++; continue }
                if (c == '/' && i + 1 < n && src[i + 1] == '*') {
                    var d = 1; i += 2
                    while (i < n && d > 0) {
                        if (src[i] == '\n') line++
                        if (src.startsWith("/*", i)) { d++; i += 2 } else if (src.startsWith("*/", i)) { d--; i += 2 } else i++
                    }
                    continue
                }
                if (c == '"') { str { code(it) }; continue }
                if (c == '\'') { i++; if (i < n && src[i] == '\\') { i += 2; while (i < n && src[i] != '\'') i++ } else i++; i++; continue }
                if (inBraces) { if (c == '{') depth++ else if (c == '}') { if (depth == 0) { i++; return }; depth-- } }
                i++
            }
        }
        code(false)
        return out
    }

    private val rules = listOf(
        "three dots (use the one … character)" to Regex("""\.\.\."""),
        "straight double quotes (use curly “ ”)" to Regex("\\\\\""),
        "Favorite (user text says Favourites)" to Regex("""(^|[^\w./])([Ff]avorites?|[Uu]nfavorite)\b"""),
        "\"for offline\" (it is just Download)" to Regex("for offline", RegexOption.IGNORE_CASE),
    )

    // lines that are not user text: logs, diagnostics, regexes, the Jellyfin auth header
    private val notText = Regex("""\bLog\.[a-z]\(|Boot\.span|println\(|Diag\.add\(|\bcheck\(|\brequire\(|Regex\(|\.toRegex|MediaBrowser|Token=""")

    /** The banned spellings in one literal. A raw string, a bare identifier ("favorites") and a pattern like [.,!?;:\"] are not prose. */
    fun banned(l: Lit, lineText: String): List<String> {
        if (notText.containsMatchIn(lineText)) return emptyList()
        val t = l.text
        if (Regex("""^[a-z_]+$""").matches(t.trim()) || t.startsWith("[")) return emptyList()
        return rules.filter { (name, re) -> !(l.raw && name.startsWith("straight")) && re.containsMatchIn(t) }.map { it.first }
    }

    private fun offenders(f: File): List<String> {
        val src = f.readText(); val lines = src.lines()
        return literals(src).flatMap { l ->
            banned(l, lines.getOrElse(l.line - 1) { "" }).map { "${f.name}:${l.line} $it: ${l.text.take(80)}" }
        }
    }

    private fun files(): List<File> {
        val screens = File(main, "player").listFiles { f -> f.extension == "kt" }!!.toList()
        val messages = listOf("AccountSync", "DownloadActivity", "DownloadCoordinator", "DownloadGuard", "DownloadTags").map { File(main, "library/$it.kt") }
        return screens + messages
    }

    @Test fun theScreensAndMessagesUseTheWordsList() {
        val bad = files().flatMap { offenders(it) }
        assertTrue("Words not on the list (docs/plans/one-app-words.md):\n" + bad.joinToString("\n"), bad.isEmpty())
    }

    private fun found(code: String) = literals(code).flatMap { banned(it, code.lines().getOrElse(it.line - 1) { "" }) }

    @Test fun theScannerCatchesTheBannedSpellings() {
        assertEquals(1, found("val a = \"Add to Favorites\"").size)
        assertEquals(1, found("val a = if (x) \"Unfavorite\" else \"Add to favourites\"").size)
        assertEquals(1, found("val a = \"Syncing...\"").size)
        assertEquals(1, found("val a = \"Delete \\\"x\\\"?\"").size)
        assertEquals(1, found("val a = \"Saving 3 songs for offline\"").size)
        // the literal inside a template is found too
        assertEquals(1, found("val a = \"Done \${if (x) \"Favorite\" else \"\"}\"").size)
    }

    @Test fun theScannerLeavesCommentsIdentifiersAndLogsAlone() {
        assertEquals(0, found("// Favorites... \"Favorite\"\nval a = userData.favorites.size").size)
        assertEquals(0, found("/* Favorite \"...\" */ val b = isFavorite(t)").size)
        assertEquals(0, found("val route = \"favorites\"").size)
        assertEquals(0, found("""Log.w("FLACie", "Home shelves failed...")""").size)
        assertEquals(0, found("val ok = \"Added “x” to favourites…\"").size)
    }
}
