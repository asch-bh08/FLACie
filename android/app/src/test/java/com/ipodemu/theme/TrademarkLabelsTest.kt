package com.ipodemu.theme

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Apple's and Sony's marks are not FLACie's own labels: nothing the retro players draw or list (model and colour names, menu titles, the
 * mode names in the picker) may carry one. The real iPod sync feature talks about real iPods and is not scanned here.
 */
class TrademarkLabelsTest {
    private val marks = Regex("iPod|Cover Flow|Click Wheel|Apple|U2 |PRODUCT|Retina|Siri|App Store|Discman|MiniDisc|Walkman|MEGA BASS", RegexOption.IGNORE_CASE)
    private val dir = File("src/main/java/com/ipodemu")

    /** Every "string literal" of a file's code lines (comment lines and trailing comments left out). */
    private fun literals(f: File): List<Pair<Int, String>> =
        f.readLines().mapIndexedNotNull { i, line ->
            val t = line.trim()
            if (t.startsWith("//") || t.startsWith("*") || t.startsWith("/*")) null else i + 1 to line.substringBefore(" // ")
        }.flatMap { (n, line) -> Regex("\"((?:[^\"\\\\]|\\\\.)*)\"").findAll(line).map { n to it.groupValues[1] }.toList() }

    private fun assertClean(path: String, only: Regex? = null) {
        val f = File(dir, path)
        assertTrue("${f.absolutePath} not found", f.exists())
        val bad = literals(f).filter { (_, s) -> !s.matches(Regex("[a-z0-9]+")) && (only == null || only.containsMatchIn(s)) && marks.containsMatchIn(s) }
        assertTrue("$path carries a trademark in: $bad", bad.isEmpty())
    }

    @Test fun modelAndColourNamesAndBlurbsAreNeutral() = assertClean("theme/Models.kt")

    @Test fun menusSayCoversAndTheTopMenuIsCalledFLACie() = assertClean("nav/MenuBuilder.kt")

    @Test fun thePickersModeNamesAreNeutral() = assertClean("player/IpodPicker.kt")

    @Test fun theTopBarsAndTheModeRowsAreNeutral() {
        // the sync screens may talk about a real iPod; only the labels of the player itself are checked
        val own = Regex("^iPod$|^iPod (Player|Emulator|appearance)|^Click Wheel|^iPods, desk|^iPod \\(classic|^Retro \\(|classic iPod")
        for (p in listOf("player/ForYouUi.kt", "player/PlayerUi.kt", "player/SettingsUi.kt")) assertClean(p, own)
    }
}
