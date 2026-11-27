package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** ONE APP 1g-2 (layout B, "mode slots"): Shuffle and Repeat are two-state slots in Now Playing's one row of quiet actions, so the transport keeps its
 *  big row alone; only when the row shrinks to More do they go back beside the time, so they are never out of reach. Add is in More's sheet. */
class NowPlayingSlotsTest {
    @Test fun aFullRowHasTheModesFirstThenThePanesThenMore() =
        assertEquals(listOf("shuffle", "repeat", "lyrics", "info", "more"), NowPlayingSlots.of(NowPlayingFit.Actions.FULL))

    @Test fun aCompactRowKeepsTheSameFive() =
        assertEquals(listOf("shuffle", "repeat", "lyrics", "info", "more"), NowPlayingSlots.of(NowPlayingFit.Actions.COMPACT))

    @Test fun atTheLastStepOnlyMoreIsLeftAndTheModesGoBesideTheTime() {
        assertEquals(listOf("more"), NowPlayingSlots.of(NowPlayingFit.Actions.MORE_ONLY))
        assertEquals(true, NowPlayingSlots.togglesBesideTime(NowPlayingFit.Actions.MORE_ONLY))
        assertEquals(false, NowPlayingSlots.togglesBesideTime(NowPlayingFit.Actions.FULL))
    }
}
