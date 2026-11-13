package com.ipodemu.library

import org.json.JSONObject
import java.io.File

/**
 * What FLACie Web knows about the library that the phone, reading Jellyfin itself, can't (owner, 13 Nov: "make sure the phone has it all"): the name a
 * song's other version shows ("Voodoo People (05 Edit)"), a song's own album and cover when its file is filed under a playlist's name ("Grenade" on
 * "Pop Megahits"), and the second copies of a recording the song lists hide. By Jellyfin item id, from GET /api/library/fixes; kept in a file.
 */
object LibraryFixes {
    data class Fix(val title: String?, val album: String?, val albumArtist: String?, val artKey: String?)
    @Volatile private var fixes: Map<String, Fix> = emptyMap()
    @Volatile private var hidden: Set<String> = emptySet()
    @Volatile var version = 0; private set
    private val jfItem = Regex("/Audio/([0-9a-fA-F]{32})/")

    fun load(file: File) { try { if (file.exists()) parse(file.readText()) } catch (_: Exception) { } }
    fun refresh(web: FlacieWebClient, file: File) {
        try { if (!web.available) return; val raw = web.libraryFixesRaw(); parse(raw); file.writeText(raw) } catch (_: Exception) { }
    }

    fun parse(raw: String) {
        val o = JSONObject(raw)
        val out = HashMap<String, Fix>()
        o.optJSONArray("fixes")?.let { a -> for (i in 0 until a.length()) a.getJSONObject(i).let { f ->
            fun s(k: String) = if (f.isNull(k)) null else f.optString(k).takeIf { it.isNotBlank() }
            s("id")?.let { id -> out[id.lowercase()] = Fix(s("title"), s("album"), s("albumArtist"), s("artKey")) } } }
        val h = HashSet<String>(); o.optJSONArray("hidden")?.let { a -> for (i in 0 until a.length()) h.add(a.getString(i).lowercase()) }
        fixes = out; hidden = h; version++
    }

    private fun idOf(t: Track) = jfItem.find(t.path)?.groupValues?.get(1)?.lowercase()

    /** The tracks with their fixes over them (the same list when there are none). */
    fun apply(tracks: List<Track>): List<Track> {
        val f = fixes; if (f.isEmpty()) return tracks
        return tracks.map { t -> idOf(t)?.let { f[it] }?.let { x -> t.copy(title = x.title ?: t.title, album = x.album ?: t.album, albumArtist = x.albumArtist ?: t.albumArtist, artKey = x.artKey ?: t.artKey) } ?: t }
    }

    /** A second copy of a recording: left out of the song lists (its album keeps it). */
    fun isHidden(t: Track): Boolean = hidden.isNotEmpty() && idOf(t)?.let { it in hidden } == true
}
