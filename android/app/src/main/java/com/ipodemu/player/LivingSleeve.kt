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
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
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
    var tab by rememberSaveable { mutableStateOf("queue") }
    var moreOpen by remember { mutableStateOf(false) }
    var lyricsFull by remember { mutableStateOf(false) }
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    val pager = androidx.compose.foundation.pager.rememberPagerState { 4 }
    val pageOf = mapOf("lyrics" to 1, "queue" to 2, "info" to 3)
    fun go(page: Int) { scope.launch { if (Tweaks.motion) pager.animateScrollToPage(page, animationSpec = androidx.compose.animation.core.spring(.86f, 320f)) else pager.scrollToPage(page) } }
    BoxWithConstraints(Modifier.fillMaxSize().drawBehind { drawRect(palette.ground.value) }) {
        val wide = maxWidth >= 600.dp
        if (wide) {
            Row(Modifier.fillMaxSize()) {
                SleeveStage(snap, nav, t, palette, wide = true, tab = tab, onOpen = { tab = it }, onMore = { moreOpen = true }, modifier = Modifier.weight(1f).widthIn(max = 520.dp).fillMaxHeight())
                SleevePanel(snap, nav, t, palette, tab, { tab = it }, onClose = null,
                    Modifier.weight(1f).fillMaxHeight().windowInsetsPadding(WindowInsets.safeDrawing).padding(start = 24.dp, end = 32.dp, top = 64.dp, bottom = 12.dp))
            }
        } else {
            // one vertical page, like a music app: the player fills the first screen, and Lyrics, Up next and Info follow as cards that snap into place
            // with a spring. The top of the next card peeks out at the bottom of each screen (the home-gesture strip, where no control belongs)
            val nav0 = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding()
            val peek = maxDp(96.dp, nav0 + 72.dp)
            BackHandler(enabled = pager.currentPage > 0 && !moreOpen && !lyricsFull) { go(0) }
            androidx.compose.foundation.pager.VerticalPager(
                pager, Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = peek), beyondViewportPageCount = 1,
                flingBehavior = androidx.compose.foundation.pager.PagerDefaults.flingBehavior(pager, snapAnimationSpec = androidx.compose.animation.core.spring(.86f, 320f)),
                key = { it },
            ) { page ->
                when (page) {
                    0 -> SleeveStage(snap, nav, t, palette, wide = false, tab = null, onOpen = { go(pageOf[it] ?: 2) }, onMore = { moreOpen = true }, modifier = Modifier.fillMaxSize())
                    else -> {
                        val here = pager.currentPage == page
                        val toPage: () -> Unit = { if (here) go(0) else go(page) }
                        // the last card also covers the strip below it, so the page never ends in empty space
                        val extra = if (page == 3) peek else 0.dp
                        Box(Modifier.fillMaxSize().wrapContentHeight(Alignment.Top, unbounded = true).height(this@BoxWithConstraints.maxHeight - peek + extra)) {
                            when (page) {
                                1 -> LyricsCard(t, snap.playing, palette, here, toPage, onExpand = { lyricsFull = true }, bottomPad = extra)
                                2 -> SleeveCard(app0QueueCount(snap), "Up next", app0UpNextLine(snap), palette, here, toPage, bottomPad = extra) {
                                    androidx.compose.runtime.CompositionLocalProvider(LocalPlayerNav provides nav) { QueuePanel(snap, Modifier.fillMaxSize()) }
                                }
                                else -> SleeveCard("", "Song info", listOf(t.album, t.year.takeIf { it > 0 }?.toString()).filter { !it.isNullOrBlank() }.joinToString(" · "), palette, here, toPage, bottomPad = extra) {
                                    TrackInfoPanel(t, snap, Modifier.fillMaxSize(), tint = palette.main.value, tone = palette.tone.value, ground = palette.ground.value)
                                }
                            }
                        }
                    }
                }
            }
        }
        AnimatedVisibility(
            lyricsFull, Modifier.fillMaxSize(),
            enter = if (Tweaks.motion) androidx.compose.animation.fadeIn(tween(220)) + androidx.compose.animation.scaleIn(androidx.compose.animation.core.spring(.85f, 380f), initialScale = .94f) else EnterTransition.None,
            exit = if (Tweaks.motion) androidx.compose.animation.fadeOut(tween(180)) + androidx.compose.animation.scaleOut(tween(180), targetScale = .96f) else ExitTransition.None,
        ) { LyricsFullScreen(t, snap.playing, palette) { lyricsFull = false } }
        BackHandler(enabled = lyricsFull) { lyricsFull = false }
        PlayerMoreSheet(moreOpen, t, nav, palette, onDismiss = { moreOpen = false }, onOpen = { id ->
            if (wide) tab = id else if (id == "lyrics") lyricsFull = true else go(pageOf[id] ?: 3)
        })
    }
}

/** "Next: <song>" for the peek of the Up next card, or nothing queued. */
@Composable
private fun app0UpNextLine(snap: PlayerSnap): String {
    val app = LocalApp.current
    val next = remember(snap.track?.path, snap.index, snap.rev, snap.shuffle) { app.player.upNext(1).firstOrNull()?.second }
    return next?.let { "Next: " + it.title + if (it.artist.isNotEmpty()) " · " + it.artist else "" } ?: "Nothing queued after this song"
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
private fun SleeveStage(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, wide: Boolean, tab: String?, onOpen: (String) -> Unit, onMore: () -> Unit, modifier: Modifier) {
    val app = LocalApp.current
    // the next two songs' shapes are made while this one plays, so the frame is drawn the moment each starts (as the web does); looked at again now and
    // then, because the prefetcher fills the cache with them as the song goes on
    LaunchedEffect(t.path) { delay(4000); while (true) { try { WaveAnalyzer.prefetch(app, app.player.upNext(2).map { it.second }) } catch (_: Exception) { }; delay(20_000) } }
    BoxWithConstraints(modifier.drawBehind {
        // the cover's tone at the top sinking into the ground, as on the web player
        drawRect(Brush.verticalGradient(listOf(p.tone.value, p.ground.value), endY = size.height * .7f))
    }) {
        val h = maxHeight
        // a phone on its side (or a very short window) scrolls as a last resort, with a smaller cover
        val cramped = h < 560.dp
        val scroll = rememberScrollState()
        // the bottom ~100 dp is the home gesture's: nothing that matters goes there (the navigation bar is inside this, not added to it)
        val nav0 = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding()
        // on a phone the next card's peek already keeps the home-gesture strip clear; a wide screen keeps its own margin
        val bottomClear = if (!wide) 8.dp else if (cramped) nav0 + 16.dp else maxDp(92.dp, nav0 + 24.dp)
        val pos = rememberPosition(app.player, snap.playing)
        val dur = remember { mutableLongStateOf(app.player.durationMs) }
        LaunchedEffect(t.path) { while (true) { dur.longValue = app.player.durationMs; delay(500) } }
        val scrub = remember { mutableFloatStateOf(-1f) }   // where the finger is on the frame while it is down (0..1), else -1
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal))
                .then(if (cramped) Modifier.verticalScroll(scroll) else Modifier).padding(bottom = bottomClear),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            SleeveTopBar(snap, nav, t, p)
            // the cover in its frame of bars takes whatever height is left, as a square
            BoxWithConstraints(if (cramped) Modifier.fillMaxWidth().height(280.dp) else Modifier.fillMaxWidth().weight(1f).padding(top = 4.dp), contentAlignment = Alignment.Center) {
                val side = minOf(maxWidth - 16.dp, maxHeight, 560.dp)
                SleeveFrame(snap, t, p, side, pos, dur, scrub)
            }
            SleeveTime(pos, dur, scrub)
            Spacer(Modifier.height(if (h < 700.dp) 8.dp else 14.dp))
            SleeveTitle(nav, t, p, Modifier.padding(horizontal = 22.dp))
            Spacer(Modifier.height(if (h < 700.dp) 10.dp else 16.dp))
            SleeveControls(snap, p, Modifier.padding(horizontal = 14.dp))
            Spacer(Modifier.height(12.dp))
            Spacer(Modifier.height(if (h < 700.dp) 8.dp else 14.dp))
            SleeveActions(wide, tab, onOpen, onMore)
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
private fun SleeveTopBar(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette) {
    val app = LocalApp.current
    val ctx = LocalContext.current
    app.userData.rev
    val sleepLeft = rememberSleepLeft(app)
    Row(Modifier.fillMaxWidth().heightIn(min = 60.dp).padding(horizontal = 14.dp), verticalAlignment = Alignment.CenterVertically) {
        GlassButton("Close Now Playing", { nav.nowPlaying = false }) { GlyphIcon(Glyph.DOWN, Modifier.size(24.dp), Color.White) }
        Spacer(Modifier.width(10.dp))
        val fallback = app.player.contextName ?: com.ipodemu.library.SourceLabel.line(t.source)
        SText(
            SleeveText.where(t.album, t.trackNo, fallback),
            Modifier.weight(1f, fill = false).clip(RoundedCornerShape(14.dp)).background(Glass).padding(horizontal = 12.dp, vertical = 7.dp),
            family = SleeveFonts.mono, size = 11f, color = Color.White, spacing = .14f,
        )
        Spacer(Modifier.weight(1f).widthIn(min = 8.dp))
        // a running sleep timer stays in sight: Options alone would hide it
        if (sleepLeft != null) SleepChip(sleepLeft) { nav.sheet = sleepSheet(app) }
        // the speed is shown when it is not normal, for the same reason
        if (snap.speed != 1f) SText(com.ipodemu.playback.SpeedPolicy.label(snap.speed), Modifier.padding(end = 6.dp).clickable { nav.sheet = speedSheet(app) }, size = 13f, weight = FontWeight.Bold, color = p.main.value)
        GlassButton("Options", { nav.sheet = optionsSheet(app, nav, t) }) { SlidersIcon(Color.White) }
        // (Options keeps the quick rating / speed / sleep / sound list; everything else is in More, beside the Lyrics / Up next / Info chips)
        Spacer(Modifier.width(10.dp))
        GlassButton("Retro", { openRetro(ctx) }) { GlyphIcon(Glyph.CASSETTE, Modifier.size(22.dp), Color.White) }
    }
}

@Composable
private fun SleeveTitle(nav: PlayerNav, t: Track, p: SleevePalette, modifier: Modifier) {
    val app = LocalApp.current
    val fav by app.userData.favState(t.path)
    val ai = rememberAudioInfo(app, t)
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            val (lead, last) = remember(t.title) { SleeveText.split(t.title) }
            val title = remember(lead, last) { buildAnnotatedString { append(lead); withStyle(SpanStyle(fontStyle = FontStyle.Italic)) { append(last) } } }
            SText(title, family = SleeveFonts.serif, size = if (t.title.length > SleeveText.LONG_TITLE) 40f else 52f, weight = FontWeight.Normal, maxLines = 2, spacing = -.01f, lineHeight = .98f)
            // the chips wrap under the artist on a narrow screen rather than squeezing the name down to a few letters
            @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
            androidx.compose.foundation.layout.FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                SText(t.artist.ifEmpty { "Unknown Artist" }, Modifier.align(Alignment.CenterVertically).clickable(enabled = t.artist.isNotEmpty()) {
                    nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ARTIST, t.albumArtist.ifEmpty { t.artist }))
                }, size = 17f, weight = FontWeight.Bold)
                val main = if (ai.hiRes) "Hi-Res" else ai.codec
                if (main.isNotEmpty()) SleeveChip((main + if (ai.sampling.isNotEmpty()) " " + ai.sampling else "").uppercase(), if (ai.hiRes) p.main.value else p.tone.value, if (ai.hiRes) OnMain else p.toneFg.value)
                SleeveChip(com.ipodemu.library.SourceLabel.line(t.source).uppercase(), Color(0x1FFFFFFF), InkDim)
            }
            DownloadTagLine(t, size = 13f, showNew = false)
        }
        Box(
            Modifier.size(52.dp).clip(CircleShape).drawBehind { drawRect(p.tone.value) }.focusRing(CircleShape)
                .semantics { contentDescription = if (fav) "Unfavorite" else "Favorite"; role = Role.Button }.clickable { app.userData.toggleFavorite(t.path) },
            contentAlignment = Alignment.Center,
        ) { GlyphIcon(if (fav) Glyph.HEART_FILLED else Glyph.HEART, Modifier.size(24.dp), if (fav) p.main.value else p.toneFg.value) }
    }
}

/** A small DM Mono chip: the file's format, or where it plays from. */
@Composable
private fun SleeveChip(text: String, bg: Color, fg: Color) {
    SText(text, Modifier.clip(RoundedCornerShape(8.dp)).background(bg).padding(horizontal = 8.dp, vertical = 4.dp), family = SleeveFonts.mono, size = 11f, color = fg, spacing = .02f)
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
        // the shape grows as the song downloads (FlacWave from the player's own cache), and the exact one replaces it when it is known
        WaveAnalyzer.shapes(app, t).collect { s ->
            if (first) { first = false; android.util.Log.i("FLACieWave", "first shape for ${t.title} after ${android.os.SystemClock.elapsedRealtime() - t0} ms (${s.count { it >= 0 }} of ${s.size} bars known)") }
            value = s
        }
    }
}

@Composable
private fun SleeveControls(snap: PlayerSnap, p: SleevePalette, modifier: Modifier) {
    val app = LocalApp.current
    // shuffle, previous, play, next, repeat in one row, as the web's PC player: play is a plain rounded square in the cover's colour
    Row(modifier.fillMaxWidth().widthIn(max = 460.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        IconAction(Glyph.SHUFFLE, if (snap.shuffle) "Shuffle on" else "Shuffle off", { app.prefs.shuffle = !app.prefs.shuffle; app.player.applyModes() },
            tint = if (snap.shuffle) p.main.value else InkDim, size = 48.dp, iconScale = .48f, dot = snap.shuffle)
        SleevePillButton(Glyph.PREV, "Previous", p) { app.player.prev() }
        SleevePlay(snap, p)
        SleevePillButton(Glyph.NEXT, "Next", p) { app.player.next() }
        IconAction(if (snap.repeat == 2) Glyph.REPEAT_ONE else Glyph.REPEAT, when (snap.repeat) { 1 -> "Repeat all"; 2 -> "Repeat one"; else -> "Repeat off" },
            { app.prefs.repeat = (app.prefs.repeat + 1) % 3; app.player.applyModes() }, tint = if (snap.repeat != 0) p.main.value else InkDim, size = 48.dp, iconScale = .48f, dot = snap.repeat != 0)
    }
}

/** Previous and next: a tinted pill in the cover's tone. */
@Composable
private fun SleevePillButton(g: Glyph, label: String, p: SleevePalette, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    Box(
        Modifier.size(62.dp, 52.dp).graphicsLayer { val s = if (pressed) .95f else 1f; scaleX = s; scaleY = s }
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
private fun SleeveFrame(snap: PlayerSnap, t: Track, p: SleevePalette, side: Dp, pos: State<Long>, dur: State<Long>, scrub: androidx.compose.runtime.MutableFloatState) {
    val app = LocalApp.current
    val shape by rememberWaveShape(t)
    val density = LocalDensity.current
    // the frame's width: room for the gap and the bars on each side of the cover (the web uses 40 px on a PC; a phone gets a little less)
    val frameW = if (side < 300.dp) 22.dp else if (side < 420.dp) 28.dp else 36.dp
    val gap = 7.dp
    val cover = side - frameW * 2
    val corner = cover * .06f
    val geo = remember(side, density) { with(density) { FrameSeek(cover.toPx(), corner.toPx(), gap.toPx()) } }
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
                    down.consume()
                    if (f0 == null) {
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
                    scrub.floatValue = f0
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
            Modifier.size(cover).shadow(24.dp, RoundedCornerShape(corner), clip = false, ambientColor = Color.Black, spotColor = Color.Black)
                .semantics { contentDescription = if (snap.playing) "Pause" else "Play"; role = Role.Button },
            corner = corner, full = true,
        )
    }
}

/** The play button: a rounded square in the cover's colour with dark text, between the previous and next pills. */
@Composable
private fun SleevePlay(snap: PlayerSnap, p: SleevePalette) {
    val app = LocalApp.current
    val playFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { playFocus.requestFocus() } catch (_: Exception) {} }
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val keys = androidx.compose.ui.platform.LocalInputModeManager.current.inputMode == androidx.compose.ui.input.InputMode.Keyboard
    val pressed by src.collectIsPressedAsState()
    val shape = RoundedCornerShape(22.dp)
    Box(
        Modifier.size(84.dp, 64.dp).graphicsLayer { val s = if (pressed) .95f else 1f; scaleX = s; scaleY = s }
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
        }) { playing -> GlyphIcon(if (playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(30.dp), OnMain) }
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

// ---- Lyrics, Up next, More --------------------------------------------------------------------------------------------------

@Composable
private fun SleeveActions(wide: Boolean, tab: String?, onOpen: (String) -> Unit, onMore: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterHorizontally), verticalAlignment = Alignment.CenterVertically) {
        // a segmented row that scrolls the page to that card (on a wide screen the panel is always beside the stage, so the tabs switch it)
        Row(Modifier.weight(1f).height(48.dp).clip(RoundedCornerShape(24.dp)).background(Glass).padding(4.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            for ((id, label) in listOf("lyrics" to "Lyrics", "queue" to "Up next", "info" to "Info")) {
                val on = wide && tab == id
                Box(
                    Modifier.weight(1f).fillMaxHeight().clip(RoundedCornerShape(20.dp)).background(if (on) Ink else Color.Transparent)
                        .semantics { role = Role.Tab; if (on) stateDescription = "Selected" }.clickable { onOpen(id) },
                    contentAlignment = Alignment.Center,
                ) { SText(label, size = 14f, weight = FontWeight.SemiBold, color = if (on) OnMain else Ink) }
            }
        }
        Box(
            Modifier.size(48.dp).clip(CircleShape).background(Glass).focusRing(CircleShape)
                .semantics { contentDescription = "More options"; role = Role.Button }.clickable(onClick = onMore),
            contentAlignment = Alignment.Center,
        ) { GlyphIcon(Glyph.MORE, Modifier.size(22.dp), Ink) }
    }
}

/** Shares the song as words (title, artist, album) through Android's share sheet: the file itself stays where it is. */
internal fun shareTrack(ctx: android.content.Context, t: Track) {
    val text = listOf(t.title, t.artist, t.album).filter { it.isNotBlank() }.joinToString(" · ")
    val send = android.content.Intent(android.content.Intent.ACTION_SEND).setType("text/plain").putExtra(android.content.Intent.EXTRA_TEXT, text)
    try { ctx.startActivity(android.content.Intent.createChooser(send, "Share").addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: Exception) { }
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
            LaunchedEffect(current) {
                if (current >= 0) {
                    // the line's middle on the middle of the panel
                    val vh = list.layoutInfo.viewportSize.height
                    val h = list.layoutInfo.visibleItemsInfo.firstOrNull { it.index == current + 1 }?.size ?: 60
                    val offset = -(vh / 2 - h / 2)
                    if (Tweaks.motion) list.animateScrollToItem(current + 1, offset) else list.scrollToItem(current + 1, offset)
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
                        Modifier.fillMaxWidth().heightIn(min = 48.dp).graphicsLayer { scaleX = scale; scaleY = scale; transformOrigin = TransformOrigin(0f, .5f) }
                            .then(if (l.synced) Modifier.clickable(onClickLabel = "Play from this line") { app.player.seekTo(line.timeMs) } else Modifier)
                            .semantics { if (cur) stateDescription = "Now singing" }
                            .padding(vertical = 6.dp),
                        size = if (big) 30f else if (l.synced) 24f else 20f, weight = if (l.synced) FontWeight.Bold else FontWeight.SemiBold, color = color, maxLines = 6, lineHeight = 1.2f,
                    )
                }
                item { Txt("Lyrics via ${l.source}", Modifier.padding(top = 12.dp), size = 12f, color = InkFaint) }
            }
        }
    }
}

// ---- the cards under the player (phone): Lyrics, Up next, Info --------------------------------------------------------------

/**
 * A rounded card with a drag handle and a header (DM Mono kicker, serif title, one line of what is in it). Its top is what peeks out under the player:
 * a tap on the header brings the card up (or, when it is up, goes back to the player); the content is the card's own scrolling list.
 */
@Composable
private fun SleeveCard(
    kicker: String, title: String, line: String, p: SleevePalette, here: Boolean, onHeader: () -> Unit, bottomPad: Dp = 0.dp,
    action: (@Composable () -> Unit)? = null, content: @Composable () -> Unit,
) {
    val shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp)
    Column(
        Modifier.fillMaxSize().padding(horizontal = 8.dp).clip(shape)
            .drawBehind { drawRect(Brush.verticalGradient(listOf(p.tone.value.copy(alpha = .92f), p.ground.value), endY = size.height * .5f)) }
            .border(1.dp, Line, shape)
            .then(if (here) Modifier.windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top)) else Modifier)
            .padding(bottom = bottomPad),
    ) {
        Column(
            Modifier.fillMaxWidth().heightIn(min = 72.dp)
                .semantics(mergeDescendants = true) { role = Role.Button; contentDescription = if (here) "$title. Back to the player" else "$title. $line. Open" }
                .clickable(onClick = onHeader).padding(start = 22.dp, end = 12.dp, top = 8.dp, bottom = 8.dp),
        ) {
            Box(Modifier.align(Alignment.CenterHorizontally).size(40.dp, 4.dp).clip(CircleShape).background(InkFaint))
            Spacer(Modifier.height(8.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        SText(remember(title) { serifTitle(title) }, family = SleeveFonts.serif, size = 26f, weight = FontWeight.Normal)
                        if (kicker.isNotEmpty()) SText(kicker, Modifier.padding(bottom = 5.dp), family = SleeveFonts.mono, size = 11f, color = InkDim, spacing = .14f)
                    }
                    if (line.isNotEmpty()) SText(line, size = 14f, color = InkDim)
                }
                action?.invoke()
                // which way the header goes: up into the card, or down back to the player
                val turn by animateFloatAsState(if (here) 0f else 180f, androidx.compose.animation.core.spring(.8f, 300f), label = "cardChevron")
                GlyphIcon(Glyph.DOWN, Modifier.size(48.dp).padding(12.dp).graphicsLayer { rotationZ = turn }, Ink)
            }
        }
        Box(Modifier.weight(1f).fillMaxWidth().padding(horizontal = 12.dp).fadeEdges(top = 16.dp, bottom = 40.dp)) { content() }
    }
}

@Composable
private fun rememberLyrics(t: Track): LyricsState {
    val app = LocalApp.current
    var state by remember(t.path) { mutableStateOf<LyricsState>(LyricsState.Loading) }
    LaunchedEffect(t.path) { state = app.lyrics.get(t)?.let { LyricsState.Ready(it) } ?: LyricsState.None }
    return state
}

/** The Lyrics card: its header shows the line being sung, so the peek under the player already reads as lyrics. */
@Composable
private fun LyricsCard(t: Track, playing: Boolean, p: SleevePalette, here: Boolean, onHeader: () -> Unit, onExpand: () -> Unit, bottomPad: Dp) {
    val app = LocalApp.current
    val st = rememberLyrics(t)
    val l = (st as? LyricsState.Ready)?.lyrics
    val pos = rememberPosition(app.player, playing && l?.synced == true)
    val current by remember(l) { derivedStateOf { if (l == null || !l.synced) -1 else l.lines.indexOfLast { it.timeMs <= pos.value + 250 } } }
    val line = when {
        st is LyricsState.Loading -> "Looking for lyrics…"
        l == null -> "No lyrics for this song"
        current >= 0 -> l.lines[current].text.ifEmpty { "♪" }
        else -> l.lines.firstOrNull { it.text.isNotBlank() }?.text ?: ""
    }
    SleeveCard("", "Lyrics", line, p, here, onHeader, bottomPad, action = if (l != null) ({
        Box(
            Modifier.size(48.dp).clip(CircleShape).semantics { contentDescription = "Lyrics full screen"; role = Role.Button }.clickable(onClick = onExpand),
            contentAlignment = Alignment.Center,
        ) { ExpandIcon(Ink) }
    }) else null) { SleeveLyrics(t, playing, big = false) }
}

/** The lyrics on their own over the whole screen: bigger lines, the cover's colours, a close button within thumb reach at the top. */
@Composable
private fun LyricsFullScreen(t: Track, playing: Boolean, p: SleevePalette, onClose: () -> Unit) {
    Column(
        Modifier.fillMaxSize().drawBehind { drawRect(Brush.verticalGradient(listOf(p.tone.value, p.ground.value), endY = size.height * .7f)) }
            .pointerInput(Unit) { detectTapGestures { } }
            .windowInsetsPadding(WindowInsets.safeDrawing).padding(horizontal = 20.dp),
    ) {
        Row(Modifier.fillMaxWidth().height(64.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                SText(remember(t.title) { serifTitle(t.title) }, family = SleeveFonts.serif, size = 24f, weight = FontWeight.Normal)
                SText(t.artist, size = 13f, color = InkDim)
            }
            IconAction(Glyph.CLOSE, "Close lyrics", onClose, tint = Ink, size = 48.dp, iconScale = .5f)
        }
        Box(Modifier.weight(1f).fillMaxWidth().fadeEdges()) { SleeveLyrics(t, playing, big = true) }
    }
}

/** Two arrows apart: open full screen. */
@Composable
private fun ExpandIcon(tint: Color) {
    Canvas(Modifier.size(20.dp)) {
        val w = 1.8.dp.toPx(); val s = size.width; val a = s * .32f
        drawLine(tint, Offset(s * .58f, s * .42f), Offset(s * .92f, s * .08f), w, StrokeCap.Round)
        drawLine(tint, Offset(s * .92f - a, s * .08f), Offset(s * .92f, s * .08f), w, StrokeCap.Round)
        drawLine(tint, Offset(s * .92f, s * .08f), Offset(s * .92f, s * .08f + a), w, StrokeCap.Round)
        drawLine(tint, Offset(s * .42f, s * .58f), Offset(s * .08f, s * .92f), w, StrokeCap.Round)
        drawLine(tint, Offset(s * .08f, s * .92f - a), Offset(s * .08f, s * .92f), w, StrokeCap.Round)
        drawLine(tint, Offset(s * .08f, s * .92f), Offset(s * .08f + a, s * .92f), w, StrokeCap.Round)
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
private fun PlayerMoreSheet(open: Boolean, t: Track, nav: PlayerNav, p: SleevePalette, onDismiss: () -> Unit, onOpen: (String) -> Unit) {
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
            val fav by app.userData.favState(t.path)
            app.userData.rev
            val close: (() -> Unit) -> () -> Unit = { action -> { onDismiss(); action() } }
            val off = app.offline.entry(t.path)
            val song = buildList {
                if (t.isMusic && t.album.isNotEmpty()) add(MoreRow("Go to album", { GlyphIcon(Glyph.ALBUM, Modifier.size(24.dp), it) }, t.album, true, close { nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ALBUM, t.albumKey)) }))
                if (t.isMusic && t.artist.isNotEmpty()) add(MoreRow("Go to artist", { GlyphIcon(Glyph.ARTIST, Modifier.size(24.dp), it) }, t.artist, true, close { nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ARTIST, t.albumArtist.ifEmpty { t.artist })) }))
                add(MoreRow("Song info", { GlyphIcon(Glyph.INFO, Modifier.size(24.dp), it) }, null, true, close { onOpen("info") }))
                add(MoreRow("Lyrics", { GlyphIcon(Glyph.LYRICS, Modifier.size(24.dp), it) }, null, true, close { onOpen("lyrics") }))
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
                        MoreTile(if (fav) "Favourite" else "Favourite", if (fav) Glyph.HEART_FILLED else Glyph.HEART, p, fav, Modifier.weight(1f)) { app.userData.toggleFavorite(t.path) }
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
        modifier.height(84.dp).graphicsLayer { scaleX = s; scaleY = s }.clip(RoundedCornerShape(18.dp))
            .drawBehind { drawRect(if (on) p.main.value else p.tone.value) }
            .semantics(mergeDescendants = true) { role = Role.Button; contentDescription = label; if (g == Glyph.HEART || g == Glyph.HEART_FILLED) stateDescription = if (on) "On" else "Off" }
            .clickable(src, null, onClick = onClick).padding(6.dp),
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
    ) {
        GlyphIcon(g, Modifier.size(24.dp), if (on) OnMain else p.toneFg.value)
        Spacer(Modifier.height(6.dp))
        SText(AnnotatedString(label), size = 12f, weight = FontWeight.SemiBold, color = if (on) OnMain else p.toneFg.value, maxLines = 2, align = TextAlign.Center)
    }
}

@Composable
private fun MoreGroup(name: String, rows: List<MoreRow>) {
    if (rows.isEmpty()) return
    SText(name.uppercase(), Modifier.padding(start = 24.dp, top = 18.dp, bottom = 4.dp).semantics { heading() }, family = SleeveFonts.mono, size = 11f, color = InkDim, spacing = .14f)
    for (r in rows) {
        Row(
            Modifier.fillMaxWidth().height(56.dp).semantics(mergeDescendants = true) { role = Role.Button }.clickable(onClick = r.onClick).padding(horizontal = 24.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Box(Modifier.size(24.dp), contentAlignment = Alignment.Center) { r.icon(Ink) }
            SText(r.label, Modifier.weight(1f), size = 16f, weight = FontWeight.Medium)
            if (r.value != null) SText(r.value, Modifier.widthIn(max = 160.dp), family = SleeveFonts.mono, size = 13f, color = InkDim)
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

/** "12 SONGS" beside the Up next card's title. */
@Composable
private fun app0QueueCount(snap: PlayerSnap): String {
    val left = (snap.count - snap.index - 1).coerceAtLeast(0)
    return if (left == 1) "1 SONG" else "$left SONGS"
}