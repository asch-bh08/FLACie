package com.ipodemu.player

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * What the DJ is doing after a press of the mic, for the bar above the mini player (as the web's DJ bar, 6 Nov): listening (with a level), then what it
 * heard and that it is thinking. Its words come after that as the mini bar's caption.
 */
class DjStatus {
    enum class Stage { NONE, LISTENING, THINKING }

    var stage by mutableStateOf(Stage.NONE); private set
    var heard by mutableStateOf(""); private set
    /** The voice level, 0 to 1 (the recogniser's dB: about 0 is quiet, 8 is loud). */
    var level by mutableFloatStateOf(0f); private set

    fun listening() { stage = Stage.LISTENING; heard = ""; level = 0f }
    fun level(rmsDb: Float) { level = (rmsDb / 8f).coerceIn(0f, 1f) }
    fun heard(text: String) { stage = Stage.THINKING; heard = text; level = 0f }
    fun done() { stage = Stage.NONE; heard = ""; level = 0f }

    fun text(): String? = when (stage) {
        Stage.NONE -> null
        Stage.LISTENING -> "Listening…"
        Stage.THINKING -> "“$heard” · Thinking…"
    }

    companion object {
        /** The one the phone's mic buttons report to. */
        val now = DjStatus()
    }
}

@Composable
fun DjStatusBar(modifier: Modifier = Modifier) {
    val s = DjStatus.now
    val text = s.text()
    AnimatedVisibility(text != null, modifier) {
        val sc = LocalScheme.current
        Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp).clip(RoundedCornerShape(12.dp)).background(sc.accent.copy(alpha = 0.16f))
            .padding(horizontal = 12.dp, vertical = 8.dp).semantics { liveRegion = LiveRegionMode.Polite }, verticalAlignment = Alignment.CenterVertically) {
            if (s.stage == DjStatus.Stage.LISTENING) {
                // the level as a small bar, so it is clear the mic hears you
                val lv by animateFloatAsState(s.level, label = "dj-level")
                Box(Modifier.width(36.dp).height(6.dp).clip(RoundedCornerShape(3.dp)).background(sc.onBgDim.copy(alpha = 0.3f))) {
                    Box(Modifier.fillMaxWidth(0.08f + 0.92f * lv).height(6.dp).clip(RoundedCornerShape(3.dp)).background(sc.accent))
                }
                Box(Modifier.width(10.dp))
            }
            Txt(text ?: "", size = 13f, weight = FontWeight.SemiBold, color = sc.onBg, maxLines = 2)
        }
    }
}
