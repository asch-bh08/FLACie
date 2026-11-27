package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** ONE APP 1j: only whoever runs the server sees where the songs come from and the source setup rows. */
class SettingsSourcesTest {
    @Test fun theOwnerSeesTheSources() = assertEquals(true, showsSources(guest = false, canAdmin = true))
    @Test fun someoneInvitedDoesNot() = assertEquals(false, showsSources(guest = false, canAdmin = false))
    @Test fun aGuestDoesNot() = assertEquals(false, showsSources(guest = true, canAdmin = true))
}
