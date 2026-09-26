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
import androidx.compose.runtime.setValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.ipodemu.theme.Colorway
import com.ipodemu.theme.Family
import com.ipodemu.theme.Model
import com.ipodemu.theme.Themes


/**
 * Pick your iPod: every generation is one tap away, grouped like Apple's line-up (Classic 1-7, mini 1-2, nano 1-7,
 * touch 1-7). Every change applies immediately and the preview updates as you go.
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
            val preview = @Composable { Preview(model, cw, ui.viewMode, Modifier.fillMaxSize().padding(12.dp)) }
            val controls = @Composable { Controls(model, ci) }
            if (land) Row(Modifier.fillMaxSize()) {
                Box(Modifier.weight(0.42f).fillMaxHeight()) { preview() }
                Box(Modifier.weight(0.58f).fillMaxHeight()) { controls() }
            } else Column(Modifier.fillMaxSize()) {
                Box(Modifier.weight(0.36f).fillMaxWidth()) { preview() }
                Box(Modifier.weight(0.64f).fillMaxWidth()) { controls() }
            }
        }
    }
}

@Composable
private fun Preview(m: Model, cw: Colorway, viewMode: Int, modifier: Modifier) {
    BoxWithConstraints(modifier, contentAlignment = Alignment.Center) {
        val bh = minOf(maxHeight, maxWidth / m.aspect)
        val bw = bh * m.aspect
        Canvas(Modifier.size(bw, bh)) {
            drawIpodBody(m, cw)
            // a tiny mock of the player on the screen
            val w = size.width; val h = size.height
            val sx = m.screen.left * w; val sy = m.screen.top * h
            val sw = m.screen.width() * w; val sh = m.screen.height() * h
            val accent = Color(cw.bottom).let { c -> if (m.touch || viewMode < 2) Color(0xFF2F7BE8) else c }
            drawRoundRect(Brush.verticalGradient(listOf(accent.copy(alpha = .9f), Color(0xFF0B0C10)), sy, sy + sh), Offset(sx, sy), Size(sw, sh), CornerRadius(w * 0.01f))
            val art = sh * 0.42f
            drawRoundRect(Color(0x55FFFFFF), Offset(sx + sw / 2 - art / 2, sy + sh * 0.1f), Size(art, art), CornerRadius(art * .1f))
            drawRoundRect(Color(0xCCFFFFFF), Offset(sx + sw * 0.2f, sy + sh * 0.6f), Size(sw * 0.6f, sh * 0.05f), CornerRadius(4f))
            drawRoundRect(Color(0x88FFFFFF), Offset(sx + sw * 0.3f, sy + sh * 0.7f), Size(sw * 0.4f, sh * 0.04f), CornerRadius(4f))
            drawRoundRect(Color(0x44FFFFFF), Offset(sx + sw * 0.1f, sy + sh * 0.82f), Size(sw * 0.8f, sh * 0.03f), CornerRadius(4f))
            drawCircle(Color.White, sh * 0.055f, Offset(sx + sw * 0.5f, sy + sh * 0.92f))
            drawCircle(Color(0x66FFFFFF), sh * 0.04f, Offset(sx + sw * 0.34f, sy + sh * 0.92f))
            drawCircle(Color(0x66FFFFFF), sh * 0.04f, Offset(sx + sw * 0.66f, sy + sh * 0.92f))
        }
    }
}

/** The five looks: each maps onto a family of iPod models that share one visual language. */
private enum class Skin(val label: String, val sub: String, val model: String, val top: Long, val bottom: Long) {
    LCD("LCD", "1G-4G, mini", "ipod4", 0xFFD1D8C0, 0xFFBAC2A6),
    AQUA("Aqua", "Classic, video", "classic6", 0xFF7DB2F5, 0xFF1D4FA8),
    NANO("Nano", "Colourful nano", "nano3", 0xFFF08FB5, 0xFF7B3FA0),
    TOUCH6("Touch iOS 6", "Glossy touch", "touch4", 0xFF6C8FC2, 0xFF25334F),
    TOUCH7("Touch iOS 7+", "Flat touch", "touch6", 0xFFFFFFFF, 0xFFE3E6EC),
}

private fun skinOf(m: Model): Skin = when {
    m.family == Family.MONO -> Skin.LCD
    m.touch && m.year >= 2012 -> Skin.TOUCH7
    m.touch -> Skin.TOUCH6
    m.group == Themes.NANO -> Skin.NANO
    else -> Skin.AQUA
}

private val LABEL = Color(0xFF8B92A3)

@Composable
private fun Section(title: String, content: @Composable () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Txt(title, Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.Bold, color = LABEL)
        content()
    }
}

@Composable
private fun SkinCard(skin: Skin, selected: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val shape = RoundedCornerShape(14.dp)
    Column(
        modifier.clip(shape).background(if (selected) Color(0x332F7BE8) else Color(0x14FFFFFF))
            .border(if (focused) 2.5.dp else 1.dp, if (focused) Color.White else if (selected) Color(0xFF63A9FF) else Color(0x22FFFFFF), shape)
            .clickable(src, null, onClick = onClick).padding(8.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Box(Modifier.fillMaxWidth().height(26.dp).clip(RoundedCornerShape(8.dp)).background(Brush.verticalGradient(listOf(Color(skin.top), Color(skin.bottom)))))
        Txt(skin.label, size = 13f, weight = FontWeight.Bold, color = Color.White)
        Txt(skin.sub, size = 11f, color = Color(0xFF9AA1B1), maxLines = 1)
    }
}

/**
 * One screen for the whole look of the app (Player and click-wheel views alike): Look (skin), Mode (+ iPod body), Colour and Theme.
 * Every change applies immediately and the preview above updates. Picking an exact model is the Advanced section.
 */
@Composable
private fun Controls(model: Model, ci: Int) {
    val app = LocalApp.current
    val ui = app.ui
    ui.rev
    val skin = skinOf(model)
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val wheel = ui.viewMode >= 2
    val body = ui.viewMode == 1 || ui.viewMode == 3
    var advanced by remember { androidx.compose.runtime.mutableStateOf(false) }
    fun setMode(w: Boolean, b: Boolean) = ui.changeViewMode(if (w) (if (b) 3 else 2) else (if (b) 1 else 0))

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(bottom = 30.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Column(Modifier.padding(top = 6.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Txt("Appearance", size = 22f, weight = FontWeight.Bold, color = Color.White)
            Txt("${model.name} - ${model.year}", size = 13f, color = Color(0xFF63A9FF))
        }

        Section("Look") {
            Skin.values().toList().chunked(3).forEach { rowSkins ->
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    rowSkins.forEach { s ->
                        SkinCard(s, s == skin, Modifier.weight(1f)) {
                            if (s != skin) { ui.changeModel(s.model); ui.changeColorway(0) }
                        }
                    }
                    repeat(3 - rowSkins.size) { Box(Modifier.weight(1f)) }
                }
            }
        }

        Section("Mode") {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Chip("Player", !wheel) { setMode(false, body) }
                Chip("Click wheel", wheel, enabled = !model.touch) { setMode(true, body) }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Txt("Show iPod body", Modifier.weight(1f), size = 15f, weight = FontWeight.SemiBold, color = Color.White)
                Chip("Off", !body) { setMode(wheel, false) }
                Chip("On", body, ) { setMode(wheel, true) }
            }
            if (model.touch) Txt("Touch iPods have no click wheel.", size = 12f, color = Color(0xFF9AA1B1))
        }

        Section("Colour") {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Chip("Auto from artwork", ui.dynamicColor && skin != Skin.LCD, enabled = skin != Skin.LCD) { ui.changeDynamic(true) }
                Chip("Fixed", !ui.dynamicColor || skin == Skin.LCD) { ui.changeDynamic(false) }
            }
            if (skin == Skin.LCD) Txt("LCD skins keep their own colours.", size = 12f, color = Color(0xFF9AA1B1))
            if ((!ui.dynamicColor || skin == Skin.LCD) && model.colors.size > 1) {
                LazyRow(horizontalArrangement = Arrangement.spacedBy(12.dp), contentPadding = PaddingValues(vertical = 4.dp)) {
                    items(model.colors.indices.toList()) { i -> Swatch(model.colors[i], i == ci) { ui.changeColorway(i) } }
                }
                Txt(model.colors[ci].name, size = 13f, color = Color(0xFFC9CEDA))
            }
        }

        Section("Theme") {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                val p = app.prefs
                listOf("System", "Dark", "Light").forEachIndexed { i, n ->
                    val code = intArrayOf(2, 0, 1)[i]
                    Chip(n, p.appearance == code) { p.appearance = code; ui.refreshFromPrefs() }
                }
            }
        }

        Section("Display") {
            val p = app.prefs
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

        Chip(if (advanced) "Advanced: hide exact models" else "Advanced: choose the exact iPod", advanced) { advanced = !advanced }
        if (advanced) {
            Themes.groups.forEach { group ->
                Txt(if (group == Themes.CLASSIC) "iPod / classic" else "iPod $group", Modifier.padding(top = 4.dp), size = 13f, weight = FontWeight.Bold, color = LABEL)
                LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp), contentPadding = PaddingValues(vertical = 2.dp)) {
                    items(Themes.inGroup(group), key = { it.id }) { m -> Chip("${Themes.genLabel(m)}  ${m.year}", m.id == model.id) { ui.changeModel(m.id) } }
                }
            }
        }
        GlossPill("Done", { ui.pickerOpen = false }, Modifier.fillMaxWidth().padding(top = 10.dp), primary = true, height = 50.dp)
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

