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
fun Modifier.sharedCover(active: Boolean = true): Modifier {
    if (!active || !Tweaks.motion) return this
    val s = LocalSharedCover.current ?: return this
    return with(s.shared) { this@sharedCover.sharedElement(rememberSharedContentState("cover"), s.visibility) }
}

/** The shared-transition layout around the whole player, so a cover outside the mini player (a list row) can fly into Now Playing too. */
@OptIn(ExperimentalSharedTransitionApi::class)
val LocalSharedRoot = compositionLocalOf<SharedTransitionScope?> { null }

/**
 * A list row's (or a card's) cover that opened Now Playing flies into the big cover and back when it closes (owner, 9 Nov: "covers fly into Now
 * Playing", as on the web). [id] names where the tap came from; only the cover that opened the player takes part, so the mini player's cover and a
 * row's never both claim the flight. Off with Animations off.
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun Modifier.sharedCoverFrom(id: String, nav: PlayerNav): Modifier {
    if (!Tweaks.motion || nav.coverFrom != id) return this
    val root = LocalSharedRoot.current ?: return this
    return with(root) { this@sharedCoverFrom.sharedElementWithCallerManagedVisibility(rememberSharedContentState("cover"), visible = !nav.nowPlaying) }
}

/** Opens Now Playing from a cover on a page: that cover flies into the big one. */
fun PlayerNav.openFromCover(id: String) { coverFrom = id; nowPlaying = true }
