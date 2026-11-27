package com.ipodemu.player

import com.ipodemu.player.NowPlayingFit.Actions
import com.ipodemu.player.NowPlayingFit.Layout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Now Playing fits every screen with nothing to scroll (owner, 10 Nov: his dad had to scroll down to the controls and the graphs). */
class NowPlayingFitTest {
    private val scales = listOf(1f, 1.3f, 1.5f)

    private fun assertFits(w: Float, h: Float, layout: Layout, top: Float = 24f, bottom: Float = 24f) {
        for (fs in scales) {
            val p = NowPlayingFit.plan(w, h, fs, top, bottom)
            val at = "${w.toInt()}x${h.toInt()} at $fs: $p"
            assertEquals(at, layout, p.layout)
            assertTrue(at, p.fits)
            // the controls, the time and the frame are all inside the height
            assertTrue(at, p.fixed + (if (layout == Layout.SIDE) 0f else p.side) <= h + .5f)
            assertTrue(at, p.cover >= 120f)
        }
    }

    @Test fun smallPhonesFitAtEveryFontScale() {
        assertFits(360f, 640f, Layout.STACKED)
        assertFits(320f, 568f, Layout.STACKED)
        // three-button navigation takes 48 dp at the bottom
        assertFits(320f, 568f, Layout.STACKED, bottom = 48f)
        assertFits(360f, 640f, Layout.STACKED, top = 32f, bottom = 48f)
    }

    @Test fun ordinaryPhonesFit() {
        assertFits(360f, 800f, Layout.STACKED)
        assertFits(393f, 851f, Layout.STACKED)
        assertFits(412f, 915f, Layout.STACKED)
    }

    @Test fun phonesOnTheirSideFitWithTheCoverBesideTheControls() {
        assertFits(568f, 320f, Layout.SIDE, bottom = 0f)
        assertFits(640f, 360f, Layout.SIDE, bottom = 0f)
        assertFits(800f, 360f, Layout.SIDE, bottom = 24f)
        assertFits(915f, 412f, Layout.SIDE, bottom = 0f)
    }

    @Test fun tabletsAndTheFoldFit() {
        assertFits(800f, 1280f, Layout.WIDE)
        assertFits(1280f, 800f, Layout.WIDE)
        // the Fold unfolded (about 673 x 841 dp) both ways, and its narrow outer screen
        assertFits(673f, 841f, Layout.WIDE)
        assertFits(841f, 673f, Layout.WIDE)
        assertFits(344f, 882f, Layout.STACKED)
    }

    @Test fun aRoomyPhoneKeepsEverything() {
        val p = NowPlayingFit.plan(412f, 915f, 1f)
        assertFalse(p.compactBand)
        assertEquals(52f, p.titleSp, 0f); assertEquals(2, p.titleLines)
        assertTrue(p.chips); assertTrue(p.tip); assertEquals(Actions.FULL, p.actions)
        assertTrue(p.cover >= NowPlayingFit.MIN_COVER)
    }

    private fun secondaryHidden(p: NowPlayingFit.Plan) = !p.tip || p.titleSp < 52f || p.titleLines < 2 || !p.chips || p.actions != Actions.FULL

    @Test fun theCoverShrinksFirstThenTheGraphThenTheRestGoesBehindMore() {
        for (w in listOf(320f, 360f, 412f)) for (fs in scales) {
            var h = 1000f
            while (h >= 560f) {
                val p = NowPlayingFit.plan(w, h, fs)
                val at = "${w.toInt()}x${h.toInt()} at $fs: $p"
                // a full-height band only while the cover is still at least its smallest full size
                if (!p.compactBand) assertTrue(at, p.cover >= NowPlayingFit.MIN_COVER)
                // the band compacts only once the cover could not shrink further with it full height
                if (p.compactBand && !secondaryHidden(p)) assertTrue(at, p.side - 2 * NowPlayingFit.normalBand(p.side) < NowPlayingFit.MIN_COVER)
                // nothing is put away while the graph still has its full band
                if (secondaryHidden(p)) assertTrue(at, p.compactBand)
                h -= 8f
            }
        }
    }

    @Test fun lessRoomNeverGivesABiggerTitleOrMoreButtons() {
        for (fs in scales) {
            var prev = NowPlayingFit.plan(360f, 1000f, fs)
            var h = 992f
            while (h >= 560f) {
                val p = NowPlayingFit.plan(360f, h, fs)
                assertTrue("$h at $fs", p.titleSp <= prev.titleSp)
                assertTrue("$h at $fs", p.actions.ordinal >= prev.actions.ordinal)
                prev = p; h -= 8f
            }
        }
    }

    @Test fun theCompactGraphKeepsRoomForItsBars() {
        // bars as long as the band less the gap and a 4 dp margin: still at least 6 dp, and the same 3 dp bars at the same spacing
        assertTrue(NowPlayingFit.COMPACT_BAND - NowPlayingFit.gap(true) - 4f >= 6f)
        assertTrue(NowPlayingFit.COMPACT_BAND < NowPlayingFit.normalBand(200f))
        assertEquals(7f, NowPlayingFit.gap(false), 0f)
    }

    @Test fun theBandIsWhatTheFrameAlwaysDrew() {
        assertEquals(22f, NowPlayingFit.normalBand(260f), 0f)
        assertEquals(28f, NowPlayingFit.normalBand(360f), 0f)
        assertEquals(36f, NowPlayingFit.normalBand(480f), 0f)
    }

    @Test fun moreOnlyIsTheLastStep() {
        val p = NowPlayingFit.plan(320f, 568f, 2f)
        assertEquals(Actions.MORE_ONLY, p.actions)
        assertTrue(p.fits)
        // no row at all: More is beside the heart
        assertEquals(0f, p.actionsHeight, 0f)
    }

    @Test fun aSplitScreenSliverGetsTheMostCompactPlan() {
        val p = NowPlayingFit.plan(360f, 400f, 1f)
        assertEquals(Actions.MORE_ONLY, p.actions)
        assertFalse(p.fits)
    }

    @Test fun theInfoGraphIsUnchangedWhenThereIsRoom() {
        assertEquals(204f, NowPlayingFit.infoGraphHeight(600f), .01f)
        assertEquals(230f, NowPlayingFit.infoGraphHeight(900f), .01f)
        assertEquals(280f, NowPlayingFit.infoGraphHeight(900f, hiRes = true), .01f)
        assertEquals(150f, NowPlayingFit.infoGraphHeight(400f), .01f)
    }

    @Test fun theInfoGraphCompactsInAShortPanel() {
        for (panel in listOf(240f, 260f, 280f)) for (fs in scales) {
            val g = NowPlayingFit.infoGraphHeight(panel, fs)
            assertTrue("$panel at $fs: $g", g < 150f)
            assertTrue("$panel at $fs: $g", g >= 96f)
        }
        // the tabs, the name and the graph together in a 280 dp panel
        val g = NowPlayingFit.infoGraphHeight(280f)
        assertTrue(g + 28f + 48f + 10f + 44f + 10f <= 280f + .5f)
    }

    // ---- the web phone's sizes (living-sleeve.css), and the remote that uses the same player ----

    @Test fun theTransportIsBigAndWholeOnEveryPhone() {
        // shuffle and repeat are beside the time, so previous / play / next have the row: big, play the biggest, at full size from 360 dp up
        assertTrue(NowPlayingFit.PLAY_W > NowPlayingFit.PILL_W && NowPlayingFit.PLAY_H > NowPlayingFit.PILL_H)
        assertTrue(NowPlayingFit.PLAY_W_TIGHT > NowPlayingFit.PILL_W_TIGHT && NowPlayingFit.PLAY_H_TIGHT > NowPlayingFit.PILL_H_TIGHT)
        for (w in listOf(360f, 375f, 390f, 412f)) {
            assertEquals("$w", 1f, NowPlayingFit.controlsScale(w - 28f), 0f)
            assertEquals("$w", NowPlayingFit.PLAY_H, NowPlayingFit.controlsHeight(w), .01f)
        }
        // never under a 48 dp target, tight or not
        assertTrue(NowPlayingFit.PILL_H_TIGHT >= 48f && NowPlayingFit.ROUND >= 48f)
    }

    @Test fun aVeryNarrowScreenShrinksThePillsAndPlayAndTheyStillFit() {
        val room = 300f - 28f
        val k = NowPlayingFit.controlsScale(room)
        assertTrue(k < 1f && k >= .8f)
        assertTrue(k * NowPlayingFit.transportW(true) <= room + .01f)
        assertTrue("play keeps a 48 dp target", NowPlayingFit.PLAY_H * k >= 48f)
    }

    // ---- the owner's size list (26 Nov): big by default, nothing scrolls, and the spare room is shared out, not left as one band ----

    private fun spare(p: NowPlayingFit.Plan) = p.height - p.fixed - p.side

    @Test fun theSizeListFitsWithBigControls() {
        for ((w, h) in listOf(360f to 640f, 390f to 844f, 412f to 915f, 412f to 1000f)) {
            val p = NowPlayingFit.plan(w, h, 1f)
            val at = "${w.toInt()}x${h.toInt()}: $p"
            assertEquals(at, Layout.STACKED, p.layout)
            assertTrue(at, p.fits)
            assertTrue(at, p.bigControls)
            assertEquals(at, Actions.FULL, p.actions)
            assertTrue(at, p.cover >= NowPlayingFit.COMPACT_COVER)
        }
        // the tall phones keep everything (tip, chips, full band) and a cover of at least MIN_COVER
        for ((w, h) in listOf(390f to 844f, 412f to 915f, 412f to 1000f)) {
            val p = NowPlayingFit.plan(w, h, 1f)
            assertTrue("$p", p.tip && p.chips && !p.compactBand && p.cover >= NowPlayingFit.MIN_COVER)
        }
    }

    @Test fun theSizeListFitsAtBigText() {
        for ((w, h) in listOf(360f to 640f, 390f to 844f, 412f to 915f, 412f to 1000f)) for (fs in scales) {
            val p = NowPlayingFit.plan(w, h, fs)
            assertTrue("${w.toInt()}x${h.toInt()} at $fs: $p", p.fits && p.cover >= 120f)
        }
    }

    @Test fun aTallPhoneSharesItsSpareRoomRatherThanLeavingABand() {
        // 390 x 844 and 412 x 915: the frame takes all the height there is, nothing is left over
        for ((w, h) in listOf(390f to 844f, 412f to 915f)) {
            val p = NowPlayingFit.plan(w, h, 1f)
            assertTrue("$p", spare(p) < 1f)
        }
        // 412 x 1000 (21:9): the frame is as wide as the screen allows; what is left over is about the Up next line's height, which takes it
        // (or, without a next song, Column SpaceBetween spreads it between the parts), never one empty band
        val p = NowPlayingFit.plan(412f, 1000f, 1f)
        assertEquals("$p", 412f - 16f, p.side, .01f)
        assertTrue("$p", spare(p) in 0f..64f)
    }

    @Test fun lessRoomNeverMakesTheControlsBigAgain() {
        for (fs in scales) {
            var prev = NowPlayingFit.plan(360f, 1000f, fs)
            var h = 992f
            while (h >= 560f) {
                val p = NowPlayingFit.plan(360f, h, fs)
                assertTrue("$h at $fs", prev.bigControls || !p.bigControls)
                prev = p; h -= 8f
            }
        }
    }

    @Test fun theTitleIsTheWebsSize() {
        assertEquals(40f, SleeveText.titleSize(false, 860, "Moves Like Jagger"), 0f)
        assertEquals(32f, SleeveText.titleSize(false, 860, "A Long Title That Goes On And On"), 0f)
        assertEquals(26f, SleeveText.titleSize(false, 860, "Iniquity Worker (Number of the Beast) [Live at Somewhere]"), 0f)
        // a phone 760 dp tall or less
        assertEquals(32f, SleeveText.titleSize(false, 667, "Chandelier"), 0f)
        assertEquals(52f, SleeveText.titleSize(true, 900, "Chandelier"), 0f)
        assertEquals(40f, SleeveText.titleSize(true, 900, "A Long Title That Goes On And On"), 0f)
    }

    @Test fun theRemoteSaysWhichDeviceAndWhere() {
        assertEquals("LISTENING ON KITCHEN SPEAKER", SleeveText.listeningOn("Kitchen speaker"))
        val f: (Long) -> String = { ms -> "%d:%02d".format(ms / 60000, ms / 1000 % 60) }
        assertEquals("Sia · 1:02 / 3:45", SleeveText.remoteLine("Sia", 62_000, 225_000, f))
        assertEquals("Sia", SleeveText.remoteLine("Sia", 62_000, 0, f))
        // a report a little past the end never shows past it
        assertEquals("Sia · 3:45 / 3:45", SleeveText.remoteLine("Sia", 230_000, 225_000, f))
    }
}
