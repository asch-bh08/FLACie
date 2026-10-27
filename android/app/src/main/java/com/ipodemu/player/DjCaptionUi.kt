package com.ipodemu.player

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.Crossfade
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

/** The mini bar's second line: the DJ's words while it talks (Captions), else [otherwise] (the artist). A cross-fade between them, nothing more. */
@Composable
fun DjCaptionOr(caption: String?, modifier: Modifier = Modifier, otherwise: @Composable () -> Unit) {
    Crossfade(caption, modifier, animationSpec = tween(300), label = "dj-caption") { c ->
        if (c == null) otherwise()
        else Txt(c, Modifier.semantics { liveRegion = LiveRegionMode.Polite }, size = 12.5f, weight = FontWeight.SemiBold, color = LocalScheme.current.onBg)
    }
}

/** Now Playing's caption: the DJ's words in a dark card over the cover, faded in when it starts talking and out after it stops. */
@Composable
fun DjCaptionCard(caption: String?, modifier: Modifier = Modifier) {
    // the last words stay in the card while it fades out
    var shown by remember { mutableStateOf(caption) }
    if (caption != null) shown = caption
    AnimatedVisibility(caption != null, modifier, enter = fadeIn(tween(300)), exit = fadeOut(tween(400)), label = "dj-caption-card") {
        Box(Modifier.clip(RoundedCornerShape(14.dp)).background(Color(0xB8000000)).padding(horizontal = 16.dp, vertical = 10.dp)
            .semantics { liveRegion = LiveRegionMode.Polite }) {
            Txt(shown ?: "", size = 16f, color = Color.White, maxLines = 3, align = TextAlign.Center)
        }
    }
}
