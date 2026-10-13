package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.App
import kotlinx.coroutines.delay

/** "14 min left", or "45 s left" in the last minute. */
fun formatSleepLeft(ms: Long): String {
    val s = (ms.coerceAtLeast(0L) + 999L) / 1000L
    return if (s >= 60L) "${(s + 59L) / 60L} min left" else "$s s left"
}

/** The sleep timer state as text ("14 min left", "End of this song (2 min left)"), kept current while it runs; null while it is off. */
@Composable
fun rememberSleepLeft(app: App): String? {
    var mode by remember { mutableStateOf(app.player.sleepMode) }
    DisposableEffect(app) { val un = app.player.observe { mode = app.player.sleepMode }; onDispose { un() } }
    val label by produceState<String?>(if (mode == com.ipodemu.playback.SleepMode.OFF) null else sleepStatus(app), mode) {
        if (mode == com.ipodemu.playback.SleepMode.OFF) { value = null; return@produceState }
        while (true) {
            value = sleepStatus(app)
            delay(if ((app.player.sleepRemainingOrNull ?: Long.MAX_VALUE) > 60_000L) 10_000L else 1_000L)
        }
    }
    return label
}

/** A small chip with the clock and the time left; tapping it opens the timer's choices. */
@Composable
fun SleepChip(text: String, onClick: () -> Unit) {
    val sc = LocalScheme.current
    Box(Modifier.height(44.dp).clip(RoundedCornerShape(22.dp)).focusRing(RoundedCornerShape(22.dp)).clickable(onClick = onClick), contentAlignment = Alignment.Center) {
        Row(
            Modifier.clip(RoundedCornerShape(50)).background(sc.accent.copy(alpha = .22f)).padding(horizontal = 12.dp, vertical = 5.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            GlyphIcon(Glyph.CLOCK, Modifier.size(14.dp), sc.accent)
            Txt(text, size = 12.5f, weight = FontWeight.SemiBold, color = Color.White, maxLines = 1)
        }
    }
}
