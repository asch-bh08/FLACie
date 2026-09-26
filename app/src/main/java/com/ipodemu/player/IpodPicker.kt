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

private val VIEWS = listOf(
    Triple(0, "Player", "The modern music player, styled like this iPod"),
    Triple(1, "Player in an iPod", "The same player inside a physical iPod"),
    Triple(2, "Click wheel", "The classic wheel interface, full screen"),
    Triple(3, "Click wheel in an iPod", "The wheel interface inside a physical iPod"),
)

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

@Composable
private fun Controls(model: Model, ci: Int) {
    val app = LocalApp.current
    val ui = app.ui
    ui.rev
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(bottom = 30.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Column(Modifier.padding(top = 6.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Txt(model.name, size = 24f, weight = FontWeight.Bold, color = Color.White)
            Txt("${model.year}", size = 14f, weight = FontWeight.SemiBold, color = Color(0xFF63A9FF))
            Txt(model.blurb, size = 14f, color = Color(0xFFA3A9B6), maxLines = 2)
        }

        // ---- which iPod ----
        Themes.groups.forEach { group ->
            val ms = Themes.inGroup(group)
            Txt(if (group == Themes.CLASSIC) "iPod / classic" else "iPod $group", Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.Bold, color = Color(0xFF8B92A3))
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp), contentPadding = PaddingValues(vertical = 2.dp)) {
                items(ms, key = { it.id }) { m ->
                    Chip("${Themes.genLabel(m)}  ${m.year}", m.id == model.id) {
                        ui.changeModel(m.id)
                        if (m.touch && ui.viewMode >= 2) ui.changeViewMode(0)
                    }
                }
            }
        }

        // ---- colour ----
        Txt("Color", Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.Bold, color = Color(0xFF8B92A3))
        LazyRow(horizontalArrangement = Arrangement.spacedBy(12.dp), contentPadding = PaddingValues(vertical = 4.dp)) {
            items(model.colors.indices.toList()) { i ->
                val col = model.colors[i]
                Swatch(col, i == ci) { ui.changeColorway(i) }
            }
        }
        Txt(model.colors[ci].name, size = 13f, color = Color(0xFFC9CEDA))

        // ---- view ----
        Txt("Show it as", Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.Bold, color = Color(0xFF8B92A3))
        VIEWS.forEach { (mode, title, sub) ->
            val ok = mode < 2 || !model.touch
            ViewRow(title, sub, ui.viewMode == mode, ok) { if (ok) ui.changeViewMode(mode) }
        }

        // ---- colours ----
        Txt("Colors", Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.Bold, color = Color(0xFF8B92A3))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Chip("Fade with artwork", ui.dynamicColor) { ui.changeDynamic(true) }
            Chip("Model colors only", !ui.dynamicColor) { ui.changeDynamic(false) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val p = app.prefs
            listOf("Dark", "Light", "System").forEachIndexed { i, n -> Chip(n, p.appearance == i) { p.appearance = i; ui.refreshFromPrefs() } }
        }
        GlossPill("Done", { ui.pickerOpen = false }, Modifier.fillMaxWidth().padding(top = 10.dp), primary = true, height = 50.dp)
    }
}

@Composable
private fun Chip(text: String, selected: Boolean, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val shape = RoundedCornerShape(50)
    Box(
        Modifier.clip(shape)
            .background(if (selected) Brush.verticalGradient(listOf(Color(0xFF5DA6F5), Color(0xFF1D6BDB))) else Brush.verticalGradient(listOf(Color(0x2EFFFFFF), Color(0x14FFFFFF))))
            .border(if (focused) 2.5.dp else 1.dp, if (focused) Color.White else if (selected) Color(0xFF8CC0FA) else Color(0x33FFFFFF), shape)
            .clickable(src, null, onClick = onClick).padding(horizontal = 16.dp, vertical = 10.dp),
        contentAlignment = Alignment.Center,
    ) { Txt(text, size = 14f, weight = FontWeight.SemiBold, color = if (selected) Color.White else Color(0xFFD5D9E2)) }
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
private fun ViewRow(title: String, sub: String, selected: Boolean, enabled: Boolean, onClick: () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val shape = RoundedCornerShape(14.dp)
    Row(
        Modifier.fillMaxWidth().clip(shape)
            .background(if (selected) Color(0x332F7BE8) else Color(0x14FFFFFF))
            .border(if (focused) 2.5.dp else 1.dp, if (focused) Color.White else if (selected) Color(0xFF63A9FF) else Color(0x22FFFFFF), shape)
            .clickable(src, null, onClick = onClick).padding(horizontal = 14.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.size(22.dp).clip(CircleShape).border(2.dp, if (selected) Color(0xFF63A9FF) else Color(0x66FFFFFF), CircleShape), contentAlignment = Alignment.Center) {
            if (selected) Box(Modifier.size(11.dp).clip(CircleShape).background(Color(0xFF63A9FF)))
        }
        Column(Modifier.weight(1f)) {
            Txt(title, size = 15f, weight = FontWeight.SemiBold, color = if (enabled) Color.White else Color(0x66FFFFFF))
            Txt(if (enabled) sub else "Not available - this iPod has no wheel", size = 12f, color = Color(0xFF9AA1B1))
        }
    }
}
