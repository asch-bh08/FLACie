package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.EnterTransition
import androidx.compose.animation.ExitTransition
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.MutableTransitionState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch

/*
 * The kit: the one set of building blocks every modern screen is made of, matching "The one design" in docs/plans/one-app-overhaul.md and the web's
 * kit.css, so the phone and the web are one app. Screens use these (or the old names, now thin wrappers over them); new code must not size text
 * by hand (KitLintTest fails on a new Txt(size = number)). The iPod themes keep their own widgets.
 */

/** The colour, radius and size tokens (the web's names). */
object Kit {
    // the web's slate ground (owner decision 1 of the plan), the same surfaces, line and ink as app.css
    val bg = Color(0xFF101416)
    val surface = Color(0xFF15151A); val surface2 = Color(0xFF1E1E25); val surface3 = Color(0xFF282832)
    val line = Color(0xFF2A2A33)
    val ink = Color(0xFFF4F3F7); val dim = Color(0xFFA9A6B3); val faint = Color(0xFF6F6C78)
    /** The accent until a cover gives one (screens read the live one from LocalScheme). */
    val pink = Color(0xFFFF4D73)
    val glassFill = Color(0x0FFFFFFF)
    val glassRim = Color(0x24FFFFFF)
    /** Ink for tags: Hi-Res amber (and the dark text on it), streaming blue, playing green. */
    val amber = Color(0xFFFFC857); val onAmber = Color(0xFF2A1D00)
    val cloud = Color(0xFF6DB3FF)
    val green = Color(0xFF4EE0A1)
    val tagInk = Color(0xB3FFFFFF)

    /** One button shape (owner, 26 Nov: "why did you make half the buttons rounded rectangles but not the others"): every button is this rounded
     *  rectangle, main, small, slot and icon alike; the small one's shorter body takes 16 so it reads as the same shape. Tags stay pills. */
    val rButton = 18.dp; val rButtonSmall = 16.dp
    val rCard = 22.dp; val rSlot = rButton; val rFormat = 5.dp
    /** Never a smaller touch target than this, tight or not. */
    val touch = 48.dp

    /** The sizes of the pieces: big by default (owner, 26 Nov: "I LOVE big buttons"), one step down only when the screen is [tight]. */
    fun sizes(tight: Boolean): KitSizes = if (tight) TIGHT else BIG
    private val BIG = KitSizes(main = 56.dp, small = 44.dp, slot = 60.dp, icon = 52.dp, row = 72.dp, cover = 52.dp, coverR = 10.dp)
    private val TIGHT = KitSizes(main = 52.dp, small = 40.dp, slot = 52.dp, icon = 48.dp, row = 64.dp, cover = 44.dp, coverR = 8.dp)

    /** Toasts: a plain note goes after 2.5 s, one with Undo stays 5 s so there is time to reach it. */
    fun toastMs(hasAction: Boolean): Long = if (hasAction) 5_000L else 2_500L
}

// ---- the five text styles ------------------------------------------------------------------------------------------

/** One size step of the kit (visible heights; touch targets are never under [Kit.touch]). */
class KitSizes(val main: Dp, val small: Dp, val slot: Dp, val icon: Dp, val row: Dp, val cover: Dp, val coverR: Dp)

/**
 * The fit rule (NowPlayingFit's idea for every screen): the big sizes unless the room is too small for them. A short window (a phone on its side,
 * the Fold's cover screen, a split screen) or a very narrow one steps down; every phone in the size list (360 × 640 up) stays big upright.
 */
object KitFit {
    fun tight(widthDp: Float, heightDp: Float): Boolean = widthDp < 360f || heightDp < 600f
}

/** A screen that measured its own room (BoxWithConstraints) can say tight or not here; null = decide from the window. */
val LocalKitTight = androidx.compose.runtime.compositionLocalOf<Boolean?> { null }

@Composable
fun kitTight(): Boolean = LocalKitTight.current ?: androidx.compose.ui.platform.LocalConfiguration.current.let { KitFit.tight(it.screenWidthDp.toFloat(), it.screenHeightDp.toFloat()) }

@Composable
fun kitSizes(): KitSizes = Kit.sizes(kitTight())

/** The five named styles. Sizes are sp; Title and Section put the last word in italics. */
object KitText {
    fun title(color: Color, align: TextAlign = TextAlign.Start) =
        TextStyle(color = color, fontSize = 30.sp, fontFamily = DesignFonts.serif, textAlign = align, lineHeight = 31.5.sp, letterSpacing = (-0.01f).em)
    fun section(color: Color, align: TextAlign = TextAlign.Start) =
        TextStyle(color = color, fontSize = 24.sp, fontFamily = DesignFonts.serif, textAlign = align, lineHeight = 25.2.sp, letterSpacing = (-0.01f).em)
    fun body(color: Color, weight: FontWeight = FontWeight.Normal, align: TextAlign = TextAlign.Start) =
        TextStyle(color = color, fontSize = 16.sp, fontFamily = DesignFonts.sans, fontWeight = weight, textAlign = align)
    fun small(color: Color, weight: FontWeight = FontWeight.Normal, align: TextAlign = TextAlign.Start) =
        TextStyle(color = color, fontSize = 14.sp, fontFamily = DesignFonts.sans, fontWeight = weight, textAlign = align)
    fun label(color: Color, align: TextAlign = TextAlign.Start) =
        TextStyle(color = color, fontSize = 11.sp, fontFamily = DesignFonts.mono, fontWeight = FontWeight.Medium, letterSpacing = 0.12f.em, textAlign = align)

    /** A Title or Section's two parts: the words in the upright serif and the last one in italics (no italics for a bare number). */
    fun titleParts(text: String): Pair<String, String> = splitLastWords(text, if (text.isNotBlank() && text.none { it.isLetter() }) -1 else 1)
}

@Composable
private fun KitTextLine(text: AnnotatedString, style: TextStyle, modifier: Modifier, maxLines: Int) =
    BasicText(text, modifier, style = style, maxLines = maxLines, overflow = TextOverflow.Ellipsis)

/** A screen's heading: serif 30, the last word in italics. */
@Composable
fun Title(text: String, modifier: Modifier = Modifier, color: Color = LocalScheme.current.onBg, maxLines: Int = 1, align: TextAlign = TextAlign.Start) {
    val s = remember(text) { KitText.titleParts(text).let { (a, b) -> titleString(a, b) } }
    KitTextLine(s, KitText.title(color, align), modifier, maxLines)
}

/** A section's heading: serif 24, the last word in italics. */
@Composable
fun Section(text: String, modifier: Modifier = Modifier, color: Color = LocalScheme.current.onBg, maxLines: Int = 1, align: TextAlign = TextAlign.Start) {
    val s = remember(text) { KitText.titleParts(text).let { (a, b) -> titleString(a, b) } }
    KitTextLine(s, KitText.section(color, align), modifier, maxLines)
}

private fun titleString(lead: String, last: String) = androidx.compose.ui.text.buildAnnotatedString {
    append(lead)
    pushStyle(androidx.compose.ui.text.SpanStyle(fontStyle = androidx.compose.ui.text.font.FontStyle.Italic)); append(last); pop()
}

/** Running text and row titles: sans 16 (SemiBold for a row's title). */
@Composable
fun Body(text: String, modifier: Modifier = Modifier, color: Color = LocalScheme.current.onBg, weight: FontWeight = FontWeight.Normal, maxLines: Int = 1, align: TextAlign = TextAlign.Start) =
    KitTextLine(AnnotatedString(text), KitText.body(color, weight, align), modifier, maxLines)

/** Second lines, notes and hints: sans 14, dim. */
@Composable
fun Small(text: String, modifier: Modifier = Modifier, color: Color = LocalScheme.current.onBgDim, weight: FontWeight = FontWeight.Normal, maxLines: Int = 1, align: TextAlign = TextAlign.Start) =
    KitTextLine(AnnotatedString(text), KitText.small(color, weight, align), modifier, maxLines)

/** Counts, times and group names: DM Mono 11 in spaced capitals. */
@Composable
fun Label(text: String, modifier: Modifier = Modifier, color: Color = LocalScheme.current.onBgDim, maxLines: Int = 1, align: TextAlign = TextAlign.Start) =
    KitTextLine(AnnotatedString(text.uppercase()), KitText.label(color, align), modifier, maxLines)

// ---- buttons -------------------------------------------------------------------------------------------------------

enum class MainKind { PRIMARY, SECONDARY }

/**
 * The main button: a 56 dp rounded rectangle (52 when tight), sans 15/600. Primary is filled with the cover's colour (the live accent unless [fill] says otherwise), Secondary
 * with its darker tone (Play / Shuffle). The ink is worked out for the fill when not given.
 */
@Composable
fun MainButton(
    text: String, onClick: () -> Unit, modifier: Modifier = Modifier, icon: Glyph? = null, kind: MainKind = MainKind.PRIMARY,
    fill: Color? = null, ink: Color? = null, enabled: Boolean = true,
) {
    val sc = LocalScheme.current
    val bg = fill ?: if (kind == MainKind.PRIMARY) sc.accent else Kit.surface3
    val fg = ink ?: Color(DesignColour.readableOn(bg.toArgbInt()))
    val shape = RoundedCornerShape(Kit.rButton)
    Row(
        modifier.height(kitSizes().main).clip(shape).background(bg).graphicsLayer { alpha = if (enabled) 1f else .45f }
            .semantics { role = Role.Button }.focusRing(shape).clickable(enabled = enabled, onClick = onClick).padding(horizontal = 22.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center,
    ) {
        if (icon != null) { GlyphIcon(icon, Modifier.size(18.dp), fg); if (text.isNotEmpty()) Box(Modifier.width(9.dp)) }
        if (text.isNotEmpty()) BasicText(text, style = TextStyle(color = fg, fontSize = 15.sp, fontFamily = DesignFonts.sans, fontWeight = FontWeight.SemiBold), maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

/**
 * The small button: an outlined rounded rectangle 44 dp to see (40 when tight) and 48 dp to touch, sans 15/600; the chosen one is filled with the ink (dark text on it). Filters,
 * tabs, settings choices and every quiet action. The fill fades in and out as it is chosen.
 */
@Composable
fun SmallButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, selected: Boolean = false, icon: Glyph? = null, enabled: Boolean = true) {
    val sc = LocalScheme.current
    val shape = RoundedCornerShape(Kit.rButtonSmall)
    val h = kitSizes().small
    val k = chosenAmount(selected, "chip")
    val fg = androidx.compose.ui.graphics.lerp(sc.onBg, Kit.bg, k)
    // (no press dip: with one per button, lists with button headers scrolled at half the frames on the emulator, 10 Nov)
    Row(
        modifier.heightIn(min = Kit.touch).clip(shape)
            .semantics { role = Role.Button; this.selected = selected }
            .clickable(enabled = enabled, onClick = onClick).padding(vertical = (Kit.touch - h).coerceAtLeast(0.dp) / 2)
            .height(h).clip(shape).graphicsLayer { alpha = if (enabled) 1f else .45f }
            .background(sc.onBg.copy(alpha = sc.onBg.alpha * k)).border(1.dp, sc.onBg.copy(alpha = .28f * (1f - k)), shape)
            .padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center,
    ) {
        if (icon != null) { GlyphIcon(icon, Modifier.size(18.dp), fg); if (text.isNotEmpty()) Box(Modifier.width(8.dp)) }
        if (text.isNotEmpty()) BasicText(text, style = TextStyle(color = fg, fontSize = 15.sp, fontFamily = DesignFonts.sans, fontWeight = FontWeight.SemiBold), maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

/**
 * The slot the owner likes: a rounded rectangle 60 dp tall (52 when tight, radius 18) on glass, an icon over its word; on = filled with the ink. Every row of quiet
 * actions uses it (Now Playing, playlist, album, artist, a Devices card). [compact] is Now Playing's short-screen step: 40 dp, the word alone.
 * [fill], [ink] and [onInk] let Now Playing keep its darker glass and cream ink over the cover's colours.
 */
@Composable
fun Slot(
    word: String, icon: Glyph, onClick: () -> Unit, modifier: Modifier = Modifier, on: Boolean = false, description: String = word,
    tab: Boolean = false, compact: Boolean = false, fill: Color = Kit.glassFill, ink: Color = LocalScheme.current.onBg, onInk: Color = Kit.bg,
    rim: Boolean = true,
) {
    val shape = RoundedCornerShape(Kit.rSlot)
    Column(
        modifier.height(if (compact) 40.dp else kitSizes().slot).clip(shape).background(if (on) ink else fill)
            .then(if (rim && !on) Modifier.border(1.dp, Kit.glassRim, shape) else Modifier).focusRing(shape)
            .semantics(mergeDescendants = true) { contentDescription = description; role = if (tab) Role.Tab else Role.Button; if (on) stateDescription = "Selected" }
            .clickable(onClick = onClick).padding(horizontal = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(3.dp, Alignment.CenterVertically),
    ) {
        val fg = if (on) onInk else ink
        if (!compact) GlyphIcon(icon, Modifier.size(22.dp), fg)
        BasicText(word, style = TextStyle(color = fg, fontSize = 12.5.sp, fontFamily = DesignFonts.sans, fontWeight = FontWeight.SemiBold), maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

/** An icon button: a 52 dp rounded square of glass (48 when tight), never under a 48 dp touch target; on = the icon in the accent. */
@Composable
fun IconButton(icon: Glyph, description: String, onClick: () -> Unit, modifier: Modifier = Modifier, on: Boolean = false, tint: Color = LocalScheme.current.onBg) {
    val accent = LocalScheme.current.accent
    val d = kitSizes().icon
    Box(
        modifier.size(maxOf(d, Kit.touch)).clip(RoundedCornerShape(Kit.rButton)).semantics { contentDescription = description; role = Role.Button; if (on) stateDescription = "On" }
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Box(Modifier.size(d).clip(RoundedCornerShape(Kit.rButton)).background(Kit.glassFill).border(1.dp, Kit.glassRim, RoundedCornerShape(Kit.rButton)), contentAlignment = Alignment.Center) {
            GlyphIcon(icon, Modifier.size(22.dp), if (on) accent else tint)
        }
    }
}

// ---- tags ----------------------------------------------------------------------------------------------------------

/** What a status tag says, which picks its ink. */
enum class StatusTone { PLAIN, ACCENT, ON, OFF, HIRES, CLOUD }

/** The tags' colour rules as plain ARGB ints, so they can be unit-tested. */
object KitTags {
    /** The ink of a status tag ([accent] is the live accent's ARGB). */
    fun statusInk(tone: StatusTone, accent: Int): Int = when (tone) {
        StatusTone.ACCENT -> accent
        StatusTone.ON -> 0xFF4EE0A1.toInt()
        StatusTone.HIRES -> 0xFFFFC857.toInt()
        StatusTone.CLOUD -> 0xFF6DB3FF.toInt()
        StatusTone.PLAIN, StatusTone.OFF -> 0xB3FFFFFF.toInt()
    }

    /** The rim is fainter for the quiet tones, so the ones that say something stand out. */
    fun rimAlpha(tone: StatusTone): Float = if (tone == StatusTone.PLAIN || tone == StatusTone.OFF) .35f else .6f

    /** The tone for the spec's status words (This device, Playing, Offline, Streaming, Recently downloaded); anything else is plain. */
    fun toneFor(word: String): StatusTone = when (word.trim().lowercase()) {
        "this device" -> StatusTone.ACCENT
        "playing", "recently downloaded", "on this phone" -> StatusTone.ON
        "offline" -> StatusTone.OFF
        "streaming" -> StatusTone.CLOUD
        "hi-res", "✦ hi-res" -> StatusTone.HIRES
        else -> StatusTone.PLAIN
    }

    /** A format tag is the Hi-Res amber one when its words say Hi-Res. */
    fun formatIsHiRes(text: String): Boolean = text.trim().uppercase().let { it.startsWith("HI-RES") || it.startsWith("✦ HI-RES") }
}

/** A status tag: an outlined pill, sans 11.5/600 (the Devices tag). [compact] trims the padding for a narrow row. */
@Composable
fun StatusTag(text: String, tone: StatusTone = KitTags.toneFor(text), modifier: Modifier = Modifier, compact: Boolean = false) {
    val ink = Color(KitTags.statusInk(tone, LocalScheme.current.accent.toArgbInt()))
    val shape = RoundedCornerShape(50)
    Box(modifier.clip(shape).border(1.dp, ink.copy(alpha = KitTags.rimAlpha(tone)), shape).padding(horizontal = if (compact) 7.dp else 10.dp, vertical = if (compact) 0.dp else 3.dp)) {
        BasicText(text, style = TextStyle(color = ink, fontSize = 11.5.sp, fontFamily = DesignFonts.sans, fontWeight = FontWeight.SemiBold), maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

/**
 * A format tag, the Now Playing one the owner liked: DM Mono capitals 10.5, radius 5, filled. HI-RES is amber; the rest a quiet fill unless [bg] / [fg]
 * say otherwise (Now Playing tints them with the cover). Only where the format matters.
 */
@Composable
fun FormatTag(text: AnnotatedString, modifier: Modifier = Modifier, bg: Color? = null, fg: Color? = null) {
    if (text.text.isBlank()) return
    val hi = KitTags.formatIsHiRes(text.text)
    val fill = bg ?: if (hi) Kit.amber else Color(0x1FFFFFFF)
    val ink = fg ?: if (hi) Kit.onAmber else Kit.ink
    BasicText(
        AnnotatedString(text.text.uppercase(), text.spanStyles, text.paragraphStyles),
        modifier.clip(RoundedCornerShape(Kit.rFormat)).background(fill).padding(horizontal = 7.dp, vertical = 4.dp),
        style = TextStyle(color = ink, fontSize = 10.5.sp, fontFamily = DesignFonts.mono, fontWeight = FontWeight.SemiBold, letterSpacing = .06f.em),
        maxLines = 1, overflow = TextOverflow.Ellipsis,
    )
}

@Composable
fun FormatTag(text: String, modifier: Modifier = Modifier, bg: Color? = null, fg: Color? = null) = FormatTag(AnnotatedString(text), modifier, bg, fg)

// ---- card, row, empty ----------------------------------------------------------------------------------------------

/** The card: glass with a hairline rim, radius 22. [rim] lets a live card (the playing device) take the accent. */
@Composable
fun GlassCard(modifier: Modifier = Modifier, rim: Color = Kit.glassRim, padding: Dp = 14.dp, spacing: Dp = 10.dp, content: @Composable ColumnScope.() -> Unit) {
    val shape = RoundedCornerShape(Kit.rCard)
    Column(
        modifier.fillMaxWidth().clip(shape).background(Kit.glassFill).border(1.dp, rim, shape).padding(padding),
        verticalArrangement = Arrangement.spacedBy(spacing),
    ) { content() }
}

/**
 * The list row: 72 dp (64 when tight), a 52 dp cover (radius 10; 44/8 when tight), the title in Body 600 over a Small second line, and at the right the time (Label) and ⋯.
 * [leading] replaces the cover (a device's badge, a playlist's glyph); a null [onMore] leaves the ⋯ out.
 */
@Composable
fun SongRow(
    title: String, second: String?, onClick: () -> Unit, modifier: Modifier = Modifier, artKey: String? = null, time: String? = null,
    onMore: (() -> Unit)? = null, now: Boolean = false, leading: (@Composable () -> Unit)? = null,
) {
    val sc = LocalScheme.current
    val z = kitSizes()
    Row(
        modifier.fillMaxWidth().heightIn(min = z.row).clickable(onClick = onClick).padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (leading != null) leading() else ArtImage(artKey, Modifier.size(z.cover), thumb = true, corner = z.coverR)
        Column(Modifier.weight(1f)) {
            Body(title, color = if (now) sc.accent else sc.onBg, weight = FontWeight.SemiBold)
            if (!second.isNullOrBlank()) Small(second)
        }
        if (time != null) Label(time)
        if (onMore != null) Box(
            Modifier.size(Kit.touch).clip(RoundedCornerShape(Kit.rButton)).semantics { contentDescription = "More for $title"; role = Role.Button }.clickable(onClick = onMore),
            contentAlignment = Alignment.Center,
        ) { GlyphIcon(Glyph.MORE, Modifier.size(20.dp), sc.onBgDim) }
    }
}

/**
 * The empty state: a centred glyph, one line, and an optional main button. [detail] is a second, smaller line when the one line needs a why.
 * [fill] centres it in all the room there is (a page with nothing on it); false keeps it its own size (a section in a scrolling page).
 */
@Composable
fun EmptyState(
    text: String, modifier: Modifier = Modifier, glyph: Glyph = Glyph.NOTE, detail: String? = null, action: String? = null, onAction: (() -> Unit)? = null,
    fill: Boolean = true,
) {
    val sc = LocalScheme.current
    Box(if (fill) modifier.fillMaxSize() else modifier.fillMaxWidth().padding(vertical = 18.dp), contentAlignment = Alignment.Center) {
        Column(Modifier.padding(horizontal = 32.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            GlyphIcon(glyph, Modifier.size(if (fill) 56.dp else 36.dp), sc.onBg.copy(alpha = .3f))
            Body(text, color = sc.onBgDim, maxLines = 6, align = TextAlign.Center)
            if (detail != null) Small(detail, maxLines = 4, align = TextAlign.Center)
            if (action != null && onAction != null) MainButton(action, onAction)
        }
    }
}

// ---- switch, toast -------------------------------------------------------------------------------------------------

/** A setting's On/Off as a switch: 44 × 26, a white 22 dp knob, the accent track when on. Drawn only; the row around it takes the tap. */
@Composable
fun Switch(on: Boolean, modifier: Modifier = Modifier) {
    val sc = LocalScheme.current
    val x by animateDpAsState(if (on) 20.dp else 2.dp, tween(160), label = "knob")
    Box(modifier.size(width = 44.dp, height = 26.dp).clip(RoundedCornerShape(13.dp)).background(if (on) sc.accent else Kit.surface3)) {
        Box(Modifier.padding(start = x, top = 2.dp).size(22.dp).clip(CircleShape).background(Color.White))
    }
}

// ---- the sheet -----------------------------------------------------------------------------------------------------

/** A sheet's colours as getters, so Now Playing's cover fade repaints it without recomposing. */
class SheetColours(
    val top: () -> Color, val bottom: () -> Color, val accent: () -> Color,
    val ink: Color, val dim: Color, val faint: Color, val line: Color,
)

/** The colours of a sheet over an ordinary screen: the scheme's ground, darkened, as the old ActionSheet had. */
@Composable
fun plainSheetColours(): SheetColours {
    val sc = LocalScheme.current
    return SheetColours(
        { sc.top.copy(alpha = 1f).mix(Color.Black, .35f) }, { sc.bottom.mix(Color.Black, .25f) }, { sc.accent },
        sc.onBg, sc.onBgDim, sc.onBgDim.copy(alpha = .45f), Kit.line,
    )
}

/**
 * The one sheet (the iPod themes keep ActionSheet): drag handle, serif title, the rows under mono group labels, 56 dp rows, the chosen value in the
 * accent with a check. It slides up with a spring and down when closed; the last menu is kept while it slides away. [onDismiss] is a close (scrim,
 * Back, handle, drag); a row calls [onPick], which by default closes it and then runs the row.
 */
@Composable
fun Sheet(
    spec: SheetSpec?, onDismiss: () -> Unit, colours: SheetColours = plainSheetColours(), focusFirst: Boolean = false,
    onPick: (SheetItem) -> Unit = { onDismiss(); it.onClick() },
) {
    var last by remember { mutableStateOf<SheetSpec?>(null) }
    if (spec != null) last = spec
    val open = spec != null
    // starts closed, so a sheet composed already open still slides and fades in
    val scrim = remember { Animatable(0f) }
    LaunchedEffect(open) { scrim.animateTo(if (open) 1f else 0f, tween(if (Tweaks.motion) 220 else 0)) }
    val shown = remember { MutableTransitionState(false) }
    shown.targetState = open
    if (open) BackHandler { onDismiss() }
    if (scrim.value > 0f) Box(Modifier.fillMaxSize().drawBehind { drawRect(Color.Black.copy(alpha = .55f * scrim.value)) }.then(if (open) Modifier.pointerInput(Unit) { detectTapGestures { onDismiss() } } else Modifier))
    val drag = remember { Animatable(0f) }
    val scope = rememberCoroutineScope()
    val first = remember { FocusRequester() }
    LaunchedEffect(spec) { if (spec != null) drag.snapTo(0f) }
    AnimatedVisibility(
        shown, Modifier.fillMaxSize(),
        enter = if (Tweaks.motion) slideInVertically(spring(.86f, 420f)) { it } else EnterTransition.None,
        exit = if (Tweaks.motion) slideOutVertically(tween(220)) { it } else ExitTransition.None,
    ) {
        val s = last ?: return@AnimatedVisibility
        if (focusFirst) LaunchedEffect(s) { try { first.requestFocus() } catch (_: Exception) {} }
        val accent = colours.accent()
        BoxWithConstraints(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)), contentAlignment = Alignment.BottomCenter) {
            val shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp)
            Column(
                Modifier.fillMaxWidth().widthIn(max = 640.dp).heightIn(max = maxHeight * .9f)
                    .graphicsLayer { translationY = drag.value }
                    .clip(shape).drawBehind { drawRect(Brush.verticalGradient(listOf(colours.top(), colours.bottom()), endY = 360.dp.toPx())) }
                    .border(1.dp, colours.line, shape)
                    .pointerInput(Unit) { detectTapGestures { } },
            ) {
                Box(
                    Modifier.fillMaxWidth().height(28.dp).semantics { contentDescription = "Close"; role = Role.Button }.clickable(onClick = onDismiss)
                        .pointerInput(Unit) {
                            detectVerticalDragGestures(
                                onDragEnd = { scope.launch { if (drag.value > 120.dp.toPx()) onDismiss() else drag.animateTo(0f, spring(.8f, 500f)) } },
                            ) { c, dy -> c.consume(); scope.launch { drag.snapTo((drag.value + dy).coerceAtLeast(0f)) } }
                        },
                    contentAlignment = Alignment.Center,
                ) { Box(Modifier.size(40.dp, 4.dp).clip(CircleShape).background(colours.faint)) }
                // the title and the subtitle scroll with the rows (a long subtitle was cut, and on a phone on its side at big text a fixed header
                // left the rows no room)
                Column(Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()).windowInsetsPadding(WindowInsets.navigationBars).padding(bottom = 12.dp)) {
                    Column(Modifier.padding(start = 24.dp, end = 24.dp, bottom = 6.dp).semantics(mergeDescendants = true) { heading() }) {
                        Title(s.title, color = colours.ink, maxLines = 3)
                        if (!s.subtitle.isNullOrEmpty()) Small(s.subtitle, color = colours.dim, maxLines = 8)
                    }
                    if (s.items.isEmpty()) Body("One moment…", Modifier.padding(24.dp), color = colours.dim)
                    var group: String? = null
                    s.items.forEachIndexed { i, item ->
                        if (item.group != null && item.group != group) {
                            group = item.group
                            Label(item.group, Modifier.padding(start = 24.dp, top = 14.dp, bottom = 4.dp).semantics { heading() }, color = colours.dim)
                        }
                        val on = item.selected
                        Row(
                            Modifier.fillMaxWidth().heightIn(min = 56.dp)
                                .then(if (i == 0 && focusFirst) Modifier.focusRequester(first) else Modifier)
                                .semantics(mergeDescendants = true) { role = Role.Button; if (on) stateDescription = "Selected" }
                                .clickable { onPick(item) }
                                .then(if (on) Modifier.background(accent.copy(alpha = .14f)) else Modifier)
                                .padding(horizontal = 24.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp),
                        ) {
                            GlyphIcon(item.glyph, Modifier.size(24.dp), if (on) accent else colours.ink)
                            Body(item.label, Modifier.weight(1f), color = if (on) accent else colours.ink, weight = if (on) FontWeight.SemiBold else FontWeight.Medium, maxLines = 3)
                            if (on) GlyphIcon(Glyph.CHECK, Modifier.size(20.dp), accent)
                        }
                    }
                }
            }
        }
    }
}

/** ARGB of a Compose colour, without android.graphics (so the kit's sums stay JVM-testable). */
internal fun Color.toArgbInt(): Int {
    fun c(x: Float) = (x.coerceIn(0f, 1f) * 255f + .5f).toInt()
    return (c(alpha) shl 24) or (c(red) shl 16) or (c(green) shl 8) or c(blue)
}
