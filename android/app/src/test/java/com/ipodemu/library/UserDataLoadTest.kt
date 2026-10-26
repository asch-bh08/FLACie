package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Since star ratings came in (13 Oct) the saved file never loaded: init { load() } ran before `ratings` existed, load() hit a null and gave up,
 * so every start had no favourites, playlists or history until the account sync brought them back (and none at all while the server was down). */
class UserDataLoadTest {
    @Test fun what_was_saved_is_there_after_a_restart() {
        val dir = kotlin.io.path.createTempDirectory("ud").toFile()
        File(dir, "userdata.json").writeText(
            """{"fav":["/m/a.flac"],"favat":{},"favgone":[],"ratings":[],"recent":["/m/b.flac"],"recentat":{},"dl":[],"plays":{},""" +
                """"lists":[{"id":"u1","n":"pop","p":["/m/a.flac"],"m":1,"jf":""}],"meta":{},"deleted":[],"deljf":[]}"""
        )
        val ud = UserData(dir)
        assertTrue("/m/a.flac" in ud.favorites)
        assertEquals(listOf("/m/b.flac"), ud.recents.toList())
        assertEquals("pop", ud.playlists.single().name)
    }
}
