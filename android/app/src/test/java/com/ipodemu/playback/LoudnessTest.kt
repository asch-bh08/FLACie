package com.ipodemu.playback

import com.ipodemu.library.JellyfinFacts
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class LoudnessTest {
    @Test fun offOrUnknownChangesNothing() {
        assertEquals(1f, Loudness.factor(-6f, false), 0f); assertEquals(1f, Loudness.factor(Float.NaN, true), 0f); assertEquals(1f, Loudness.factor(Float.POSITIVE_INFINITY, true), 0f)
    }

    @Test fun decibelsBecomeAFactor() {
        assertEquals(1f, Loudness.factor(0f, true), 0.0001f)
        assertEquals(0.5012f, Loudness.factor(-6f, true), 0.001f)      // -6 dB is about half the amplitude
        assertEquals(1.9953f, Loudness.factor(6f, true), 0.001f)
    }

    @Test fun extremesAreClamped() {
        assertEquals(Loudness.factor(-15f, true), Loudness.factor(-40f, true), 0f)
        assertEquals(Loudness.factor(6f, true), Loudness.factor(20f, true), 0f)
    }

    @Test fun outputNeverPassesFullVolume() {
        assertEquals(1f, Loudness.output(0.9f, 1f, 2f), 0f)
        assertEquals(0.3f, Loudness.output(0.6f, 1f, 0.5f), 0.0001f)
        assertEquals(0f, Loudness.output(0.6f, 0f, 1f), 0f)       // the sleep fade at silence
        assertTrue(Loudness.output(0.5f, 0.5f, 1.5f) < 0.5f)
    }

    @Test fun jellyfinGainIsReadFromTheItem() {
        val item = JSONObject("""{"Id":"a","NormalizationGain":-7.25,"MediaSources":[{"Size":1000,"MediaStreams":[{"Type":"Audio","Codec":"flac","SampleRate":44100,"BitDepth":16,"BitRate":900000}]}]}""")
        val p = JellyfinFacts.parse(item)
        assertEquals(-7.25f, p.fact!!.gainDb, 0.001f)
        val none = JellyfinFacts.parse(JSONObject("""{"Id":"b","MediaSources":[{"Size":1,"MediaStreams":[{"Type":"Audio","Codec":"mp3","SampleRate":44100,"BitRate":320000}]}]}"""))
        assertTrue(none.fact!!.gainDb.isNaN())
    }
}
