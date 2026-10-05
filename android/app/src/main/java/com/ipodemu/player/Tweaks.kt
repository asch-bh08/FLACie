package com.ipodemu.player

import androidx.compose.ui.graphics.Color
import com.ipodemu.Prefs

/** Personal look-and-feel tweaks (Settings > Look and feel): an accent colour over the theme's own, tighter rows, and the small source badges on or off. Read by the theme and the list rows. */
object Tweaks {
    @Volatile var accent: Color? = null
    @Volatile var compact = false
    @Volatile var badges = true
    @Volatile var motion = true

    val ACCENTS: List<Pair<String, Int>> = listOf(
        "Default" to 0, "Red" to 0xFFFF4D4D.toInt(), "Orange" to 0xFFFF8A3D.toInt(), "Yellow" to 0xFFFFD23D.toInt(), "Green" to 0xFF3DDC84.toInt(),
        "Teal" to 0xFF2DD4BF.toInt(), "Blue" to 0xFF4D9BFF.toInt(), "Purple" to 0xFFA566FF.toInt(),
    )

    fun load(p: Prefs) {
        p.applySafeTouchDefaults()
        accent = p.accentColor.takeIf { it != 0 }?.let { Color(it) }
        compact = p.compactLists
        badges = p.showBadges
        motion = p.motion
    }
}
