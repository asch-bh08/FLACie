package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** The bottom tabs are the web's five, in the web's order (MainLayout.razor, ONE APP part 10). */
class TabsTest {
    @Test fun everyoneSignedInHasTheFive() =
        assertEquals(listOf("Home", "Search", "Library", "Devices", "Settings"), tabsFor(guest = false).map { it.label })

    @Test fun aGuestHasNoDevices() =
        assertEquals(listOf("Home", "Search", "Library", "Settings"), tabsFor(guest = true).map { it.label })
}
