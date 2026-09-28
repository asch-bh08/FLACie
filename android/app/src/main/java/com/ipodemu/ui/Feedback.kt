package com.ipodemu.ui

import android.content.Context
import android.media.AudioAttributes
import android.media.SoundPool
import android.os.SystemClock
import android.view.HapticFeedbackConstants
import android.view.View
import com.ipodemu.Prefs
import java.io.ByteArrayOutputStream
import java.io.File
import kotlin.math.PI
import kotlin.math.exp
import kotlin.math.sin

/** The wheel's tick: a synthesized click sample plus a haptic pulse. Both optional in Settings. */
class Feedback(ctx: Context, private val prefs: Prefs, private val view: View) {
    private val pool = SoundPool.Builder().setMaxStreams(4)
        .setAudioAttributes(
            AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ASSISTANCE_SONIFICATION)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build()
        ).build()
    private var sound = 0
    private var loaded = false
    private var lastMs = 0L

    init {
        try {
            val f = File(ctx.cacheDir, "click.wav")
            if (!f.exists()) f.writeBytes(clickWav())
            pool.setOnLoadCompleteListener { _, _, status -> loaded = status == 0 }
            sound = pool.load(f.path, 1)
        } catch (_: Exception) {}
    }

    /** One wheel detent. Rate-limited so a fast spin stays a texture, not a buzz. */
    fun tick() {
        val now = SystemClock.uptimeMillis()
        if (now - lastMs < 22) return
        lastMs = now
        if (prefs.clickSound && loaded) pool.play(sound, TICK_VOL[prefs.clickVolume.coerceIn(0, 2)], TICK_VOL[prefs.clickVolume.coerceIn(0, 2)], 1, 0, 1f)
        if (prefs.haptics) view.performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
    }

    /** Button press (select/back/transport). */
    fun press() {
        if (prefs.clickSound && loaded) pool.play(sound, PRESS_VOL[prefs.clickVolume.coerceIn(0, 2)], PRESS_VOL[prefs.clickVolume.coerceIn(0, 2)], 1, 0, 0.8f)
        if (prefs.haptics) view.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY)
    }

    fun release() = pool.release()

    companion object {
        private val TICK_VOL = floatArrayOf(0.15f, 0.32f, 0.6f)
        private val PRESS_VOL = floatArrayOf(0.3f, 0.6f, 0.95f)
    }

    private fun clickWav(): ByteArray {
        val rate = 22050
        val n = rate / 60
        val pcm = ByteArray(n * 2)
        for (i in 0 until n) {
            val t = i.toDouble() / rate
            val env = exp(-t / 0.0011)
            val v = (sin(2 * PI * 2600 * t) * 0.7 + (Math.random() - 0.5) * 0.5) * env
            val s = (v * 22000).toInt().coerceIn(-32768, 32767)
            pcm[i * 2] = (s and 0xFF).toByte(); pcm[i * 2 + 1] = (s shr 8 and 0xFF).toByte()
        }
        val out = ByteArrayOutputStream()
        fun i32(v: Int) { out.write(v and 0xFF); out.write(v shr 8 and 0xFF); out.write(v shr 16 and 0xFF); out.write(v shr 24 and 0xFF) }
        fun i16(v: Int) { out.write(v and 0xFF); out.write(v shr 8 and 0xFF) }
        out.write("RIFF".toByteArray()); i32(36 + pcm.size); out.write("WAVEfmt ".toByteArray())
        i32(16); i16(1); i16(1); i32(rate); i32(rate * 2); i16(2); i16(16)
        out.write("data".toByteArray()); i32(pcm.size); out.write(pcm)
        return out.toByteArray()
    }
}
