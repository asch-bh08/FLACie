package com.ipodemu.library

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The live socket replaces polling: only library changes start a delta, deletions apply at once, play counts start nothing. */
class ServerChangeTest {
    @Test fun aLibraryChangeStartsADeltaAndDropsWhatWasRemoved() {
        val d = JSONObject("""{"ItemsAdded":["a1"],"ItemsUpdated":[],"ItemsRemoved":["r1","r2"],"FoldersAddedTo":[],"FoldersRemovedFrom":[]}""")
        val c = serverChange("LibraryChanged", d)!!
        assertEquals(listOf("r1", "r2"), c.removed)
        assertTrue(c.sync); assertTrue(c.wantCount); assertFalse(c.onlyIfStale)
    }

    @Test fun aLibraryChangeWithoutDetailsStillStartsADelta() {
        val c = serverChange("LibraryChanged", null)!!
        assertTrue(c.removed.isEmpty()); assertTrue(c.sync)
    }

    @Test fun playCountsFavouritesAndSessionsStartNothing() {
        for (t in listOf("UserDataChanged", "Sessions", "KeepAlive", "Playstate", "Play", "ScheduledTasksInfo", "RefreshProgress", "")) assertNull(t, serverChange(t, null))
    }

    @Test fun anOpenedSocketCatchesUpOnlyAfterAGap() {
        val c = serverChange("SocketOpen", null)!!
        assertTrue(c.sync); assertTrue(c.onlyIfStale); assertFalse(c.wantCount)
        assertNotNull(c)
    }
}
