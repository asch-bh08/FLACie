package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.UiState
import kotlinx.coroutines.delay

/** A short message over the bottom of the screen, with an optional action (Undo). One at a time; a newer one replaces the older. */
class Snack(val text: String, val actionLabel: String? = null, val action: (() -> Unit)? = null, val warn: Boolean = false)

@Composable
fun SnackHost(ui: UiState, modifier: Modifier = Modifier) {
    val s = ui.snack ?: return
    LaunchedEffect(s) { delay(5_000); if (ui.snack === s) ui.snack = null }
    Box(modifier.fillMaxWidth().padding(horizontal = 16.dp), contentAlignment = Alignment.BottomCenter) {
        // slides up with a little spring each time a new message arrives
        androidx.compose.animation.AnimatedVisibility(
            androidx.compose.runtime.remember(s) { androidx.compose.animation.core.MutableTransitionState(false).apply { targetState = true } },
            enter = androidx.compose.animation.slideInVertically(androidx.compose.animation.core.spring(0.7f, 420f)) { it / 2 } + androidx.compose.animation.fadeIn(),
        ) {
        Row(
            Modifier.clip(RoundedCornerShape(14.dp)).background(Color(0xF02A2A32)).padding(start = 16.dp, end = 6.dp, top = 4.dp, bottom = 4.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Txt(s.text, Modifier.padding(vertical = 10.dp), size = 14f, color = if (s.warn) Color(0xFFFFC857) else Color.White, maxLines = 2)
            if (s.actionLabel != null) {
                Txt(s.actionLabel, Modifier.clip(RoundedCornerShape(10.dp)).clickable { ui.snack = null; s.action?.invoke() }.padding(horizontal = 12.dp, vertical = 10.dp), size = 14f, weight = FontWeight.ExtraBold, color = LocalScheme.current.accent)
            }
        }
        }
    }
}
