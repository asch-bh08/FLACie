package com.ipodemu.library

/** One of the account's Jellyfin playlists: its id, name and songs (path to title + artist). */
typealias ServerList = Triple<String, String, List<Pair<String, Pair<String, String>>>>

/**
 * The playlist side of the account sync, kept pure so it can be tested without Android: how the server's own Jellyfin playlists fold into the
 * phone's, and when a playlist deleted here is really gone from the server.
 */
object PlaylistMerge {

    /**
     * The account's own Jellyfin playlists (see AccountSync.pullJellyfinPlaylists): new ones are added, ones deleted on the server go unless edited
     * here since [syncedAt]. [deletedIds] / [deletedJf] are the playlists deleted here (by the app's id, and by their Jellyfin id): one still on the
     * server because its delete has not gone through yet is not brought back.
     */
    fun fromJellyfin(playlists: MutableList<UserPlaylist>, server: List<ServerList>, syncedAt: Long, deletedIds: Set<String>, deletedJf: Set<String>,
                     meta: MutableMap<String, Pair<String, String>>) {
        val ids = server.mapTo(HashSet()) { it.first }
        playlists.removeAll { it.jfId != null && it.jfId !in ids && it.mtime <= syncedAt }
        for ((jf, name, tracks) in server) {
            if (isDeleted(jf, deletedIds, deletedJf)) continue
            tracks.forEach { (p, ta) -> if (ta.first.isNotEmpty() && p !in meta) meta[p] = ta }
            val paths = tracks.mapTo(ArrayList()) { it.first }
            val mine = playlists.firstOrNull { it.jfId == jf }
            if (mine == null) playlists.add(UserPlaylist("jf$jf", name, paths, syncedAt.coerceAtLeast(1), jf))
            // the account profile is the master copy of a playlist it already holds (FLACie Web edits it without touching the Jellyfin playlist, which the next push brings up to date); the server only fills one that is empty here
            else if (mine.paths.isEmpty()) { mine.name = name; mine.paths.addAll(paths) }
        }
    }

    /**
     * The Jellyfin song id a playlist entry is mirrored as: the one in its stream path while Jellyfin still has it, else the song found again by its title
     * and artist ([byTitle]). A song swapped for a Hi-Res copy has a new id; sending the old one dropped it from the playlist (6 Nov). Null: not on Jellyfin.
     */
    fun mirrorId(path: String, isLive: (String) -> Boolean, byTitle: () -> String?): String? {
        val id = jfSong.find(path)?.groupValues?.get(1)
        if (id != null && isLive(id)) return id
        return byTitle()
    }

    /** A Jellyfin playlist deleted on this phone (or on another device, by the id it had there). */
    fun isDeleted(jfId: String, deletedIds: Set<String>, deletedJf: Set<String>) = jfId in deletedJf || "jf$jfId" in deletedIds

    /** The answer to the delete of a Jellyfin playlist that means it is gone: done, or already not there (404). Anything else (offline, refused) is tried again. */
    fun goneOnServer(code: Int) = code in 200..299 || code == 404

    /**
     * Deletes the Jellyfin copies of playlists deleted here: [delete] sends one and returns the HTTP code (throws when offline). Returns the ids still to
     * delete; each stays a tombstone until the server says it is gone.
     */
    suspend fun sendDeletes(tombstones: Collection<String>, delete: suspend (String) -> Int): Set<String> {
        val left = LinkedHashSet<String>()
        for (dead in tombstones) {
            val gone = try { goneOnServer(delete(dead)) } catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (_: Exception) { false }
            if (!gone) left += dead
        }
        return left
    }

    /** How many deleted playlists are kept in the backup file (newest first). */
    const val BACKUPS = 20
    private val jfSong = Regex("/Audio/([0-9a-fA-F]{32})/")

    /**
     * The backup file with one more deleted playlist in front (name, its Jellyfin id, when, and its songs: path plus the Jellyfin song id when it has one),
     * the last [BACKUPS] kept, so a playlist deleted by mistake (or by a sync) can be put back by hand later. [old] is the file's text, or null.
     */
    fun withBackup(old: String?, id: String, name: String, jfId: String?, paths: List<String>, at: Long): String {
        val arr = try { org.json.JSONArray(old ?: "[]") } catch (_: Exception) { org.json.JSONArray() }
        val out = org.json.JSONArray().put(org.json.JSONObject().put("id", id).put("n", name).put("jf", jfId ?: "").put("w", at)
            .put("songs", org.json.JSONArray().also { a -> paths.forEach { p -> a.put(org.json.JSONObject().put("p", p).put("id", jfSong.find(p)?.groupValues?.get(1) ?: "")) } }))
        for (i in 0 until minOf(arr.length(), BACKUPS - 1)) out.put(arr.get(i))
        return out.toString()
    }

    /** A playlist another device deleted (its id in the profile's "deleted"): its Jellyfin copy goes too, so the server's list can't bring it back. */
    fun jellyfinIdOfDeleted(id: String, local: UserPlaylist?): String? = local?.jfId ?: id.removePrefix("jf").takeIf { id.startsWith("jf") && it.isNotEmpty() }
}
