package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
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
import com.ipodemu.R
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/*
 * Design I for the whole app, not just the player: the same three faces and small parts as the web's new look, so the library, Home,
 * playlists and settings read as one design with Now Playing (LivingSleeve.kt). The modern theme's Txt uses Archivo through IpodStyle.font.
 */

/** The three faces of design I (SIL OFL fonts in res/font; their licences are in src/main/fonts-licence). */
@OptIn(ExperimentalTextApi::class)
object DesignFonts {
    val serif = FontFamily(
        Font(R.font.instrument_serif_regular, FontWeight.Normal, FontStyle.Normal),
        Font(R.font.instrument_serif_italic, FontWeight.Normal, FontStyle.Italic),
    )
    // Archivo is one variable file: each weight asks it for that weight explicitly, or every weight would draw as the file's default
    val sans = FontFamily(
        Font(R.font.archivo_variable, FontWeight.Normal, variationSettings = FontVariation.Settings(FontVariation.weight(400))),
        Font(R.font.archivo_variable, FontWeight.Medium, variationSettings = FontVariation.Settings(FontVariation.weight(500))),
        Font(R.font.archivo_variable, FontWeight.SemiBold, variationSettings = FontVariation.Settings(FontVariation.weight(600))),
        Font(R.font.archivo_variable, FontWeight.Bold, variationSettings = FontVariation.Settings(FontVariation.weight(700))),
    )
    val mono = FontFamily(Font(R.font.dm_mono_regular, FontWeight.Normal), Font(R.font.dm_mono_medium, FontWeight.Medium))
}

/** A heading in the serif with its last word in italics, the design's signature (the split is SleeveText.split, shared with the player). */
fun serifTitle(text: String, italicWords: Int = 1): AnnotatedString {
    val (lead, last) = splitLastWords(text, italicWords)
    return buildAnnotatedString { append(lead); withStyle(SpanStyle(fontStyle = FontStyle.Italic)) { append(last) } }
}

/** Like SleeveText.split, but the last [n] words go in italics (Home's "Pick up where you *left off*"). Never fewer than one word. */
fun splitLastWords(text: String, n: Int): Pair<String, String> {
    // a negative count asks for no italics at all (a number on the Dashboard)
    if (n < 0) return text to ""
    var lead = text.trimEnd(); var last = ""
    repeat(n.coerceAtLeast(1)) {
        if (lead.isEmpty()) return@repeat
        val (a, b) = SleeveText.split(lead)
        last = if (last.isEmpty()) b else "$b $last"
        lead = a.trimEnd()
    }
    return (if (lead.isEmpty()) "" else "$lead ") to last
}

@Composable
fun SerifTitle(text: String, modifier: Modifier = Modifier, size: Float = 30f, color: Color = LocalScheme.current.onBg, maxLines: Int = 1, align: TextAlign = TextAlign.Start, italicWords: Int = 1) {
    val s = remember(text, italicWords) { serifTitle(text, italicWords) }
    BasicText(
        s, modifier,
        style = TextStyle(color = color, fontSize = size.sp, fontFamily = DesignFonts.serif, textAlign = align, lineHeight = (size * 1.05f).sp, letterSpacing = (-0.01f).em),
        maxLines = maxLines, overflow = TextOverflow.Ellipsis,
    )
}

/** The small DM Mono label: greetings, counts, times, "ALBUM", format chips. */
@Composable
fun MonoLabel(text: String, modifier: Modifier = Modifier, size: Float = 11f, color: Color = LocalScheme.current.onBgDim, upper: Boolean = true, maxLines: Int = 1) {
    BasicText(
        if (upper) text.uppercase() else text, modifier,
        style = TextStyle(color = color, fontSize = size.sp, fontFamily = DesignFonts.mono, fontWeight = FontWeight.Medium, letterSpacing = if (upper) 0.12f.em else 0.em),
        maxLines = maxLines, overflow = TextOverflow.Ellipsis,
    )
}

/** An outlined chip; the chosen one is filled with the ink (the kit's SmallButton, kept under this name for the screens that use it). */
@Composable
fun DesignChip(text: String, selected: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier, icon: Glyph? = null) =
    SmallButton(text, onClick, modifier, selected = selected, icon = icon)

/** The format under a title ("FLAC", "HI-RES"): the kit's FormatTag, the square mono one the owner liked on Now Playing (plan decision 2). */
@Composable
fun FormatChip(text: String, color: Color = LocalScheme.current.onBgDim) = FormatTag(text, fg = if (KitTags.formatIsHiRes(text)) null else color)

/** Covers in design I: rounded, with a soft shadow under them. */
fun Modifier.coverShadow(corner: Dp = 14.dp, elevation: Dp = 10.dp): Modifier =
    this.shadow(elevation, RoundedCornerShape(corner), clip = false, ambientColor = Color.Black, spotColor = Color.Black)

/** The cover's colours for the main button and the tinted pills, read once per cover off the main thread; the warm default until then. */
@Composable
fun rememberCoverColours(artKey: String?): SleeveColours {
    val bmp = rememberArt(artKey, thumb = true)
    var cols by remember { mutableStateOf(SleeveColours.DEFAULT) }
    LaunchedEffect(bmp, artKey) {
        if (bmp != null) cols = withContext(Dispatchers.Default) {
            val small = android.graphics.Bitmap.createScaledBitmap(bmp, 24, 24, true)
            val px = IntArray(24 * 24)
            small.getPixels(px, 0, 24, 0, 0, 24, 24)
            if (small !== bmp) small.recycle()
            SleeveColours.extract(px)
        } else if (artKey == null) cols = SleeveColours.DEFAULT
    }
    return cols
}

/** The main button filled with the cover's colour (the kit's MainButton, Primary); [height] is kept for old callers, the kit sets the size. */
@Composable
fun AccentPill(text: String, icon: Glyph?, colours: SleeveColours, onClick: () -> Unit, modifier: Modifier = Modifier, @Suppress("UNUSED_PARAMETER") height: Dp = 48.dp) =
    MainButton(text, onClick, modifier, icon, MainKind.PRIMARY, fill = Color(colours.main), ink = Color(DesignColour.readableOn(colours.main)))

/** The main button tinted with the cover's darker tone (the kit's MainButton, Secondary). */
@Composable
fun TonePill(text: String, icon: Glyph?, colours: SleeveColours, onClick: () -> Unit, modifier: Modifier = Modifier, @Suppress("UNUSED_PARAMETER") height: Dp = 48.dp) =
    MainButton(text, onClick, modifier, icon, MainKind.SECONDARY, fill = Color(colours.tone), ink = Color(colours.toneFg))

/** The DM Mono facts line under an album's or an artist's title, as the web writes it (Album.razor): "2020 · 8 songs · 16 min · FLAC / MP3". */
object DetailFacts {
    private val FORMATS = mapOf("flac" to "FLAC", "mp3" to "MP3", "m4a" to "AAC", "aac" to "AAC", "alac" to "ALAC", "ogg" to "Vorbis", "opus" to "Opus", "wav" to "WAV", "aiff" to "AIFF", "aif" to "AIFF")

    /** [year] 0 leaves the year out; [files] gives the formats (by extension, each named once in order of first use; unknown ones are left out). */
    fun line(year: Int, songs: Int, totalMs: Long, files: List<String>): String {
        val formats = files.mapNotNull { f -> FORMATS[f.substringBefore('?').substringAfterLast('.', "").lowercase()] }.distinct()
        return listOfNotNull(
            year.takeIf { it > 0 }?.toString(),
            if (songs == 1) "1 song" else "$songs songs",
            (totalMs / 60000).takeIf { totalMs > 0 }?.let { "$it min" },
            formats.takeIf { it.isNotEmpty() }?.joinToString(" / "),
        ).joinToString(" · ")
    }
}

/** The greeting over Home in DM Mono, by the hour (morning before noon, afternoon until six, evening after). */
object HomeWords {
    fun greeting(hour: Int): String = when (hour) { in 5..11 -> "Good morning"; in 12..17 -> "Good afternoon"; else -> "Good evening" }
    /** The headline: "Pick up where you left off" with something to pick up, else an invitation to start. */
    fun headline(hasRecent: Boolean): String = if (hasRecent) "Pick up where you left off" else "Put something on"
}

/** Colour sums shared by the design's pieces (plain ARGB ints, so they can be unit-tested). */
object DesignColour {
    /** The text colour that reads on [argb]: near-black when it is light, white when it is dark (the colour where both contrast alike is luminance 0.18). */
    fun readableOn(argb: Int): Int {
        fun lin(c: Int): Double { val x = c / 255.0; return if (x <= 0.03928) x / 12.92 else Math.pow((x + 0.055) / 1.055, 2.4) }
        val l = 0.2126 * lin(argb shr 16 and 0xFF) + 0.7152 * lin(argb shr 8 and 0xFF) + 0.0722 * lin(argb and 0xFF)
        return if (l > 0.18) 0xFF1A120C.toInt() else 0xFFFFFFFF.toInt()
    }
}

