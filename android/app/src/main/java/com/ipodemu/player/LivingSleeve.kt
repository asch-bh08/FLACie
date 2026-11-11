package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.togetherWith
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.EnterTransition
import androidx.compose.animation.ExitTransition
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
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
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
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
import androidx.compose.foundation.text.BasicText
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
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.graphics.CompositingStrategy
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
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
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.ExperimentalTextApi
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.max as maxDp
import com.ipodemu.R
import com.ipodemu.library.Lyrics
import com.ipodemu.library.RatingStore
import com.ipodemu.library.Track
import com.ipodemu.playback.RepeatCycle
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.wrapContentHeight
import androidx.compose.ui.semantics.heading
import kotlinx.coroutines.withContext
import kotlin.math.max

/*
 * The full-screen player, design I (the same design as the web player: ipodsync/.../NowPlaying.razor and living-sleeve.css, approved by the owner).
 *
 * On a phone: the cover full-bleed across the top ~62% of the screen, fading into a dark ground colour taken from the cover. Over it a top bar (close,
 * an "ALBUM · TRACK n" pill, Options for rating / speed / sleep / sound, and Retro). Below: the title in a big serif with its last word in italics, the
 * artist with a format chip and a source chip, a tinted heart; then shuffle, previous, the ring (the song's loudness as bars round the play button,
 * which is the seek bar: dragging round it seeks), next, repeat; the time under the ring; and outlined Lyrics, Up next and More buttons.
 * Nothing that matters sits in the bottom ~100 dp, where the system's home gesture is, and the lyrics are never on the main screen.
 *
 * Lyrics, Up next and Info share one panel. On a phone it is a sheet that slides up over the cover (Back closes the sheet first, then the player);
 * on a wide screen (600 dp and up) it is always there as a second column on the right.
 */

private val Ink = Color(0xFFF4EFE6)
private val InkDim = Color(0xB8F4EFE6)
private val InkFaint = Color(0x70F4EFE6)
private val OnMain = Color(0xFF1A120C)
private val Glass = Color(0x47000000)
private val Line = Color(0x40F4EFE6)

/** The cover's colours, as States: read inside draw blocks they repaint without recomposing anything. */
private class SleevePalette(val main: State<Color>, val second: State<Color>, val ground: State<Color>, val tone: State<Color>, val toneFg: State<Color>)

/** The three faces of design I, now shared with the rest of the app (DesignI.kt). */
private val SleeveFonts = DesignFonts

/** Text in one of the design's faces (the app's own Txt always uses the app font). */
@Composable
private fun SText(
    text: AnnotatedString, modifier: Modifier = Modifier, family: FontFamily = SleeveFonts.sans, size: Float = 14f, weight: FontWeight = FontWeight.Medium,
    color: Color = Ink, maxLines: Int = 1, spacing: Float = 0f, lineHeight: Float = 0f, align: TextAlign = TextAlign.Start,
) {
    BasicText(
        text, modifier,
        style = TextStyle(
            color = color, fontSize = size.sp, fontWeight = weight, fontFamily = family, letterSpacing = spacing.em, textAlign = align,
            lineHeight = if (lineHeight > 0f) (size * lineHeight).sp else androidx.compose.ui.unit.TextUnit.Unspecified,
        ),
        maxLines = maxLines, overflow = TextOverflow.Ellipsis,
    )
}

@Composable
private fun SText(text: String, modifier: Modifier = Modifier, family: FontFamily = SleeveFonts.sans, size: Float = 14f, weight: FontWeight = FontWeight.Medium, color: Color = Ink, maxLines: Int = 1, spacing: Float = 0f) =
    SText(AnnotatedString(text), modifier, family, size, weight, color, maxLines, spacing)

@Composable
fun LivingSleeveBody(snap: PlayerSnap, nav: PlayerNav) {
    val t = snap.track ?: return
    val palette = rememberSleevePalette(t)
    val app = LocalApp.current
    // the + in the bottom row: the same playlist picker as the More sheet's "Add to playlist"
    val addToPlaylist: () -> Unit = { nav.sheet = playlistPicker(app, nav, t) }
    var tab by rememberSaveable { mutableStateOf("queue") }
    // which More sheet is up: "all" (the ⋯ button) or "playback" (the top bar's Options: rating, speed, sleep timer, sound); null when none
    var moreMode by remember { mutableStateOf<String?>(null) }
    // the player draws every menu it opens in design I itself
    androidx.compose.runtime.DisposableEffect(nav) { nav.designSheets = true; onDispose { nav.designSheets = false } }
    // the phone's panes, as in the old player: Lyrics, the cover, Info, Up next side by side; swipe sideways between them, or use the row at the bottom
    var pane by rememberSaveable { mutableStateOf("art") }
    val toggle: (String) -> Unit = { id -> pane = if (pane == id) "art" else id }
    BoxWithConstraints(Modifier.fillMaxSize().drawBehind { drawRect(palette.ground.value) }) {
        // how the player fits this screen with nothing to scroll (owner, 10 Nov: his dad had to scroll down to the controls and the graphs): the cover
        // shrinks first, then the band of bars round it, then the secondary things go behind More (NowPlayingFit, unit tested)
        val nav0 = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding()
        val top0 = WindowInsets.safeDrawing.asPaddingValues().calculateTopPadding()
        val fontScale = LocalDensity.current.fontScale
        val fit = remember(maxWidth, maxHeight, fontScale, top0, nav0) { NowPlayingFit.plan(maxWidth.value, maxHeight.value, fontScale, top0.value, nav0.value) }
        val wide = fit.layout == NowPlayingFit.Layout.WIDE
        if (wide) {
            Row(Modifier.fillMaxSize()) {
                SleeveStage(snap, nav, t, palette, wide = true, tab = tab, onOpen = { tab = it }, onMore = { moreMode = "all" }, onOptions = { moreMode = "playback" }, modifier = Modifier.weight(1f).widthIn(max = 520.dp).fillMaxHeight(), fit = fit)
                SleevePanel(snap, nav, t, palette, tab, { tab = it }, onClose = null,
                    Modifier.weight(1f).fillMaxHeight().windowInsetsPadding(WindowInsets.safeDrawing).padding(start = 24.dp, end = 32.dp, top = 64.dp, bottom = 12.dp))
            }
        } else {
            BackHandler(enabled = pane != "art" && moreMode == null && nav.sheet == null) { pane = "art" }
            Column(Modifier.fillMaxSize()) {
                PanePager(PaneNav(pane, toggle, { pane = it }), Modifier.weight(1f).fillMaxWidth()) { id ->
                    when (id) {
                        "art" -> SleeveStage(snap, nav, t, palette, wide = false, tab = null, onOpen = toggle, onMore = { moreMode = "all" }, onOptions = { moreMode = "playback" }, modifier = Modifier.fillMaxSize(), fit = fit)
                        // Lyrics and Info take the cover's place as a card of its size: the title, the seek line and the controls stay where they are
                        "lyrics", "info" -> SleeveStage(snap, nav, t, palette, wide = false, tab = null, onOpen = toggle, onMore = { moreMode = "all" }, onOptions = { moreMode = "playback" }, modifier = Modifier.fillMaxSize(), card = id, fit = fit)
                        else -> SleevePane(id, snap, nav, t, palette) { pane = "art" }
                    }
                }
                // the first few times: say that the pages and the buttons below are there (owner, 1 Nov: his dad never knew about lyrics or swiping)
                var tip by remember { mutableStateOf(app.prefs.playerTips < 3).also { if (it.value) app.prefs.playerTips = app.prefs.playerTips + 1 } }
                if (tip && pane == "art" && fit.tip) Row(
                    Modifier.padding(horizontal = 14.dp).fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Glass).clickable { tip = false; app.prefs.playerTips = 3 }
                        .padding(horizontal = 14.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically,
                ) {
                    SText("Swipe sideways for Lyrics, Song info and Up next, or tap the buttons below", Modifier.weight(1f), size = 12.5f, color = Ink, maxLines = 2)
                    SText("OK", Modifier.padding(start = 10.dp), size = 13f, weight = FontWeight.Bold, color = Ink)
                }
                // Lyrics, Up next, Info and More: secondary actions, so they take the bottom strip (above the gesture bar) instead of leaving it empty;
                // on its side the phone has them at the foot of the controls' column instead
                if (fit.layout != NowPlayingFit.Layout.SIDE) Box(Modifier.fillMaxWidth().padding(top = 6.dp, bottom = maxDp(nav0 + 10.dp, 18.dp))) {
                    SleeveActions(false, pane, toggle, addToPlaylist, fit.actions) { moreMode = "all" }
                }
            }
        }
        // the DJ's words while it talks, over the top of the cover (faded in and out)
        DjCaptionCard(snap.djCaption, Modifier.align(Alignment.TopCenter).windowInsetsPadding(WindowInsets.safeDrawing).padding(top = 72.dp, start = 24.dp, end = 24.dp))
        // when the row under the controls is just More, its sheet lists Lyrics, Up next and Song info too
        PlayerMoreSheet(moreMode != null, moreMode == "playback", t, nav, palette, onDismiss = { moreMode = null }, onOpen = { id -> if (wide) tab = id else pane = id },
            panes = !wide && fit.actions == NowPlayingFit.Actions.MORE_ONLY)
        // every menu the player opens (rating, speed, sleep timer, sound, playlists, MusicBrainz, a queue row) in the same design I sheet
        DesignSheet(nav.sheet, palette) { nav.sheet = null }
    }
}

/** One of the side panes on a phone: a serif title with the way back to the cover, then Lyrics, Info or Up next filling the screen. */
@Composable
private fun SleevePane(id: String, snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, onBack: () -> Unit) {
    val title = when (id) { "lyrics" -> "Lyrics"; "info" -> "Song info"; else -> "Up next" }
    Column(
        Modifier.fillMaxSize().drawBehind { drawRect(Brush.verticalGradient(listOf(p.tone.value, p.ground.value), endY = size.height * .6f)) }
            .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)).padding(horizontal = 18.dp),
    ) {
        Row(Modifier.fillMaxWidth().heightIn(min = 64.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f).semantics(mergeDescendants = true) { heading() }) {
                SText(remember(title) { serifTitle(title) }, family = SleeveFonts.serif, size = 30f, weight = FontWeight.Normal)
                SText(listOf(t.title, t.artist).filter { it.isNotEmpty() }.joinToString(" · "), size = 13f, color = InkDim)
            }
            IconAction(Glyph.DOWN, "Back to the cover", onBack, tint = Ink, size = 48.dp, iconScale = .55f)
        }
        Box(Modifier.weight(1f).fillMaxWidth().fadeEdges(top = 16.dp, bottom = 40.dp)) {
            when (id) {
                "lyrics" -> SleeveLyrics(t, snap.playing, big = true)
                "info" -> TrackInfoPanel(t, snap, Modifier.fillMaxSize(), tint = p.main.value, tone = p.tone.value, ground = p.ground.value)
                else -> androidx.compose.runtime.CompositionLocalProvider(LocalPlayerNav provides nav) { QueuePanel(snap, Modifier.fillMaxSize()) }
            }
        }
    }
}

// ---- colours --------------------------------------------------------------------------------------------------------------

/** The cover's colours, read once per cover off the main thread from a 24 px copy; a song with no cover gets the default, and one whose cover is still loading keeps the last colours until it is in. */
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
        animateColorAsState(Color(cols.ground), tween(ms), label = "sleeveGround"),
        animateColorAsState(Color(cols.tone), tween(ms), label = "sleeveTone"),
        animateColorAsState(Color(cols.toneFg), tween(ms), label = "sleeveToneFg"),
    )
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

// ---- the stage: hero cover, top bar, title, controls, actions ------------------------------------------------------------

@Composable
private fun SleeveStage(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, wide: Boolean, tab: String?, onOpen: (String) -> Unit, onMore: () -> Unit, onOptions: () -> Unit, modifier: Modifier, card: String? = null, fit: NowPlayingFit.Plan) {
    val app = LocalApp.current
    // the next two songs' shapes are made while this one plays, so the frame is drawn the moment each starts (as the web does); looked at again now and
    // then, because the prefetcher fills the cache with them as the song goes on
    LaunchedEffect(t.path) { delay(4000); while (true) { try { WaveAnalyzer.prefetch(app, app.player.upNext(2).map { it.second }) } catch (_: Exception) { }; delay(20_000) } }
    BoxWithConstraints(modifier.drawBehind {
        // the cover's tone at the top sinking into the ground, as on the web player
        drawRect(Brush.verticalGradient(listOf(p.tone.value, p.ground.value), endY = size.height * .7f))
    }) {
        val h = maxHeight
        // nothing scrolls: a short phone shrinks the cover, then the frame's band, then puts the secondary things behind More (NowPlayingFit), and a phone
        // on its side puts the cover beside the controls. Only a split-screen sliver that no plan fits still scrolls, as a last resort
        val cramped = !fit.fits
        val scroll = rememberScrollState()
        // the bottom ~100 dp is the home gesture's: nothing that matters goes there (the navigation bar is inside this, not added to it)
        val nav0 = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding()
        // on a phone the next card's peek already keeps the home-gesture strip clear; a wide screen keeps its own margin
        val bottomClear = if (!wide) 8.dp else if (cramped) nav0 + 16.dp else maxDp(92.dp, nav0 + 24.dp)
        val pos = rememberPosition(app.player, snap.playing)
        val dur = remember { mutableLongStateOf(app.player.durationMs) }
        LaunchedEffect(t.path) { while (true) { dur.longValue = app.player.durationMs; delay(500) } }
        val scrub = remember { mutableFloatStateOf(-1f) }   // where the finger is on the frame while it is down (0..1), else -1
        if (fit.layout == NowPlayingFit.Layout.SIDE && !cramped) Row(
            // a phone on its side: the cover in its frame as tall as the screen allows on the left, the rest on the right, nothing below the fold
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            BoxWithConstraints(Modifier.weight(.45f).fillMaxHeight().padding(bottom = 8.dp), contentAlignment = Alignment.Center) {
                val side = minOf(maxWidth - 16.dp, maxHeight, 560.dp)
                if (card == null) SleeveFrame(snap, t, p, side, pos, dur, scrub, fit.compactBand)
                else SleeveCard(card, snap, t, p, Modifier.width(maxWidth - 16.dp).fillMaxHeight()) { onOpen(card) }
            }
            Column(Modifier.weight(.55f).fillMaxHeight().padding(end = 8.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                SleeveTopBar(snap, nav, t, p, onOptions)
                Spacer(Modifier.weight(1f))
                if (card == null) SleeveTitle(nav, t, p, Modifier.padding(horizontal = 12.dp), fit)
                Spacer(Modifier.height(8.dp))
                val time: @Composable () -> Unit = { if (card == null) SleeveTime(pos, dur, scrub) else SleeveSeekLine(pos, dur, p, Modifier.padding(horizontal = 12.dp)) }
                // with no row of actions, More sits beside the time
                if (fit.actions == NowPlayingFit.Actions.MORE_ONLY) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.weight(1f), contentAlignment = Alignment.Center) { time() }
                    Box(Modifier.width(110.dp)) { SleeveActions(false, card, onOpen, null, fit.actions, onMore) }
                } else time()
                Spacer(Modifier.height(8.dp))
                SleeveControls(snap, p, Modifier.padding(horizontal = 8.dp), nav)
                Spacer(Modifier.weight(1f))
                if (fit.actions != NowPlayingFit.Actions.MORE_ONLY) Box(Modifier.fillMaxWidth().padding(top = 6.dp, bottom = maxDp(nav0 + 10.dp, 18.dp))) {
                    SleeveActions(false, card, onOpen, { nav.sheet = playlistPicker(app, nav, t) }, fit.actions, onMore)
                } else Spacer(Modifier.height(maxDp(nav0, 8.dp)))
            }
        } else Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal))
                .then(if (cramped) Modifier.verticalScroll(scroll) else Modifier).padding(bottom = bottomClear),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            SleeveTopBar(snap, nav, t, p, onOptions)
            // the cover in its frame of bars takes whatever height is left, as a square: on a short screen the plan has already made room by
            // compacting the band and putting the secondary things away, so the time, the title and the controls below are always on screen
            BoxWithConstraints(if (cramped) Modifier.fillMaxWidth().height(200.dp) else Modifier.fillMaxWidth().weight(1f).padding(top = 4.dp), contentAlignment = Alignment.Center) {
                val side = minOf(maxWidth - 16.dp, maxHeight, 560.dp)
                if (card == null) SleeveFrame(snap, t, p, side, pos, dur, scrub, fit.compactBand)
                // Lyrics and Info take all the room above the controls, not just the cover's square: on an S22 the graph and the lyrics were below the fold (owner, 1 Nov)
                else SleeveCard(card, snap, t, p, Modifier.width(minOf(maxWidth - 16.dp, 560.dp)).fillMaxHeight()) { onOpen(card) }
            }
            if (card == null) SleeveTime(pos, dur, scrub) else SleeveSeekLine(pos, dur, p, Modifier.padding(horizontal = 22.dp))
            Spacer(Modifier.height(if (h < 700.dp) 8.dp else 14.dp))
            // the card's header names the song, so the big title steps aside for it
            if (card == null) {
                SleeveTitle(nav, t, p, Modifier.padding(horizontal = 22.dp), fit)
                Spacer(Modifier.height(if (h < 700.dp) 10.dp else 16.dp))
            }
            SleeveControls(snap, p, Modifier.padding(horizontal = 14.dp), nav)
            Spacer(Modifier.height(12.dp))
            // on a phone the Lyrics / Up next / Info / More row sits at the foot of the screen, under every pane
            if (wide) { Spacer(Modifier.height(if (h < 700.dp) 8.dp else 14.dp)); SleeveActions(wide, tab, onOpen, actions = fit.actions, onMore = onMore) }
        }
    }
}

/** A round dark-glass icon button, for the top bar over the cover. */
@Composable
private fun GlassButton(label: String, onClick: () -> Unit, content: @Composable () -> Unit) {
    Box(
        Modifier.size(44.dp).clip(CircleShape).background(Glass).focusRing(CircleShape)
            .semantics { contentDescription = label; role = Role.Button }.clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { content() }
}

/** Three sliders, the Options icon (the app's glyph set has none). */
@Composable
private fun SlidersIcon(tint: Color) {
    Canvas(Modifier.size(22.dp)) {
        val sw = 1.8.dp.toPx(); val r = 2.6.dp.toPx()
        for ((row, knob) in listOf(.25f to .68f, .5f to .32f, .75f to .6f)) {
            val y = size.height * row
            drawLine(tint, Offset(size.width * .1f, y), Offset(size.width * .9f, y), sw, StrokeCap.Round)
            drawCircle(tint, r, Offset(size.width * knob, y))
        }
    }
}

/** Options: what the old top bar offered (rating, speed, sleep timer, sound), each opening its own sheet. */
private fun optionsSheet(app: com.ipodemu.App, nav: PlayerNav, t: Track): SheetSpec {
    val rating = app.userData.rating(t)
    return SheetSpec("Options", null, listOf(
        SheetItem("Rating: " + RatingStore.label(rating), Glyph.STAR) { nav.sheet = ratingSheet(app, t) },
        SheetItem("Speed: " + com.ipodemu.playback.SpeedPolicy.label(app.player.speed), Glyph.CLOCK) { nav.sheet = speedSheet(app) },
        SheetItem("Sleep timer: " + sleepStatus(app), Glyph.CLOCK) { nav.sheet = sleepSheet(app) },
        SheetItem("Sound: " + app.prefs.eq, Glyph.LIST) { nav.sheet = eqSheet(app) },
    ))
}

@Composable
private fun SleeveTopBar(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, onOptions: () -> Unit) {
    val app = LocalApp.current
    val ctx = LocalContext.current
    app.userData.rev
    val sleepLeft = rememberSleepLeft(app)
    // on a narrow phone (360 dp) the close button, the DJ, the mic, Options and Retro at 52 dp with 8-10 dp gaps took more than the width, so the
    // last button was pushed off the right edge; there they are 44 dp (still a full touch target) with smaller gaps (owner, 9 Nov: nothing may
    // hang off). The bar stays one row of fixed height, so the cover below it never moves under the buttons.
    BoxWithConstraints(Modifier.fillMaxWidth()) {
    val narrow = maxWidth < 400.dp
    val big = if (narrow) 44.dp else 52.dp
    val gap = if (narrow) 4.dp else 8.dp
    Row(Modifier.fillMaxWidth().heightIn(min = 60.dp).padding(horizontal = if (narrow) 10.dp else 14.dp), verticalAlignment = Alignment.CenterVertically) {
        GlassButton("Close Now Playing", { nav.nowPlaying = false }) { GlyphIcon(Glyph.DOWN, Modifier.size(24.dp), Color.White) }
        Spacer(Modifier.width(gap + 2.dp))
        val fallback = app.player.contextName ?: com.ipodemu.library.SourceLabel.line(t.source)
        // the pill takes what it needs from the space left (up to all of it), and the buttons stay against the right edge
        Box(Modifier.weight(1f).padding(end = 8.dp)) {
            SText(
                SleeveText.where(t.album, t.trackNo, fallback),
                Modifier.clip(RoundedCornerShape(50)).background(Glass).padding(horizontal = 12.dp, vertical = 7.dp),
                family = SleeveFonts.mono, size = 11f, color = Color.White, spacing = .14f,
            )
        }
        // a running sleep timer stays in sight: Options alone would hide it
        if (sleepLeft != null) SleepChip(sleepLeft) { nav.sheet = sleepSheet(app) }
        // the speed is shown when it is not normal, for the same reason
        if (snap.speed != 1f) SText(com.ipodemu.playback.SpeedPolicy.label(snap.speed), Modifier.padding(end = 6.dp).clickable { nav.sheet = speedSheet(app) }, size = 13f, weight = FontWeight.Bold, color = p.main.value)
        // the DJ and the mic, back up here and bigger than before b122 (owner, 8 Nov): white when off, the cover's colour when on
        snap.dj?.let { dj ->
            DjPlainButton(dj, { djTap(app) }, { openDjSettings(nav) }, p.main.value, big, glass = true)
            Spacer(Modifier.width(gap))
            DjMicButton(big, tint = Color.White, glass = Glass, lit = p.main.value)
            Spacer(Modifier.width(gap))
        }
        GlassButton("Options", onOptions) { SlidersIcon(Color.White) }
        // Options opens the design I sheet with just its playback group (rating, speed, sleep timer, sound)
        Spacer(Modifier.width(gap + 2.dp))
        GlassButton("Retro", { openRetro(ctx) }) { GlyphIcon(Glyph.CASSETTE, Modifier.size(22.dp), Color.White) }
    }
    }
}

@Composable
private fun SleeveTitle(nav: PlayerNav, t: Track, p: SleevePalette, modifier: Modifier, fit: NowPlayingFit.Plan) {
    val app = LocalApp.current
    val fav by app.userData.favState(t)
    val ai = rememberAudioInfo(app, t)
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            val (lead, last) = remember(t.title) { SleeveText.split(t.title) }
            val title = remember(lead, last) { buildAnnotatedString { append(lead); withStyle(SpanStyle(fontStyle = FontStyle.Italic)) { append(last) } } }
            // on a short screen the title is smaller, then one line (NowPlayingFit), so the controls below stay on screen
            SText(title, family = SleeveFonts.serif, size = minOf(if (t.title.length > SleeveText.LONG_TITLE) 40f else 52f, fit.titleSp), weight = FontWeight.Normal, maxLines = fit.titleLines, spacing = -.01f, lineHeight = .98f)
            // the chips wrap under the artist on a narrow screen rather than squeezing the name down to a few letters; on a short screen only the
            // artist is here (the format is in Song info, the download in More)
            @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
            androidx.compose.foundation.layout.FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                SText(t.artist.ifEmpty { "Unknown Artist" }, Modifier.align(Alignment.CenterVertically).clickable(enabled = t.artist.isNotEmpty()) {
                    nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ARTIST, t.albumArtist.ifEmpty { t.artist }))
                }, size = 17f, weight = FontWeight.Bold)
                if (fit.chips) {
                    val main = if (ai.hiRes) "Hi-Res" else ai.codec
                    if (main.isNotEmpty()) SleeveChip(main + if (ai.sampling.isNotEmpty()) " " + ai.sampling else "", if (ai.hiRes) p.main.value else p.tone.value, if (ai.hiRes) Color(DesignColour.readableOn(p.main.value.toArgb())) else p.toneFg.value)
                    SleeveChip(com.ipodemu.library.SourceLabel.line(t.source), Color(0x1FFFFFFF), InkDim)
                }
            }
            if (fit.chips) DownloadTagLine(t, size = 13f, showNew = false)
        }
        Box(
            Modifier.size(52.dp).clip(CircleShape).drawBehind { drawRect(p.tone.value) }.focusRing(CircleShape)
                .semantics { contentDescription = if (fav) "Unfavorite" else "Favorite"; role = Role.Button }.clickable { app.userData.toggleFavorite(t) },
            contentAlignment = Alignment.Center,
        ) { GlyphIcon(if (fav) Glyph.HEART_FILLED else Glyph.HEART, Modifier.size(24.dp), if (fav) p.main.value else p.toneFg.value) }
    }
}

/** A small round chip: the file's format, or where it plays from. A pill in the sans like every chip in the app (owner, 9 Nov: no boxy or mono chips). */
@Composable
private fun SleeveChip(text: String, bg: Color, fg: Color) {
    SText(text, Modifier.clip(RoundedCornerShape(50)).background(bg).padding(horizontal = 10.dp, vertical = 4.dp), size = 12f, weight = FontWeight.SemiBold, color = fg, spacing = .01f)
}

// ---- the ring: the song's loudness round the play button -----------------------------------------------------------------

/** The song's loudness shape: from disk when it has been worked out before, else decoded once in the background (null, and so level bars, until then). */
@Composable
private fun rememberWaveShape(t: Track): State<IntArray?> {
    val app = LocalApp.current
    return produceState<IntArray?>(null, t.path) {
        value = null
        val t0 = android.os.SystemClock.elapsedRealtime()
        var first = true
        // a still shape once one is known for the whole song (saved, from the player's cache, or exact); the frame rests level until then
        WaveAnalyzer.shapes(app, t).collect { s ->
            if (first) { first = false; android.util.Log.i("FLACieWave", "first shape for ${t.title} after ${android.os.SystemClock.elapsedRealtime() - t0} ms (${s.count { it >= 0 }} of ${s.size} bars known)") }
            value = s
        }
    }
}

@Composable
private fun SleeveControls(snap: PlayerSnap, p: SleevePalette, modifier: Modifier, nav: PlayerNav) {
    val app = LocalApp.current
    // shuffle, previous | play | next, repeat. Play is a plain rounded square in the cover's colour
    BoxWithConstraints(modifier.fillMaxWidth().widthIn(max = 520.dp)) {
        // the DJ and the mic are in the top bar (owner, 8 Nov), so these five are always full size
        val k = (maxWidth / 330.dp).coerceIn(.8f, 1f)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            IconAction(Glyph.SHUFFLE, if (snap.shuffle) "Shuffle on" else "Shuffle off", { app.prefs.shuffle = !app.prefs.shuffle; app.player.applyModes() },
                tint = if (snap.shuffle) p.main.value else InkDim, size = 48.dp * k, iconScale = .48f, dot = snap.shuffle)
            SleevePillButton(Glyph.PREV, "Previous", p, k) { app.player.prev() }
            SleevePlay(snap, p, k)
            SleevePillButton(Glyph.NEXT, "Next", p, k) { app.player.next() }
            // off, all (the queue starts again at its end, Autoplay adds nothing), one; a short note says which, as the glyph alone did not
            IconAction(if (snap.repeat == RepeatCycle.ONE) Glyph.REPEAT_ONE else Glyph.REPEAT, RepeatCycle.label(snap.repeat),
                { repeatTap(app) }, tint = if (snap.repeat != RepeatCycle.OFF) p.main.value else InkDim, size = 48.dp * k, iconScale = .48f, dot = snap.repeat != RepeatCycle.OFF)
        }
    }
}

/** The DJ button at the left of the controls: the icon in the cover's colour while the DJ is on for this queue, dim when off; a long press opens its settings. */
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
private fun DjPlainButton(on: Boolean, onClick: () -> Unit, onLong: () -> Unit, accent: Color, size: Dp, glass: Boolean = false) {
    Box(
        Modifier.size(size).clip(CircleShape).background(if (glass) Glass else Color.Transparent).focusRing(CircleShape)
            .semantics { contentDescription = djLabel(on); role = Role.Button; stateDescription = if (on) "On" else "Off" }
            .combinedClickable(onClick = onClick, onLongClick = onLong),
        contentAlignment = Alignment.Center,
    ) { GlyphIcon(Glyph.DJ, Modifier.size(size * .48f), if (on) accent else if (glass) Color.White else InkDim) }
}

/** Previous and next: a tinted pill in the cover's tone. */
@Composable
private fun SleevePillButton(g: Glyph, label: String, p: SleevePalette, k: Float = 1f, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    Box(
        Modifier.size(62.dp * k, 52.dp * k).graphicsLayer { val s = if (pressed) .95f else 1f; scaleX = s; scaleY = s }
            .clip(RoundedCornerShape(26.dp)).drawBehind { drawRect(p.tone.value) }.focusRing(RoundedCornerShape(26.dp))
            .semantics { contentDescription = label; role = Role.Button }.clickable(src, null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { GlyphIcon(g, Modifier.size(24.dp), p.toneFg.value) }
}

/**
 * The seek bar as a FRAME round the cover (the web's PC player, flacie.js npWave drawFrame): bars standing out from a rounded square just outside the
 * picture, as long as the song is loud there, starting at 12 o'clock and going clockwise, the played part in the cover's colour. A press on the frame
 * seeks there and dragging scrubs (the seek happens on release); a tap on the picture itself plays or pauses.
 */
@Composable
private fun SleeveFrame(snap: PlayerSnap, t: Track, p: SleevePalette, side: Dp, pos: State<Long>, dur: State<Long>, scrub: androidx.compose.runtime.MutableFloatState, compact: Boolean = false) {
    val app = LocalApp.current
    val shape by rememberWaveShape(t)
    val density = LocalDensity.current
    // the frame's width: room for the gap and the bars on each side of the cover (the web uses 40 px on a PC; a phone gets a little less). On a short
    // screen the band is compact (NowPlayingFit): shorter bars, the same 3 dp bars at the same spacing, so the graph keeps its detail
    val frameW = if (compact) NowPlayingFit.COMPACT_BAND.dp else NowPlayingFit.normalBand(side.value).dp
    val gap = NowPlayingFit.gap(compact).dp
    val cover = side - frameW * 2
    val corner = cover * .06f
    val geo = remember(side, compact, density) { with(density) { FrameSeek(cover.toPx(), corner.toPx(), gap.toPx()) } }
    val bw = with(density) { 3.dp.toPx() }
    val n = remember(geo, bw) { geo.bars(bw) }
    val heights = remember(shape, n) { WaveBars.heights(shape, n) }
    fun fraction() = if (scrub.floatValue >= 0f) scrub.floatValue else if (dur.value > 0) (pos.value.toFloat() / dur.value).coerceIn(0f, 1f) else 0f
    fun seek(f: Float) { val d = dur.value; if (d > 0) app.player.seekTo((f.coerceIn(0f, 1f) * d).toLong()) }
    // how many bars are played: only a change in this whole number repaints the frame (not every 250 ms tick)
    val played by remember(n) { derivedStateOf { (0 until n).count { RingSeek.isPlayed(it, n, fraction()) } } }
    Box(
        Modifier.size(side)
            .semantics {
                contentDescription = "Seek"
                stateDescription = ScrubModel.stateDescription(pos.value, dur.value)
                progressBarRangeInfo = ProgressBarRangeInfo(fraction(), 0f..1f)
                setProgress { f -> seek(f); true }
                customActions = listOf(
                    CustomAccessibilityAction("Back 10 seconds") { app.player.skipBy(-10_000L); true },
                    CustomAccessibilityAction("Forward 30 seconds") { app.player.skipBy(30_000L); true },
                )
            }
            .pointerInput(geo) {
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = true)
                    val cx = size.width / 2f; val cy = size.height / 2f
                    val f0 = geo.fraction(down.position.x - cx, down.position.y - cy)
                    if (f0 == null) {
                        down.consume()
                        // on the picture: a tap plays or pauses (a finger that slides far away is not a tap)
                        var moved = false
                        while (true) {
                            val ev = awaitPointerEvent()
                            val c = ev.changes.firstOrNull { it.id == down.id } ?: break
                            if ((c.position - down.position).getDistance() > viewConfiguration.touchSlop) moved = true
                            if (!c.pressed) { c.consume(); if (!moved) app.player.toggle(); break }
                        }
                        return@awaitEachGesture
                    }
                    // on the frame: nothing is claimed until SeekIntent says tap or scrub; a swipe goes on to the page untouched (owner, 11 Nov)
                    val t0 = System.nanoTime(); var at = down.position; var released = false
                    var kind = SeekIntent.Kind.WAIT
                    while (kind == SeekIntent.Kind.WAIT) {
                        val ev = withTimeoutOrNull(20L) { awaitPointerEvent() }
                        if (ev != null) {
                            val c = ev.changes.firstOrNull { it.id == down.id }
                            if (c == null) { kind = SeekIntent.Kind.SWIPE; break }
                            at = c.position; released = !c.pressed
                        }
                        kind = SeekIntent.decide((System.nanoTime() - t0) / 1_000_000, (at - down.position).getDistance(), viewConfiguration.touchSlop, released)
                    }
                    when (kind) {
                        SeekIntent.Kind.SWIPE -> return@awaitEachGesture
                        SeekIntent.Kind.TAP -> { currentEvent.changes.forEach { it.consume() }; seek(f0); return@awaitEachGesture }
                        else -> {}
                    }
                    scrub.floatValue = geo.nearest(at.x - cx, at.y - cy)
                    currentEvent.changes.forEach { it.consume() }
                    while (true) {
                        val ev = awaitPointerEvent()
                        val c = ev.changes.firstOrNull { it.id == down.id } ?: break
                        if (!c.pressed) { c.consume(); break }
                        c.consume()
                        scrub.floatValue = geo.nearest(c.position.x - cx, c.position.y - cy)
                    }
                    if (scrub.floatValue >= 0f) seek(scrub.floatValue)
                    scrub.floatValue = -1f
                }
            },
        contentAlignment = Alignment.Center,
    ) {
        val rest = Color(0x38F4EFE6)
        Canvas(Modifier.fillMaxSize()) {
            val maxL = (frameW - gap - 4.dp).toPx()
            val inset = 1.5.dp.toPx(); val minL = 3.dp.toPx()
            val on = p.main.value
            val upTo = played
            for (i in 0 until n) {
                val w = geo.walk((i + .5f) / n)
                val len = max(minL, heights[i] * maxL)
                val x = center.x + w[0]; val y = center.y + w[1]
                drawLine(if (i < upTo) on else rest, Offset(x + w[2] * inset, y + w[3] * inset), Offset(x + w[2] * (len - inset), y + w[3] * (len - inset)), bw, StrokeCap.Round)
            }
        }
        // the cover, rounded, with a soft shadow; a tap on it is play / pause (handled above, so a press on the frame never reaches it)
        ArtImage(
            t.artKey,
            // the cover that opened the player (the mini player's, or a row's) flies into this one and back (SharedCover.kt)
            Modifier.size(cover).sharedCover().shadow(24.dp, RoundedCornerShape(corner), clip = false, ambientColor = Color.Black, spotColor = Color.Black)
                .semantics { contentDescription = if (snap.playing) "Pause" else "Play"; role = Role.Button },
            corner = corner, full = true,
        )
    }
}

/** The play button: a rounded square in the cover's colour with dark text, between the previous and next pills. */
@Composable
private fun SleevePlay(snap: PlayerSnap, p: SleevePalette, k: Float = 1f) {
    val app = LocalApp.current
    val playFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { playFocus.requestFocus() } catch (_: Exception) {} }
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val keys = androidx.compose.ui.platform.LocalInputModeManager.current.inputMode == androidx.compose.ui.input.InputMode.Keyboard
    val pressed by src.collectIsPressedAsState()
    val shape = RoundedCornerShape(22.dp)
    Box(
        Modifier.size(84.dp * k, 64.dp * k).graphicsLayer { val s = if (pressed) .95f else 1f; scaleX = s; scaleY = s }
            .clip(shape).drawBehind { drawRect(p.main.value) }
            // the ring only for a keyboard or remote: the player hands focus to play when it opens, and a finger should not see that
            .then(if (focused && keys) Modifier.border(3.dp, Ink, shape) else Modifier)
            .focusRequester(playFocus)
            .semantics { contentDescription = if (snap.playing) "Pause" else "Play"; role = Role.Button }
            .clickable(src, null) { app.player.toggle() },
        contentAlignment = Alignment.Center,
    ) {
        androidx.compose.animation.AnimatedContent(snap.playing, label = "sleevePlay", transitionSpec = {
            if (Tweaks.motion) (androidx.compose.animation.fadeIn(tween(140)) + androidx.compose.animation.scaleIn(tween(160), initialScale = .6f)) togetherWith
                (androidx.compose.animation.fadeOut(tween(90)) + androidx.compose.animation.scaleOut(tween(120), targetScale = .6f))
            else EnterTransition.None togetherWith ExitTransition.None
        }) { playing -> GlyphIcon(if (playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(30.dp), Color(DesignColour.readableOn(p.main.value.toArgb()))) }
    }
}
/** "1:36 / 4:12" under the ring; while the ring is being dragged the first time is where the finger is. */
@Composable
private fun SleeveTime(pos: State<Long>, dur: State<Long>, scrub: State<Float>) {
    val s = scrub.value
    val d = dur.value
    val text = buildAnnotatedString {
        append(fmtTime(if (s >= 0f) (s * d).toLong() else pos.value))
        withStyle(SpanStyle(color = InkDim)) { append(" / " + fmtTime(d.coerceAtLeast(0))) }
    }
    SText(text, Modifier.padding(top = 2.dp), family = SleeveFonts.mono, size = 15f, weight = FontWeight.Medium, align = TextAlign.Center)
}

/** Lyrics or Info in a card over the cover's place, with its name, the song and a way back to the cover at the top; the content scrolls inside it. */
@Composable
private fun SleeveCard(id: String, snap: PlayerSnap, t: Track, p: SleevePalette, size: Modifier, onClose: () -> Unit) {
    val shape = RoundedCornerShape(28.dp)
    Column(size.clip(shape).background(Color(0x38000000)).border(1.dp, Color(0x1FF4EFE6), shape).padding(start = 18.dp, end = 8.dp, top = 6.dp)) {
        // at least 52 dp, taller at big text (the serif name and the song line were cut at font scale 1.5)
        Row(Modifier.fillMaxWidth().heightIn(min = 52.dp).semantics(mergeDescendants = true) { heading() }, verticalAlignment = Alignment.CenterVertically) {
            val title = if (id == "lyrics") "Lyrics" else "Song info"
            Column(Modifier.weight(1f)) {
                SText(remember(title) { serifTitle(title) }, family = SleeveFonts.serif, size = 24f, weight = FontWeight.Normal)
                SText(listOf(t.title, t.artist).filter { it.isNotEmpty() }.joinToString(" · "), size = 12f, color = InkDim, maxLines = 1)
            }
            IconAction(Glyph.CLOSE, "Back to the cover", onClose, tint = Ink, size = 48.dp, iconScale = .45f)
        }
        Box(Modifier.weight(1f).fillMaxWidth().padding(end = 10.dp).fadeEdges(top = 12.dp, bottom = 28.dp)) {
            if (id == "lyrics") SleeveLyrics(t, snap.playing)
            else TrackInfoPanel(t, snap, Modifier.fillMaxSize(), tint = p.main.value, tone = p.tone.value, ground = p.ground.value)
        }
    }
}

/** While a card covers the frame, the seek is a plain line: the time at each end, the played part in the cover's colour; tap or drag to seek. */
@Composable
private fun SleeveSeekLine(pos: State<Long>, dur: State<Long>, p: SleevePalette, modifier: Modifier) {
    val app = LocalApp.current
    var drag by remember { mutableFloatStateOf(-1f) }
    val d = dur.value.coerceAtLeast(1)
    val frac = if (drag >= 0f) drag else (pos.value.toFloat() / d).coerceIn(0f, 1f)
    Column(modifier.fillMaxWidth().padding(top = 10.dp)) {
        Box(
            Modifier.fillMaxWidth().height(28.dp)
                .semantics { contentDescription = "Seek"; stateDescription = fmtTime((frac * d).toLong()) + " of " + fmtTime(d) }
                .pointerInput(Unit) {
                    detectTapGestures { o -> app.player.seekTo(((o.x / size.width).coerceIn(0f, 1f) * app.player.durationMs).toLong()) }
                }
                .pointerInput(Unit) {
                    detectHorizontalDragGestures(
                        onDragStart = { o -> drag = (o.x / size.width).coerceIn(0f, 1f) },
                        onDragEnd = { app.player.seekTo((drag * app.player.durationMs).toLong()); drag = -1f },
                        onDragCancel = { drag = -1f },
                    ) { ch, _ -> drag = (ch.position.x / size.width).coerceIn(0f, 1f) }
                }
                .drawBehind {
                    val y = size.height / 2; val h = 4.dp.toPx()
                    drawRoundRect(Line, Offset(0f, y - h / 2), androidx.compose.ui.geometry.Size(size.width, h), androidx.compose.ui.geometry.CornerRadius(h))
                    drawRoundRect(p.main.value, Offset(0f, y - h / 2), androidx.compose.ui.geometry.Size(size.width * frac, h), androidx.compose.ui.geometry.CornerRadius(h))
                    drawCircle(Ink, 7.dp.toPx(), Offset(size.width * frac, y))
                },
        )
        Row(Modifier.fillMaxWidth()) {
            SText(fmtTime((frac * d).toLong()), family = SleeveFonts.mono, size = 13f, color = InkDim)
            Spacer(Modifier.weight(1f))
            SText(fmtTime(dur.value.coerceAtLeast(0)), family = SleeveFonts.mono, size = 13f, color = InkDim)
        }
    }
}

// ---- Lyrics, Up next, More --------------------------------------------------------------------------------------------------

@Composable
private fun SleeveActions(wide: Boolean, tab: String?, onOpen: (String) -> Unit, onAdd: (() -> Unit)? = null, actions: NowPlayingFit.Actions = NowPlayingFit.Actions.FULL, onMore: () -> Unit) {
    // five equal slots, More in the middle: Lyrics, Up next | More | Info, Add. Each is an icon over its word (icons alone, or words beside two bare icons,
    // left the owner's dad not knowing there were lyrics; 1 Nov); the open pane's slot is solid cream. On a short screen (NowPlayingFit) the slots are
    // 44 dp with the word alone, and at the last step only More is left, its sheet listing Lyrics, Up next and Song info
    val full = actions == NowPlayingFit.Actions.FULL
    Row(Modifier.fillMaxWidth().padding(horizontal = 14.dp), horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally), verticalAlignment = Alignment.CenterVertically) {
        @Composable
        fun Slot(label: String, word: String, on: Boolean, glyph: Glyph, onClick: () -> Unit, tab: Boolean = true) {
            val shape = RoundedCornerShape(18.dp)
            Column(
                Modifier.weight(1f).height(if (full) 58.dp else 44.dp).clip(shape).background(if (on) Ink else Glass).focusRing(shape)
                    .semantics(mergeDescendants = true) { contentDescription = label; role = if (tab) Role.Tab else Role.Button; if (on) stateDescription = "Selected" }
                    .clickable(onClick = onClick),
                horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
            ) {
                if (full) GlyphIcon(glyph, Modifier.size(20.dp), if (on) OnMain else Ink)
                SText(word, size = if (full) 11.5f else 13f, weight = FontWeight.SemiBold, color = if (on) OnMain else Ink)
            }
        }
        if (actions == NowPlayingFit.Actions.MORE_ONLY) Slot("More options", "More", false, Glyph.MORE, onMore, tab = false)
        else {
            Slot("Lyrics", "Lyrics", tab == "lyrics", Glyph.LYRICS, { onOpen("lyrics") })
            Slot("Up next", "Up next", tab == "queue", Glyph.QUEUE, { onOpen("queue") })
            Slot("More options", "More", false, Glyph.MORE, onMore, tab = false)
            Slot("Song info", "Info", tab == "info", Glyph.INFO, { onOpen("info") })
            Slot("Add to playlist", "Add", false, Glyph.PLUS, { onAdd?.invoke() }, tab = false)
        }
    }
}

/** Shares the song as words (title, artist, album) through Android's share sheet: the file itself stays where it is. */
internal fun shareTrack(ctx: android.content.Context, t: Track) = shareSongs(ctx, "song", t.title, listOf(t))

/**
 * Shares songs as a link to them on the FLACie server (ShareLinks), which opens in the FLACie of whoever gets it; with no server to ask, the song's
 * name goes as words instead. Android's share sheet sends it (Messages, WhatsApp, email...).
 */
internal fun shareSongs(ctx: android.content.Context, kind: String, name: String, songs: List<Track>) {
    val app = com.ipodemu.App.of(ctx)
    kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.IO).launch {
        val link = com.ipodemu.library.ShareLinks.create(app.prefs, kind, name, songs)
        val one = songs.firstOrNull()
        val text = when {
            link != null && kind == "song" && one != null -> "${one.title} by ${one.artist}: $link"
            link != null -> "$name: $link"
            one != null && songs.size == 1 -> listOf(one.title, one.artist, one.album).filter { it.isNotBlank() }.joinToString(" · ")
            else -> name + "\n" + songs.take(50).joinToString("\n") { "${it.title} · ${it.artist}" }
        }
        // a subject too, so apps that file shared text (email, notes) name it after the song rather than "text"
        val send = android.content.Intent(android.content.Intent.ACTION_SEND).setType("text/plain").putExtra(android.content.Intent.EXTRA_TEXT, text)
            .putExtra(android.content.Intent.EXTRA_SUBJECT, name).putExtra(android.content.Intent.EXTRA_TITLE, name)
        kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Main) {
            // no link: say why, rather than quietly sending only the name
            if (link == null) app.ui.say(if (app.prefs.flacieWebUrl.isBlank()) "No link: open FLACie on the web once so this phone learns its address" else "Couldn't reach FLACie Web for a link: sharing the name", warn = true)
            try { ctx.startActivity(android.content.Intent.createChooser(send, "Share").addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: Exception) { }
        }
    }
}

/** An outlined button (Lyrics, Up next, More): solid cream with dark text while its panel is open. */
@Composable
private fun SleeveOutline(label: String, on: Boolean, glyph: Glyph? = null, onClick: () -> Unit) {
    val shape = RoundedCornerShape(12.dp)
    Box(
        Modifier.height(48.dp).then(if (glyph != null) Modifier.width(48.dp) else Modifier).clip(shape)
            .background(if (on) Ink else Color.Transparent).border(1.dp, if (on) Ink else Line, shape).focusRing(shape)
            .semantics { contentDescription = label; role = Role.Button }.clickable(onClick = onClick)
            .padding(horizontal = if (glyph != null) 0.dp else 16.dp),
        contentAlignment = Alignment.Center,
    ) {
        if (glyph != null) GlyphIcon(glyph, Modifier.size(20.dp), Ink)
        else SText(label, size = 14f, weight = FontWeight.Medium, color = if (on) OnMain else Ink)
    }
}

// ---- the panel: Lyrics | Up next | Info -----------------------------------------------------------------------------------

@Composable
private fun SleevePanel(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, tab: String, onTab: (String) -> Unit, onClose: (() -> Unit)?, modifier: Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
            // the web's three outlined tabs, in its order, the open one filled
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                for ((id, label) in listOf("queue" to "Up next", "lyrics" to "Lyrics", "info" to "Info")) SleeveOutline(label, tab == id) { onTab(id) }
            }
            if (onClose != null) IconAction(Glyph.DOWN, "Back to the cover", onClose, tint = Ink, size = 48.dp, iconScale = .55f)
        }
        Box(Modifier.weight(1f).fillMaxWidth().fadeEdges()) {
            when (tab) {
                "lyrics" -> SleeveLyrics(t, snap.playing)
                "info" -> TrackInfoPanel(t, snap, Modifier.fillMaxSize(), tint = p.main.value, tone = p.tone.value, ground = p.ground.value)
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
private fun SleeveLyrics(t: Track, playing: Boolean, big: Boolean = false) {
    val app = LocalApp.current
    when (val s = rememberLyrics(t)) {
        LyricsState.Loading -> Txt("Looking for lyrics…", Modifier.padding(4.dp), size = 16f, color = InkDim)
        LyricsState.None -> Txt("No lyrics for this song.", Modifier.padding(4.dp), size = 16f, color = InkDim)
        is LyricsState.Ready -> {
            val l = s.lyrics
            val pos = rememberPosition(app.player, playing && l.synced)
            // the current line is a derived number: the list is only touched when the song moves on to the next line
            val current by remember(l) { derivedStateOf { if (!l.synced) -1 else l.lines.indexOfLast { it.timeMs <= pos.value + 250 } } }
            val list = rememberLazyListState()
            // the first scroll after the lyrics open lands on the current line at once; only the moves after it glide (owner, 5 Nov)
            var placed by remember(l) { androidx.compose.runtime.mutableStateOf(false) }
            LaunchedEffect(current) {
                if (current >= 0) {
                    // the line's middle on the middle of the panel
                    val vh = list.layoutInfo.viewportSize.height
                    val h = list.layoutInfo.visibleItemsInfo.firstOrNull { it.index == current + 1 }?.size ?: 60
                    val offset = -(vh / 2 - h / 2)
                    if (Tweaks.motion && placed) list.animateScrollToItem(current + 1, offset) else list.scrollToItem(current + 1, offset)
                    placed = true
                }
            }
            LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = PaddingValues(top = 0.dp, bottom = 120.dp), verticalArrangement = Arrangement.spacedBy(if (big) 14.dp else 8.dp)) {
                item { Spacer(Modifier.height(if (big) 80.dp else 24.dp)) }
                itemsIndexed(l.lines) { i, line ->
                    val cur = l.synced && i == current
                    val scale by animateFloatAsState(if (cur) 1.05f else 1f, androidx.compose.animation.core.spring(.8f, 260f), label = "lyricScale")
                    // the sung line in full cream, the rest dimmed but still well readable on the dark ground (no line drops below ~4.5:1)
                    val color by androidx.compose.animation.animateColorAsState(when {
                        !l.synced -> Ink
                        cur -> Ink
                        else -> InkDim
                    }, tween(if (Tweaks.motion) 300 else 0), label = "lyricColour")
                    val text = line.text.ifEmpty { "♪" }
                    SText(
                        AnnotatedString(text),
                        // 5% short of the edge: the sung line grows 5% from its left side, and a full-width line lost its last word past the edge
                        Modifier.fillMaxWidth(1f / 1.05f).heightIn(min = 48.dp).graphicsLayer { scaleX = scale; scaleY = scale; transformOrigin = TransformOrigin(0f, .5f) }
                            .then(if (l.synced) Modifier.clickable(onClickLabel = "Play from this line") { app.player.seekTo(line.timeMs) } else Modifier)
                            .semantics { if (cur) stateDescription = "Now singing" }
                            .padding(vertical = 6.dp),
                        // kept under 40 / 32 sp as drawn at big text, so a line is more than eight letters (LyricsFit)
                        size = LyricsFit.sp(big, l.synced, LocalDensity.current.fontScale), weight = if (l.synced) FontWeight.Bold else FontWeight.SemiBold, color = color, maxLines = 8, lineHeight = 1.2f,
                    )
                }
                item { Txt("Lyrics via ${l.source}", Modifier.padding(top = 12.dp), size = 12f, color = InkFaint) }
            }
        }
    }
}

// ---- the More sheet ----------------------------------------------------------------------------------------------------------

/** A row of the More sheet: its own icon, the label, the current value in DM Mono, a chevron when it opens something further. */
private class MoreRow(val label: String, val icon: @Composable (Color) -> Unit, val value: String? = null, val opens: Boolean = false, val onClick: () -> Unit)

/**
 * Everything the player can do with the song, in design I: a rounded sheet with a handle (drag it down, tap outside or Back to close), the song's cover,
 * title and artist, four big tiles for the things done most, then the rest in groups (SONG, PLAYBACK, MORE) of 56 dp rows, each with its own icon.
 */
@Composable
private fun PlayerMoreSheet(open: Boolean, playbackOnly: Boolean, t: Track, nav: PlayerNav, p: SleevePalette, onDismiss: () -> Unit, onOpen: (String) -> Unit, panes: Boolean = false) {
    val app = LocalApp.current
    val ctx = LocalContext.current
    val scrim by animateFloatAsState(if (open) 1f else 0f, tween(if (Tweaks.motion) 220 else 0), label = "moreScrim")
    if (open) BackHandler { onDismiss() }
    if (scrim > 0f) Box(Modifier.fillMaxSize().drawBehind { drawRect(Color.Black.copy(alpha = .55f * scrim)) }.then(if (open) Modifier.pointerInput(Unit) { detectTapGestures { onDismiss() } } else Modifier))
    val drag = remember { androidx.compose.animation.core.Animatable(0f) }
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    LaunchedEffect(open) { if (open) drag.snapTo(0f) }
    AnimatedVisibility(
        open, Modifier.fillMaxSize(),
        enter = if (Tweaks.motion) slideInVertically(androidx.compose.animation.core.spring(.86f, 420f)) { it } else EnterTransition.None,
        exit = if (Tweaks.motion) slideOutVertically(tween(220)) { it } else ExitTransition.None,
    ) {
        BoxWithConstraints(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)), contentAlignment = Alignment.BottomCenter) {
            val fav by app.userData.favState(t)
            app.userData.rev
            val close: (() -> Unit) -> () -> Unit = { action -> { onDismiss(); action() } }
            val off = app.offline.entry(t.path)
            val song = buildList {
                if (t.isMusic && t.album.isNotEmpty()) add(MoreRow("Go to album", { GlyphIcon(Glyph.ALBUM, Modifier.size(24.dp), it) }, t.album, true, close { nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ALBUM, t.albumKey)) }))
                if (t.isMusic && t.artist.isNotEmpty()) add(MoreRow("Go to artist", { GlyphIcon(Glyph.ARTIST, Modifier.size(24.dp), it) }, t.artist, true, close { nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ARTIST, t.albumArtist.ifEmpty { t.artist })) }))
                // (Info and Lyrics are their own buttons under the cover, so they are not repeated here: owner, 7 Nov, "menus are too complicated";
                // only on a screen too short for that row, where More is all that is left of it, are they listed here, first)
                if (panes) {
                    add(MoreRow("Lyrics", { GlyphIcon(Glyph.LYRICS, Modifier.size(24.dp), it) }, null, true, close { onOpen("lyrics") }))
                    add(MoreRow("Up next", { GlyphIcon(Glyph.QUEUE, Modifier.size(24.dp), it) }, null, true, close { onOpen("queue") }))
                    add(MoreRow("Song info", { GlyphIcon(Glyph.INFO, Modifier.size(24.dp), it) }, null, true, close { onOpen("info") }))
                }
                add(MoreRow("Rate", { GlyphIcon(Glyph.STAR, Modifier.size(24.dp), it) }, app.userData.rating(t).takeIf { it > 0 }?.let { RatingStore.label(it) } ?: "None", true, close { nav.sheet = ratingSheet(app, t) }))
                if (t.isMusic) add(MoreRow("MusicBrainz match", { GlyphIcon(Glyph.SEARCH, Modifier.size(24.dp), it) }, null, true, close { musicBrainzSheet(app, nav, t) }))
                if (t.source == com.ipodemu.library.TrackSource.JELLYFIN) {
                    if (off == null) add(MoreRow("Download for offline", { GlyphIcon(Glyph.DOWN, Modifier.size(24.dp), it) }, null, false, close {
                        val n = app.offline.enqueue(listOf(t), com.ipodemu.library.offline.OfflineReason.MANUAL); app.ui.say(if (n > 0) "Downloading \"${t.title}\"" else "Already offline")
                    }))
                    else {
                        val failed = off.state == com.ipodemu.library.offline.OfflineState.FAILED
                        add(MoreRow(when (off.state) { com.ipodemu.library.offline.OfflineState.DONE -> "Remove download"; com.ipodemu.library.offline.OfflineState.FAILED -> "Download again"; else -> "Cancel download" },
                            { GlyphIcon(if (failed) Glyph.DOWN else Glyph.CLOSE, Modifier.size(24.dp), it) }, if (failed) "Failed" else null, false,
                            close { if (failed) app.offline.enqueue(listOf(t), com.ipodemu.library.offline.OfflineReason.MANUAL) else app.offline.remove(t.path) }))
                    }
                }
                add(MoreRow("Share", { ShareIcon(it) }, null, false, close { shareTrack(ctx, t) }))
                if (app.ui.isAdmin && t.isMusic && app.prefs.slskdUrl.isNotBlank() && app.prefs.slskdApiKey.isNotBlank() && app.prefs.fileMoverUrl.isNotBlank() && !t.path.contains("[Hi-Res]", ignoreCase = true))
                    add(MoreRow("Hi-Res version", { GlyphIcon(Glyph.IPOD, Modifier.size(24.dp), it) }, null, true, close {
                        nav.sheet = SheetSpec("Get a Hi-Res copy?", "Hi-Res files are 2 to 5 times bigger (roughly 40 to 200 MB). It is saved next to this one. If no Hi-Res copy exists, nothing is changed.", listOf(
                            SheetItem("Yes, download Hi-Res", Glyph.DOWN) { app.library.requestDownload(t.artist, t.title, t.album, t.durationMs, hiRes = true, fallbackToNormal = false) },
                            SheetItem("Cancel", Glyph.CLOSE) { },
                        ))
                    }))
            }
            val playback = listOf(
                MoreRow("Speed", { GaugeIcon(it) }, com.ipodemu.playback.SpeedPolicy.label(app.player.speed), true, close { nav.sheet = speedSheet(app) }),
                MoreRow("Sleep timer", { MoonIcon(it) }, sleepStatus(app), true, close { nav.sheet = sleepSheet(app) }),
                MoreRow("Sound", { GlyphIcon(Glyph.VOLUME, Modifier.size(24.dp), it) }, app.prefs.eq, true, close { nav.sheet = eqSheet(app) }),
            )
            val more = buildList {
                if (!app.ui.guest) {
                    add(MoreRow(if (app.jam.inJam) "In a Jam" else "Jam", { GlyphIcon(Glyph.JAM, Modifier.size(24.dp), it) }, null, true, close { app.ui.jamOpen = true }))
                    add(MoreRow("Devices", { GlyphIcon(Glyph.DEVICES, Modifier.size(24.dp), it) }, null, true, close { app.ui.devicesOpen = true }))
                }
                add(MoreRow("Retro", { GlyphIcon(Glyph.CASSETTE, Modifier.size(24.dp), it) }, null, true, close { openRetro(ctx) }))
            }
            val shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp)
            Column(
                Modifier.fillMaxWidth().widthIn(max = 640.dp).heightIn(max = maxHeight * .92f)
                    .graphicsLayer { translationY = drag.value }
                    .clip(shape).drawBehind { drawRect(Brush.verticalGradient(listOf(p.tone.value, p.ground.value), endY = 420.dp.toPx())) }
                    .border(1.dp, Line, shape)
                    .pointerInput(Unit) { detectTapGestures { } },
            ) {
                // the handle: drag the sheet down to close it
                Box(
                    Modifier.fillMaxWidth().height(28.dp)
                        .semantics { contentDescription = "Close"; role = Role.Button }
                        .clickable(onClick = onDismiss)
                        .pointerInput(Unit) {
                            detectVerticalDragGestures(
                                onDragEnd = { scope.launch { if (drag.value > 120.dp.toPx()) onDismiss() else drag.animateTo(0f, androidx.compose.animation.core.spring(.8f, 500f)) } },
                            ) { c, dy -> c.consume(); scope.launch { drag.snapTo((drag.value + dy).coerceAtLeast(0f)) } }
                        },
                    contentAlignment = Alignment.Center,
                ) { Box(Modifier.size(40.dp, 4.dp).clip(CircleShape).background(InkFaint)) }
                Column(Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()).windowInsetsPadding(WindowInsets.navigationBars).padding(bottom = 12.dp)) {
                    Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        ArtImage(t.artKey, Modifier.size(56.dp), thumb = true, corner = 10.dp)
                        Column(Modifier.weight(1f)) {
                            SText(remember(t.title) { serifTitle(t.title) }, family = SleeveFonts.serif, size = 24f, weight = FontWeight.Normal)
                            SText(listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString(" · "), size = 14f, color = InkDim)
                        }
                    }
                    Spacer(Modifier.height(14.dp))
                    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        MoreTile("Play next", Glyph.NEXT, p, false, Modifier.weight(1f), close { app.player.addNext(t); app.ui.say("Playing next") })
                        MoreTile("Add to queue", Glyph.QUEUE, p, false, Modifier.weight(1f), close { app.player.addToQueue(t); app.ui.say("Added to queue") })
                        MoreTile(if (fav) "Favourite" else "Favourite", if (fav) Glyph.HEART_FILLED else Glyph.HEART, p, fav, Modifier.weight(1f)) { app.userData.toggleFavorite(t) }
                        MoreTile("Add to playlist", Glyph.PLUS, p, false, Modifier.weight(1f), close { nav.sheet = playlistPicker(app, nav, t) })
                    }
                    MoreGroup("Song", song)
                    MoreGroup("Playback", playback)
                    MoreGroup("More", more)
                }
            }
        }
    }
}

@Composable
private fun MoreTile(label: String, g: Glyph, p: SleevePalette, on: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    val s by animateFloatAsState(if (pressed) .94f else 1f, androidx.compose.animation.core.spring(.6f, 600f), label = "tile")
    Column(
        modifier.heightIn(min = 84.dp).graphicsLayer { scaleX = s; scaleY = s }.clip(RoundedCornerShape(18.dp))
            .drawBehind { drawRect(if (on) p.main.value else p.tone.value) }
            .semantics(mergeDescendants = true) { role = Role.Button; contentDescription = label; if (g == Glyph.HEART || g == Glyph.HEART_FILLED) stateDescription = if (on) "On" else "Off" }
            .clickable(src, null, onClick = onClick).padding(6.dp),
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
    ) {
        // on, the tile is the cover's colour: its ink is the one that reads on it (white on a dark cover), not a fixed dark
        val onInk = Color(DesignColour.readableOn(p.main.value.toArgb()))
        GlyphIcon(g, Modifier.size(24.dp), if (on) onInk else p.toneFg.value)
        Spacer(Modifier.height(6.dp))
        SText(AnnotatedString(label), size = 12f, weight = FontWeight.SemiBold, color = if (on) onInk else p.toneFg.value, maxLines = 2, align = TextAlign.Center)
    }
}

@Composable
private fun MoreGroup(name: String, rows: List<MoreRow>) {
    if (rows.isEmpty()) return
    SText(name.uppercase(), Modifier.padding(start = 24.dp, top = 18.dp, bottom = 4.dp).semantics { heading() }, family = SleeveFonts.mono, size = 11f, color = InkDim, spacing = .14f)
    // the value leaves the label its room on a narrow phone at big text (SheetFit); the rows grow rather than cut a label
    val cfg = androidx.compose.ui.platform.LocalConfiguration.current
    val valueMax = SheetFit.valueMax(minOf(cfg.screenWidthDp, 640).toFloat(), cfg.fontScale).dp
    for (r in rows) {
        Row(
            Modifier.fillMaxWidth().heightIn(min = 56.dp).semantics(mergeDescendants = true) { role = Role.Button }.clickable(onClick = r.onClick).padding(horizontal = 24.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Box(Modifier.size(24.dp), contentAlignment = Alignment.Center) { r.icon(Ink) }
            SText(r.label, Modifier.weight(1f), size = 16f, weight = FontWeight.Medium, maxLines = 2)
            if (r.value != null) SText(r.value, Modifier.widthIn(max = valueMax), family = SleeveFonts.mono, size = 13f, color = InkDim)
            if (r.opens) GlyphIcon(Glyph.CHEVRON, Modifier.size(16.dp), InkFaint)
        }
    }
}

/** Outlined icons the app's glyph set lacks, drawn in the same 1.8 dp line: share, a speed gauge, a moon for the sleep timer. */
@Composable
private fun ShareIcon(tint: Color) = Canvas(Modifier.size(22.dp)) {
    val w = 1.8.dp.toPx(); val r = size.width * .13f
    val a = Offset(size.width * .78f, size.height * .2f); val b = Offset(size.width * .22f, size.height * .5f); val c = Offset(size.width * .78f, size.height * .8f)
    drawLine(tint, a, b, w); drawLine(tint, b, c, w)
    for (o in listOf(a, b, c)) drawCircle(tint, r, o, style = androidx.compose.ui.graphics.drawscope.Stroke(w))
}

@Composable
private fun GaugeIcon(tint: Color) = Canvas(Modifier.size(22.dp)) {
    val w = 1.8.dp.toPx()
    drawArc(tint, 150f, 240f, false, topLeft = Offset(w, w + size.height * .08f), size = Size(size.width - 2 * w, size.height - 2 * w), style = androidx.compose.ui.graphics.drawscope.Stroke(w, cap = StrokeCap.Round))
    drawLine(tint, Offset(size.width / 2, size.height * .58f), Offset(size.width * .74f, size.height * .32f), w, StrokeCap.Round)
}

@Composable
private fun MoonIcon(tint: Color) = Canvas(Modifier.size(22.dp)) {
    val w = 1.8.dp.toPx()
    val path = androidx.compose.ui.graphics.Path().apply {
        val r = size.width * .4f; val cx = size.width / 2; val cy = size.height / 2
        addOval(androidx.compose.ui.geometry.Rect(Offset(cx, cy), r))
        val cut = androidx.compose.ui.graphics.Path().apply { addOval(androidx.compose.ui.geometry.Rect(Offset(cx + r * .55f, cy - r * .45f), r * .85f)) }
        op(this, cut, androidx.compose.ui.graphics.PathOperation.Difference)
    }
    drawPath(path, tint, style = androidx.compose.ui.graphics.drawscope.Stroke(w))
}


@Composable
private fun rememberLyrics(t: Track): LyricsState {
    val app = LocalApp.current
    var state by remember(t.path) { mutableStateOf<LyricsState>(LyricsState.Loading) }
    LaunchedEffect(t.path) { state = app.lyrics.get(t)?.let { LyricsState.Ready(it) } ?: LyricsState.None }
    return state
}

/**
 * A menu of the player (a SheetSpec) as a design I sheet: drag handle, serif title, the rows under DM Mono group labels, 56 dp rows, the chosen value
 * in the cover's accent with a check. It slides up with a spring and down when closed; the last menu is kept while it slides away.
 */
@Composable
private fun DesignSheet(spec: SheetSpec?, p: SleevePalette, onDismiss: () -> Unit) {
    var last by remember { mutableStateOf<SheetSpec?>(null) }
    if (spec != null) last = spec
    val open = spec != null
    val scrim by animateFloatAsState(if (open) 1f else 0f, tween(if (Tweaks.motion) 220 else 0), label = "designScrim")
    if (open) BackHandler { onDismiss() }
    if (scrim > 0f) Box(Modifier.fillMaxSize().drawBehind { drawRect(Color.Black.copy(alpha = .55f * scrim)) }.then(if (open) Modifier.pointerInput(Unit) { detectTapGestures { onDismiss() } } else Modifier))
    val drag = remember { androidx.compose.animation.core.Animatable(0f) }
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    LaunchedEffect(spec) { if (spec != null) drag.snapTo(0f) }
    AnimatedVisibility(
        open, Modifier.fillMaxSize(),
        enter = if (Tweaks.motion) slideInVertically(androidx.compose.animation.core.spring(.86f, 420f)) { it } else EnterTransition.None,
        exit = if (Tweaks.motion) slideOutVertically(tween(220)) { it } else ExitTransition.None,
    ) {
        val s = last ?: return@AnimatedVisibility
        val accent = p.main.value
        BoxWithConstraints(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)), contentAlignment = Alignment.BottomCenter) {
            val shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp)
            Column(
                Modifier.fillMaxWidth().widthIn(max = 640.dp).heightIn(max = maxHeight * .9f)
                    .graphicsLayer { translationY = drag.value }
                    .clip(shape).drawBehind { drawRect(Brush.verticalGradient(listOf(p.tone.value, p.ground.value), endY = 360.dp.toPx())) }
                    .border(1.dp, Line, shape)
                    .pointerInput(Unit) { detectTapGestures { } },
            ) {
                Box(
                    Modifier.fillMaxWidth().height(28.dp).semantics { contentDescription = "Close"; role = Role.Button }.clickable(onClick = onDismiss)
                        .pointerInput(Unit) {
                            detectVerticalDragGestures(
                                onDragEnd = { scope.launch { if (drag.value > 120.dp.toPx()) onDismiss() else drag.animateTo(0f, androidx.compose.animation.core.spring(.8f, 500f)) } },
                            ) { c, dy -> c.consume(); scope.launch { drag.snapTo((drag.value + dy).coerceAtLeast(0f)) } }
                        },
                    contentAlignment = Alignment.Center,
                ) { Box(Modifier.size(40.dp, 4.dp).clip(CircleShape).background(InkFaint)) }
                // the title and the subtitle scroll with the rows (a long subtitle was cut at three lines, and on a phone on its side at big text the
                // fixed header left the rows no room)
                Column(Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()).windowInsetsPadding(WindowInsets.navigationBars).padding(bottom = 12.dp)) {
                    Column(Modifier.padding(start = 24.dp, end = 24.dp, bottom = 6.dp).semantics(mergeDescendants = true) { heading() }) {
                        SText(remember(s.title) { serifTitle(s.title) }, family = SleeveFonts.serif, size = 28f, weight = FontWeight.Normal, maxLines = 3)
                        if (!s.subtitle.isNullOrEmpty()) SText(s.subtitle, size = 14f, color = InkDim, maxLines = 8)
                    }
                    if (s.items.isEmpty()) SText("One moment\u2026", Modifier.padding(24.dp), size = 15f, color = InkDim)
                    var group: String? = null
                    for (item in s.items) {
                        if (item.group != null && item.group != group) {
                            group = item.group
                            SText(item.group.uppercase(), Modifier.padding(start = 24.dp, top = 14.dp, bottom = 4.dp).semantics { heading() }, family = SleeveFonts.mono, size = 11f, color = InkDim, spacing = .14f)
                        }
                        val on = item.selected
                        Row(
                            Modifier.fillMaxWidth().heightIn(min = 56.dp)
                                .semantics(mergeDescendants = true) { role = Role.Button; if (on) stateDescription = "Selected" }
                                .clickable { onDismiss(); item.onClick() }
                                .then(if (on) Modifier.background(accent.copy(alpha = .14f)) else Modifier)
                                .padding(horizontal = 24.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp),
                        ) {
                            GlyphIcon(item.glyph, Modifier.size(24.dp), if (on) accent else Ink)
                            SText(item.label, Modifier.weight(1f), size = 16f, weight = if (on) FontWeight.SemiBold else FontWeight.Medium, color = if (on) accent else Ink, maxLines = 3)
                            if (on) GlyphIcon(Glyph.CHECK, Modifier.size(20.dp), accent)
                        }
                    }
                }
            }
        }
    }
}