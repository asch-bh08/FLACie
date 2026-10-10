package com.ipodemu.library

import com.ipodemu.App
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.exp
import kotlin.math.ln
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

/** A song as Jellyfin counts it for this account: plays in any app, when last, and whether it is a favourite there. */
class JfPlay(val id: String, val plays: Int, val lastMs: Long, val favourite: Boolean)

/**
 * What Quick picks learn from besides this phone's own history: Jellyfin's play counts (the account's whole listening history, from every app) and the
 * best-known songs of the artists played most (Deezer's top list for each, only artist names are sent). Same idea as FLACie Web (FLACie.Core/Recommender.cs).
 */
object TasteData {
    @Volatile var jf: List<JfPlay> = emptyList()
    @Volatile var hits: Set<String> = emptySet()
    @Volatile private var at = 0L
    private val hitCache = ConcurrentHashMap<String, Pair<Long, List<String>>>()
    val jfIdRe = Regex("/Audio/([0-9a-fA-F]{32})/stream")

    /** Reads Jellyfin's counts and the artists' best-known songs, at most once an hour. Blocking: run it off the main thread. Never throws. */
    fun refresh(app: App, lib: Library, force: Boolean = false) {
        val now = System.currentTimeMillis()
        if (!force && now - at < 3_600_000L) return
        at = now
        val p = app.prefs
        if (p.hasJellyfinAccount) try { jf = fetchJellyfin(p.accountServer.trimEnd('/'), p.accountUserId, p.accountToken, p.deviceId) } catch (_: Exception) { }
        try {
            val artists = QuickPicks.topArtists(lib, app.userData, jf, 24)
            val keys = HashSet<String>()
            for (name in artists) {
                val lead = primaryArtist(name)
                val cached = hitCache[lead]
                val titles = if (cached != null && now - cached.first < 86_400_000L) cached.second else WebCatalog.topSongs(name, 25).also { if (it.isNotEmpty()) hitCache[lead] = now to it }
                titles.forEach { keys.add(matchKey(it, name)) }
            }
            if (keys.isNotEmpty()) hits = keys
        } catch (_: Exception) { }
    }

    private fun fetchJellyfin(server: String, userId: String, token: String, deviceId: String): List<JfPlay> {
        val res = LinkedHashMap<String, JfPlay>()
        fun pull(filter: String, sort: String) {
            val url = "$server/Users/$userId/Items?IncludeItemTypes=Audio&Recursive=true&Filters=$filter&SortBy=$sort&SortOrder=Descending&EnableUserData=true&EnableImages=false&EnableTotalRecordCount=false&Limit=3000"
            val c = URL(url).openConnection() as HttpURLConnection
            c.connectTimeout = 10_000; c.readTimeout = 60_000
            c.setRequestProperty("Authorization", JellyfinAuth.header(token, deviceId))
            try {
                if (c.responseCode !in 200..299) return
                val items = JSONObject(c.inputStream.bufferedReader().use { it.readText() }).optJSONArray("Items") ?: return
                for (i in 0 until items.length()) {
                    val o = items.getJSONObject(i); val id = o.optString("Id").replace("-", "").lowercase(); if (id.isBlank()) continue
                    val ud = o.optJSONObject("UserData")
                    val last = ud?.optString("LastPlayedDate").orEmpty().let { s -> if (s.isBlank()) 0L else try { java.time.Instant.parse(s.take(19) + "Z").toEpochMilli() } catch (_: Exception) { 0L } }
                    val old = res[id]
                    res[id] = JfPlay(id, max(old?.plays ?: 0, ud?.optInt("PlayCount") ?: 0), max(old?.lastMs ?: 0L, last), (old?.favourite ?: false) || (ud?.optBoolean("IsFavorite") ?: false))
                }
            } finally { c.disconnect() }
        }
        pull("IsPlayed", "PlayCount")
        try { pull("IsFavorite", "SortName") } catch (_: Exception) { }
        return res.values.toList()
    }
}

/**
 * The Quick picks algorithm, as on FLACie Web: each song is scored by how much it is loved (plays here and in Jellyfin, favourites, playlists), how much its
 * artist and its album are played, its genre, and whether it is one of the best-known songs of an artist you play; recently played songs sit out. A song never
 * played only counts when it is tied to something you play (its artist, its album or one of that artist's hits). The row is about three in five songs you
 * already love and two in five that are new to you but related, mixed in turn, steady within the hour and changing in the next.
 */
object QuickPicks {
    /** What this account has done, as plain data (so it can be tested without a phone): plays per path, favourites, the paths in playlists, recent paths newest first. */
    class Signals(val plays: Map<String, Int>, val favourites: Set<String>, val playlistPaths: List<String>, val recents: List<String>) {
        constructor(ud: UserData) : this(ud.plays, ud.favorites, ud.playlists.flatMap { it.paths }, ud.recents.toList())
    }

    private class Taste(songs: List<Track>, sig: Signals, jf: List<JfPlay>, val hits: Set<String>, nowMs: Long) {
        val song = HashMap<String, Double>(); val artist = HashMap<String, Double>(); val genre = HashMap<String, Double>(); val album = HashMap<String, Double>()
        val lastPlayed = HashMap<String, Long>()
        var songMax = 1.0; var artistMax = 1.0; var albumMax = 1.0; var genreMax = 1.0
        private fun albumKey(t: Track) = primaryArtist(t.albumArtist.ifEmpty { t.artist }) + "|" + t.albumNormC
        fun add(t: Track, w: Double) {
            song.merge(t.matchKey, w, Double::plus); artist.merge(primaryArtist(t.artist), w, Double::plus)
            if (t.genre.isNotEmpty()) genre.merge(t.genre.lowercase(), w, Double::plus)
            if (t.albumNormC.isNotEmpty()) album.merge(albumKey(t), w, Double::plus)
        }
        init {
            val by = songs.associateBy { it.path }
            sig.plays.forEach { (p, n) -> by[p]?.let { add(it, 1.2 * n) } }
            sig.favourites.forEach { p -> by[p]?.let { add(it, 3.0) } }
            sig.playlistPaths.forEach { p -> by[p]?.let { add(it, 0.7) } }
            sig.recents.take(60).forEachIndexed { i, p -> by[p]?.let { add(it, 2.0 * 0.97.pow(i)); if (i < 12) lastPlayed.merge(it.matchKey, nowMs - i * 20L * 60_000L) { a, b -> max(a, b) } } }
            val byJf = HashMap<String, Track>()
            songs.forEach { t -> TasteData.jfIdRe.find(t.path)?.groupValues?.get(1)?.lowercase()?.let { byJf[it] = t } }
            for (p in jf) {
                val t = byJf[p.id] ?: continue
                val days = if (p.lastMs > 0) max(0.0, (nowMs - p.lastMs) / 86_400_000.0) else 365.0
                val w = ln(1.0 + max(0, p.plays)) * 1.3 * max(0.25, 0.5.pow(days / 120.0)) + (if (p.favourite) 2.0 else 0.0)
                if (w > 0) add(t, w)
                if (p.lastMs > 0) lastPlayed.merge(t.matchKey, p.lastMs) { a, b -> max(a, b) }
            }
            songMax = max(1.0, song.values.maxOrNull() ?: 1.0); artistMax = max(1.0, artist.values.maxOrNull() ?: 1.0)
            albumMax = max(1.0, album.values.maxOrNull() ?: 1.0); genreMax = max(1.0, genre.values.maxOrNull() ?: 1.0)
        }
        fun known(t: Track) = song[t.matchKey] ?: 0.0
        fun love(t: Track) = ln(1 + known(t)) / ln(1 + songMax)
        fun artistFit(t: Track) = (artist[primaryArtist(t.artist)] ?: 0.0) / artistMax
        fun albumFit(t: Track) = if (t.albumNormC.isNotEmpty()) (album[albumKey(t)] ?: 0.0) / albumMax else 0.0
        fun genreFit(t: Track) = if (t.genre.isNotEmpty()) (genre[t.genre.lowercase()] ?: 0.0) / genreMax else 0.0
        fun playedWithinHours(t: Track, nowMs: Long, h: Double): Boolean { val at = lastPlayed[t.matchKey] ?: return false; return nowMs - at < h * 3_600_000L }
    }

    /** The lead artists played most (names as in the library), to look their best-known songs up. */
    fun topArtists(lib: Library, ud: UserData, jf: List<JfPlay>, n: Int): List<String> {
        val songs = lib.songs().filter { it.isMusic }
        val t = Taste(songs, Signals(ud), jf, emptySet(), System.currentTimeMillis())
        return songs.groupBy { primaryArtist(it.artist) }.filterKeys { (t.artist[it] ?: 0.0) > 0 && it.isNotEmpty() }
            .entries.sortedByDescending { t.artist[it.key] ?: 0.0 }.take(n).map { it.value.first().artist }
    }

    fun pick(songs: List<Track>, ud: UserData, jf: List<JfPlay>, hits: Set<String>, slot: Long, nowMs: Long, again: Set<String>, take: Int = 20, perArtist: Int = 2): List<Track> =
        pick(songs, Signals(ud), jf, hits, slot, nowMs, again, take, perArtist)

    fun pick(songs: List<Track>, sig: Signals, jf: List<JfPlay>, hits: Set<String>, slot: Long, nowMs: Long, again: Set<String>, take: Int = 20, perArtist: Int = 2): List<Track> {
        val taste = Taste(songs, sig, jf, hits, nowMs)
        val rnd = java.util.Random(31L + slot)
        val loved = ArrayList<Pair<Track, Double>>(); val fresh = ArrayList<Pair<Track, Double>>(); val other = ArrayList<Pair<Track, Double>>()
        for (t in songs) {
            if (t.durationMs in 1..39_999) continue
            if (t.matchKey in again || taste.playedWithinHours(t, nowMs, 6.0)) continue
            val a = taste.artistFit(t); val al = taste.albumFit(t); val hit = t.matchKey in hits
            val s = taste.love(t) * 2.2 + a * 1.2 + al * 0.9 + taste.genreFit(t) * 0.35 + (if (hit) 0.6 + 0.8 * a else 0.0) +
                (if (t.mtime > 0 && nowMs - t.mtime < 14L * 86_400_000L) 0.15 else 0.0) + rnd.nextDouble() * 0.25 - (if (taste.playedWithinHours(t, nowMs, 24.0)) 0.6 else 0.0)
            if (taste.known(t) > 0) loved.add(t to s) else if (a > 0.12 || al > 0.12 || hit) fresh.add(t to s) else other.add(t to s)
        }
        val per = HashMap<String, Int>(); val used = HashSet<String>()
        fun take(src: List<Pair<Track, Double>>, n: Int, cap: Int): MutableList<Track> {
            val got = ArrayList<Track>()
            for ((t, _) in src.sortedByDescending { it.second }) {
                val ar = primaryArtist(t.artist)
                if ((per[ar] ?: 0) >= cap || t.matchKey in used) continue
                used.add(t.matchKey)
                per[ar] = (per[ar] ?: 0) + 1; got.add(t); if (got.size >= n) break
            }
            return got
        }
        if (loved.isEmpty() && fresh.isEmpty()) return emptyList()
        // songs you love: two of one artist at most; new ones may bring an artist to three (their best-known songs are what you want from artists you play)
        val a1 = take(loved, Math.round(take * 0.6f), perArtist); val b1 = take(fresh, take - a1.size, perArtist + 1)
        // short of songs (a small history or library): more of what you love, then more new ones with a looser limit, and only then songs that merely share a taste
        if (a1.size + b1.size < take) a1.addAll(take(loved, take - a1.size - b1.size, perArtist + 2))
        if (a1.size + b1.size < take) b1.addAll(take(fresh, take - a1.size - b1.size, perArtist + 2))
        if (a1.size + b1.size < take) b1.addAll(take(other, take - a1.size - b1.size, perArtist + 1))
        if (a1.size + b1.size < take) b1.addAll(take(other, take - a1.size - b1.size, 99))   // a library with only a few artists: no limit left
        val out = ArrayList<Track>(); var i = 0; var j = 0
        while (out.size < take && (i < a1.size || j < b1.size)) {
            for (k in 0 until 2) if (i < a1.size && out.size < take) out.add(a1[i++])
            if (j < b1.size && out.size < take) out.add(b1[j++]) else if (i >= a1.size && j < b1.size) out.add(b1[j++])
        }
        return out
    }
}
