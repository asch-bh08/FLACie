package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.playback.ToneEngine
import com.ipodemu.playback.ToneEngine.Chan
import com.ipodemu.playback.ToneEngine.Mode
import com.ipodemu.playback.ToneEngine.Wave
import kotlin.math.roundToInt

/** A plain drag slider (0..1): this app does not pull in a Material slider. */
@Composable
private fun TestSlider(value: Float, onChange: (Float) -> Unit, modifier: Modifier = Modifier) {
    val sc = LocalScheme.current
    var width by remember { mutableFloatStateOf(1f) }
    Box(
        modifier.fillMaxWidth().height(44.dp)
            .pointerInput(Unit) { detectTapGestures { o -> onChange((o.x / width).coerceIn(0f, 1f)) } }
            .pointerInput(Unit) { detectDragGestures { change, _ -> change.consume(); onChange((change.position.x / width).coerceIn(0f, 1f)) } },
        contentAlignment = Alignment.CenterStart,
    ) {
        Canvas(Modifier.fillMaxWidth().height(44.dp)) {
            width = size.width
            val y = size.height / 2
            drawLine(Color(0x33FFFFFF), Offset(0f, y), Offset(size.width, y), strokeWidth = 6.dp.toPx())
            drawLine(sc.accent, Offset(0f, y), Offset(size.width * value, y), strokeWidth = 6.dp.toPx())
            drawCircle(Color.White, 12.dp.toPx(), Offset((size.width * value).coerceIn(12.dp.toPx(), size.width - 12.dp.toPx()), y))
        }
    }
}

private val TABS = listOf("lr" to "1 · Left / right", "tone" to "2 · Tone generator", "bass" to "3 · Bass", "treble" to "4 · Treble & clarity", "space" to "5 · 3D & phase", "amp" to "Amp tips")

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun HeadphoneTestScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val sc = LocalScheme.current
    val engine = remember { ToneEngine() }
    var level by remember { mutableFloatStateOf(0.12f) }
    var chan by remember { mutableStateOf(Chan.LR) }
    var wave by remember { mutableStateOf(Wave.SINE) }
    var pos by remember { mutableFloatStateOf(ToneEngine.posOf(1000.0)) }
    var tab by remember { mutableStateOf("lr") }
    var active by remember { mutableStateOf("") }
    val hz = ToneEngine.hzOf(pos)

    // the music stops while testing; the tones stop when this screen goes
    DisposableEffect(Unit) { app.player.pause(); onDispose { engine.stop() } }
    BackHandler { engine.stop(); ui.headphonesOpen = false }
    engine.level = level
    val running = engine.playing

    fun play(id: String, mode: Mode) { active = id; engine.start(mode) }
    fun toggle(id: String, mode: Mode) { if (active == id && engine.playing) { engine.stop(); active = "" } else play(id, mode) }
    fun setHz(f: Double) { pos = ToneEngine.posOf(f); if (active == "tone" && engine.playing) engine.retune(ToneEngine.hzOf(pos)) }

    /** One test: a round play button (a square while it plays), what it is, and what to listen for. */
    @Composable fun Test(id: String, title: String, listen: String, mode: Mode) {
        val on = active == id && running
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(if (on) sc.accent.copy(alpha = 0.16f) else Color.Transparent).clickable { toggle(id, mode) }.padding(horizontal = 8.dp, vertical = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(Modifier.size(46.dp).clip(CircleShape).background(if (on) sc.accent else Color(0x22FFFFFF)), contentAlignment = Alignment.Center) {
                Txt(if (on) "■" else "▶", size = 17f, color = Color.White)
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Txt(title, size = 16f, weight = FontWeight.SemiBold, color = Color.White, maxLines = 2)
                Txt(listen, size = 13f, color = Color(0xB3FFFFFF), maxLines = 5)
            }
        }
    }
    @Composable fun Card(content: @Composable () -> Unit) = Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Color(0x14FFFFFF)).padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) { content() }
    @Composable fun Hint(t: String) = Txt(t, Modifier.padding(horizontal = 4.dp, vertical = 4.dp), size = 13f, color = Color(0xB3FFFFFF), maxLines = 8)

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(Modifier.widthIn(max = 620.dp).fillMaxSize().padding(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.BACK, "Back", { engine.stop(); ui.headphonesOpen = false }, tint = Color.White)
                Txt("Headphone test", Modifier.padding(start = 4.dp), size = 22f, weight = FontWeight.Bold, color = Color.White)
            }
            // always visible: the level, Stop, and what is playing
            Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Color(0x26FFFFFF)).padding(horizontal = 14.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                val db = if (level <= 0f) "off" else "%.0f dB".format(20 * Math.log10((level * level * 0.35f).toDouble().coerceAtLeast(1e-4)))
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Txt("Test level  $db", size = 14f, weight = FontWeight.SemiBold, color = Color.White)
                    Box(Modifier.weight(1f)) { TestSlider(level, { level = it }) }
                    GlossPill("■ Stop", { engine.stop(); active = "" }, height = 38.dp)
                }
                Txt(if (engine.playing) engine.status else "Ready: pick a test. Start with the level low.", size = 13f, color = Color.White, maxLines = 2)
            }
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                for ((id, name) in TABS) GlossPill(name, { tab = id }, primary = tab == id, height = 38.dp)
            }
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                when (tab) {
                    "lr" -> Card {
                        Hint("Checks each side on its own. Each should be clear, equally loud, and silent in the other ear. If it comes from the wrong side, the cable or channel setting is swapped.")
                        Test("L", "Left ear only", "A steady tone in the left ear, nothing in the right.", Mode.Tone(1000.0, Wave.SINE, Chan.L))
                        Test("R", "Right ear only", "A steady tone in the right ear, nothing in the left.", Mode.Tone(1000.0, Wave.SINE, Chan.R))
                        Test("LR", "Both ears", "The tone should sit in the middle of your head, not to one side.", Mode.Tone(1000.0, Wave.SINE, Chan.LR))
                        Test("alt", "Alternate left / right", "Jumps between ears every second at the same loudness: a quick way to hear a volume imbalance.", Mode.Alternate)
                    }
                    "tone" -> Card {
                        Hint("One steady tone you control. Move the slider while it plays. 1 Hz to 44 kHz, spread evenly by pitch.")
                        Txt(if (hz >= 1000) "%.2f kHz".format(hz / 1000).replace(Regex("\\.?0+ kHz"), " kHz") else if (hz < 100) "%.1f Hz".format(hz).replace(".0 Hz", " Hz") else "${hz.roundToInt()} Hz", Modifier.padding(horizontal = 4.dp), size = 36f, weight = FontWeight.Bold, color = Color.White)
                        Txt(ToneEngine.zoneOf(hz), Modifier.padding(horizontal = 4.dp), size = 13f, color = Color(0xB3FFFFFF), maxLines = 3)
                        TestSlider(pos, { pos = it; if (active == "tone" && engine.playing) engine.retune(ToneEngine.hzOf(it)) })
                        Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                            for (l in listOf("1 Hz", "20", "100", "1 k", "10 k", "44 kHz")) Txt(l, size = 11f, color = Color(0x99FFFFFF))
                        }
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            GlossPill("−1 Hz", { setHz(hz - 1) }, height = 36.dp); GlossPill("+1 Hz", { setHz(hz + 1) }, height = 36.dp)
                            GlossPill("½× lower", { setHz(hz / 2) }, height = 36.dp); GlossPill("2× higher", { setHz(hz * 2) }, height = 36.dp)
                        }
                        Txt("Jump to (Hz)", Modifier.padding(horizontal = 4.dp, vertical = 2.dp), size = 12f, color = Color(0x99FFFFFF))
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            for ((label, f) in listOf("5" to 5.0, "20" to 20.0, "40" to 40.0, "100" to 100.0, "440" to 440.0, "1k" to 1000.0, "4k" to 4000.0, "8k" to 8000.0, "12k" to 12000.0, "16k" to 16000.0, "20k" to 20000.0, "30k" to 30000.0))
                                GlossPill(label, { setHz(f) }, height = 36.dp)
                        }
                        Txt("Ear and shape", Modifier.padding(horizontal = 4.dp, vertical = 2.dp), size = 12f, color = Color(0x99FFFFFF))
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            for ((label, c) in listOf("Left" to Chan.L, "Both" to Chan.LR, "Right" to Chan.R)) GlossPill(label, { chan = c; if (active == "tone" && engine.playing) play("tone", Mode.Tone(hz, wave, chan)) }, primary = chan == c, height = 36.dp)
                            for ((label, w) in listOf("Sine" to Wave.SINE, "Triangle" to Wave.TRIANGLE, "Square" to Wave.SQUARE, "Saw" to Wave.SAW)) GlossPill(label, { wave = w; if (active == "tone" && engine.playing) play("tone", Mode.Tone(hz, wave, chan)) }, primary = wave == w, height = 36.dp)
                        }
                        Test("tone", "Play this tone", "Plays the frequency above until you stop it.", Mode.Tone(hz, wave, chan))
                        Test("sweep", "Full sweep, 20 Hz → 20 kHz (30 s)", "Glides up through everything you can hear. Note where it appears, where it gets quiet, and where it vanishes. Listen for buzzing or dips.", Mode.Sweep(20.0, 20000.0, 30.0, chan))
                        Test("sweepd", "Deep bass sweep, 1 → 100 Hz (25 s)", "Starts below hearing. You will feel it before you hear it; note where the tone first becomes audible.", Mode.Sweep(1.0, 100.0, 25.0, chan))
                        Test("sweept", "Treble sweep, 2 kHz → 44 kHz (22 s)", "Note where it stops being audible (for most adults somewhere 14–20 kHz). The rest is above hearing and only shows what your device can output.", Mode.Sweep(2000.0, 44000.0, 22.0, chan))
                    }
                    "bass" -> Card {
                        Hint("How low and how clean the bass goes. A good pair plays 40 Hz clearly and 30 Hz faintly, steady and not rattling. Below 20 Hz is mostly felt, if at all.")
                        Test("bsteps", "Bass steps, 5 → 125 Hz", "Twelve notes, two seconds each, climbing. The screen shows each one. Note the lowest one you hear.", Mode.Steps(listOf(5.0, 10.0, 15.0, 20.0, 25.0, 31.0, 40.0, 50.0, 63.0, 80.0, 100.0, 125.0), Chan.LR))
                        Test("kick", "Kick drum loop", "Should thump deep, then stop cleanly. Boomy or lingering means loose bass; a thin click with no weight means weak bass.", Mode.Kick)
                        Test("bsweep", "Bass sweep, 1 → 200 Hz (30 s)", "Watch the Hz on screen and note where it first becomes audible and whether the volume stays even or has dips and bumps.", Mode.Sweep(1.0, 200.0, 30.0, Chan.LR))
                        Test("pink", "Pink noise", "Balanced across the range. It should sound smooth, like steady rain, with no part sticking out.", Mode.Noise(true, Chan.LR))
                    }
                    "treble" -> Card {
                        Hint("Top-end detail. Crisp and clean is good, dull means rolled-off treble, and painful or splashy means a harsh peak.")
                        Test("tsteps", "Treble steps, 4 → 22 kHz", "Ten notes, two seconds each. The last ones are very high: note the highest you hear.", Mode.Steps(listOf(4000.0, 6000.0, 8000.0, 10000.0, 12000.0, 14000.0, 16000.0, 18000.0, 20000.0, 22000.0), Chan.LR))
                        Test("clicks", "Clicks", "Each should be one crisp tick. Ringing or a smeared tail means poor transient response.", Mode.Clicks)
                        Test("hat", "Hi-hat", "A short tick and a longer open hat, each with a clear edge, not a hiss.", Mode.HiHat)
                        Test("sss", "“sss” sibilance", "Bright and clear is good. Piercing or painful means a harsh peak around 6–9 kHz.", Mode.Sibilance)
                        Test("c4", "Two close tones (4 Hz apart)", "1000 Hz and 1004 Hz together: a slow wobble, four beats a second.", Mode.Close(4.0))
                        Test("c12", "Two close tones (12 Hz apart)", "A faster flutter. If it is smooth in both, the tones are being kept apart cleanly.", Mode.Close(12.0))
                        Test("white", "White noise", "Bright hiss. Gives a quick feel for how much high-frequency energy there is.", Mode.Noise(false, Chan.LR))
                    }
                    "space" -> Card {
                        Hint("With headphones on, a sound should move around you, not just between your ears. Front and behind are the hardest to tell apart and depend a lot on your own ears.")
                        Box(Modifier.size(150.dp).align(Alignment.CenterHorizontally), contentAlignment = Alignment.Center) {
                            val spot = engine.spot
                            Canvas(Modifier.fillMaxSize()) {
                                val c = Offset(size.width / 2, size.height / 2); val r = size.width * 0.37f
                                drawCircle(Color(0x22FFFFFF), r, c, style = Stroke(1.dp.toPx())); drawCircle(Color(0x33FFFFFF), size.width * 0.13f, c)
                                if (spot != null) drawCircle(sc.accent, 7.dp.toPx(), Offset(c.x + spot.first * r, c.y - spot.second * r))
                            }
                            Txt("front", Modifier.align(Alignment.TopCenter), size = 11f, color = Color(0x88FFFFFF))
                            Txt("behind", Modifier.align(Alignment.BottomCenter), size = 11f, color = Color(0x88FFFFFF))
                        }
                        Test("circle", "Circle around me", "Goes round your head clockwise from the front. The dot shows where.", Mode.Spin("circle", 10.0))
                        Test("lr", "Left ↔ right in front", "Sweeps across the front. It should stay out in front, not drop inside your head.", Mode.Spin("lr", 6.0))
                        Test("fb", "Front ↔ back", "From in front, over the top, to behind you and back.", Mode.Spin("fb", 8.0))
                        Test("ud", "Low ↔ high", "Rises and falls in front of you. The hardest one; many people only hear it get brighter.", Mode.Spin("ud", 8.0))
                        Test("pin", "Phase: in phase", "Same noise in both ears. It should sit in the centre of your head.", Mode.Phase(true))
                        Test("pout", "Phase: out of phase", "One ear flipped. It should sound wide and hollow, outside the centre. If both sound the same, check the wiring or any “mono” setting.", Mode.Phase(false))
                    }
                    else -> Card {
                        Txt("Using an amp with 250 Ω headphones", Modifier.padding(4.dp), size = 18f, weight = FontWeight.Bold, color = Color.White)
                        Hint("High-impedance headphones (250 Ω, like the DT 990 Pro) need more voltage than a phone or laptop gives, so they sound quiet and thin without an amp.")
                        Hint("1. Set the amp's gain low, play “Both ears” on tab 1, and raise the amp until the test level here is comfortable, at about the loudness you like for music.")
                        Hint("2. Leave the amp there and use the volume in the app from now on.")
                        Hint("3. Run the bass steps and treble steps before and after the amp to hear what it changed.")
                        Hint("4. Tones below 20 Hz are kept quieter on purpose: they move the driver a long way, so do not turn them up loud.")
                    }
                }
                Box(Modifier.height(24.dp))
            }
        }
    }
}
