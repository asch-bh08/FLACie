package com.ipodemu.player

import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.Modifier

/**
 * The cover that flies between the mini player and the full Now Playing screen: both screens give their cover the same key, and the shared-transition layout around
 * them moves and scales one into the other while the full player slides up. Outside that layout (or with nothing to share) the modifier does nothing.
 */
@OptIn(ExperimentalSharedTransitionApi::class)
class SharedCoverScope(val shared: SharedTransitionScope, val visibility: AnimatedVisibilityScope)

val LocalSharedCover = compositionLocalOf<SharedCoverScope?> { null }

@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun Modifier.sharedCover(): Modifier {
    val s = LocalSharedCover.current ?: return this
    return with(s.shared) { this@sharedCover.sharedElement(rememberSharedContentState("cover"), s.visibility) }
}
