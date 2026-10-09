package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
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
            drawCircle(Color.White, 11.dp.toPx(), Offset(size.width * value, y))
        }
    }
}

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
    val hz = ToneEngine.hzOf(pos)
    var active by remember { mutableStateOf("") }

    // the music stops while testing; the tones stop when this screen goes
    DisposableEffect(Unit) { app.player.pause(); onDispose { engine.stop() } }
    BackHandler { engine.stop(); ui.headphonesOpen = false }
    engine.level = level

    fun play(id: String, mode: Mode) { active = id; engine.start(mode) }
    @Composable fun Test(id: String, text: String, mode: Mode) = GlossPill(text, { if (active == id && engine.playing) { engine.stop(); active = "" } else play(id, mode) }, primary = active == id && engine.playing, height = 40.dp)
    @Composable fun Section(title: String, hint: String, content: @Composable () -> Unit) {
        Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Color(0x14FFFFFF)).padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Txt(title, size = 18f, weight = FontWeight.Bold, color = Color.White)
            Txt(hint, size = 13f, color = Color(0xB3FFFFFF), maxLines = 8)
            content()
        }
    }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(Modifier.widthIn(max = 620.dp).fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.BACK, "Back", { engine.stop(); ui.headphonesOpen = false }, tint = Color.White)
                Txt("Headphone test", Modifier.padding(start = 4.dp), size = 22f, weight = FontWeight.Bold, color = Color.White)
            }
            Section("Start quiet", "Test tones are much more tiring than music, and an amplifier makes it easy to go too loud. Put your amp low, keep this level low, start a test and raise the level slowly. Tap a test again, or Stop, to end it.") {
                val db = if (level <= 0f) "off" else "%.0f dB".format(20 * Math.log10((level * level * 0.35f).toDouble().coerceAtLeast(1e-4)))
                Txt("Test level: $db", size = 14f, color = Color.White)
                TestSlider(level, { level = it })
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                    GlossPill("Stop", { engine.stop(); active = "" }, height = 40.dp)
                    Txt(engine.status, size = 13f, color = Color(0xCCFFFFFF), maxLines = 3, modifier = Modifier.weight(1f))
                }
            }
            Section("Left and right", "Each side should be clear and equally loud, with nothing in the other ear. If it comes from the wrong side, the cable or channel setting is reversed.") {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Test("L", "Left only", Mode.Tone(1000.0, Wave.SINE, Chan.L))
                    Test("R", "Right only", Mode.Tone(1000.0, Wave.SINE, Chan.R))
                    Test("LR", "Both", Mode.Tone(1000.0, Wave.SINE, Chan.LR))
                    Test("alt", "Alternate", Mode.Alternate)
                }
            }
            Section("Frequency slider", "One steady tone: move the slider while it plays. Below about 40 Hz you feel more than hear; above about 16 kHz many adult ears fade out.") {
                Txt(if (hz >= 1000) "%.2f kHz".format(hz / 1000) else "${hz.roundToInt()} Hz", size = 34f, weight = FontWeight.Bold, color = Color.White)
                TestSlider(pos, { pos = it; if (active == "tone" && engine.playing) engine.retune(ToneEngine.hzOf(it)) })
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    for ((label, f) in listOf("40" to 40.0, "100" to 100.0, "440" to 440.0, "1k" to 1000.0, "4k" to 4000.0, "8k" to 8000.0, "12k" to 12000.0, "16k" to 16000.0))
                        GlossPill(label, { pos = ToneEngine.posOf(f); if (active == "tone" && engine.playing) engine.retune(f) }, height = 36.dp)
                }
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    for ((label, c) in listOf("Left" to Chan.L, "Both" to Chan.LR, "Right" to Chan.R)) GlossPill(label, { chan = c; if (active == "tone" && engine.playing) play("tone", Mode.Tone(hz, wave, chan)) }, primary = chan == c, height = 36.dp)
                    for ((label, w) in listOf("Sine" to Wave.SINE, "Triangle" to Wave.TRIANGLE, "Square" to Wave.SQUARE, "Saw" to Wave.SAW)) GlossPill(label, { wave = w; if (active == "tone" && engine.playing) play("tone", Mode.Tone(hz, wave, chan)) }, primary = wave == w, height = 36.dp)
                }
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Test("tone", "Play tone", Mode.Tone(hz, wave, chan))
                    Test("sweep", "Full sweep 20 Hz – 20 kHz", Mode.Sweep(20.0, 20000.0, 30.0, chan))
                    Test("sweepb", "Bass sweep", Mode.Sweep(20.0, 200.0, 14.0, chan))
                    Test("sweept", "Treble sweep", Mode.Sweep(2000.0, 20000.0, 16.0, chan))
                }
            }
            Section("Bass test", "Steps from the lowest notes up. A good set plays 40 Hz clearly and 30 Hz faintly, smooth and steady. The kick drum shows punch: tight and deep, then silent, not boomy.") {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Test("bsteps", "Bass steps 20 – 125 Hz", Mode.Steps(listOf(20.0, 25.0, 31.0, 40.0, 50.0, 63.0, 80.0, 100.0, 125.0), Chan.LR))
                    Test("kick", "Kick drum loop", Mode.Kick)
                }
            }
            Section("Treble and clarity", "High steps show how far up you hear. Clicks, hi-hat and the “sss” show detail: crisp and clean is good, dull is rolled off, painful or splashy is a harsh peak. Two close tones should wobble slowly if they are kept apart cleanly.") {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Test("tsteps", "Treble steps 4 – 20 kHz", Mode.Steps(listOf(4000.0, 6000.0, 8000.0, 10000.0, 12000.0, 14000.0, 16000.0, 18000.0, 20000.0), Chan.LR))
                    Test("clicks", "Clicks", Mode.Clicks)
                    Test("hat", "Hi-hat", Mode.HiHat)
                    Test("sss", "“sss” sibilance", Mode.Sibilance)
                    Test("c4", "Two close tones (4 Hz)", Mode.Close(4.0))
                    Test("c12", "(12 Hz)", Mode.Close(12.0))
                    Test("pink", "Pink noise", Mode.Noise(true, Chan.LR))
                    Test("white", "White noise", Mode.Noise(false, Chan.LR))
                }
            }
            Section("3D and stereo image", "The sound should move around you, not just between your ears. Front and behind are the hardest to tell apart. The phase test should sound centred in your head, then wide and hollow.") {
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
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Test("circle", "Circle around me", Mode.Spin("circle", 10.0))
                    Test("lr", "Left ↔ right in front", Mode.Spin("lr", 6.0))
                    Test("fb", "Front ↔ back", Mode.Spin("fb", 8.0))
                    Test("ud", "Low ↔ high", Mode.Spin("ud", 8.0))
                    Test("pin", "Phase: in phase", Mode.Phase(true))
                    Test("pout", "Phase: out of phase", Mode.Phase(false))
                }
            }
            Section("Using an amp with 250 Ω headphones", "High-impedance headphones (250 Ω, like the DT 990 Pro) need more voltage than a phone gives, so they sound quiet and thin without an amp. With the amp: start its gain low, play the 1 kHz tone, raise the amp until this test level is comfortable at about the loudness you like for music, then leave the amp there. Compare the bass and treble steps before and after.") { }
            Box(Modifier.height(24.dp))
        }
    }
}
