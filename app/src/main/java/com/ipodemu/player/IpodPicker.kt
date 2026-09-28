package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import kotlinx.coroutines.launch
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.ipodemu.theme.Colorway
import com.ipodemu.theme.Family
import com.ipodemu.theme.Model
import com.ipodemu.theme.Themes

/** The three ways to use the app. Everything else on this screen (which iPod, colours) refines the chosen mode. */
private enum class UseMode(val title: String, val tagline: String, val detail: String) {
    MODERN("Modern Player", "Flat music app, iPod style", "Shelves, suggestions, search and swipes. No device body, no wheel."),
    EMULATOR("iPod Emulator", "The device itself", "Full iPod body, screen and click wheel, faithful to the real hardware."),
    WHEEL("Click Wheel Fullscreen", "Wheel UI, no body", "The iPod menus and click wheel filling the screen, without the device around it."),
}

private fun modeOf(viewMode: Int): UseMode = when (viewMode) { 0 -> UseMode.MODERN; 2 -> UseMode.WHEEL; else -> UseMode.EMULATOR }

private fun viewModeFor(mode: UseMode, m: Model): Int = when (mode) {
    UseMode.MODERN -> 0
    UseMode.WHEEL -> 2
    UseMode.EMULATOR -> if (m.touch) 1 else 3   // touch iPods have no wheel: the body then shows the touch UI
}

/**
 * One screen for the whole look: 1) pick how you use it (three previewed modes), 2) pick which iPod and colour
 * (a gallery of real device drawings), 3) app colours, theme and display. The preview on top always shows the result.
 */
@Composable
fun PickerScreen() {
    val app = LocalApp.current
    val ui = app.ui
    ui.rev
    BackHandler { ui.pickerOpen = false }
    val model = Themes.model(ui.model)
    val ci = ui.colorway.coerceIn(0, model.colors.lastIndex)
    val cw = model.colors[ci]
    val bg = Brush.verticalGradient(listOf(Color(0xFF1B1F2A), Color(0xFF07080B)))
    val edge = with(androidx.compose.ui.platform.LocalDensity.current) { 22.dp.toPx() }
    Box(Modifier.fillMaxSize().background(bg).pointerInput(Unit) { detectTapGestures { } }.edgeSwipeBack(true, edge, {}) { ui.pickerOpen = false }) {
        BoxWithConstraints(Modifier.fillMaxSize().statusBarsPadding()) {
            val land = maxWidth > maxHeight * 1.15f
            val preview = @Composable { BigPreview(model, cw, modeOf(ui.viewMode), Modifier.fillMaxSize().padding(10.dp)) }
            val controls = @Composable { Controls(model, ci) }
            // always-visible Back (the screen used to have only Done at the very bottom)
            if (land) Row(Modifier.fillMaxSize()) {
                Box(Modifier.weight(0.42f).fillMaxHeight()) { preview() }
                Box(Modifier.weight(0.58f).fillMaxHeight()) { controls() }
            } else Column(Modifier.fillMaxSize()) {
                Box(Modifier.weight(0.36f).fillMaxWidth()) { preview() }
                Box(Modifier.weight(0.64f).fillMaxWidth()) { controls() }
            }
            GlossPill("Back", { ui.pickerOpen = false }, Modifier.align(Alignment.TopStart).padding(start = 10.dp, top = 8.dp), icon = Glyph.BACK, height = 36.dp)
            // the click-wheel views have no other way back to the default theme
            GlossPill("Modern theme", { ui.changeTheme(0); ui.pickerOpen = false }, Modifier.align(Alignment.TopEnd).padding(end = 10.dp, top = 8.dp), height = 36.dp)
        }
    }
}

// ---- previews -----------------------------------------------------------------------------------------------------

private fun accentOf(m: Model, cw: Colorway): Color = IpodStyle(m, cw).accent

/** The real device drawing (body, screen, wheel) with a tiny UI mock on the screen. */
@Composable
private fun DevicePreview(m: Model, cw: Colorway, modifier: Modifier) {
    BoxWithConstraints(modifier, contentAlignment = Alignment.Center) {
        val bh = minOf(maxHeight, maxWidth / m.aspect)
        val bw = bh * m.aspect
        Canvas(Modifier.size(bw, bh)) {
            drawIpodBody(m, cw)
            val w = size.width; val h = size.height
            val sx = m.screen.left * w; val sy = m.screen.top * h
            val sw = m.screen.width() * w; val sh = m.screen.height() * h
            val lcd = m.family == Family.MONO
            val top = if (lcd) Color(0xFFD1D8C0) else accentOf(m, cw).copy(alpha = .9f)
            val bottom = if (lcd) Color(0xFFBAC2A6) else Color(0xFF0B0C10)
            drawRoundRect(Brush.verticalGradient(listOf(top, bottom), sy, sy + sh), Offset(sx, sy), Size(sw, sh), CornerRadius(w * 0.01f))
            val ink = if (lcd) Color(0xFF15190F) else Color.White
            val art = sh * 0.36f
            drawRoundRect(ink.copy(alpha = .3f), Offset(sx + sw / 2 - art / 2, sy + sh * 0.1f), Size(art, art), CornerRadius(art * .1f))
            drawRoundRect(ink.copy(alpha = .8f), Offset(sx + sw * 0.2f, sy + sh * 0.58f), Size(sw * 0.6f, sh * 0.06f), CornerRadius(4f))
            drawRoundRect(ink.copy(alpha = .5f), Offset(sx + sw * 0.3f, sy + sh * 0.7f), Size(sw * 0.4f, sh * 0.05f), CornerRadius(4f))
        }
    }
}

/** Flat music-app mock: header, now-playing card, a shelf of cover tiles and list rows, in the model's colours. */
private fun DrawScope.drawModernMock(accent: Color, lcd: Boolean) {
    val w = size.width; val h = size.height
    val top = if (lcd) Color(0xFFD1D8C0) else accent.copy(alpha = .75f)
    val bottom = if (lcd) Color(0xFFBAC2A6) else Color(0xFF0A0C12)
    val ink = if (lcd) Color(0xFF15190F) else Color.White
    drawRoundRect(Brush.verticalGradient(listOf(top, bottom)), Offset.Zero, size, CornerRadius(w * 0.05f))
    drawRoundRect(ink.copy(alpha = .16f), Offset(w * .06f, h * .05f), Size(w * .88f, h * .09f), CornerRadius(6f))                   // title bar
    drawRoundRect(ink.copy(alpha = .2f), Offset(w * .06f, h * .18f), Size(w * .88f, h * .2f), CornerRadius(h * .04f))                 // now playing card
    drawRoundRect(ink.copy(alpha = .35f), Offset(w * .09f, h * .2f), Size(h * .16f, h * .16f), CornerRadius(6f))
    drawRoundRect(ink.copy(alpha = .8f), Offset(w * .09f + h * .2f, h * .23f), Size(w * .35f, h * .035f), CornerRadius(4f))
    drawCircle(accent, h * .06f, Offset(w * .86f, h * .28f))
    val tile = (w * .88f - 2 * w * .03f) / 3
    for (i in 0..2) drawRoundRect(ink.copy(alpha = .28f), Offset(w * .06f + i * (tile + w * .03f), h * .43f), Size(tile, tile * .8f), CornerRadius(8f))   // shelf
    for (i in 0..2) drawRoundRect(ink.copy(alpha = if (i == 0) .5f else .22f), Offset(w * .06f, h * .72f + i * h * .09f), Size(w * .88f, h * .06f), CornerRadius(5f))
}

/** Full-screen wheel UI mock: menu list on top with a highlighted row, wheel with side cards below. */
private fun DrawScope.drawWheelMock(m: Model, cw: Colorway) {
    val w = size.width; val h = size.height
    val lcd = m.family == Family.MONO
    drawRoundRect(Color(0xFF15171C), Offset.Zero, size, CornerRadius(w * 0.05f))
    val listH = h * .56f
    drawRoundRect(if (lcd) Color(0xFFD1D8C0) else Color.White, Offset(0f, 0f), Size(w, listH), CornerRadius(w * 0.05f))
    for (i in 0..3) {
        val y = h * .02f + i * h * .13f
        if (i == 0) drawRect(if (lcd) Color(0xFF15190F) else accentOf(m, cw), Offset(0f, y), Size(w * .62f, h * .12f))
        drawRoundRect(if (i == 0) Color.White else Color(0xFF333333), Offset(w * .05f, y + h * .04f), Size(w * .3f, h * .035f), CornerRadius(3f))
    }
    val cy = listH + (h - listH) / 2f
    val r = (h - listH) * .44f
    drawRoundRect(Color(0xFF2A2D35), Offset(w * .03f, listH + h * .05f), Size(w * .24f, h - listH - h * .1f), CornerRadius(8f))
    drawRoundRect(Color(0xFF2A2D35), Offset(w * .73f, listH + h * .05f), Size(w * .24f, h - listH - h * .1f), CornerRadius(8f))
    drawCircle(Color(0xFFE6E7EB), r, Offset(w / 2, cy))
    drawCircle(Color(0xFFCFD1D8), r * .38f, Offset(w / 2, cy))
}

@Composable
private fun ModePreview(mode: UseMode, m: Model, cw: Colorway, modifier: Modifier) {
    when (mode) {
        UseMode.EMULATOR -> DevicePreview(m, cw, modifier)
        UseMode.MODERN -> Canvas(modifier) { drawModernMock(accentOf(m, cw), m.family == Family.MONO) }
        UseMode.WHEEL -> Canvas(modifier) { drawWheelMock(m, cw) }
    }
}

/** Big live preview on top: exactly what the current mode + iPod + colour will look like. */
@Composable
private fun BigPreview(m: Model, cw: Colorway, mode: UseMode, modifier: Modifier) {
    Column(modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
            val side = if (mode == UseMode.EMULATOR) Modifier.fillMaxSize() else Modifier.fillMaxHeight().aspectRatio(1.3f)
            ModePreview(mode, m, cw, side)
        }
        Txt("${mode.title}  -  ${m.name}, ${cw.name}", Modifier.padding(top = 4.dp), size = 12f, weight = FontWeight.SemiBold, color = Color.White, maxLines = 1)
    }
}

// ---- controls -----------------------------------------------------------------------------------------------------

@Composable
private fun ModeCard(mode: UseMode, selected: Boolean, enabled: Boolean, why: String?, m: Model, cw: Colorway, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val shape = RoundedCornerShape(14.dp)
    Row(
        Modifier.fillMaxWidth().clip(shape)
            .background(if (selected) Color(0x332F7BE8) else Color(0x14FFFFFF))
            .border(if (focused) 2.5.dp else if (selected) 2.dp else 1.dp, if (focused) Color.White else if (selected) Color(0xFF63A9FF) else Color(0x22FFFFFF), shape)
            .clickable(src, null, enabled = enabled, onClick = onClick).padding(10.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.width(96.dp).height(80.dp).clip(RoundedCornerShape(10.dp)).background(Color(0xFF0D0F14)), contentAlignment = Alignment.Center) {
            ModePreview(mode, m, cw, Modifier.fillMaxSize().padding(4.dp))
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Txt(mode.title, size = 16f, weight = FontWeight.Bold, color = if (enabled) Color.White else Color(0x66FFFFFF))
            Txt(mode.tagline, size = 12f, weight = FontWeight.SemiBold, color = Color(0xFF63A9FF))
            Txt(why ?: mode.detail, size = 12f, color = Color(0xFF9AA1B1), maxLines = 3)
        }
    }
}

@Composable
private fun DeviceCard(m: Model, cw: Colorway, selected: Boolean, enabled: Boolean, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val shape = RoundedCornerShape(12.dp)
    Column(
        Modifier.width(92.dp).clip(shape)
            .background(if (selected) Color(0x332F7BE8) else Color(0x14FFFFFF))
            .border(if (focused) 2.5.dp else if (selected) 2.dp else 1.dp, if (focused) Color.White else if (selected) Color(0xFF63A9FF) else Color(0x22FFFFFF), shape)
            .clickable(src, null, enabled = enabled, onClick = onClick).padding(6.dp),
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Box(Modifier.height(96.dp).fillMaxWidth().background(Color.Transparent), contentAlignment = Alignment.Center) {
            DevicePreview(m, cw, Modifier.fillMaxSize().padding(2.dp))
            if (!enabled) Box(Modifier.fillMaxSize().background(Color(0x99000000)))
        }
        Txt(Themes.genLabel(m), size = 12f, weight = FontWeight.Bold, color = if (enabled) Color.White else Color(0x66FFFFFF), maxLines = 1)
        Txt("${m.year}", size = 11f, color = Color(0xFF9AA1B1))
    }
}

@Composable
private fun Controls(model: Model, ci: Int) {
    val app = LocalApp.current
    val ui = app.ui
    ui.rev
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val mode = modeOf(ui.viewMode)
    val cw = model.colors[ci]
    var group by remember(model.id) { androidx.compose.runtime.mutableStateOf(model.group) }
    val needsWheel = mode == UseMode.WHEEL

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(bottom = 30.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Txt("Appearance", Modifier.padding(top = 6.dp), size = 22f, weight = FontWeight.Bold, color = Color.White)

        Section("1  How do you want to use it?") {
            UseMode.values().forEach { md ->
                val ok = !(md == UseMode.WHEEL && model.touch)
                ModeCard(md, md == mode, ok, if (ok) null else "Touch iPods have no click wheel - pick a click-wheel iPod below first.", model, cw) {
                    ui.changeViewMode(viewModeFor(md, model))
                }
            }
        }

        Section("2  Which iPod?") {
            IpodCarousel(model, ci)
            Text_model_info(model)
            if (model.colors.size > 1) {
                LazyRow(horizontalArrangement = Arrangement.spacedBy(12.dp), contentPadding = PaddingValues(vertical = 4.dp)) {
                    items(model.colors.indices.toList()) { i -> Swatch(model.colors[i], i == ci) { ui.changeColorway(i) } }
                }
            }
        }

        Section("3  App colours and theme") {
            val lcd = model.family == Family.MONO
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Chip("Fade with artwork", ui.dynamicColor && !lcd, enabled = !lcd) { ui.changeDynamic(true) }
                Chip("Fixed", !ui.dynamicColor || lcd) { ui.changeDynamic(false) }
            }
            if (lcd) Txt("LCD iPods keep their own colours.", size = 12f, color = Color(0xFF9AA1B1))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                val p = app.prefs
                listOf("System", "Dark", "Light").forEachIndexed { i, n ->
                    val code = intArrayOf(2, 0, 1)[i]
                    Chip(n, p.appearance == code) { p.appearance = code; ui.refreshFromPrefs() }
                }
            }
        }

        Section("4  Display") {
            val p = app.prefs
            Txt("Click wheel layout", size = 14f, weight = FontWeight.SemiBold, color = Color.White)
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(listOf(0 to "Auto (controller)", 1 to "Wheel shown", 2 to "Wheel hidden")) { (v, n) ->
                    Chip(n, p.layoutMode == v) { p.layoutMode = v; (ctx as? com.ipodemu.ui.MainActivity)?.ipodView?.refreshLayout(); ui.refreshFromPrefs() }
                }
            }
            val steps = listOf(-1f, 0.25f, 0.5f, 0.75f, 1f)
            Txt("Brightness", size = 14f, weight = FontWeight.SemiBold, color = Color.White)
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(steps) { b -> Chip(brightnessLabel(b), p.brightness == b) { p.brightness = b; (ctx as? com.ipodemu.ui.MainActivity)?.applyBrightness(); ui.refreshFromPrefs() } }
            }
            Txt("Backlight (dims after)", Modifier.padding(top = 4.dp), size = 14f, weight = FontWeight.SemiBold, color = Color.White)
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(listOf(0, 10, 30, 60)) { s -> Chip(if (s == 0) "Always on" else "${s}s", p.backlightSec == s) { p.backlightSec = s; ui.refreshFromPrefs() } }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Txt("Time in title", Modifier.weight(1f), size = 14f, weight = FontWeight.SemiBold, color = Color.White)
                Chip("Off", !p.timeInTitle) { p.timeInTitle = false; ui.refreshFromPrefs() }
                Chip("On", p.timeInTitle) { p.timeInTitle = true; ui.refreshFromPrefs() }
            }
        }
        GlossPill("Done", { ui.pickerOpen = false }, Modifier.fillMaxWidth().padding(top = 10.dp), primary = true, height = 50.dp)
    }
}

private val LABEL = Color(0xFF8B92A3)

@Composable
private fun Section(title: String, content: @Composable () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Txt(title, Modifier.padding(top = 10.dp), size = 14f, weight = FontWeight.Bold, color = Color(0xFFB6BDCB))
        content()
    }
}

@Composable
private fun Chip(text: String, selected: Boolean, enabled: Boolean = true, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val shape = RoundedCornerShape(50)
    Box(
        Modifier.clip(shape)
            .background(if (selected) Brush.verticalGradient(listOf(Color(0xFF5DA6F5), Color(0xFF1D6BDB))) else Brush.verticalGradient(listOf(Color(0x2EFFFFFF), Color(0x14FFFFFF))))
            .border(if (focused) 2.5.dp else 1.dp, if (focused) Color.White else if (selected) Color(0xFF8CC0FA) else Color(0x33FFFFFF), shape)
            .clickable(src, null, enabled = enabled, onClick = onClick).padding(horizontal = 16.dp, vertical = 10.dp),
        contentAlignment = Alignment.Center,
    ) { Txt(text, size = 14f, weight = FontWeight.SemiBold, color = if (!enabled) Color(0x55FFFFFF) else if (selected) Color.White else Color(0xFFD5D9E2)) }
}

@Composable
private fun Swatch(col: Colorway, selected: Boolean, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    Box(
        Modifier.size(46.dp).clip(CircleShape)
            .background(Brush.verticalGradient(listOf(Color(col.top), Color(col.bottom))))
            .border(if (selected) 3.dp else if (focused) 3.dp else 1.dp, if (selected || focused) Color.White else Color(0x55FFFFFF), CircleShape)
            .clickable(src, null, onClick = onClick),
    )
}

@Composable
private fun Text_model_info(model: Model) {
    Txt(model.blurb, size = 12f, color = Color(0xFF9AA1B1), maxLines = 2)
}

/**
 * The iPod gallery: every model as a tilted object on a horizontal carousel. The centred iPod is large and turned toward
 * you, neighbours peek in from the sides, smaller and rotated away with perspective, each with a soft floor shadow. This is
 * faked 3D (real perspective transforms on the vector device drawings), not a 3D engine. Swipe, tap a neighbour, use the
 * arrows, or the L1/R1 shoulder buttons. The name and year sit in a pill underneath.
 */
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
private fun IpodCarousel(model: Model, ci: Int) {
    val app = LocalApp.current
    val ui = app.ui
    val all = Themes.models
    val start = all.indexOfFirst { it.id == model.id }.coerceAtLeast(0)
    val pager = androidx.compose.foundation.pager.rememberPagerState(initialPage = start) { all.size }
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    // settle on an iPod -> select it
    androidx.compose.runtime.LaunchedEffect(pager) {
        androidx.compose.runtime.snapshotFlow { pager.settledPage }.collect { p -> if (all[p].id != ui.model) ui.changeModel(all[p].id) }
    }
    // controller shoulders
    androidx.compose.runtime.LaunchedEffect(ui.pickerStep) {
        if (ui.pickerStep > 0) pager.animateScrollToPage((pager.currentPage + ui.pickerStepDir).coerceIn(0, all.lastIndex))
    }
    val cur = all[pager.currentPage.coerceIn(0, all.lastIndex)]
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.fillMaxWidth().height(250.dp)) {
            // floor glow
            Box(Modifier.align(Alignment.BottomCenter).padding(bottom = 6.dp).width(220.dp).height(16.dp).clip(CircleShape).background(Brush.radialGradient(listOf(Color(0x66000000), Color.Transparent))))
            androidx.compose.foundation.pager.HorizontalPager(
                pager, Modifier.fillMaxSize(), contentPadding = PaddingValues(horizontal = 118.dp), pageSpacing = (-34).dp, beyondBoundsPageCount = 1,
            ) { page ->
                val m = all[page]
                val offset = (pager.currentPage - page) + pager.currentPageOffsetFraction   // 0 centred, + left of centre, - right
                val d = kotlin.math.abs(offset).coerceIn(0f, 2f)
                val shown = if (m.id == model.id) model.colors[ci] else m.colors.first()
                Box(
                    Modifier.fillMaxSize().clickable(remember { MutableInteractionSource() }, null) { scope.launch { pager.animateScrollToPage(page) } }
                        .graphicsLayer {
                            cameraDistance = 14f * density
                            rotationY = (-offset).coerceIn(-1.5f, 1.5f) * 32f      // turn away from the centre
                            val s = 1f - 0.24f * d.coerceAtMost(1.5f)
                            scaleX = s; scaleY = s
                            alpha = (1f - 0.32f * d).coerceIn(0.35f, 1f)
                            translationY = 14.dp.toPx() * d
                        },
                    contentAlignment = Alignment.Center,
                ) {
                    DevicePreview(m, shown, Modifier.fillMaxSize().padding(top = 8.dp, bottom = 22.dp))
                }
            }
            CarouselArrow(true, Modifier.align(Alignment.CenterStart)) { scope.launch { pager.animateScrollToPage((pager.currentPage - 1).coerceAtLeast(0)) } }
            CarouselArrow(false, Modifier.align(Alignment.CenterEnd)) { scope.launch { pager.animateScrollToPage((pager.currentPage + 1).coerceAtMost(all.lastIndex)) } }
        }
        Box(Modifier.clip(RoundedCornerShape(50)).background(Color(0x33FFFFFF)).border(1.dp, Color(0x44FFFFFF), RoundedCornerShape(50)).padding(horizontal = 18.dp, vertical = 8.dp)) {
            Txt("${cur.name}  -  ${cur.year}", size = 15f, weight = FontWeight.Bold, color = Color.White, maxLines = 1)
        }
        Txt("${pager.currentPage + 1} / ${all.size}   L1 / R1 or swipe", Modifier.padding(top = 4.dp), size = 11f, color = Color(0xFF7F8697))
    }
}

@Composable
private fun CarouselArrow(left: Boolean, modifier: Modifier, onClick: () -> Unit) {
    Box(modifier.padding(2.dp).size(38.dp).clip(CircleShape).background(Color(0x55000000)).clickable(onClick = onClick), contentAlignment = Alignment.Center) {
        GlyphIcon(Glyph.CHEVRON, Modifier.size(20.dp).graphicsLayer { rotationZ = if (left) 180f else 0f }, Color.White)
    }
}
