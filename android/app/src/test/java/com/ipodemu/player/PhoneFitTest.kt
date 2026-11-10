package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The phone's screens fit 320x568 and 360x640, font scale 1.5 and a phone on its side (PhoneFit). */
class PhoneFitTest {
    private val scales = listOf(1f, 1.3f, 1.5f)

    // ---- classic Now Playing ----

    @Test fun classicUprightKeepsACoverOnSmallPhonesAtEveryScale() {
        // the room inside the bars: 24 dp status bar, 48 dp three-button navigation
        for ((w, h) in listOf(320f to 568f - 72f, 360f to 640f - 72f)) for (fs in scales) for (b in listOf(4, 6)) {
            val p = ClassicFit.plan(w, h, fs, b)
            val at = "${w}x$h at $fs ($b buttons): $p"
            assertFalse(at, p.twoCol)
            assertTrue(at, p.art >= ClassicFit.MIN_ART)
            assertFalse(at, p.scroll)
        }
    }

    @Test fun classicUprightOnARoomyPhoneDropsNothing() {
        val p = ClassicFit.plan(412f, 915f - 72f, 1f)
        assertTrue(p.album); assertTrue(p.tag); assertEquals(24f, p.titleSp); assertFalse(p.tight)
    }

    @Test fun classicUprightAtBigTextStepsAsideInOrder() {
        val p = ClassicFit.plan(320f, 496f, 1.5f)
        // the album line goes before the download line, which goes before the title shrinks
        assertFalse(p.album)
        if (p.titleSp < 24f) assertFalse(p.tag)
    }

    @Test fun classicOnItsSideFitsWithoutScrollingAtBigText() {
        for ((w, h) in listOf(568f to 296f, 640f to 336f, 732f to 388f, 800f to 336f)) for (fs in scales) for (b in listOf(4, 6)) {
            val p = ClassicFit.plan(w, h, fs, b)
            val at = "${w}x$h at $fs ($b buttons): $p"
            assertTrue(at, p.twoCol)
            assertFalse(at, p.scroll)
            assertTrue(at, p.art >= 180f)
        }
    }

    @Test fun classicOnItsSideMovesTheHeaderOverTheCoverOnlyWhenNeeded() {
        assertFalse(ClassicFit.plan(800f, 410f, 1f).headerOverArt)
        assertTrue(ClassicFit.plan(568f, 296f, 1.5f).headerOverArt)
    }

    @Test fun classicExtrasFitTheColumn() {
        // six round buttons in a 251 dp column (a 568 dp phone on its side)
        val s = ClassicFit.extrasSize(251f, 6)
        assertTrue(6 * s + 7 * 6f <= 251f + .5f || s == 36f)
        assertEquals(44f, ClassicFit.extrasSize(320f, 4))
        assertTrue(ClassicFit.extrasSize(200f, 6) >= 36f)
    }

    // ---- mini player ----

    private fun modernTitleRoom(w: Float, p: MiniFit.Plan, short: Boolean = false) =
        w - 24f - 14f - (if (short) 44f else 50f) - 24f - 96f - (if (p.dj) 40f else 0f) - (if (p.mic) 40f else 0f) - (if (p.devices) 48f else 0f)

    @Test fun miniKeepsTheTitleReadableOnNarrowPhones() {
        for (w in listOf(320f, 360f, 412f)) for (fs in scales) {
            val p = MiniFit.modern(w, fs, short = false, dj = true, devices = true)
            assertTrue("$w at $fs: $p", modernTitleRoom(w, p) >= MiniFit.MIN_TITLE)
        }
    }

    @Test fun miniDropsTheMicFirstThenDevicesThenTheDj() {
        val roomy = MiniFit.modern(480f, 1f, false, dj = true, devices = true)
        assertTrue(roomy.dj); assertTrue(roomy.mic); assertTrue(roomy.devices)
        val p = MiniFit.modern(360f, 1f, false, dj = true, devices = true)
        assertFalse(p.mic)
        if (!p.dj) assertFalse(p.devices)
        val tight = MiniFit.modern(320f, 1.5f, false, dj = true, devices = true)
        assertFalse(tight.mic); assertFalse(tight.devices)
    }

    @Test fun miniWordsFitTheBarAtBigText() {
        for (fs in scales) for (short in listOf(false, true)) {
            val p = MiniFit.modern(360f, fs, short, dj = false, devices = false)
            val text = p.titleSp * 1.05f * fs + fitLine(12.5f, fs) + (if (p.tag) fitLine(11f, fs) else 0f)
            assertTrue("$fs short=$short: $p", text <= p.height - 6f)
            assertTrue(p.height <= (if (short) 58f else 66f) + MiniFit.GROW)
        }
        // ordinary text: the bar as it was, with the download line and the format chip
        val p = MiniFit.modern(412f, 1f, false, dj = false, devices = false)
        assertEquals(66f, p.height); assertTrue(p.tag); assertTrue(p.chip)
        assertFalse(MiniFit.modern(412f, 1.5f, false, dj = false, devices = false).tag)
    }

    @Test fun classicMiniGrowsForBigTextAndKeepsTheTitle() {
        assertEquals(56f, MiniFit.classic(360f, 1f, dj = false).height)
        for (fs in scales) {
            val p = MiniFit.classic(320f, fs, dj = true)
            val text = fitLine(14f, fs) + fitLine(12f, fs) + (if (p.tag) fitLine(10.5f, fs) else 0f)
            assertTrue("$fs: $p", text <= p.height - 4f)
            assertTrue(320f - 172f - (if (p.dj) 48f else 0f) - (if (p.mic) 48f else 0f) >= MiniFit.MIN_TITLE)
        }
    }

    // ---- Home ----

    @Test fun homeHeroIsAsBeforeOnAnOrdinaryPhone() {
        assertEquals(HomeFit.Hero(300f, 40f), HomeFit.hero(412f, 915f, 1f))
        assertEquals(HomeFit.Hero(300f, 40f), HomeFit.hero(320f, 568f, 1f))
    }

    @Test fun homeHeroLeavesRoomOnItsSideAndTheHeadlineFitsIt() {
        for ((w, h) in listOf(568f to 320f, 640f to 360f, 915f to 412f, 320f to 568f, 360f to 640f)) for (fs in scales) {
            val p = HomeFit.hero(w, h, fs)
            val at = "${w}x$h at $fs: $p"
            if (w > h) assertTrue(at, p.height <= h * .6f + .5f)
            assertTrue(at, HomeFit.TOP + fitLine(11f, fs) + 6f + 2 * p.headlineSp * 1.05f * fs + 8f <= p.height + .5f)
            assertTrue(at, p.headlineSp * fs <= 52f || p.headlineSp == 24f)
        }
    }

    // ---- album and artist pages ----

    @Test fun detailCoverLeavesPlayOnScreenOnItsSide() {
        assertEquals(300f, DetailFit.coverCap(360f, 640f))
        assertEquals(340f, DetailFit.coverCap(320f, 568f, 340f))
        for ((w, h) in listOf(568f to 320f, 640f to 360f, 915f to 412f)) assertTrue(DetailFit.coverCap(w, h) <= h * .45f + .5f)
        assertTrue(DetailFit.coverCap(568f, 280f) >= 140f)
    }

    @Test fun detailTwoPanesOnLandscapePhonesWithTheRail() {
        // a 640 dp phone on its side, less the 80 dp rail
        assertTrue(DetailFit.twoPane(560f, 336f))
        assertFalse(DetailFit.twoPane(360f, 640f))
        assertFalse(DetailFit.twoPane(600f, 560f))
        assertTrue(DetailFit.sideWidth(560f) <= 300f)
    }

    @Test fun detailTitleStaysUnder50SpAsDrawn() {
        assertEquals(42f, DetailFit.titleSp(false, 1f))
        assertEquals(34f, DetailFit.titleSp(true, 1f))
        for (fs in scales) for (long in listOf(false, true)) assertTrue(DetailFit.titleSp(long, fs) * fs <= 50f)
    }

    // ---- search ----

    @Test fun searchRowsUseIconsWhenThePillWouldCrushTheTitle() {
        assertFalse(SearchFit.compactActions(412f, 1f))
        assertFalse(SearchFit.compactActions(600f, 1.5f))
        for (fs in scales) { assertTrue(SearchFit.compactActions(320f, fs)); assertTrue(SearchFit.compactActions(360f, fs)) }
        assertTrue(SearchFit.compactActions(412f, 1.3f))
    }

    // ---- settings ----

    @Test fun settingsKeepsTheValueBesideTheNameWhenThereIsRoom() {
        assertFalse(SettingsFit.stack(412f, 1f, 6, 7))          // "2.3 GB" and "Clear" on an ordinary phone
        assertFalse(SettingsFit.stack(360f, 1f, 0, 0, chevron = true))
        assertFalse(SettingsFit.stack(320f, 1.5f, 0, 0, chevron = true))
    }

    @Test fun settingsPutsTheValueAndChipUnderTheNameWhenCramped() {
        assertTrue(SettingsFit.stack(320f, 1.5f, 12, 8))         // "Synced 5 min ago" and "Sync now"
        assertTrue(SettingsFit.stack(360f, 1.5f, 16, 8))
        assertTrue(SettingsFit.stack(320f, 1f, 20, 12))
        // whatever stays beside the name leaves it at least 110 dp per unit of font scale
        for (w in listOf(320f, 360f, 412f)) for (fs in scales) for ((v, a) in listOf(0 to 5, 8 to 6, 14 to 9, 20 to 12, 0 to 0)) {
            if (!SettingsFit.stack(w, fs, v, a)) assertTrue(w - 76f - SettingsFit.trailing(fs, v, a, false) >= 110f * fs)
        }
    }

    // ---- Up next ----

    @Test fun upNextSaysSaveAsPlaylistWhenThereIsRoom() {
        assertEquals(QueueFit.LONG, QueueFit.saveLabel(412f, 1f))
        assertEquals(QueueFit.LONG, QueueFit.saveLabel(360f, 1f))
        assertEquals(QueueFit.SHORT, QueueFit.saveLabel(320f, 1.5f))
        assertEquals(QueueFit.SHORT, QueueFit.saveLabel(360f, 1.5f))
        assertEquals(QueueFit.LONG, QueueFit.saveLabel(600f, 1.5f))
    }

    // ---- lyrics ----

    @Test fun lyricsAreAsBeforeAtOrdinaryTextAndCappedAtBigText() {
        assertEquals(30f, LyricsFit.sp(big = true, synced = true, fontScale = 1f))
        assertEquals(24f, LyricsFit.sp(big = false, synced = true, fontScale = 1f))
        assertEquals(20f, LyricsFit.sp(big = false, synced = false, fontScale = 1f))
        for (fs in scales) for (big in listOf(false, true)) for (synced in listOf(false, true)) {
            val drawn = LyricsFit.sp(big, synced, fs) * fs
            assertTrue("$big $synced $fs", drawn <= (if (big) LyricsFit.PANE_MAX else LyricsFit.CARD_MAX) + .01f)
        }
    }

    // ---- song info ----

    @Test fun infoTilesGoTwoToARowWhenFourWouldCutTheNumbers() {
        // a 360 dp phone's side pane: 360 - 36 - 28
        assertEquals(4, InfoFit.tileColumns(296f, 1f))
        assertEquals(2, InfoFit.tileColumns(296f, 1.5f))
        assertEquals(2, InfoFit.tileColumns(256f, 1f))
        assertEquals(4, InfoFit.tileColumns(480f, 1.5f))
    }

    @Test fun fullScreenGraphKeepsItsHeightOnAShortScreen() {
        assertEquals(3, InfoFit.hintLines(640f - 72f, 1f))
        for ((h, fs) in listOf(296f to 1f, 296f to 1.5f, 336f to 1.3f, 568f to 1.5f)) {
            val n = InfoFit.hintLines(h, fs)
            val used = 32f + maxOf(48f, 32f * 1.3f * fs) + 44f + 58f + 36f + (if (n > 0) 12f + n * fitLine(12.5f, fs) else 0f)
            assertTrue("$h $fs $n", n == 0 || h - used >= InfoFit.MIN_GRAPH)
        }
        assertTrue(InfoFit.hintLines(296f, 1.5f) < 3)
    }

    // ---- menus and sheets ----

    @Test fun sheetValuesLeaveTheLabelItsRoom() {
        assertEquals(160f, SheetFit.valueMax(640f, 1f))
        assertEquals(120f, SheetFit.valueMax(360f, 1f))
        assertEquals(60f, SheetFit.valueMax(320f, 1.5f))
        for (w in listOf(360f, 412f)) for (fs in scales) {
            val v = SheetFit.valueMax(w, fs)
            assertTrue("$w $fs", v == 60f || w - 120f - v >= 120f * fs)
        }
    }

    @Test fun accountMenuFitsTheScreen() {
        assertEquals(224f, SheetFit.popupMaxHeight(320f))
        assertEquals(160f, SheetFit.popupMaxHeight(200f))
    }

    // ---- lists and grids ----

    @Test fun songRowsKeepTheTimeUnlessTheTitleWouldBeCrushed() {
        assertTrue(ListFit.showTime(320f, 1f))
        assertTrue(ListFit.showTime(360f, 1.3f))
        assertTrue(ListFit.showTime(412f, 1.5f))
        assertFalse(ListFit.showTime(360f, 1.5f))
        assertFalse(ListFit.showTime(320f, 1.3f))
    }

    @Test fun gridsHaveTwoColumnsOnEveryPhone() {
        assertEquals(2, GridFit.columns(320f))
        assertEquals(2, GridFit.columns(360f))
        assertEquals(2, GridFit.columns(412f))
        assertEquals(3, GridFit.columns(640f - 80f))
        assertEquals(1, GridFit.columns(240f))
        // never a cell under 120 dp
        for (w in listOf(280f, 300f, 320f, 360f, 412f, 488f, 600f, 840f)) {
            val n = GridFit.columns(w)
            assertTrue("$w: $n", (w - 24f - 8f * (n - 1)) / n >= 120f)
        }
    }
}
