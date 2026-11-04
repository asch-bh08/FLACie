package com.ipodemu.library

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner, 2 Nov: playlists deleted on the phone came back with the next sync (the old TEST-* playlists kept reappearing). */
class PlaylistDeleteSyncTest {
    private val jf = "a".repeat(32)
    private fun server(vararg ids: String) = ids.map { ServerList(it, "TEST-A-import", listOf("http://jf/Audio/${"b".repeat(32)}/stream" to ("Song" to "Artist"))) }

    @Test fun aPlaylistDeletedHereIsNotBroughtBackByTheServersListWhileItsDeleteIsPending() {
        // deleted on the phone (UserData.deletePlaylist): gone from the list, its id and its Jellyfin id kept as tombstones
        val lists = ArrayList<UserPlaylist>()
        PlaylistMerge.fromJellyfin(lists, server(jf), 1000L, setOf("u1"), setOf(jf), HashMap())
        assertTrue("came back from the server's list: ${lists.map { it.name }}", lists.isEmpty())
    }

    @Test fun aPlaylistFirstSeenOnTheServerAndDeletedHereStaysDeleted() {
        // a playlist the phone only knew from Jellyfin has the id "jf<id>"; deleting it here before its Jellyfin id was tombstoned
        val lists = ArrayList<UserPlaylist>()
        PlaylistMerge.fromJellyfin(lists, server(jf), 1000L, setOf("jf$jf"), emptySet(), HashMap())
        assertTrue(lists.isEmpty())
    }

    @Test fun otherServerPlaylistsStillArrive() {
        val other = "c".repeat(32)
        val lists = ArrayList<UserPlaylist>()
        PlaylistMerge.fromJellyfin(lists, server(jf, other), 1000L, setOf("u1"), setOf(jf), HashMap())
        assertEquals(listOf(other), lists.map { it.jfId })
    }

    @Test fun aTombstoneStaysUntilTheServerSaysThePlaylistIsGone() = runBlocking {
        val sent = ArrayList<String>()
        // offline: the delete throws; refused: 500; done: 204; already gone: 404
        val left = PlaylistMerge.sendDeletes(listOf("off", "err", "ok", "gone")) { id ->
            sent += id
            when (id) { "off" -> throw java.io.IOException("offline"); "err" -> 500; "ok" -> 204; else -> 404 }
        }
        assertEquals(listOf("off", "err", "ok", "gone"), sent)
        assertEquals(setOf("off", "err"), left)
    }

    @Test fun aDeletedPlaylistIsBackedUpAndOnlyTheLast20AreKept() {
        var file: String? = null
        for (i in 1..25) file = PlaylistMerge.withBackup(file, "u$i", "List $i", if (i == 25) jf else null, listOf("http://jf/Audio/${"b".repeat(32)}/stream?static=true", "/m/x.flac"), i.toLong())
        val a = org.json.JSONArray(file)
        assertEquals(20, a.length())
        val newest = a.getJSONObject(0)
        assertEquals("List 25", newest.getString("n")); assertEquals(jf, newest.getString("jf"))
        assertEquals("b".repeat(32), newest.getJSONArray("songs").getJSONObject(0).getString("id"))
        assertEquals("/m/x.flac", newest.getJSONArray("songs").getJSONObject(1).getString("p"))
        assertEquals("List 6", a.getJSONObject(19).getString("n"))
    }

    @Test fun aPlaylistDeletedOnTheWebTakesItsJellyfinCopyWithIt() {
        assertEquals(jf, PlaylistMerge.jellyfinIdOfDeleted("u1", UserPlaylist("u1", "x", ArrayList(), 1, jf)))
        assertEquals(jf, PlaylistMerge.jellyfinIdOfDeleted("jf$jf", null))
        assertEquals(null, PlaylistMerge.jellyfinIdOfDeleted("u1", null))
    }
}
