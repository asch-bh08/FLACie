package com.ipodemu.player

import androidx.compose.ui.unit.dp
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The kit's pure rules: the italic last word, the tags' tones, the fit step and the toast timings. */
class KitTest {
    @Test fun titlePutsTheLastWordInItalics() {
        assertEquals("Your " to "devices", KitText.titleParts("Your devices"))
        assertEquals("" to "Settings", KitText.titleParts("Settings"))
    }

    @Test fun aNumberAsATitleHasNoItalics() = assertEquals("1,204" to "", KitText.titleParts("1,204"))

    @Test fun statusWordsPickTheirTones() {
        assertEquals(StatusTone.ACCENT, KitTags.toneFor("This device"))
        assertEquals(StatusTone.ON, KitTags.toneFor("Playing"))
        assertEquals(StatusTone.ON, KitTags.toneFor("Recently downloaded"))
        assertEquals(StatusTone.OFF, KitTags.toneFor("Offline"))
        assertEquals(StatusTone.CLOUD, KitTags.toneFor("Streaming"))
        assertEquals(StatusTone.HIRES, KitTags.toneFor("✦ Hi-Res"))
        assertEquals(StatusTone.PLAIN, KitTags.toneFor("Phone"))
    }

    @Test fun toneInksAndRims() {
        val accent = 0xFF123456.toInt()
        assertEquals(accent, KitTags.statusInk(StatusTone.ACCENT, accent))
        assertEquals(0xFFFFC857.toInt(), KitTags.statusInk(StatusTone.HIRES, accent))
        assertEquals(KitTags.statusInk(StatusTone.PLAIN, accent), KitTags.statusInk(StatusTone.OFF, accent))
        assertTrue(KitTags.rimAlpha(StatusTone.ON) > KitTags.rimAlpha(StatusTone.PLAIN))
    }

    @Test fun formatTagIsAmberOnlyForHiRes() {
        assertTrue(KitTags.formatIsHiRes("Hi-Res 24/96"))
        assertTrue(KitTags.formatIsHiRes("HI-RES"))
        assertFalse(KitTags.formatIsHiRes("FLAC 16/44.1"))
        assertFalse(KitTags.formatIsHiRes("JELLYFIN"))
    }

    @Test fun bigUnlessTheScreenIsTooSmall() {
        // every upright phone in the plan's size list stays big
        for ((w, h) in listOf(360f to 640f, 375f to 667f, 390f to 844f, 412f to 915f, 412f to 1000f)) assertFalse("$w x $h", KitFit.tight(w, h))
        assertTrue("a landscape phone", KitFit.tight(844f, 390f))
        assertTrue("a very narrow window", KitFit.tight(320f, 700f))
    }

    @Test fun sizesStepDownButNeverBelowTheTouchFloor() {
        val big = Kit.sizes(false); val tight = Kit.sizes(true)
        assertEquals(56.dp, big.main); assertEquals(52.dp, tight.main)
        assertEquals(44.dp, big.small); assertEquals(40.dp, tight.small)
        assertEquals(60.dp, big.slot); assertEquals(52.dp, tight.slot)
        assertEquals(52.dp, big.icon); assertEquals(48.dp, tight.icon)
        assertEquals(72.dp, big.row); assertEquals(64.dp, tight.row)
        assertEquals(52.dp, big.cover)
        assertTrue(tight.icon >= Kit.touch && tight.main >= Kit.touch && tight.slot >= Kit.touch)
    }

    @Test fun toastTimings() {
        assertEquals(2_500L, Kit.toastMs(false))
        assertEquals(5_000L, Kit.toastMs(true))
    }
}
