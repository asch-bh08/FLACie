package com.ipodemu.player

import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/**
 * The kit's lint: text on the modern screens comes from the five kit styles (Title, Section, Body, Small, Label), not a hand-picked size. A file may
 * keep the `Txt(..., size = <number>)` calls it had when the kit landed (txt-size-allowlist.txt, migrated in wave 1); a new one fails here.
 * Fewer than allowed is fine (lower the number when you migrate). Run with UPDATE_TXT_ALLOWLIST=1 to rewrite the list from the code.
 */
class KitLintTest {
    private val dir = File("src/main/java/com/ipodemu/player")
    private val listFile = File("src/test/resources/txt-size-allowlist.txt")

    @Test fun noNewHandSizedText() {
        val counts = dir.listFiles { f -> f.extension == "kt" }!!.sortedBy { it.name }
            .associate { it.name to handSized(it.readText()) }.filterValues { it > 0 }
        if (System.getenv("UPDATE_TXT_ALLOWLIST")?.trim() == "1") {
            listFile.parentFile.mkdirs()
            listFile.writeText("# Txt(..., size = <number>) calls each file may still have (KitLintTest); lower them as screens move to the kit styles.\n" +
                counts.entries.joinToString("") { "${it.key} ${it.value}\n" })
        }
        val allowed = listFile.readLines().filter { it.isNotBlank() && !it.startsWith("#") }
            .associate { it.substringBefore(' ') to it.substringAfter(' ').trim().toInt() }
        val over = counts.filter { (f, n) -> n > (allowed[f] ?: 0) }
        assertTrue(
            "New Txt(size = number) calls; use the kit's Title/Section/Body/Small/Label instead: " +
                over.entries.joinToString { "${it.key} has ${it.value}, allowed ${allowed[it.key] ?: 0}" },
            over.isEmpty(),
        )
    }

    @Test fun theCounterSeesOneLineAndManyLineCalls() {
        assertTrue(handSized("""Txt("a", size = 14f)""") == 1)
        assertTrue(handSized("Txt(\n  title,\n  Modifier.padding(4.dp),\n  size = 12.5f,\n)") == 1)
        assertTrue(handSized("""Txt(t.title, maxLines = 1)""") == 0)
        assertTrue(handSized("""Txt(x, size = fit.titleSp)""") == 0)
        assertTrue(handSized("""Txt("x", Modifier.size(18.dp))""") == 0)
        assertTrue(handSized("""fun Txt(text: String, size: Float = 16f)""") == 0)
    }

    companion object {
        private val call = Regex("""(?<![A-Za-z0-9_])Txt\(""")
        private val literalSize = Regex("""(?<![A-Za-z0-9_.])size\s*=\s*[0-9]""")

        /** How many Txt(...) calls in [src] set size to a number (the call's own arguments, up to its closing bracket). */
        fun handSized(src: String): Int = call.findAll(src).count { m ->
            if (src.substring(maxOf(0, m.range.first - 4), m.range.first).endsWith("fun ")) return@count false
            var depth = 0; var i = m.range.last; var inStr = false
            val sb = StringBuilder()
            while (i < src.length) {
                val c = src[i]
                if (c == '"' && (i == 0 || src[i - 1] != '\\')) inStr = !inStr
                if (!inStr) { if (c == '(') depth++ else if (c == ')') { depth--; if (depth == 0) break } }
                // only the call's own arguments: what is inside a nested bracket is someone else's
                if (depth == 1 && !inStr) sb.append(c)
                i++
            }
            literalSize.containsMatchIn(sb)
        }
    }
}
