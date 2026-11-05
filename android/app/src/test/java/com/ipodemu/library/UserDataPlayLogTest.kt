package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File

class UserDataPlayLogTest {
    private fun dir() = java.nio.file.Files.createTempDirectory("ud").toFile()

    // the file is written on a background thread: wait for it to hold the plays
    private fun waitForFile(d: File, text: String) { val f = File(d, "userdata.json"); repeat(100) { if (f.exists() && f.readText().contains(text)) return; Thread.sleep(20) } }

    @Test fun heardPlaysAreKeptAcrossRestarts() {
        val d = dir()
        UserData(d).apply { recordHeard("a", 200.0, 210.0, 5L); recordHeard("a", 10.0, 210.0, 6L) }
        waitForFile(d, ",6]")
        assertEquals(listOf(PlayRecord("a", 200.0, 210.0, 5L), PlayRecord("a", 10.0, 210.0, 6L)), UserData(d).playLog)
    }

    @Test fun signingOutForgetsThem() {
        val ud = UserData(dir()); ud.recordHeard("a", 1.0, 2.0); ud.clearPersonal()
        assertEquals(0, ud.playLog.size)
    }
}
