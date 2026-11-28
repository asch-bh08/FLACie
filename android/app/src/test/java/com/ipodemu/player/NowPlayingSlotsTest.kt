package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** ONE APP 1g-2 (layout B, "mode slots"): Shuffle and Repeat are two-state slots in Now Playing's one row of quiet actions, so the transport keeps its
 *  big row alone; only when the row shrinks to More do they go back beside the time, so they are never out of reach. Add is in More's sheet. */
class NowPlayingSlotsTest {
    // owner, 28 Nov: the row drops to Shuffle, Repeat, Lyrics, More; Info is in More
    @Test fun aFullRowHasTheModesFirstThenLyricsThenMore() =
        assertEquals(listOf("shuffle", "repeat", "lyrics", "more"), NowPlayingSlots.of(NowPlayingFit.Actions.FULL))

    @Test fun aCompactRowKeepsTheSameFive() =
        assertEquals(listOf("shuffle", "repeat", "lyrics", "more"), NowPlayingSlots.of(NowPlayingFit.Actions.COMPACT))

    @Test fun theRowsGrowWithTheScreensHeightNotBelowTheirSize() {
        assertEquals(1f, NowPlayingFit.heightGrow(640f), 0f)
        assertEquals(1f, NowPlayingFit.heightGrow(780f), 0f)
        assert(NowPlayingFit.heightGrow(870f) in 1.1f..1.15f)
        assertEquals(1.25f, NowPlayingFit.heightGrow(1000f), 0f)
    }

    @Test fun atTheLastStepOnlyMoreIsLeftAndTheModesGoBesideTheTime() {
        assertEquals(listOf("more"), NowPlayingSlots.of(NowPlayingFit.Actions.MORE_ONLY))
        assertEquals(true, NowPlayingSlots.togglesBesideTime(NowPlayingFit.Actions.MORE_ONLY))
        assertEquals(false, NowPlayingSlots.togglesBesideTime(NowPlayingFit.Actions.FULL))
    }
}
