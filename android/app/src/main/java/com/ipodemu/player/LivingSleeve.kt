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

/** The three faces of design I (SIL OFL fonts in res/font; their licences are in src/main/fonts-licence). */
@OptIn(ExperimentalTextApi::class)
private object SleeveFonts {
    val serif = FontFamily(
        Font(R.font.instrument_serif_regular, FontWeight.Normal, FontStyle.Normal),
        Font(R.font.instrument_serif_italic, FontWeight.Normal, FontStyle.Italic),
    )
    // Archivo is one variable file: each weight asks it for that weight explicitly, or every weight would draw as the file's default
    val sans = FontFamily(
        Font(R.font.archivo_variable, FontWeight.Medium, variationSettings = FontVariation.Settings(FontVariation.weight(500))),
        Font(R.font.archivo_variable, FontWeight.SemiBold, variationSettings = FontVariation.Settings(FontVariation.weight(600))),
        Font(R.font.archivo_variable, FontWeight.Bold, variationSettings = FontVariation.Settings(FontVariation.weight(700))),
    )
    val mono = FontFamily(Font(R.font.dm_mono_regular, FontWeight.Normal), Font(R.font.dm_mono_medium, FontWeight.Medium))
}

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
    var sheetOpen by rememberSaveable { mutableStateOf(false) }
    BoxWithConstraints(Modifier.fillMaxSize().drawBehind { drawRect(palette.ground.value) }) {
        val wide = maxWidth >= 600.dp
        val open: (String) -> Unit = { id -> if (!wide && sheetOpen && tab == id) sheetOpen = false else { tab = id; sheetOpen = true } }
        if (wide) {
            Row(Modifier.fillMaxSize()) {
                SleeveStage(snap, nav, t, palette, wide = true, tab = tab, onOpen = open, modifier = Modifier.weight(1f).widthIn(max = 520.dp).fillMaxHeight())
                SleevePanel(snap, nav, t, palette, tab, { tab = it }, onClose = null,
                    Modifier.weight(1f).fillMaxHeight().windowInsetsPadding(WindowInsets.safeDrawing).padding(start = 24.dp, end = 32.dp, top = 64.dp, bottom = 12.dp))
            }
        } else {
            SleeveStage(snap, nav, t, palette, wide = false, tab = if (sheetOpen) tab else null, onOpen = open, modifier = Modifier.fillMaxSize())
            BackHandler(enabled = sheetOpen) { sheetOpen = false }
            val motion = Tweaks.motion
            AnimatedVisibility(
                sheetOpen, Modifier.fillMaxSize(),
                enter = if (motion) slideInVertically(tween(380)) { it } else EnterTransition.None,
                exit = if (motion) slideOutVertically(tween(300)) { it } else ExitTransition.None,
            ) {
                // the sheet sits over the stage: the cover's tone at the top, sinking into the ground, and it takes every touch so nothing behind it moves
                Box(
                    Modifier.fillMaxSize().drawBehind { drawRect(Brush.verticalGradient(listOf(palette.tone.value, palette.ground.value), endY = size.height * .6f)) }
                        .pointerInput(Unit) { detectTapGestures { } }
                        .windowInsetsPadding(WindowInsets.safeDrawing).padding(horizontal = 18.dp, vertical = 10.dp),
                ) { SleevePanel(snap, nav, t, palette, tab, { tab = it }, onClose = { sheetOpen = false }, Modifier.fillMaxSize()) }
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
private fun SleeveStage(snap: PlayerSnap, nav: PlayerNav, t: Track, p: SleevePalette, wide: Boolean, tab: String?, onOpen: (String) -> Unit, modifier: Modifier) {
    BoxWithConstraints(modifier) {
        val h = maxHeight
        // a phone on its side (or a very short window) scrolls as a last resort, with a shorter cover
        val cramped = h < 560.dp
        val heroH = if (cramped) 220.dp else (h * .616f).coerceIn(340.dp, 560.dp)
        val scroll = rememberScrollState()
        SleeveHero(t, p, heroH, Modifier.graphicsLayer { translationY = -scroll.value.toFloat() })
        // the bottom ~100 dp is the home gesture's: nothing that matters goes there (the navigation bar is inside this, not added to it)
        val nav0 = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding()
        val bottomClear = if (cramped) nav0 + 16.dp else maxDp(92.dp, nav0 + 24.dp)
        Column(
            Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal))
                .then(if (cramped) Modifier.verticalScroll(scroll) else Modifier).padding(bottom = bottomClear),
        ) {
            SleeveTopBar(snap, nav, t, p)
            if (cramped) Spacer(Modifier.height(heroH - 96.dp)) else Spacer(Modifier.weight(1f))
            SleeveTitle(nav, t, p, Modifier.padding(horizontal = 22.dp))
            Spacer(Modifier.height(if (h < 700.dp) 10.dp else 18.dp))
            SleeveControls(snap, t, p, Modifier.padding(horizontal = 10.dp))
            Spacer(Modifier.height(12.dp))
            SleeveActions(snap, nav, t, wide, tab, onOpen)
        }
    }
}

/** The cover full-bleed across the top, fading into the ground, with a little shade at the top so the buttons read on a light cover. */
@Composable
private fun SleeveHero(t: Track, p: SleevePalette, height: Dp, modifier: Modifier) {
    // the fade is drawn over the hero's own content, so it always lies on top of the picture; the last few pixels are solid ground so no edge shows
    Box(modifier.fillMaxWidth().height(height).clipToBounds().drawWithContent {
        drawContent()
        val g = p.ground.value
        drawRect(Brush.verticalGradient(0f to Color.Transparent, .42f to Color.Transparent, .92f to g, 1f to g))
        drawRect(g, Offset(0f, size.height - 4f), Size(size.width, 4f))
        drawRect(Brush.verticalGradient(listOf(Color(0x66000000), Color.Transparent), endY = 110.dp.toPx()), size = Size(size.width, 110.dp.toPx()))
    }) {
        // no shared-element hand-off here: a shared cover is drawn in an overlay above everything, fade included
        ArtImage(t.artKey, Modifier.fillMaxSize(), corner = 0.dp, full = true)
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
        value = WaveAnalyzer.shapeFor(app, t)
    }
}

@Composable
private fun SleeveControls(snap: PlayerSnap, t: Track, p: SleevePalette, modifier: Modifier) {
    val app = LocalApp.current
    val pos = rememberPosition(app.player, snap.playing)
    val dur = remember { mutableLongStateOf(app.player.durationMs) }
    LaunchedEffect(t.path) { while (true) { dur.longValue = app.player.durationMs; delay(500) } }
    val scrub = remember { mutableFloatStateOf(-1f) }   // where the finger is on the ring while it is down (0..1), else -1
    BoxWithConstraints(modifier.fillMaxWidth()) {
        // the ring takes what the four side buttons leave, as on the web (168 px at 390 wide)
        val ring = (maxWidth - (44.dp + 58.dp) * 2 - 12.dp).coerceIn(120.dp, 190.dp)
        Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.SHUFFLE, if (snap.shuffle) "Shuffle on" else "Shuffle off", { app.prefs.shuffle = !app.prefs.shuffle; app.player.applyModes() },
                    tint = if (snap.shuffle) p.main.value else InkDim, size = 44.dp, iconScale = .5f, dot = snap.shuffle)
                SleevePillButton(Glyph.PREV, "Previous", p) { app.player.prev() }
                SleeveRing(snap, t, p, ring, pos, dur, scrub)
                SleevePillButton(Glyph.NEXT, "Next", p) { app.player.next() }
                IconAction(if (snap.repeat == 2) Glyph.REPEAT_ONE else Glyph.REPEAT, when (snap.repeat) { 1 -> "Repeat all"; 2 -> "Repeat one"; else -> "Repeat off" },
                    { app.prefs.repeat = (app.prefs.repeat + 1) % 3; app.player.applyModes() }, tint = if (snap.repeat != 0) p.main.value else InkDim, size = 44.dp, iconScale = .5f, dot = snap.repeat != 0)
            }
            SleeveTime(pos, dur, scrub)
        }
    }
}

/** Previous and next: a tinted pill in the cover's tone. */
@Composable
private fun SleevePillButton(g: Glyph, label: String, p: SleevePalette, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    Box(
        Modifier.size(58.dp, 52.dp).graphicsLayer { val s = if (pressed) .95f else 1f; scaleX = s; scaleY = s }
            .clip(RoundedCornerShape(26.dp)).drawBehind { drawRect(p.tone.value) }.focusRing(RoundedCornerShape(26.dp))
            .semantics { contentDescription = label; role = Role.Button }.clickable(src, null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { GlyphIcon(g, Modifier.size(24.dp), p.toneFg.value) }
}

/**
 * The seek bar as a ring: [RingSeek.BARS] bars hanging in from the outer edge, as long as the song is loud there, starting at 12 o'clock and going
 * clockwise, the played part in the cover's colour. A press on the ring seeks there; dragging round it scrubs and seeks on release. The play button sits
 * inside, and a press on it is never a seek.
 */
@Composable
private fun SleeveRing(snap: PlayerSnap, t: Track, p: SleevePalette, side: Dp, pos: State<Long>, dur: State<Long>, scrub: androidx.compose.runtime.MutableFloatState) {
    val app = LocalApp.current
    val shape by rememberWaveShape(t)
    val n = RingSeek.BARS
    val heights = remember(shape) { WaveBars.heights(shape, n) }
    fun fraction() = if (scrub.floatValue >= 0f) scrub.floatValue else if (dur.value > 0) (pos.value.toFloat() / dur.value).coerceIn(0f, 1f) else 0f
    fun seek(f: Float) { val d = dur.value; if (d > 0) app.player.seekTo((f.coerceIn(0f, 1f) * d).toLong()) }
    // how many bars are played: only a change in this whole number repaints the ring (not every 250 ms tick)
    val played by remember { derivedStateOf { (0 until n).count { RingSeek.isPlayed(it, n, fraction()) } } }
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
            .pointerInput(Unit) {
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = true)
                    val w = size.width.toFloat(); val h = size.height.toFloat()
                    val f0 = RingSeek.fraction(down.position.x, down.position.y, w, h) ?: return@awaitEachGesture
                    down.consume()
                    scrub.floatValue = f0
                    while (true) {
                        val ev = awaitPointerEvent()
                        val c = ev.changes.firstOrNull { it.id == down.id } ?: break
                        if (!c.pressed) { c.consume(); break }
                        c.consume()
                        scrub.floatValue = RingSeek.angleFraction(c.position.x - w / 2f, c.position.y - h / 2f)
                    }
                    if (scrub.floatValue >= 0f) seek(scrub.floatValue)
                    scrub.floatValue = -1f
                }
            },
        contentAlignment = Alignment.Center,
    ) {
        val rest = Color(0x40F4EFE6)
        Canvas(Modifier.fillMaxSize()) {
            val r = size.minDimension / 2f
            val bw = max(2.dp.toPx(), r * .048f); val maxL = r * .27f; val minL = r * .05f
            val on = p.main.value
            val upTo = played
            for (i in 0 until n) {
                rotate(360f * i / n) {
                    val len = max(minL, heights[i] * maxL)
                    drawRoundRect(if (i < upTo) on else rest, Offset(center.x - bw / 2f, center.y - r), Size(bw, len), CornerRadius(bw / 2f))
                }
            }
        }
        SleevePlay(snap, p, side)
    }
}

/** The play button inside the ring: a rounded square in the cover's colour. */
@Composable
private fun SleevePlay(snap: PlayerSnap, p: SleevePalette, ring: Dp) {
    val app = LocalApp.current
    val playFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { playFocus.requestFocus() } catch (_: Exception) {} }
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val keys = androidx.compose.ui.platform.LocalInputModeManager.current.inputMode == androidx.compose.ui.input.InputMode.Keyboard
    val pressed by src.collectIsPressedAsState()
    val shape = RoundedCornerShape(ring * .155f)
    Box(
        Modifier.size(ring * .535f, ring * .417f).graphicsLayer { val s = if (pressed) .95f else 1f; scaleX = s; scaleY = s }
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
        }) { playing -> GlyphIcon(if (playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(ring * .2f), OnMain) }
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
private fun SleeveActions(snap: PlayerSnap, nav: PlayerNav, t: Track, wide: Boolean, tab: String?, onOpen: (String) -> Unit) {
    val app = LocalApp.current
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterHorizontally), verticalAlignment = Alignment.CenterVertically) {
        // on a wide screen the panel is always beside the stage, so only More is needed here
        if (!wide) {
            SleeveOutline("Lyrics", tab == "lyrics") { onOpen("lyrics") }
            SleeveOutline("Up next", tab == "queue") { onOpen("queue") }
        }
        SleeveOutline("More", false, glyph = Glyph.MORE) {
            openTrackSheet(app, nav, t, buildList {
                add(SheetItem("Add to playlist", Glyph.PLUS) { nav.sheet = playlistPicker(app, nav, t) })
                add(SheetItem("Equalizer" + if (app.prefs.eq != "Off") "  (${app.prefs.eq})" else "", Glyph.LIST) { nav.sheet = eqSheet(app) })
                add(SheetItem("Song info", Glyph.INFO) { onOpen("info") })
                if (!app.ui.guest) {
                    add(SheetItem(if (app.jam.inJam) "In a Jam" else "Jam", Glyph.JAM) { app.ui.jamOpen = true })
                    add(SheetItem("Devices", Glyph.DEVICES) { app.ui.devicesOpen = true })
                }
            })
        }
    }
}

/** An outlined button (Lyrics, Up next, More): solid cream with dark text while its panel is open. */
@Composable
private fun SleeveOutline(label: String, on: Boolean, glyph: Glyph? = null, onClick: () -> Unit) {
    val shape = RoundedCornerShape(12.dp)
    Box(
        Modifier.height(40.dp).then(if (glyph != null) Modifier.width(40.dp) else Modifier).clip(shape)
            .background(if (on) Ink else Color.Transparent).border(1.dp, if (on) Ink else Line, shape).focusRing(shape)
            .semantics { contentDescription = label; role = Role.Button }.clickable(onClick = onClick)
            .padding(horizontal = if (glyph != null) 0.dp else 18.dp),
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
        LyricsState.Loading -> Txt("Looking for lyricsâ€¦", Modifier.padding(4.dp), size = 15f, color = InkDim)
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
                        line.text.ifEmpty { "â™ª" },
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
