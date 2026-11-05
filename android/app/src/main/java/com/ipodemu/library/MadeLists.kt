package com.ipodemu.library

import org.json.JSONObject

/**
 * Ready-made playlists, like Spotify's made-for-you and community lists (owner, 2 Nov): FLACie Web builds them from the library and this person's
 * taste ("2010s", "Rock", "Best of The Prodigy"; MadePlaylists.cs) and Search shows the ones matching what was typed. Opening one asks the server for
 * its songs; "Add to my playlists" has the server keep a copy as a normal playlist of the account, which the next account sync brings here.
 * Songs come in the shape /api/recommend uses, so they are found on the phone the same way (by path, else title and artist).
 */
object MadeLists {
    class Summary(val id: String, val name: String, val subtitle: String, val count: Int, val first: RecPick?)
    class Full(val id: String, val name: String, val subtitle: String, val tracks: List<RecPick>)
    class Saved(val id: String, val name: String, val count: Int)

    private fun str(o: JSONObject, k: String) = if (o.isNull(k)) null else o.optString(k).takeIf { it.isNotEmpty() }
    private fun song(o: JSONObject) = RecPick(str(o, "path"), o.optString("title"), o.optString("artist"), o.optString("album"), o.optLong("durationMs"), false, null)

    /** `GET /api/made?q=`: the matching lists, closest first. */
    fun parseSearch(json: String): List<Summary> = JSONObject(json).optJSONArray("lists")?.let { a ->
        List(a.length()) { a.getJSONObject(it).let { o -> Summary(o.optString("id"), o.optString("name"), o.optString("subtitle"), o.optInt("count"), o.optJSONObject("first")?.let(::song)) } }
    }?.filter { it.id.isNotEmpty() } ?: emptyList()

    /** `GET /api/made/{id}`: one list with its songs. */
    fun parseOne(json: String): Full = JSONObject(json).let { o ->
        val a = o.optJSONArray("tracks")
        Full(o.optString("id"), o.optString("name"), o.optString("subtitle"), if (a == null) emptyList() else List(a.length()) { song(a.getJSONObject(it)) })
    }

    /** `POST /api/made/{id}/save`: the playlist it became. */
    fun parseSaved(json: String): Saved = JSONObject(json).let { Saved(it.optString("id"), it.optString("name"), it.optInt("count")) }

    /** The list's address on the server: its id ("made:artist:the prodigy") as one path segment, spaces as %20 (a "+" in a path is a plus). */
    fun path(id: String): String = "/api/made/" + java.net.URLEncoder.encode(id, "UTF-8").replace("+", "%20")

    /** Searches worth asking the server about: two letters or more, as on the web. */
    fun worthAsking(q: String) = q.trim().length >= 2

    /** Lists opened in this run, so going back to one shows it at once (the server is asked again in the background). */
    val opened = java.util.concurrent.ConcurrentHashMap<String, Full>()
    /** Lists added to the person's playlists in this run: their button says Added. */
    val saved: MutableSet<String> = java.util.concurrent.ConcurrentHashMap.newKeySet()
}
