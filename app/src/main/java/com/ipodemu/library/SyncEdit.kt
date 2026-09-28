package com.ipodemu.library

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/** One track on the connected iPod, addressed by its database id (what the change-set ops take). */
data class IpodTrack(val id: Long, val title: String, val artist: String, val album: String, val stars: Int, val lengthMs: Long)

/** A playlist on the iPod. Smart/podcast playlists are shown but not editable (their contents are rules/feeds). */
data class IpodPlaylist(val name: String, val trackIds: List<Long>, val smart: Boolean, val podcast: Boolean)

class IpodDb(val tracks: List<IpodTrack>, val playlists: List<IpodPlaylist>)

class OpResult(val op: String, val ok: Boolean, val detail: String)

/** What ipodsync's POST /api/apply-edits returned (see ipodsync's EditEndpoint.cs / EDIT-PROTOCOL.md). */
class ApplyResult(
    val dryRun: Boolean, val ok: Boolean, val written: Boolean, val restored: Boolean, val backupDir: String?,
    val ops: List<OpResult>, val problems: List<String>, val log: List<String>, val confirmToken: String?, val error: String?,
)

/** Sync mode's read + write calls to ipodsync (the read side reuses the same /api/library the browse-only source uses). */
class SyncEditClient {
    suspend fun load(host: String, root: String): IpodDb = withContext(Dispatchers.IO) {
        parseLibrary(JSONObject(http("GET", "http://$host/api/library?root=${enc(root)}", null).second))
    }

    /** commit=false is a dry run; a commit must carry the token the dry run of the same change-set returned. */
    suspend fun apply(host: String, root: String, changeSet: JSONObject, commit: Boolean, confirmToken: String?): ApplyResult = withContext(Dispatchers.IO) {
        var url = "http://$host/api/apply-edits?root=${enc(root)}"
        if (commit) url += "&commit=1&confirm=${enc(confirmToken ?: "")}"
        val (code, body) = http("POST", url, changeSet.toString())
        parseApply(try { JSONObject(body) } catch (_: Exception) { JSONObject().put("error", "HTTP $code") }, code, commit)
    }

    companion object {
    /** The ItunesDatabase JSON both IpodSync.Web and the built-in engine return. */
    fun parseLibrary(o: JSONObject): IpodDb {
        val ta = o.getJSONArray("tracks")
        val tracks = List(ta.length()) { i ->
            val t = ta.getJSONObject(i)
            IpodTrack(t.getLong("id"), t.optString("title").ifEmpty { "(untitled)" }, str(t, "artist"), str(t, "album"), t.optInt("stars"), t.optLong("lengthMs"))
        }
        val pa = o.getJSONArray("playlists")
        val playlists = ArrayList<IpodPlaylist>()
        for (i in 0 until pa.length()) {
            val p = pa.getJSONObject(i)
            if (p.optBoolean("isMaster")) continue
            val ids = p.getJSONArray("trackIds")
            playlists += IpodPlaylist(str(p, "name").ifEmpty { "(untitled playlist)" }, List(ids.length()) { ids.getLong(it) }, p.optBoolean("isSmart"), p.optBoolean("isPodcast"))
        }
        return IpodDb(tracks, playlists)
    }

    /** An apply-edits reply body (same shape from IpodSync.Web and the engine). */
    fun parseApply(o: JSONObject, code: Int, commit: Boolean): ApplyResult {
        fun list(k: String) = o.optJSONArray(k)?.let { a -> List(a.length()) { a.optString(it) } } ?: emptyList()
        val ops = o.optJSONArray("ops")?.let { a -> List(a.length()) { a.getJSONObject(it).let { r -> OpResult(r.optString("op"), r.optBoolean("ok"), r.optString("detail")) } } } ?: emptyList()
        return ApplyResult(
            o.optBoolean("dryRun", !commit), o.optBoolean("ok"), o.optBoolean("written"), o.optBoolean("restored"), str(o, "backupDir").ifEmpty { null },
            ops, list("problems"), list("log"), str(o, "confirmToken").ifEmpty { null },
            str(o, "error").ifEmpty { str(o, "detail") }.ifEmpty { if (code !in 200..299) "HTTP $code" else "" }.ifEmpty { null },
        )
    }

    private fun str(o: JSONObject, k: String) = if (o.isNull(k)) "" else o.optString(k)
    }
    private fun enc(s: String) = URLEncoder.encode(s, "UTF-8")

    private fun http(method: String, url: String, body: String?): Pair<Int, String> {
        val c = URL(url).openConnection() as HttpURLConnection
        c.connectTimeout = 6000; c.readTimeout = 180_000   // a real write (backup + verify) can take a while
        c.requestMethod = method
        if (body != null) {
            c.doOutput = true
            c.setRequestProperty("Content-Type", "application/json")
            c.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
        }
        try {
            val code = c.responseCode
            val text = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() } ?: ""
            if (method == "GET" && code !in 200..299) throw IOException(try { JSONObject(text).optString("detail").ifEmpty { "HTTP $code" } } catch (_: Exception) { "HTTP $code" })
            return code to text
        } finally { c.disconnect() }
    }
}

/**
 * Staged edits to an [IpodDb], kept entirely in memory until the user reviews and confirms them. [ops] turns the
 * difference between what the iPod has and what the user wants into an ipodsync change-set (v1): track fields and
 * ratings, then per-playlist membership / order / renames, deletions and new playlists.
 */
class SyncStaging(val base: IpodDb) {
    class WorkPlaylist(val origName: String?, var name: String, val ids: MutableList<Long>, val smart: Boolean, val podcast: Boolean, var deleted: Boolean = false) {
        val isNew get() = origName == null
        val editable get() = !smart && !podcast
    }

    val tracks: MutableMap<Long, IpodTrack> = base.tracks.associateByTo(LinkedHashMap()) { it.id }
    val playlists: MutableList<WorkPlaylist> = base.playlists.mapTo(ArrayList()) { WorkPlaylist(it.name, it.name, it.trackIds.toMutableList(), it.smart, it.podcast) }
    private val baseTracks = base.tracks.associateBy { it.id }

    fun ops(): JSONArray {
        val out = JSONArray()
        for ((id, t) in tracks) {
            val b = baseTracks[id] ?: continue
            val f = JSONObject()
            if (t.title != b.title) f.put("title", t.title)
            if (t.artist != b.artist) f.put("artist", t.artist)
            if (t.album != b.album) f.put("album", t.album)
            if (f.length() > 0) out.put(JSONObject().put("op", "setTrackFields").put("trackId", id).put("fields", f))
            if (t.stars != b.stars) out.put(JSONObject().put("op", "setTrackRating").put("trackId", id).put("stars", t.stars))
        }
        val basePl = base.playlists.associateBy { it.name }
        for (p in playlists) {
            if (!p.editable) continue
            if (p.isNew) {
                // the engine can't address a playlist created earlier in the same change-set, so a new one goes in
                // whole, final order included
                if (!p.deleted) out.put(JSONObject().put("op", "createPlaylist").put("name", p.name).put("trackIds", JSONArray(p.ids)))
                continue
            }
            val orig = p.origName!!
            if (p.deleted) { out.put(JSONObject().put("op", "deletePlaylist").put("playlist", orig)); continue }
            val was = basePl[orig]?.trackIds ?: continue
            val now = p.ids
            val removed = was.filter { it !in now }
            val added = now.filter { it !in was }
            removed.forEach { out.put(JSONObject().put("op", "removeTrackFromPlaylist").put("playlist", orig).put("trackId", it)) }
            added.forEach { out.put(JSONObject().put("op", "addTrackToPlaylist").put("playlist", orig).put("trackId", it)) }
            val afterAddRemove = was.filter { it in now } + added
            if (afterAddRemove != now) out.put(JSONObject().put("op", "reorderPlaylist").put("playlist", orig).put("trackIds", JSONArray(now)))
            if (p.name != orig) out.put(JSONObject().put("op", "renamePlaylist").put("playlist", orig).put("name", p.name))
        }
        return out
    }

    fun changeSet(): JSONObject = JSONObject().put("version", 1).put("ops", ops())
}
