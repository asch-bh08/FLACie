package com.ipodemu.theme

import android.graphics.RectF
import com.ipodemu.Prefs

/** Which on-screen UI family a model uses. */
enum class Family { MONO, VIDEO, NANO, TOUCH }

class Colorway(
    val name: String,
    val top: Int,
    val bottom: Int,
    val edge: Int,
    val wheel: WheelStyle,
    /** Use the dark on-screen palette (nano-style UI only). */
    val darkUi: Boolean = false,
    val bezel: Int = 0xFF050506.toInt(),
    val backdrop: Int = 0xFF0B0C0E.toInt(),
)

/** One iPod: its UI family, physical proportions (fractions of the body) and available colours. */
class Model(
    val id: String,
    val name: String,
    val family: Family,
    val aspect: Float,
    val screen: RectF,
    val wheelCx: Float,
    val wheelCy: Float,
    val wheelR: Float,
    val radius: Float,
    val bezelInset: Float,
    val colors: List<Colorway>,
    /** Screen is taller than wide in the device look (nano 4th/5th gen). */
    val portrait: Boolean = false,
    /** Separate buttons around the wheel instead of a click wheel (1st gen). */
    val buttons: Boolean = false,
    val year: Int = 0,
    val blurb: String = "",
    /** iPod touch / nano 6-7 style: no wheel, the whole face is a touch screen. */
    val touch: Boolean = false,
    /** Picker grouping ("Classic", "mini", "nano", "touch") and generation number within it. */
    val group: String = "",
    val gen: Int = 1,
    /** Touch models with a round Home button under the screen. */
    val home: Boolean = false,
)

private fun rgb(v: Long) = v.toInt()

private fun mix(a: Int, b: Int, t: Float): Int {
    fun ch(s: Int) = ((a shr s and 0xFF) + ((b shr s and 0xFF) - (a shr s and 0xFF)) * t).toInt().coerceIn(0, 255)
    return (0xFF shl 24) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
}
private fun lighten(c: Int, t: Float) = mix(c, 0xFFFFFFFF.toInt(), t)
private fun darken(c: Int, t: Float) = mix(c, 0xFF000000.toInt(), t)

private fun tintedWheel(base: Int, label: Int = darken(base, 0.5f)) = WheelStyle(
    ringLight = lighten(base, 0.55f), ringDark = lighten(base, 0.12f), edge = darken(base, 0.25f),
    centerLight = lighten(base, 0.6f), centerDark = base, centerEdge = darken(base, 0.25f), label = label,
)

private fun metal(name: String, base: Long, wheel: WheelStyle = Chromes.whiteWheel) =
    Colorway(name, lighten(rgb(base), 0.38f), darken(rgb(base), 0.1f), darken(rgb(base), 0.4f), wheel)

private val SILVER = Colorway("Silver", rgb(0xFFE9EAED), rgb(0xFFBCBEC4), rgb(0xFF8C8F96), Chromes.whiteWheel)
private val WHITE = Colorway("White", rgb(0xFFFFFFFF), rgb(0xFFDBDBDE), rgb(0xFF9C9CA1), Chromes.classicWheel)
private val CREAM = Colorway("White", rgb(0xFFFBFAF5), rgb(0xFFE0DED3), rgb(0xFFA9A79C), Chromes.whiteWheel, bezel = rgb(0xFF3A3B38))
private fun black(darkUi: Boolean) = Colorway(
    "Black", rgb(0xFF44464D), rgb(0xFF17181B), rgb(0xFF000000), Chromes.darkWheel,
    darkUi = darkUi, bezel = rgb(0xFF000000), backdrop = rgb(0xFF1A1B1E),
)
private val U2 = Colorway(
    "U2 Special", rgb(0xFF3A3B40), rgb(0xFF0E0E10), rgb(0xFF000000),
    tintedWheel(rgb(0xFFC1272D), label = rgb(0xFFFFFFFF)), bezel = rgb(0xFF000000), backdrop = rgb(0xFF1A1B1E),
)
private val BLUE = metal("Blue", 0xFF5C97D0)
private val GREEN = metal("Green", 0xFF76B048)
private val RED = metal("Red", 0xFFC93A40)
private val PINK = metal("Pink", 0xFFE27BAA)
private val GOLD = metal("Gold", 0xFFC4A24E)
private val PURPLE = metal("Purple", 0xFF9367B8)
private val ORANGE = metal("Orange", 0xFFD9772C)
private val YELLOW = metal("Yellow", 0xFFE2BE2E)

private fun tinted(name: String, base: Long) =
    metal(name, base, tintedWheel(lighten(rgb(base), 0.45f)))


private val SLATE = metal("Space Gray", 0xFF5A5D63)
private val GRAPHITE = metal("Graphite", 0xFF4A4C52)
private val TOUCH_SILVER = Colorway("Silver", rgb(0xFFEDEEF0), rgb(0xFFB9BCC2), rgb(0xFF8B8E95), Chromes.whiteWheel, bezel = rgb(0xFF08080A))
private val TOUCH_BLACK = Colorway("Black", rgb(0xFF3C3E44), rgb(0xFF121316), rgb(0xFF000000), Chromes.darkWheel, darkUi = true, bezel = rgb(0xFF000000))
private val TOUCH_WHITE = Colorway("White", rgb(0xFFFFFFFF), rgb(0xFFDDDEE1), rgb(0xFF9A9CA2), Chromes.whiteWheel, bezel = rgb(0xFFF6F6F8))
private fun touchMetal(name: String, base: Long) = Colorway(name, lighten(rgb(base), 0.34f), darken(rgb(base), 0.08f), darken(rgb(base), 0.4f), Chromes.whiteWheel, bezel = rgb(0xFF050506))
private val T_SLATE = touchMetal("Space Gray", 0xFF6B6E75)
private val T_GOLD = touchMetal("Gold", 0xFFD9BC94)
private val T_PINK = touchMetal("Pink", 0xFFE9A6B4)
private val T_YELLOW = touchMetal("Yellow", 0xFFF2D95A)
private val T_BLUE = touchMetal("Blue", 0xFF6FA6D8)
private val T_RED = touchMetal("(PRODUCT)RED", 0xFFC8323C)
private val T_SILVER5 = touchMetal("Silver", 0xFFC9CCD2)

/** Every iPod that can be selected, grouped like Apple's own line-up and ordered oldest first within each group. */
object Themes {
    private val nanoScreen = RectF(0.1125f, 0.088f, 0.8875f, 0.5245f)
    private val classicScreen = RectF(0.115f, 0.05f, 0.885f, 0.395f)
    private val nanoTallScreen = RectF(0.105f, 0.06f, 0.895f, 0.51f)
    private val slimScreen = RectF(0.125f, 0.06f, 0.875f, 0.317f)
    private val monoScreen1 = RectF(0.15f, 0.06f, 0.85f, 0.396f)
    private val monoScreen4 = RectF(0.171f, 0.07f, 0.829f, 0.384f)
    private val miniScreen = RectF(0.175f, 0.06f, 0.825f, 0.349f)

    const val CLASSIC = "Classic"
    const val MINI = "mini"
    const val NANO = "nano"
    const val TOUCH = "touch"
    val groups = listOf(CLASSIC, MINI, NANO, TOUCH)

    private fun click(id: String, name: String, group: String, gen: Int, year: Int, family: Family, aspect: Float, screen: RectF,
                      wcy: Float, wr: Float, radius: Float, bezel: Float, colors: List<Colorway>, blurb: String,
                      portrait: Boolean = false, buttons: Boolean = false) =
        Model(id, name, family, aspect, screen, 0.5f, wcy, wr, radius, bezel, colors, portrait, buttons, year, blurb, group = group, gen = gen)

    private fun touch(id: String, name: String, group: String, gen: Int, year: Int, aspect: Float, screen: RectF, radius: Float,
                      colors: List<Colorway>, blurb: String, home: Boolean = true) =
        Model(id, name, Family.TOUCH, aspect, screen, 0.5f, 0.5f, 0f, radius, 0.02f, colors, true, false, year, blurb,
            touch = true, group = group, gen = gen, home = home)

    val models: List<Model> = listOf(
        // ---- iPod / iPod classic ----
        click("ipod1", "iPod (1st gen)", CLASSIC, 1, 2001, Family.MONO, 0.6f, monoScreen1, 0.7f, 0.36f, 0.07f, 0.03f, listOf(CREAM),
            "The original: 5 GB, mechanical scroll wheel and a mono screen.", buttons = true),
        click("ipod2", "iPod (2nd gen)", CLASSIC, 2, 2002, Family.MONO, 0.6f, monoScreen1, 0.7f, 0.36f, 0.07f, 0.03f, listOf(CREAM),
            "Up to 20 GB and Windows support. Still the mechanical wheel.", buttons = true),
        click("ipod3", "iPod (3rd gen)", CLASSIC, 3, 2003, Family.MONO, 0.605f, monoScreen4, 0.70f, 0.31f, 0.085f, 0.028f, listOf(WHITE),
            "Thinner, with a touch-sensitive wheel and dock connector."),
        click("ipod4", "iPod (4th gen)", CLASSIC, 4, 2004, Family.MONO, 0.597f, monoScreen4, 0.70f, 0.31f, 0.085f, 0.028f, listOf(WHITE, U2),
            "The Click Wheel arrives. Mono screen, plus a U2 edition."),
        click("video5", "iPod video (5th gen)", CLASSIC, 5, 2005, Family.VIDEO, 0.597f, classicScreen, 0.703f, 0.335f, 0.085f, 0.028f, listOf(WHITE, black(false)),
            "Colour 2.5\" screen and video playback."),
        click("classic6", "iPod classic (6th gen)", CLASSIC, 6, 2007, Family.NANO, 0.597f, classicScreen, 0.703f, 0.335f, 0.085f, 0.028f, listOf(SILVER, black(true)),
            "All-metal 160 GB classic with the modern menus and Cover Flow."),
        click("classic7", "iPod classic (7th gen)", CLASSIC, 7, 2009, Family.NANO, 0.59f, classicScreen, 0.703f, 0.335f, 0.085f, 0.028f, listOf(SILVER, black(true)),
            "The last classic: 160 GB, thinner, same great menus."),
        // ---- iPod mini ----
        click("mini", "iPod mini (1st gen)", MINI, 1, 2004, Family.MONO, 0.556f, miniScreen, 0.665f, 0.35f, 0.1f, 0.03f,
            listOf(tinted("Silver", 0xFFB4B7BD), tinted("Blue", 0xFF6FA8DC), tinted("Green", 0xFF7FBF5A), tinted("Pink", 0xFFE68FB5), tinted("Gold", 0xFFD4B45E)),
            "Small aluminium body in five colours."),
        click("mini2", "iPod mini (2nd gen)", MINI, 2, 2005, Family.MONO, 0.556f, miniScreen, 0.665f, 0.35f, 0.1f, 0.03f,
            listOf(tinted("Silver", 0xFFB4B7BD), tinted("Blue", 0xFF6FA8DC), tinted("Green", 0xFF7FBF5A), tinted("Pink", 0xFFE68FB5)),
            "Longer battery life and a brighter wheel."),
        // ---- iPod nano ----
        click("nano1", "iPod nano (1st gen)", NANO, 1, 2005, Family.VIDEO, 0.457f, slimScreen, 0.66f, 0.32f, 0.1f, 0.03f, listOf(WHITE, black(false)),
            "Pencil-thin flash player that replaced the mini."),
        click("nano2", "iPod nano (2nd gen)", NANO, 2, 2006, Family.VIDEO, 0.451f, slimScreen, 0.66f, 0.32f, 0.09f, 0.03f, listOf(SILVER, BLUE, GREEN, PINK, RED, GOLD),
            "Anodised aluminium in six colours."),
        click("nano3", "iPod nano (3rd gen)", NANO, 3, 2007, Family.NANO, 0.751f, nanoScreen, 0.777f, 0.245f, 0.075f, 0.022f, listOf(SILVER, black(true), BLUE, GREEN, RED),
            "The squat \"fatty\" nano with the menus and preview pane."),
        click("nano4", "iPod nano (4th gen)", NANO, 4, 2008, Family.NANO, 0.43f, nanoTallScreen, 0.745f, 0.33f, 0.12f, 0.03f,
            listOf(SILVER, black(true), PURPLE, BLUE, GREEN, YELLOW, ORANGE, RED, PINK), "Tall and curved, with a portrait screen.", portrait = true),
        click("nano5", "iPod nano (5th gen)", NANO, 5, 2009, Family.NANO, 0.43f, RectF(0.105f, 0.065f, 0.895f, 0.515f), 0.75f, 0.33f, 0.12f, 0.03f,
            listOf(SILVER, black(true), PURPLE, BLUE, GREEN, YELLOW, ORANGE, RED, PINK), "Adds a video camera. Nine colours.", portrait = true),
        touch("nano6", "iPod nano (6th gen)", NANO, 6, 2010, 0.94f, RectF(0.06f, 0.07f, 0.94f, 0.93f), 0.16f,
            listOf(TOUCH_SILVER, TOUCH_BLACK.let { it }, touchMetal("Blue", 0xFF6FA6D8), touchMetal("Green", 0xFF83BE62), touchMetal("Orange", 0xFFE48A3C), touchMetal("Pink", 0xFFE9A6B4), touchMetal("Red", 0xFFC8323C)),
            "A tiny multi-touch clip. No wheel at all.", home = false),
        touch("nano7", "iPod nano (7th gen)", NANO, 7, 2012, 0.5f, RectF(0.07f, 0.075f, 0.93f, 0.8f), 0.14f,
            listOf(TOUCH_SILVER, T_SLATE, touchMetal("Blue", 0xFF6FA6D8), touchMetal("Purple", 0xFF9B7CC3), T_PINK, T_YELLOW, T_RED),
            "Tall multi-touch nano with a Home button."),
        // ---- iPod touch ----
        touch("touch1", "iPod touch (1st gen)", TOUCH, 1, 2007, 0.555f, RectF(0.1f, 0.125f, 0.9f, 0.8f), 0.1f, listOf(TOUCH_SILVER),
            "The first iPod with Multi-Touch and Wi-Fi."),
        touch("touch2", "iPod touch (2nd gen)", TOUCH, 2, 2008, 0.55f, RectF(0.1f, 0.125f, 0.9f, 0.8f), 0.11f, listOf(TOUCH_SILVER),
            "Built-in speaker and the App Store."),
        touch("touch3", "iPod touch (3rd gen)", TOUCH, 3, 2009, 0.55f, RectF(0.1f, 0.125f, 0.9f, 0.8f), 0.11f, listOf(TOUCH_SILVER),
            "Faster, with Voice Control."),
        touch("touch4", "iPod touch (4th gen)", TOUCH, 4, 2010, 0.53f, RectF(0.09f, 0.125f, 0.91f, 0.8f), 0.12f, listOf(TOUCH_BLACK, TOUCH_WHITE),
            "Retina display and cameras."),
        touch("touch5", "iPod touch (5th gen)", TOUCH, 5, 2012, 0.475f, RectF(0.06f, 0.115f, 0.94f, 0.855f), 0.13f,
            listOf(T_SILVER5, T_SLATE, T_PINK, T_YELLOW, T_BLUE, T_RED), "4-inch screen, colours and a loop."),
        touch("touch6", "iPod touch (6th gen)", TOUCH, 6, 2015, 0.475f, RectF(0.06f, 0.115f, 0.94f, 0.855f), 0.13f,
            listOf(T_SLATE, T_SILVER5, T_GOLD, T_PINK, T_BLUE, T_RED), "A8 chip and Apple Music."),
        touch("touch7", "iPod touch (7th gen)", TOUCH, 7, 2019, 0.475f, RectF(0.06f, 0.115f, 0.94f, 0.855f), 0.13f,
            listOf(T_SLATE, T_SILVER5, T_GOLD, T_PINK, T_BLUE, T_RED), "The last iPod: A10 Fusion and up to 256 GB."),
    )

    const val DEFAULT_MODEL = "nano3"

    fun model(id: String): Model = models.firstOrNull { it.id == id } ?: models.first { it.id == DEFAULT_MODEL }

    fun inGroup(group: String) = models.filter { it.group == group }

    /** The name of a generation as shown on picker chips ("1st", "2nd"...). */
    fun genLabel(m: Model): String = when (m.gen) { 1 -> "1st"; 2 -> "2nd"; 3 -> "3rd"; else -> "${m.gen}th" }

    private var cacheKey: String? = null
    private var cached: IpodTheme? = null

    /** The click-wheel theme for the current model / colour / look preferences (cached). Touch models fall back to nano. */
    fun create(prefs: Prefs): IpodTheme {
        var m = model(prefs.model)
        if (m.touch) m = model(DEFAULT_MODEL)
        val ci = prefs.colorway.coerceIn(0, m.colors.lastIndex)
        val key = "${m.id}:$ci:${prefs.look}"
        if (key == cacheKey) return cached!!
        return build(m, ci, prefs.look).also { cacheKey = key; cached = it }
    }

    /** Builds a click-wheel theme for any model/colour/look, e.g. for previews. look: 0 = modern, 1 = device. */
    fun build(m: Model, colorIdx: Int, look: Int): IpodTheme {
        val ci = colorIdx.coerceIn(0, m.colors.lastIndex)
        val cw = m.colors[ci]
        val key = "${m.id}:$ci:$look"
        val dark = cw.darkUi || cw.top < 0xFF606060.toInt()
        val body = BodySpec(
            m.aspect, m.screen, m.wheelCx, m.wheelCy, m.wheelR, m.radius,
            cw.top, cw.bottom, cw.edge, if (dark) 0x44FFFFFF else 0xAAFFFFFF.toInt(), if (dark) 0x26FFFFFF else 0x55FFFFFF,
            cw.bezel, m.bezelInset,
        )
        val chrome = Chrome(
            flatTop = lighten(cw.top, 0.2f), flatBottom = cw.bottom, flatLine = darken(cw.edge, 0.3f), wheel = cw.wheel,
            body = if (look == 1) body else null, backdrop = cw.backdrop,
        )
        val device = when {
            look == 1 && m.portrait -> DeviceSpec(240, 320)
            m.family == Family.MONO -> DeviceSpec(160, 128)
            else -> DeviceSpec(320, 240)
        }
        return when (m.family) {
            Family.NANO, Family.TOUCH -> Nano3Theme(key, m.name, if (cw.darkUi) Palettes.nanoDark else Palettes.nanoLight, chrome, device)
            Family.VIDEO -> ClassicTheme(key, m.name, chrome, device)
            Family.MONO -> MonoGen1Theme(key, m.name, chrome, device, m.buttons)
        }
    }
}
