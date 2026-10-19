package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.togetherWith
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.tween
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.EnterTransition
import androidx.compose.animation.ExitTransition
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.BlendMode
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.CompositingStrategy
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.setProgress
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.min
import androidx.compose.ui.unit.max as maxDp
import com.ipodemu.library.Lyrics
import com.ipodemu.library.RatingStore
import com.ipodemu.library.Track
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import kotlin.math.max

/*
 * The full-screen player, "Living Sleeve" (the same design as the web player: ipodsync/.../NowPlaying.razor and living-sleeve.css).
 *
 * What you see: the cover sets the colours of the whole screen. Two big soft fields in the cover's two colours drift slowly behind everything while a
 * song plays (and keep still when it is paused, or when the phone's animations are off), fading into a dark ground at the bottom so the controls always
 * read. On top, from the top down: a thin bar (close, where it plays from, rating, speed, sleep timer, Retro), the cover as large as the space allows with
 * a soft glow in its own colour, the title and artist with the heart, ONE chip for the file ("FLAC 16-bit / 44.1 kHz", or Hi-Res in the cover's colour),
 * the seek bar, which is the song's real loudness drawn as thin rounded bars (the played part in the cover's colour), previous, one big play button in
 * the cover's colour, next, and at the bottom a dock: shuffle, Lyrics, Up next, more, repeat.
 *
 * Lyrics, Up next and Info share one panel. On a phone it is a sheet that slides up over the cover (Back closes the sheet first, then the player);
 * on a wide screen (600 dp and up) it is always there as a second column on the right.
 */

private val Ground = Color(0xFF120C10)
private val Ink = Color(0xFFFBEEE4)
private val InkDim = Color(0xB8FBEEE4)
private val InkFaint = Color(0x70FBEEE4)
private val OnMain = Color(0xFF140D10)

/** The two animated colours of the screen, as States: read inside draw and layer blocks they repaint without recomposing anything. */
private class SleevePalette(val main: State<Color>, val second: State<Color>)

@Composable
fun LivingSleeveBody(snap: PlayerSnap, nav: PlayerNav) {
    val t = snap.track ?: return
    val ctx = LocalContext.current
    val palette = rememberSleevePalette(t)
    val animatorScale = remember { try { android.provider.Settings.Global.getFloat(ctx.contentResolver, android.provider.Settings.Global.ANIMATOR_DURATION_SCALE, 1f) } catch (_: Exception) { 1f } }
    val drifting = SleeveMotion.drifts(snap.playing, Tweaks.motion, animatorScale)
    var tab by rememberSaveable { mutableStateOf("queue") }
    var sheetOpen by rememberSaveable { mutableStateOf(false) }
    BoxWithConstraints(Modifier.fillMaxSize().background(Ground)) {
        val fullHeight = maxHeight
        val wide = maxWidth >= 600.dp
        SleeveFields(palette, drifting, Modifier.fillMaxSize())
        val open: (String) -> Unit = { id -> if (!wide && sheetOpen && tab == id) sheetOpen = false else { tab = id; sheetOpen = true } }
        Box(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing)) {
            if (wide) {
                Row(Modifier.fillMaxSize().padding(horizontal = 32.dp), horizontalArrangement = Arrangement.spacedBy(48.dp)) {
                    Box(Modifier.weight(1f).fillMaxHeight(), contentAlignment = Alignment.TopEnd) {
                        SleeveStage(snap, nav, t, palette, wide = true, tab = tab, onOpen = open, height = fullHeight, modifier = Modifier.widthIn(max = 560.dp).fillMaxHeight())
                    }
                    SleevePanel(snap, nav, t, palette, tab, { tab = it }, onClose = null, Modifier.weight(1f).fillMaxHeight().padding(top = 64.dp, bottom = 12.dp))
                }
            } else {
                SleeveStage(snap, nav, t, palette, wide = false, tab = if (sheetOpen) tab else null, onOpen = open, height = fullHeight, modifier = Modifier.fillMaxSize().padding(horizontal = 20.dp))
                BackHandler(enabled = sheetOpen) { sheetOpen = false }
                val motion = Tweaks.motion
                AnimatedVisibility(
                    sheetOpen, Modifier.fillMaxSize(),
                    enter = if (motion) slideInVertically(tween(380)) { it } else EnterTransition.None,
                    exit = if (motion) slideOutVertically(tween(300)) { it } else ExitTransition.None,
                ) {
                    // the sheet sits over the stage: the cover's second colour at the top, sinking into the ground, and it takes every touch so nothing behind it moves
                    Box(
                        Modifier.fillMaxSize().drawBehind { drawRect(Brush.verticalGradient(listOf(lerpColor(palette.second.value, Ground, .3f), Ground), endY = size.height * .7f)) }
                            .pointerInput(Unit) { detectTapGestures { } }.padding(horizontal = 18.dp, vertical = 10.dp),
                    ) { SleevePanel(snap, nav, t, palette, tab, { tab = it }, onClose = { sheetOpen = false }, Modifier.fillMaxSize()) }
                }
            }
        }
    }
}

private fun lerpColor(a: Color, b: Color, f: Float) = Color(a.red + (b.red - a.red) * f, a.green + (b.green - a.green) * f, a.blue + (b.blue - a.blue) * f, 1f)

// ---- colours and the moving background ---------------------------------------------------------------------------------

/** The cover's two colours, read once per cover off the main thread from a 24 px copy; a song with no cover gets the warm default, and one whose cover is still loading keeps the last colours until it is in. */
@Composable
private fun rememberSleevePalette(t: Track): SleevePalette {
    val bmp = rememberArt(t.artKey, thumb = false, full = true)
    var cols by remember { mutableStateOf(SleeveColours.DEFAULT) }
    LaunchedEffect(bmp, t.artKey) {
        if (bmp != null) cols = withContext(Dispatchers.Default) {
            val small = android.graphics.Bitmap.createScaledBitmap(bmp, 24, 24, true)
            val px = IntArray(24 * 24)
            small.getPixels(px, 0, 24, 0, 0, 24, 24)
            if (small !== bmp) small.recycle()
            SleeveColours.extract(px)
        } else if (t.artKey == null) cols = SleeveColours.DEFAULT
    }
    val ms = if (Tweaks.motion) 1200 else 0
    return SleevePalette(
        animateColorAsState(Color(cols.main), tween(ms), label = "sleeveMain"),
        animateColorAsState(Color(cols.second), tween(ms), label = "sleeveSecond"),
    )
}

/**
 * Two huge soft discs of colour, one up in the top-left corner and one down in the bottom-right, that slowly wander and breathe while [drifting].
 * They move by layer transforms only (nothing is laid out or recomposed per frame); when it stops they stay where they were.
 */
@Composable
private fun SleeveFields(p: SleevePalette, drifting: Boolean, modifier: Modifier) {
    val phase = remember { Animatable(0f) }
    LaunchedEffect(drifting) {
        if (drifting) phase.animateTo(1f, infiniteRepeatable(tween(11_000, easing = FastOutSlowInEasing), RepeatMode.Reverse))
    }
    BoxWithConstraints(modifier.clipToBounds().drawWithContent {
        drawContent()
        // the fade to the ground: clear above 30% of the height, almost solid at the bottom
        drawRect(Brush.verticalGradient(0f to Color.Transparent, .3f to Color.Transparent, .75f to Ground.copy(alpha = .7f), 1f to Ground.copy(alpha = .95f)))
    }) {
        val vmax = maxDp(maxWidth, maxHeight)
        Box(
            Modifier.align(Alignment.TopStart).requiredSize(vmax * .95f).graphicsLayer {
                val v = vmax.toPx(); val ph = phase.value
                translationX = -.35f * v + .07f * v * ph; translationY = -.40f * v + .05f * v * ph
                scaleX = 1f + .1f * ph; scaleY = 1f + .1f * ph; alpha = .55f
            }.drawBehind { drawCircle(Brush.radialGradient(listOf(p.main.value, Color.Transparent), center = center, radius = size.minDimension / 2)) },
        )
        Box(
            Modifier.align(Alignment.BottomEnd).requiredSize(vmax * .95f).graphicsLayer {
                val v = vmax.toPx(); val ph = phase.value
                translationX = .40f * v - .08f * v * ph; translationY = .45f * v - .04f * v * ph
                scaleX = 1f - .06f * ph; scaleY = 1f - .06f * ph; alpha = .75f
            }.drawBehind { drawCircle(Brush.radialGradient(listOf(p.second.value, Color.Transparent), center = center, radius = size.minDimension / 2)) },
        )
    }
}

/** Fades the top and bottom edges of a scrolling panel into the background instead of cutting the text off. */
private fun Modifier.fadeEdges(top: Dp = 40.dp, bottom: Dp = 64.dp): Modifier = this
    .graphicsLayer { compositingStrategy = CompositingStrategy.Offscreen }
    .drawWithContent {
        drawContent()
        val h = size.height
        if (h > 0f) drawRect(
            Brush.verticalGradient(
                0f to Color.Transparent, (top.toPx() / h).coerceIn(0f, .5f) to Color.Black,
                (1f - bottom.toPx() / h).coerceIn(.5f, 1f) to Color.Black, 1f to Color.Transparent,
            ),
            blendMode = BlendMode.DstIn,
        )
    }

// ---- the stage: top bar, cover, title, chip, seek, transport, dock --------------------------------------------------------

@Composable
private fun SleeveStage(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, wide: Boolean, tab: String?, onOpen: (String) -> Unit, height: Dp, modifier: Modifier) {
    val short = height < 760.dp
    val veryShort = height < 480.dp
    val gap = if (short) 8.dp else 14.dp
    Column(modifier, verticalArrangement = Arrangement.spacedBy(gap)) {
        SleeveTopBar(snap, nav, t, p, Modifier)
        val body: @Composable () -> Unit = {
            SleeveTitle(nav, t, p, long = t.title.length > 30, short = short)
            if (height >= 620.dp) SleeveChip(t, p)
            SleeveSeek(snap, t, p, short)
            SleeveTransport(snap, p, short)
            SleeveDock(snap, nav, t, p, wide, tab, onOpen)
        }
        if (veryShort) {
            // a phone on its side: the cover takes a small fixed size and the column scrolls as a last resort
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(gap)) {
                Box(Modifier.fillMaxWidth().height(120.dp), contentAlignment = Alignment.Center) { SleeveCover(t, p, snap, 120.dp) }
                body()
            }
        } else {
            BoxWithConstraints(Modifier.weight(1f).fillMaxWidth().heightIn(min = 90.dp), contentAlignment = Alignment.Center) {
                SleeveCover(t, p, snap, min(min(maxWidth, maxHeight), 520.dp))
            }
            body()
            // clear of the gesture area: the system bars are already kept out by the insets, this is the breathing room on top of them
            Spacer(Modifier.height(if (short) 8.dp else 20.dp))
        }
    }
}

@Composable
private fun SleeveTopBar(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, modifier: Modifier) {
    val app = LocalApp.current
    val ctx = LocalContext.current
    app.userData.rev
    val rating = app.userData.rating(t)
    val sleepLeft = rememberSleepLeft(app)
    BoxWithConstraints(modifier.fillMaxWidth()) {
        val showWhere = maxWidth >= 480.dp
        Row(Modifier.fillMaxWidth().heightIn(min = 56.dp), verticalAlignment = Alignment.CenterVertically) {
            IconAction(Glyph.DOWN, "Close Now Playing", { nav.nowPlaying = false }, tint = Ink, size = 48.dp, iconScale = .55f)
            if (showWhere) {
                Txt("PLAYING FROM " + (app.player.contextName ?: com.ipodemu.library.SourceLabel.line(t.source)).uppercase(), Modifier.weight(1f).padding(horizontal = 8.dp), size = 12f, weight = FontWeight.SemiBold, color = InkDim, align = TextAlign.Center)
            } else Spacer(Modifier.weight(1f))
            IconAction(Glyph.STAR, "Rating: " + RatingStore.label(rating), { nav.sheet = ratingSheet(app, t) }, tint = if (rating > 0) p.main.value else InkDim)
            // the speed is a word, not a picture: "1x" is read at a glance and turns into the cover's colour when it is not normal
            val slow = snap.speed != 1f
            Box(
                Modifier.heightIn(min = 48.dp).widthIn(min = 48.dp).clip(CircleShape).semantics { contentDescription = "Playback speed " + com.ipodemu.playback.SpeedPolicy.label(snap.speed); role = Role.Button }
                    .clickable { nav.sheet = speedSheet(app) }, contentAlignment = Alignment.Center,
            ) { Txt(com.ipodemu.playback.SpeedPolicy.label(snap.speed), size = 14f, weight = FontWeight.Bold, color = if (slow) p.main.value else InkDim) }
            if (sleepLeft != null) SleepChip(sleepLeft) { nav.sheet = sleepSheet(app) }
            else IconAction(Glyph.CLOCK, "Sleep timer", { nav.sheet = sleepSheet(app) }, tint = InkDim)
            IconAction(Glyph.CASSETTE, "Retro", { openRetro(ctx) }, tint = InkDim)
        }
    }
}

/** The cover: as big as [side] allows, 28 dp corners, lifted off the page, with a soft glow in its own colour behind it. A tap plays or pauses. */
@Composable
private fun SleeveCover(t: Track, p: SleevePalette, snap: PlayerSnap, side: Dp) {
    val app = LocalApp.current
    Box(Modifier.size(side), contentAlignment = Alignment.Center) {
        Box(Modifier.size(side).graphicsLayer { scaleX = 1.35f; scaleY = 1.35f; alpha = .5f }.drawBehind {
            drawCircle(Brush.radialGradient(listOf(p.main.value, Color.Transparent), center = center, radius = size.minDimension / 2))
        })
        val shape = RoundedCornerShape(28.dp)
        ArtImage(
            t.artKey,
            Modifier.size(side).sharedCover().popIn(t.path, 0.88f).shadow(24.dp, shape, clip = false, ambientColor = Color.Black, spotColor = Color.Black)
                .semantics { contentDescription = if (snap.playing) "Pause" else "Play"; role = Role.Button }.clickable { app.player.toggle() },
            corner = 28.dp, full = true,
        )
    }
}

@Composable
private fun SleeveTitle(nav: PlayerNav, t: Track, p: SleevePalette, long: Boolean, short: Boolean) {
    val app = LocalApp.current
    val fav by app.userData.favState(t.path)
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.Bottom) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            val big = if (short) (if (long) 19f else 22f) else (if (long) 21f else 26f)
            Txt(t.title, size = big, weight = FontWeight.ExtraBold, color = Ink, maxLines = 2)
            Row {
                Txt(t.artist.ifEmpty { "Unknown Artist" }, Modifier.weight(1f, fill = false).clickable(enabled = t.artist.isNotEmpty()) {
                    nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ARTIST, t.albumArtist.ifEmpty { t.artist }))
                }, size = 16f, color = InkDim)
                if (t.album.isNotEmpty()) Txt(" · " + t.album, Modifier.weight(1f, fill = false), size = 16f, color = InkDim)
            }
            DownloadTagLine(t, size = 13f, showNew = false)
        }
        IconAction(if (fav) Glyph.HEART_FILLED else Glyph.HEART, if (fav) "Unfavorite" else "Favorite", { app.userData.toggleFavorite(t.path) }, tint = if (fav) p.main.value else InkDim, size = 52.dp, iconScale = .5f)
    }
}

/** The one chip for the file: its format and sampling ("FLAC  16-bit / 44.1 kHz"), or "Hi-Res" in the cover's colour when it is better than CD. Nothing else is badged. */
@Composable
private fun SleeveChip(t: Track, p: SleevePalette) {
    val app = LocalApp.current
    val ai = rememberAudioInfo(app, t)
    val main = if (ai.hiRes) "Hi-Res" else ai.codec
    if (main.isEmpty()) { Spacer(Modifier.height(26.dp)); return }
    Row(
        Modifier.height(26.dp).clip(RoundedCornerShape(13.dp))
            .drawBehind { drawRect(if (ai.hiRes) Brush.horizontalGradient(listOf(p.main.value, lerpColor(p.main.value, Color.White, .45f))) else Brush.horizontalGradient(listOf(Color(0x1FFFFFFF), Color(0x1FFFFFFF)))) }
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Txt(main, size = 12f, weight = FontWeight.Bold, color = if (ai.hiRes) OnMain else Ink)
        if (ai.sampling.isNotEmpty()) Txt(ai.sampling, size = 12f, color = if (ai.hiRes) OnMain.copy(alpha = .7f) else InkDim)
    }
}

// ---- the seek bar: the song's loudness ---------------------------------------------------------------------------------

/** The song's loudness shape: from disk when it has been worked out before, else decoded once in the background (null, and so level bars, until then). */
@Composable
private fun rememberWaveShape(t: Track): State<IntArray?> {
    val app = LocalApp.current
    return produceState<IntArray?>(null, t.path) {
        value = null
        value = WaveAnalyzer.shapeFor(app, t)
    }
}

@Composable
private fun SleeveSeek(snap: PlayerSnap, t: Track, p: SleevePalette, short: Boolean) {
    val app = LocalApp.current
    val pos = rememberPosition(app.player, snap.playing)
    val dur = remember { mutableLongStateOf(app.player.durationMs) }
    LaunchedEffect(t.path) { while (true) { dur.longValue = app.player.durationMs; delay(500) } }
    val shape by rememberWaveShape(t)
    val scrub = remember { mutableFloatStateOf(-1f) }   // where the finger is while it is down (0..1), else -1
    fun fraction() = if (scrub.floatValue >= 0f) scrub.floatValue else if (dur.longValue > 0) (pos.value.toFloat() / dur.longValue).coerceIn(0f, 1f) else 0f
    fun seek(f: Float) { val d = dur.longValue; if (d > 0) app.player.seekTo((f.coerceIn(0f, 1f) * d).toLong()) }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        BoxWithConstraints(
            Modifier.fillMaxWidth().height(if (short) 38.dp else 52.dp)
                .semantics {
                    contentDescription = "Seek"
                    stateDescription = ScrubModel.stateDescription(pos.value, dur.longValue)
                    progressBarRangeInfo = ProgressBarRangeInfo(fraction(), 0f..1f)
                    setProgress { f -> seek(f); true }
                    customActions = listOf(
                        CustomAccessibilityAction("Back 10 seconds") { app.player.skipBy(-10_000L); true },
                        CustomAccessibilityAction("Forward 30 seconds") { app.player.skipBy(30_000L); true },
                    )
                }
                .pointerInput(Unit) { detectTapGestures { o -> seek(o.x / size.width) } }
                .pointerInput(Unit) {
                    detectHorizontalDragGestures(
                        onDragStart = { o -> scrub.floatValue = (o.x / size.width).coerceIn(0f, 1f) },
                        onDragEnd = { seek(scrub.floatValue); scrub.floatValue = -1f },
                        onDragCancel = { scrub.floatValue = -1f },
                    ) { c, _ -> c.consume(); scrub.floatValue = (c.position.x / size.width).coerceIn(0f, 1f) }
                },
        ) {
            val density = androidx.compose.ui.platform.LocalDensity.current
            val barPx = with(density) { 3.dp.toPx() }; val gapPx = with(density) { 2.dp.toPx() }
            val n = WaveBars.count(with(density) { maxWidth.toPx() }, barPx, gapPx)
            val heights = remember(shape, n) { WaveBars.heights(shape, n) }
            // how many bars are played: read only when the canvas draws, and only a change in this whole number repaints it (not every 250 ms tick)
            val played by remember(n) { derivedStateOf { (fraction() * n).toInt() } }
            val rest = Color(0x47FBEEE4)
            androidx.compose.foundation.Canvas(Modifier.fillMaxSize()) {
                val total = n * (barPx + gapPx) - gapPx
                val x0 = (size.width - total) / 2f
                val on = p.main.value
                val upTo = played
                for (i in 0 until n) {
                    val bh = max(3.dp.toPx(), heights[i] * size.height)
                    drawRoundRect(if (i < upTo) on else rest, Offset(x0 + i * (barPx + gapPx), (size.height - bh) / 2f), Size(barPx, bh), CornerRadius(barPx / 2f))
                }
            }
        }
        SeekTimes(pos, dur, scrub)
    }
}

/** The two times under the bar; while the bar is being dragged the left one is where the finger is. */
@Composable
private fun SeekTimes(pos: State<Long>, dur: State<Long>, scrub: State<Float>) {
    val s = scrub.value
    val d = dur.value
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Txt(fmtTime(if (s >= 0f) (s * d).toLong() else pos.value), size = 12f, color = InkDim)
        Txt(fmtTime(d.coerceAtLeast(0)), size = 12f, color = InkDim)
    }
}

// ---- transport and dock -------------------------------------------------------------------------------------------------

@Composable
private fun SleeveTransport(snap: PlayerSnap, p: SleevePalette, short: Boolean) {
    val app = LocalApp.current
    val playFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { playFocus.requestFocus() } catch (_: Exception) {} }
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val keys = androidx.compose.ui.platform.LocalInputModeManager.current.inputMode == androidx.compose.ui.input.InputMode.Keyboard
    val pressed by src.collectIsPressedAsState()
    val size = if (short) 66.dp else 84.dp
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(32.dp, Alignment.CenterHorizontally), verticalAlignment = Alignment.CenterVertically) {
        IconAction(Glyph.PREV, "Previous", { app.player.prev() }, tint = Ink, size = if (short) 50.dp else 60.dp, iconScale = .5f)
        Box(
            Modifier.size(size).graphicsLayer { val s = if (pressed) .95f else 1f; scaleX = s; scaleY = s }
                .shadow(12.dp, CircleShape, ambientColor = p.main.value, spotColor = p.main.value)
                .clip(CircleShape).drawBehind { drawRect(p.main.value) }
                // the ring only for a keyboard or remote: the player hands focus to play when it opens, and a finger should not see that
                .then(if (focused && keys) Modifier.border(3.dp, Ink, CircleShape) else Modifier)
                .focusRequester(playFocus)
                .semantics { contentDescription = if (snap.playing) "Pause" else "Play"; role = Role.Button }
                .clickable(src, null) { app.player.toggle() },
            contentAlignment = Alignment.Center,
        ) {
            androidx.compose.animation.AnimatedContent(snap.playing, label = "sleevePlay", transitionSpec = {
                if (Tweaks.motion) (androidx.compose.animation.fadeIn(tween(140)) + androidx.compose.animation.scaleIn(tween(160), initialScale = .6f)) togetherWith
                    (androidx.compose.animation.fadeOut(tween(90)) + androidx.compose.animation.scaleOut(tween(120), targetScale = .6f))
                else EnterTransition.None togetherWith ExitTransition.None
            }) { playing -> GlyphIcon(if (playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(size * .42f), OnMain) }
        }
        IconAction(Glyph.NEXT, "Next", { app.player.next() }, tint = Ink, size = if (short) 50.dp else 60.dp, iconScale = .5f)
    }
}

@Composable
private fun SleeveDock(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, wide: Boolean, tab: String?, onOpen: (String) -> Unit) {
    val app = LocalApp.current
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        IconAction(Glyph.SHUFFLE, if (snap.shuffle) "Shuffle on" else "Shuffle off", { app.prefs.shuffle = !app.prefs.shuffle; app.player.applyModes() },
            tint = if (snap.shuffle) p.main.value else InkDim, size = 48.dp, iconScale = .48f, dot = snap.shuffle)
        if (!wide) {
            SleevePill(Glyph.LYRICS, "Lyrics", tab == "lyrics") { onOpen("lyrics") }
            SleevePill(Glyph.QUEUE, "Up next", tab == "queue") { onOpen("queue") }
        }
        IconAction(Glyph.MORE, "More", {
            openTrackSheet(app, nav, t, buildList {
                add(SheetItem("Add to playlist", Glyph.PLUS) { nav.sheet = playlistPicker(app, nav, t) })
                add(SheetItem("Equalizer" + if (app.prefs.eq != "Off") "  (${app.prefs.eq})" else "", Glyph.LIST) { nav.sheet = eqSheet(app) })
                add(SheetItem("Song info", Glyph.INFO) { onOpen("info") })
                if (!app.ui.guest) {
                    add(SheetItem(if (app.jam.inJam) "In a Jam" else "Jam", Glyph.JAM) { app.ui.jamOpen = true })
                    add(SheetItem("Devices", Glyph.DEVICES) { app.ui.devicesOpen = true })
                }
            })
        }, tint = InkDim, size = 48.dp, iconScale = .5f)
        IconAction(if (snap.repeat == 2) Glyph.REPEAT_ONE else Glyph.REPEAT, when (snap.repeat) { 1 -> "Repeat all"; 2 -> "Repeat one"; else -> "Repeat off" },
            { app.prefs.repeat = (app.prefs.repeat + 1) % 3; app.player.applyModes() }, tint = if (snap.repeat != 0) p.main.value else InkDim, size = 48.dp, iconScale = .48f, dot = snap.repeat != 0)
    }
}

/** A dock pill (Lyrics, Up next): soft glass, and solid cream with dark text while its panel is open. */
@Composable
private fun SleevePill(g: Glyph, label: String, on: Boolean, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val keys = androidx.compose.ui.platform.LocalInputModeManager.current.inputMode == androidx.compose.ui.input.InputMode.Keyboard
    val ink = if (on) OnMain else Ink
    Row(
        Modifier.height(40.dp).clip(RoundedCornerShape(20.dp)).background(if (on) Ink else Color(0x12FFFFFF))
            .then(if (focused && keys) Modifier.border(2.dp, Ink, RoundedCornerShape(20.dp)) else Modifier)
            .semantics { contentDescription = label; role = Role.Button }.clickable(src, null, onClick = onClick).padding(horizontal = 14.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        GlyphIcon(g, Modifier.size(18.dp), ink)
        Txt(label, size = 14f, weight = FontWeight.SemiBold, color = ink)
    }
}

// ---- the panel: Lyrics | Up next | Info -----------------------------------------------------------------------------------

@Composable
private fun SleevePanel(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, tab: String, onTab: (String) -> Unit, onClose: (() -> Unit)?, modifier: Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
            Row(Modifier.clip(RoundedCornerShape(24.dp)).background(Color(0x12FFFFFF)).padding(4.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                for ((id, label) in listOf("lyrics" to "Lyrics", "queue" to "Up next", "info" to "Info")) {
                    val on = tab == id
                    Box(
                        Modifier.heightIn(min = 38.dp).clip(RoundedCornerShape(20.dp)).background(if (on) Ink else Color.Transparent)
                            .semantics { contentDescription = label; role = Role.Tab }.clickable { onTab(id) }.padding(horizontal = 16.dp),
                        contentAlignment = Alignment.Center,
                    ) { Txt(label, size = 14f, weight = FontWeight.SemiBold, color = if (on) OnMain else InkDim) }
                }
            }
            if (onClose != null) IconAction(Glyph.DOWN, "Back to the cover", onClose, tint = Ink, size = 48.dp, iconScale = .55f)
        }
        Box(Modifier.weight(1f).fillMaxWidth().fadeEdges()) {
            when (tab) {
                "lyrics" -> SleeveLyrics(t, snap.playing)
                "info" -> TrackInfoPanel(t, snap, Modifier.fillMaxSize(), tint = p.main.value)
                else -> {
                    androidx.compose.runtime.CompositionLocalProvider(LocalPlayerNav provides nav) { QueuePanel(snap, Modifier.fillMaxSize()) }
                }
            }
        }
    }
}

private sealed interface LyricsState {
    data object Loading : LyricsState
    data object None : LyricsState
    class Ready(val lyrics: Lyrics) : LyricsState
}

/**
 * Lyrics as big bold left-aligned lines straight on the background (no box). When they are timed, the line being sung is bright and a little larger,
 * the others dim, and the list keeps that line in the middle; tapping a line jumps the song there. Plain lyrics just scroll.
 */
@Composable
private fun SleeveLyrics(t: Track, playing: Boolean) {
    val app = LocalApp.current
    var state by remember(t.path) { mutableStateOf<LyricsState>(LyricsState.Loading) }
    LaunchedEffect(t.path) { state = app.lyrics.get(t)?.let { LyricsState.Ready(it) } ?: LyricsState.None }
    when (val s = state) {
        LyricsState.Loading -> Txt("Looking for lyrics…", Modifier.padding(4.dp), size = 15f, color = InkDim)
        LyricsState.None -> Txt("No lyrics for this song.", Modifier.padding(4.dp), size = 15f, color = InkDim)
        is LyricsState.Ready -> {
            val l = s.lyrics
            val pos = rememberPosition(app.player, playing && l.synced)
            // the current line is a derived number: the list is only touched when the song moves on to the next line
            val current by remember(l) { derivedStateOf { if (!l.synced) -1 else l.lines.indexOfLast { it.timeMs <= pos.value + 250 } } }
            val list = rememberLazyListState()
            LaunchedEffect(current) {
                if (current >= 0) {
                    // the line's middle on the middle of the panel
                    val vh = list.layoutInfo.viewportSize.height
                    val h = list.layoutInfo.visibleItemsInfo.firstOrNull { it.index == current + 1 }?.size ?: 60
                    val offset = -(vh / 2 - h / 2)
                    if (Tweaks.motion) list.animateScrollToItem(current + 1, offset) else list.scrollToItem(current + 1, offset)
                }
            }
            LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = PaddingValues(top = 0.dp, bottom = 120.dp), verticalArrangement = Arrangement.spacedBy(18.dp)) {
                item { Spacer(Modifier.height(80.dp)) }
                itemsIndexed(l.lines) { i, line ->
                    val cur = l.synced && i == current
                    val scale by animateFloatAsState(if (cur) 1.06f else 1f, tween(if (Tweaks.motion) 350 else 0), label = "lyricScale")
                    val color = when {
                        !l.synced -> InkDim
                        cur -> Ink
                        i < current -> Color(0x57FBEEE4)
                        else -> InkFaint
                    }
                    Txt(
                        line.text.ifEmpty { "♪" },
                        Modifier.fillMaxWidth().graphicsLayer { scaleX = scale; scaleY = scale; transformOrigin = TransformOrigin(0f, .5f) }
                            .then(if (l.synced) Modifier.clickable { app.player.seekTo(line.timeMs) } else Modifier),
                        size = if (l.synced) 26f else 20f, weight = if (l.synced) FontWeight.ExtraBold else FontWeight.SemiBold, color = color, maxLines = 6,
                    )
                }
                item { Txt("Lyrics via ${l.source}", Modifier.padding(top = 12.dp), size = 12f, color = InkFaint) }
            }
        }
    }
}
