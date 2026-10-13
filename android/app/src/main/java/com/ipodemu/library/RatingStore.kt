package com.ipodemu.library

import org.json.JSONArray
import org.json.JSONObject

/** One rated song: its path and title/artist (so the rating follows the song across sources), the stars (0 = cleared) and when it was set. */
class RatingEntry(val path: String, val title: String, val artist: String, var stars: Int, var at: Long)

/**
 * Star ratings, 1 to 5, kept in the profile like favourites (so every device sees them) and written to Jellyfin as a rating of 2 to 10. Pure (org.json only): the rules are
 * unit-tested; UserData owns one and saves it. The newest choice wins when two devices disagree; a cleared rating is kept with its time so that clearing wins too.
 */
class RatingStore {
    private val entries = ArrayList<RatingEntry>()   // newest first

    companion object {
        fun clamp(stars: Int) = stars.coerceIn(0, 5)
        /** Jellyfin's UserData.Rating runs 0 to 10. */
        fun toJellyfin(stars: Int): Double = clamp(stars) * 2.0
        fun fromJellyfin(rating: Double): Int = if (rating.isNaN()) 0 else clamp(Math.round(rating / 2.0).toInt())
        fun label(stars: Int) = when { stars <= 0 -> "not rated"; stars == 1 -> "1 star"; else -> "$stars stars" }
        /** The address of the item's user data: the newer endpoint first, the older one for servers that do not have it. */
        fun jellyfinPaths(userId: String, itemId: String) = listOf("/UserItems/$itemId/UserData?userId=$userId", "/Users/$userId/Items/$itemId/UserData")
    }

    private fun find(path: String, title: String, artist: String): RatingEntry? =
        entries.firstOrNull { it.path == path } ?: entries.firstOrNull { matchKey(it.title, it.artist) == matchKey(title, artist) }

    fun get(path: String, title: String, artist: String): Int = find(path, title, artist)?.stars ?: 0
    fun get(t: Track): Int = get(t.path, t.title, t.artist)

    fun set(t: Track, stars: Int, nowMs: Long = System.currentTimeMillis()) {
        find(t.path, t.title, t.artist)?.let { entries.remove(it) }
        entries.add(0, RatingEntry(t.path, t.title, t.artist, clamp(stars), nowMs))
        while (entries.size > 5000) entries.removeAt(entries.lastIndex)
    }

    /** The rated songs, newest first (cleared ones left out). */
    fun rated(): List<RatingEntry> = entries.filter { it.stars > 0 }

    /** Folds in another device's ratings; true when anything here changed. */
    fun merge(incoming: List<RatingEntry>): Boolean {
        var changed = false
        for (r in incoming) {
            val mine = find(r.path, r.title, r.artist)
            if (mine == null) { entries.add(RatingEntry(r.path, r.title, r.artist, clamp(r.stars), r.at)); changed = true }
            else if (r.at > mine.at) { mine.stars = clamp(r.stars); mine.at = r.at; changed = true }
        }
        if (changed) entries.sortByDescending { it.at }
        return changed
    }

    fun toJson(): JSONArray = JSONArray().also { a -> entries.forEach { a.put(JSONObject().put("p", it.path).put("t", it.title).put("a", it.artist).put("r", it.stars).put("w", it.at)) } }

    fun load(a: JSONArray?) {
        entries.clear()
        if (a == null) return
        for (i in 0 until a.length()) { val o = a.optJSONObject(i) ?: continue; entries.add(RatingEntry(o.optString("p"), o.optString("t"), o.optString("a"), clamp(o.optInt("r")), o.optLong("w"))) }
    }

    /** Entries of an incoming profile ("ratings" array, as FLACie Web writes it). */
    fun parse(a: JSONArray?): List<RatingEntry> = buildList { if (a != null) for (i in 0 until a.length()) { val o = a.optJSONObject(i) ?: continue; add(RatingEntry(o.optString("p"), o.optString("t"), o.optString("a"), clamp(o.optInt("r")), o.optLong("w"))) } }
}
