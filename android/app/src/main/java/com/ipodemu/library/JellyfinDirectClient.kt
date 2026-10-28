package com.ipodemu.library

import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.URLEncoder

/**
 * Talks to a Jellyfin server directly from the phone -- no PC or ipodsync involved, so playback
 * doesn't depend on anything else being turned on. Auth is a server-issued API key or the signed-in user's token, sent as the
 * Authorization: MediaBrowser header for every request here (Jellyfin 12 refuses X-Emby-Token).
 *
 * Every request goes through [JellyfinNet] (the one gate to the server: limited concurrency, priorities, back-off), the bulk listing at
 * Background priority so it never competes with what the user is looking at or listening to.
 *
 * Stream URLs this returns deliberately do NOT carry the key (unlike a plain HTML <audio src>,
 * which has no other way to authenticate): PlayerController attaches the Authorization header as a real HTTP
 * header per-request instead (see MultiServerDataSource), so the key never ends up in a URL,
 * request log, or track path string.
 */
class JellyfinDirectClient {
    /** One song of a listing: the track, what Jellyfin knows about the file, and the bits the delta sync and the cover store need. */
    class Item(
        val id: String, val track: Track, val fact: AudioFacts.Fact?, val savedAt: String,
        /** Cover key ("jf" + id of the item that owns the picture) and that picture's tag; the tag changes when the picture does. */
        val coverKey: String?, val coverTag: String?,
    )
    class Page(val items: List<Item>, val total: Int)

    suspend fun testConnection(url: String, apiKey: String): Pair<Boolean, String?> = try {
        val json = JSONObject(get("${base(url)}/System/Info", apiKey, Priority.VISIBLE, "info"))
        true to json.optString("ServerName").ifEmpty { null }
    } catch (e: Exception) {
        false to (e.message ?: "Connection failed")
    }

    /** Jellyfin's API is keyed per-user; this app just uses the first account on the server. */
    suspend fun firstUserId(url: String, apiKey: String): String? {
        val arr = JSONArray(get("${base(url)}/Users", apiKey, Priority.VISIBLE, "info"))
        return if (arr.length() == 0) null else arr.getJSONObject(0).getString("Id")
    }

    private fun listUrl(url: String, userId: String, extra: String) = listUrlOf(url, userId, extra)

    /** One page of the audio catalog, with the stream facts, cover tags and save dates. [minSaved] = only items saved since then (the delta). */
    suspend fun page(url: String, apiKey: String, userId: String, startIndex: Int, limit: Int, minSaved: String? = null, priority: Priority = Priority.BACKGROUND): Page {
        val json = JSONObject(get(pageUrl(url, userId, startIndex, limit, minSaved), apiKey, priority, "list", heavy = true))
        val arr = json.optJSONArray("Items") ?: JSONArray()
        return Page(itemsFrom(url, arr), json.optInt("TotalRecordCount", startIndex + arr.length()))
    }

    /** How many audio items the server has (no items are sent): a cheap way to notice deletions. */
    suspend fun count(url: String, apiKey: String, userId: String, priority: Priority = Priority.BACKGROUND): Int =
        JSONObject(get(listUrl(url, userId, "&Limit=0"), apiKey, priority, "count")).optInt("TotalRecordCount", -1)

    /** Every audio item id on the server (no fields), for finding what was deleted. */
    suspend fun allIds(url: String, apiKey: String, userId: String, priority: Priority = Priority.BACKGROUND): Set<String> {
        val out = HashSet<String>(); var start = 0
        while (true) {
            val json = JSONObject(get(listUrl(url, userId, "&EnableImages=false&ImageTypeLimit=0&Fields=&StartIndex=$start&Limit=2000"), apiKey, priority, "ids", heavy = true))
            val arr = json.optJSONArray("Items") ?: JSONArray()
            for (i in 0 until arr.length()) out += arr.getJSONObject(i).getString("Id").lowercase()
            if (arr.length() == 0) break
            start += arr.length()
            if (start >= json.optInt("TotalRecordCount", start)) break
        }
        return out
    }

    /** Full details of specific items (ids the server has that this phone does not). */
    suspend fun byIds(url: String, apiKey: String, userId: String, ids: List<String>, priority: Priority = Priority.BACKGROUND): List<Item> {
        val out = ArrayList<Item>()
        for (chunk in ids.chunked(100)) {
            val json = JSONObject(get(listUrl(url, userId, "&Fields=${JellyfinFacts.FIELDS}&EnableImageTypes=Primary&ImageTypeLimit=1&Ids=${chunk.joinToString(",")}"), apiKey, priority, "list", heavy = true))
            out += itemsFrom(url, json.optJSONArray("Items") ?: JSONArray())
        }
        return out
    }

    /** Artists seen on properly tagged songs in the last full listing, for a search's untagged songs. */
    @Volatile var knownArtists: Set<String> = emptySet()

    suspend fun search(url: String, apiKey: String, userId: String, term: String, limit: Int = 50): List<Track> {
        if (term.isBlank()) return emptyList()
        val q = URLEncoder.encode(term, "UTF-8")
        val json = JSONObject(get("${base(url)}/Users/$userId/Items?IncludeItemTypes=Audio&Recursive=true&SearchTerm=$q&Limit=$limit", apiKey, Priority.VISIBLE, "search"))
        return TrackTitles.cleanAll(itemsFrom(url, json.optJSONArray("Items") ?: JSONArray()).map { it.track }, knownArtists)
    }

    private fun itemsFrom(url: String, arr: JSONArray): List<Item> = itemsOf(base(url), arr)

    /** Scans just this artist's folder instead of the whole library, so a freshly-downloaded track
     * shows up as fast as possible: finds the artist's existing item (if Jellyfin already knows
     * about them from other albums) and asks for a recursive refresh of just that item's path via
     * /Library/Media/Updated. A brand-new artist with no existing Jellyfin item has no known path
     * to target, so that case falls back to a full library scan instead. */
    suspend fun scanArtistFolder(url: String, apiKey: String, artist: String) {
        val userId = firstUserId(url, apiKey) ?: return
        val q = URLEncoder.encode(artist, "UTF-8")
        val json = JSONObject(get("${base(url)}/Users/$userId/Items?IncludeItemTypes=MusicArtist&Recursive=true&SearchTerm=$q&Limit=1", apiKey, Priority.VISIBLE, "search"))
        val items = json.optJSONArray("Items")
        val path = items?.takeIf { it.length() > 0 }?.getJSONObject(0)?.optString("Path")?.takeIf { it.isNotEmpty() }
        if (path == null) {
            post("${base(url)}/Library/Refresh", apiKey, null)
            return
        }
        val body = JSONObject().put("Updates", JSONArray().put(JSONObject().put("Path", path).put("UpdateType", "Modified")))
        post("${base(url)}/Library/Media/Updated", apiKey, body)
    }

    private fun base(url: String) = url.trimEnd('/')

    private suspend fun get(url: String, apiKey: String, priority: Priority, kind: String, heavy: Boolean = false): String {
        val r = JellyfinNet.get(url, apiKey, priority, kind, heavy)
        if (!r.ok) throw IOException("HTTP ${r.code}")
        return r.text
    }

    private suspend fun post(url: String, apiKey: String, body: JSONObject?) {
        val r = JellyfinNet.send("POST", url, mapOf("Authorization" to JellyfinAuth.header(apiKey)), body?.toString()?.toByteArray(Charsets.UTF_8), "application/json", Priority.VISIBLE, "admin")
        if (!r.ok) throw IOException("HTTP ${r.code}")
    }

    companion object {
        internal fun listUrlOf(url: String, userId: String, extra: String) =
            "${url.trimEnd('/')}/Users/$userId/Items?IncludeItemTypes=Audio&Recursive=true&SortBy=SortName&EnableUserData=false$extra"

        /** The request for one listing page: the streams' facts, cover tags and save dates ride along; [minSaved] = only items saved since then (the delta). */
        internal fun pageUrl(url: String, userId: String, startIndex: Int, limit: Int, minSaved: String? = null): String {
            val since = if (minSaved.isNullOrEmpty()) "" else "&MinDateLastSaved=${URLEncoder.encode(minSaved, "UTF-8")}"
            return listUrlOf(url, userId, "&Fields=${JellyfinFacts.FIELDS}&EnableImageTypes=Primary&ImageTypeLimit=1&StartIndex=$startIndex&Limit=$limit$since")
        }

        /** Listing JSON to items. [base] is the server URL without a trailing slash. Pure (unit-tested). */
        internal fun itemsOf(base: String, arr: JSONArray): List<Item> {
            fun str(o: JSONObject, key: String) = if (o.isNull(key)) "" else fixMojibake(o.optString(key))
            return List(arr.length()) { i ->
                val o = arr.getJSONObject(i)
                val id = o.getString("Id")
                val artist = fixMojibake(if (!o.isNull("AlbumArtist") && o.optString("AlbumArtist").isNotEmpty()) o.optString("AlbumArtist")
                    else o.optJSONArray("Artists")?.takeIf { it.length() > 0 }?.getString(0) ?: "")
                val parsed = JellyfinFacts.parse(o)
                val t = Track(
                    path = "$base/Audio/$id/stream?static=true",
                    title = str(o, "Name").ifEmpty { "(untitled)" },
                    artist = artist,
                    album = str(o, "Album"),
                    albumArtist = artist,
                    genre = fixMojibake(o.optJSONArray("Genres")?.takeIf { it.length() > 0 }?.getString(0) ?: ""),
                    trackNo = 0,
                    discNo = 0,
                    durationMs = o.optLong("RunTimeTicks") / 10_000L,
                    year = 0,
                    isMusic = true,
                    artKey = artKeyOf(o),
                    mtime = jfDate(o.optString("DateCreated")),
                    size = parsed.size,
                    source = TrackSource.JELLYFIN,
                    filePath = str(o, "Path"),
                )
                Item(id, t, parsed.fact, normIso(o.optString("DateLastSaved")), t.artKey, coverTagOf(o))
            }
        }
    }
}

/** The picture that stands for a song: which item owns it ([key], "jf" + that item's id) and that picture's [tag] (a hash: it changes when the picture does). */
internal class CoverRef(val key: String, val tag: String?)

private val variousRe = Regex("^(various( artists)?|v\\.?a\\.?|compilations?)$", RegexOption.IGNORE_CASE)

/**
 * The song's own picture first (its Primary image and that picture's tag). The album's picture (AlbumId + AlbumPrimaryImageTag) is only the fallback for a
 * song that has none: on a real Jellyfin AlbumId is not reliable (songs of many different albums were seen sharing one AlbumId, so one wrong picture for
 * all of them), so it must never win over what the song itself carries. A compilation ("Various Artists") uses the song's own picture as well.
 */
/** The web's Art.OnCompilation: "Various Artists", or a compilation-looking album name ("Now That's What I Call...", "...Hits", "Ministry of Sound")
 *  credited to someone other than the song's artist. */
private val compilationRe = Regex("""\b(now that'?s what i call|now \d+|various|ministry of sound|hits?|anthems?|summer|party|workout|karaoke|tribute|mix)\b""", RegexOption.IGNORE_CASE)
internal fun onCompilation(artist: String, albumArtist: String, album: String): Boolean =
    albumArtist.contains("various", ignoreCase = true) ||
        (album.isNotEmpty() && compilationRe.containsMatchIn(album) && albumArtist.isNotEmpty() && primaryArtist(albumArtist).lowercase() != primaryArtist(artist).lowercase())

internal fun coverOf(o: JSONObject): CoverRef? {
    val id = o.optString("Id")
    val songArtist = o.optJSONArray("Artists")?.optString(0)?.takeIf { it.isNotBlank() } ?: o.optString("AlbumArtist")
    val albumId = o.optString("AlbumId")
    val own = o.optJSONObject("ImageTags")?.optString("Primary")?.takeIf { it.isNotEmpty() }
    val albumTag = if (o.isNull("AlbumPrimaryImageTag")) null else o.optString("AlbumPrimaryImageTag").takeIf { it.isNotEmpty() }
    return when {
        own != null && id.isNotEmpty() -> CoverRef("jf$id", own)
        // a song on a compilation without its own picture: the catalog cover of the song itself, as the web does (the album's picture is the
        // compilation's, and an album lookup of "Various Artists" finds nothing, so the phone showed no cover for these)
        onCompilation(songArtist, o.optString("AlbumArtist"), o.optString("Album")) && CoverLookup.key(songArtist, "", o.optString("Name")) != null ->
            CoverRef(CoverLookup.key(songArtist, "", o.optString("Name"))!!, null)
        albumId.isNotEmpty() && albumTag != null -> CoverRef("jf$albumId", albumTag)
        // the server says neither the song nor its album has a picture: the public catalog (as the web does), one lookup per album, instead of a placeholder
        else -> CoverLookup.key(o.optString("AlbumArtist").ifBlank { o.optJSONArray("Artists")?.optString(0).orEmpty() }, o.optString("Album"), if (o.optString("Album").isBlank()) o.optString("Name") else "")?.let { CoverRef(it, null) }
    }
}

internal fun artKeyOf(o: JSONObject): String? = coverOf(o)?.key

/** The tag of the picture [artKeyOf] points to (null when the item says nothing). */
internal fun coverTagOf(o: JSONObject): String? = coverOf(o)?.tag

/** An ISO time with a fixed 7-digit fraction, so two of them compare as plain strings ("" when absent). */
internal fun normIso(s: String): String {
    if (s.length < 19) return ""
    val head = s.substring(0, 19)
    val frac = if (s.length > 20 && s[19] == '.') s.substring(20).takeWhile { it.isDigit() } else ""
    return head + "." + frac.padEnd(7, '0').take(7) + "Z"
}

/** Jellyfin's "2026-09-28T17:58:38.7227291Z" as epoch millis (0 if absent), so Recently Added orders Jellyfin songs by
 * when they arrived. */
internal fun jfDate(s: String): Long = if (s.length < 19) 0L else try {
    java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", java.util.Locale.US).apply { timeZone = java.util.TimeZone.getTimeZone("UTC") }.parse(s.take(19))?.time ?: 0L
} catch (_: Exception) { 0L }
