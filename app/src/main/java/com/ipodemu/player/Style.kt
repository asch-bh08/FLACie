package com.ipodemu.player

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.text.font.FontFamily
import androidx.palette.graphics.Palette
import com.ipodemu.library.ArtCache
import com.ipodemu.theme.Colorway
import com.ipodemu.theme.Family
import com.ipodemu.theme.Model
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Two colours pulled from a cover: a lively one and a deeper companion. */
class ArtColors(val primary: Color, val secondary: Color)

object ArtPalette {
    private val cache = HashMap<String, ArtColors?>()

    suspend fun of(art: ArtCache, key: String?): ArtColors? {
        if (key == null) return null
        synchronized(cache) { if (cache.containsKey(key)) return cache[key] }
        val bmp = art.load(key, thumb = true) ?: return null
        val result = withContext(Dispatchers.Default) {
            try {
                val p = Palette.from(bmp).maximumColorCount(20).generate()
                val a = p.vibrantSwatch ?: p.lightVibrantSwatch ?: p.dominantSwatch ?: p.mutedSwatch
                val b = p.darkVibrantSwatch ?: p.darkMutedSwatch ?: p.dominantSwatch ?: a
                if (a == null) null else ArtColors(Color(a.rgb), Color((b ?: a).rgb))
            } catch (_: Exception) { null }
        }
        synchronized(cache) { cache[key] = result }
        return result
    }
}

private fun hsv(c: Color): FloatArray { val o = FloatArray(3); android.graphics.Color.colorToHSV(c.toArgb(), o); return o }
private fun fromHsv(h: Float, s: Float, v: Float) = Color(android.graphics.Color.HSVToColor(floatArrayOf(h, s.coerceIn(0f, 1f), v.coerceIn(0f, 1f))))

/** What the current iPod model contributes to the app's look. */
@Immutable
class IpodStyle(val model: Model, val colorway: Colorway) {
    val family: Family get() = model.family
    val mono: Boolean get() = model.family == Family.MONO
    val touch: Boolean get() = model.touch
    /** Glossy Aqua-era widgets (everything except the mono LCD models and flat iOS 7+ touches). */
    val glossy: Boolean get() = !mono && !(model.touch && model.year >= 2012)
    val font: FontFamily get() = if (mono) FontFamily.Monospace else FontFamily.SansSerif
    /** Corner radius scale: square LCD, softer colour screens, round modern touches. */
    val corner: Float get() = when { mono -> 2f; model.touch && model.year >= 2012 -> 20f; touch -> 14f; else -> 10f }

    /** The model's signature highlight colour (iPod blue for silver/white/black bodies). */
    val accent: Color = run {
        val bottom = Color(colorway.bottom)
        val h = hsv(bottom)
        if (h[1] < 0.22f) Color(0xFF2F7BE8) else fromHsv(h[0], (h[1] * 1.5f).coerceIn(0.5f, 0.85f), 0.9f)
    }
    val defaultDark: Boolean get() = colorway.darkUi || colorway.top < 0xFF606060.toInt()
}

/** Resolved colours for the whole player UI. */
@androidx.compose.runtime.Stable
class Scheme(
    private val topS: State<Color>, private val bottomS: State<Color>, private val accentS: State<Color>,
    private val onBgS: State<Color>, private val onBgDimS: State<Color>, private val cardS: State<Color>, private val borderS: State<Color>,
    val dark: Boolean,
) {
    // Colours are States: read in the draw phase they cost nothing, read in composition only that composable recomposes,
    // so the 900 ms artwork fade no longer recomposes the whole screen every frame.
    constructor(top: Color, bottom: Color, accent: Color, onBg: Color, onBgDim: Color, card: Color, cardBorder: Color, dark: Boolean) :
        this(Const(top), Const(bottom), Const(accent), Const(onBg), Const(onBgDim), Const(card), Const(cardBorder), dark)
    val top: Color get() = topS.value
    val bottom: Color get() = bottomS.value
    val accent: Color get() = accentS.value
    val onBg: Color get() = onBgS.value
    val onBgDim: Color get() = onBgDimS.value
    val card: Color get() = cardS.value
    val cardBorder: Color get() = borderS.value
    val accentDark: Color get() = fromHsv(hsv(accent)[0], hsv(accent)[1], hsv(accent)[2] * 0.62f)
    val accentLight: Color get() = fromHsv(hsv(accent)[0], hsv(accent)[1] * 0.6f, (hsv(accent)[2] * 1.12f).coerceAtMost(1f))
}

/** Builds the target scheme. With [art] the whole app fades to the cover's colours; otherwise it uses the model accent. */
fun buildScheme(style: IpodStyle, art: ArtColors?, dark: Boolean, useArt: Boolean): Scheme {
    if (style.mono) {
        // 1G-4G and mini: a flat monochrome LCD look, never tinted by artwork
        return Scheme(Color(0xFFD1D8C0), Color(0xFFBAC2A6), Color(0xFF1A2010), Color(0xFF15190F), Color(0xB3283020), Color(0x33FFFFFF), Color(0x44283020), false)
    }
    val base = if (useArt && art != null && !style.mono) art else null
    val p = base?.primary ?: style.accent
    val s = base?.secondary ?: style.accent
    val ph = hsv(p); val sh = hsv(s)
    val sat = ph[1].coerceIn(0.28f, 0.9f)
    return if (dark) {
        val top = fromHsv(ph[0], sat * 0.9f, 0.46f)
        val bottom = fromHsv(sh[0], (sh[1] * 0.8f).coerceIn(0.2f, 0.8f), 0.12f)
        val accent = fromHsv(ph[0], sat.coerceAtLeast(0.55f), 0.95f)
        Scheme(top, bottom, accent, Color(0xFFF7F8FA), Color(0xB3F0F2F6), Color(0x22FFFFFF), Color(0x33FFFFFF), true)
    } else {
        val top = fromHsv(ph[0], sat * 0.32f, 0.97f)
        val bottom = fromHsv(sh[0], sat * 0.18f, 0.9f)
        val accent = fromHsv(ph[0], sat.coerceAtLeast(0.6f), 0.82f)
        Scheme(top, bottom, accent, Color(0xFF15181D), Color(0xA5202630), Color(0x99FFFFFF), Color(0x22000000), false)
    }
}

/** Animates between schemes so changing track fades the whole UI, as the brief asks. */
@Composable
fun animatedScheme(target: Scheme): Scheme {
    val spec = tween<Color>(durationMillis = 900)
    val top = animateColorAsState(target.top, spec, label = "top")
    val bottom = animateColorAsState(target.bottom, spec, label = "bottom")
    val accent = animateColorAsState(target.accent, spec, label = "accent")
    val onBg = animateColorAsState(target.onBg, tween(400), label = "onBg")
    val onDim = animateColorAsState(target.onBgDim, tween(400), label = "onDim")
    val card = animateColorAsState(target.card, tween(400), label = "card")
    val border = animateColorAsState(target.cardBorder, tween(400), label = "border")
    // one Scheme instance for as long as the States and the light/dark flag stay the same
    return remember(top, bottom, accent, onBg, onDim, card, border, target.dark) { Scheme(top, bottom, accent, onBg, onDim, card, border, target.dark) }
}

private class Const(override val value: Color) : State<Color>

val LocalScheme = compositionLocalOf { buildScheme(IpodStylePlaceholder.style, null, true, false) }
val LocalStyle = compositionLocalOf { IpodStylePlaceholder.style }

/** Only used as a default for the composition locals before the root provides real values. */
private object IpodStylePlaceholder {
    val style: IpodStyle by lazy {
        val m = com.ipodemu.theme.Themes.model(com.ipodemu.theme.Themes.DEFAULT_MODEL)
        IpodStyle(m, m.colors.first())
    }
}
