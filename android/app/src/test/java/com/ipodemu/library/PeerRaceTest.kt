package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** How long each Soulseek peer gets (the same rules as the server's PeerRace): the last peer used to wait up to 8 minutes on "Waiting for a peer...". */
class PeerRaceTest {
    @Test fun whenAPeerIsGivenUp() {
        assertTrue(PeerRace.giveUp(0, 21_000, 21_000, othersToTry = true))       // silent 21s, others waiting: swap
        assertFalse(PeerRace.giveUp(0, 15_000, 15_000, othersToTry = true))
        assertFalse(PeerRace.giveUp(0, 59_000, 59_000, othersToTry = false))     // the last peer: a minute to start
        assertTrue(PeerRace.giveUp(0, 61_000, 61_000, othersToTry = false))
        assertFalse(PeerRace.giveUp(500_000, 200_000, 30_000, othersToTry = false)) // sending, paused 30s: keeps going
        assertTrue(PeerRace.giveUp(500_000, 200_000, 46_000, othersToTry = false))  // stalled 46s: give up
    }

    @Test fun upToThreePeersAtOnceUntilOneIsSending() {
        assertEquals(listOf(true, true, false, false, false), listOf(
            PeerRace.startAnother(active = 0, anySending = false, waiting = 5),
            PeerRace.startAnother(active = 2, anySending = false, waiting = 5),
            PeerRace.startAnother(active = 3, anySending = false, waiting = 5),
            PeerRace.startAnother(active = 1, anySending = true, waiting = 5),
            PeerRace.startAnother(active = 0, anySending = false, waiting = 0),
        ))
    }
}
