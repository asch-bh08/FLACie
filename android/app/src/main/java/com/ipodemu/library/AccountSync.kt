package com.ipodemu.library

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.ipodemu.App
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/**
 * The app's account: a user on the Jellyfin server, which is the one thing every install of this self-hosted stack
 * already talks to. Signing in (Quick Connect code, or username + password) gets a user access token; the app's
 * "profile" -- every service connection (Jellyfin, Plex, NAS, Lidarr, Soulseek, file mover, ipodsync host) plus
 * playlists and favourites -- is kept server-side in that user's Jellyfin display preferences (client "ipodplayer",
 * one CustomPrefs entry holding JSON). A fresh install that signs in pulls it back and reconnects everything.
 *
 * Playlists are additionally mirrored as real Jellyfin playlists (the tracks that exist on Jellyfin), so they show up
 * in any Jellyfin client too; tracks that only exist as local files on one device stay in the profile, matched by
 * title + artist on the other device when it has the same song.
 *
 * Note: the profile holds the other services' API keys/passwords in plain JSON on the Jellyfin server, readable by
 * that user and server admins -- fine for a single-household homelab, not for a shared server.
 */
class AccountSync(private val app: App) {
    private val prefs get() = app.prefs
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val lock = Mutex()

    var busy by mutableStateOf(false); private set
    var status by mutableStateOf<String?>(null); private set
    /** Quick Connect code to approve in another Jellyfin client (Settings > Quick Connect), while waiting. */
    var quickCode by mutableStateOf<String?>(null); private set
    var signedIn by mutableStateOf(prefs.signedIn); private set
    private var qcJob: Job? = null
    /** The newest profile copy pulled; whatever other apps keep in it (FLACie Web's play history) is written back untouched. */
    @Volatile private var lastRemote: JSONObject? = null

    // ---- sign in / out ------------------------------------------------------------------------------------------

    fun signInWithPassword(server: String, user: String, password: String) {
        val base = normalise(server)
        run("Signing in...") {
            val body = JSONObject().put("Username", user).put("Pw", password)
            val (code, text) = http("POST", "$base/Users/AuthenticateByName", null, body.toString())
            if (code == 401) throw IOException("Wrong username or password")
            if (code !in 200..299) throw IOException("HTTP $code")
            finishSignIn(base, JSONObject(text))
        }
    }

    fun startQuickConnect(server: String) {
        val base = normalise(server)
        cancelQuickConnect()
        qcJob = scope.launch {
            try {
                busy = true; status = "Asking the server for a code..."
                val (ec, et) = http("GET", "$base/QuickConnect/Enabled", null, null)
                if (ec !in 200..299) throw IOException("Can't reach server (HTTP $ec)")
                if (!et.trim().equals("true", true)) throw IOException("Quick Connect is turned off on this server. Sign in with a password instead.")
                val (ic, it) = http("POST", "$base/QuickConnect/Initiate", null, null)
                if (ic !in 200..299) throw IOException("HTTP $ic")
                val init = JSONObject(it)
                val secret = init.getString("Secret")
                quickCode = init.getString("Code")
                status = "Waiting for approval..."
                val deadline = System.currentTimeMillis() + 5 * 60_000
                while (System.currentTimeMillis() < deadline) {
                    delay(2000)
                    val (cc, ct) = http("GET", "$base/QuickConnect/Connect?secret=$secret", null, null)
                    if (cc in 200..299 && JSONObject(ct).optBoolean("Authenticated")) {
                        val (ac, at) = http("POST", "$base/Users/AuthenticateWithQuickConnect", null, JSONObject().put("Secret", secret).toString())
                        if (ac !in 200..299) throw IOException("HTTP $ac")
                        quickCode = null
                        lock.withLock { finishSignIn(base, JSONObject(at)) }
                        return@launch
                    }
                }
                throw IOException("The code expired. Try again.")
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                status = e.message ?: "Sign-in failed"; quickCode = null
            } finally { busy = false }
        }
    }

    fun cancelQuickConnect() { qcJob?.cancel(); qcJob = null; quickCode = null }

    fun signOut() {
        val base = prefs.accountServer; val token = prefs.accountToken
        if (base.isNotBlank()) scope.launch { try { http("POST", "$base/Sessions/Logout", token, null) } catch (_: Exception) {} }
        // a Jellyfin connection that was only borrowing the account's token goes with it
        if (prefs.jellyfinApiKey == token) { prefs.jellyfinApiKey = ""; prefs.jellyfinUrl = "" }
        prefs.accountServer = ""; prefs.accountToken = ""; prefs.accountUserId = ""; prefs.accountUserName = ""; prefs.accountSyncedAt = 0
        prefs.accountKind = "jellyfin"
        // the services came with the account; the next person on this device starts clean
        app.library.forgetAllServices()
        signedIn = false; status = "Signed out"
        app.connect.refresh()
    }

    private suspend fun finishSignIn(base: String, auth: JSONObject) {
        val user = auth.getJSONObject("User")
        prefs.accountServer = base
        prefs.accountToken = auth.getString("AccessToken")
        prefs.accountUserId = user.getString("Id")
        prefs.accountUserName = user.optString("Name")
        if (!prefs.hasNasAccount || prefs.accountKind != "nas") prefs.accountKind = "jellyfin"
        signedIn = true
        syncNow()
    }

    // ---- profile sync -------------------------------------------------------------------------------------------

    /** Pull + merge + push. Run at sign-in, at app start, and from Settings > Account > Sync now. */
    fun sync() { if (prefs.signedIn) run("Syncing...") { syncNow() } }

    private var pushJob: Job? = null
    /** Debounced push after a local change (playlist edit, favourite, a service connected). */
    fun schedulePush() {
        if (!prefs.signedIn) return
        pushJob?.cancel()
        pushJob = scope.launch { delay(4000); try { lock.withLock { push() } } catch (e: Exception) { status = "Sync failed: ${e.message}" } }
    }

    /** The profile lives in every account this install has: the Jellyfin user's settings and a file on the NAS share.
     * The newest copy is merged in, then both are rewritten, so signing in with either later brings everything back
     * (a NAS sign-in restores the Jellyfin account and vice versa). */
    private suspend fun syncNow() {
        val copies = listOfNotNull(
            if (prefs.hasJellyfinAccount) pull() else null,
            if (prefs.hasNasAccount) try { pullNas() } catch (_: Exception) { null } else null,
        )
        val remote = copies.maxByOrNull { it.optLong("updated") }
        if (remote != null) lastRemote = remote
        val restored = if (remote != null) apply(remote) else 0
        if (prefs.hasJellyfinAccount) try { pullJellyfinPlaylists() } catch (_: Exception) {}
        push()
        status = "Synced" + if (restored > 0) ", restored $restored service connection${if (restored == 1) "" else "s"}" else ""
        // a sign-in (or a Jellyfin account restored from the NAS copy) brings Connect up
        kotlinx.coroutines.withContext(Dispatchers.Main) { app.connect.refresh() }
    }

    // ---- the NAS copy of the profile ------------------------------------------------------------------------------

    private fun nasProfilePath() = ".flacie/profile-${prefs.nasUsername.ifBlank { "guest" }.lowercase().replace(Regex("[^a-z0-9._-]"), "_")}.json"

    private fun pullNas(): JSONObject? = NasSmb.readText(prefs.nasUsername, prefs.nasPassword, prefs.nasDomain, prefs.nasHost, prefs.nasShare, nasProfilePath())
        ?.let { try { JSONObject(it) } catch (_: Exception) { null } }

    private fun pushNas(profile: JSONObject) =
        NasSmb.writeText(prefs.nasUsername, prefs.nasPassword, prefs.nasDomain, prefs.nasHost, prefs.nasShare, nasProfilePath(), profile.toString(2))

    /** Sign in with a NAS login: the share must be reachable with these credentials. Its profile (if any) is pulled. */
    fun signInWithNas(host: String, share: String, folder: String, user: String, password: String, domain: String) = run("Connecting to $host...") {
        val (ok, info) = NasDirectClient().testConnection(host.trim(), share.trim(), folder.trim(), user.trim(), password, domain.trim())
        if (!ok) { status = "Could not sign in: ${info ?: "the NAS didn't accept that login"}"; return@run }
        prefs.nasHost = host.trim(); prefs.nasShare = share.trim(); prefs.nasFolder = folder.trim()
        prefs.nasUsername = user.trim(); prefs.nasPassword = password; prefs.nasDomain = domain.trim()
        prefs.accountKind = "nas"
        signedIn = prefs.signedIn
        syncNow()
        app.library.reconnectAll()
    }

    private fun prefsUrl(): String {
        val uid = URLEncoder.encode(prefs.accountUserId, "UTF-8")
        return "${prefs.accountServer}/DisplayPreferences/$PREFS_ID?userId=$uid&client=$CLIENT"
    }

    private fun pull(): JSONObject? {
        val (code, text) = http("GET", prefsUrl(), prefs.accountToken, null)
        if (code == 401) {
            // signed in with the NAS: a Jellyfin token from an old profile copy has expired; drop it, keep the NAS session
            if (prefs.accountKind == "nas") { prefs.accountServer = ""; prefs.accountToken = ""; prefs.accountUserId = ""; prefs.accountUserName = ""; return null }
            signedIn = false; throw IOException("Session expired. Sign in again.")
        }
        if (code !in 200..299) throw IOException("HTTP $code")
        val raw = JSONObject(text).optJSONObject("CustomPrefs")?.optString(PREFS_KEY)?.takeIf { it.isNotBlank() && it != "null" } ?: return null
        return try { JSONObject(raw) } catch (_: Exception) { null }
    }

    private suspend fun push() {
        val profile = buildProfile()
        var failure: Exception? = null
        if (prefs.hasNasAccount) try { pushNas(profile) } catch (e: Exception) { failure = e }
        if (prefs.hasJellyfinAccount) try { pushJellyfin(profile) } catch (e: Exception) { failure = e }
        failure?.let { throw it }
        prefs.accountSyncedAt = System.currentTimeMillis()
    }

    private suspend fun pushJellyfin(profile: JSONObject) {
        mirrorPlaylists()
        val (gc, gt) = http("GET", prefsUrl(), prefs.accountToken, null)
        if (gc !in 200..299) throw IOException("HTTP $gc")
        val dto = JSONObject(gt)
        val custom = dto.optJSONObject("CustomPrefs") ?: JSONObject()
        custom.put(PREFS_KEY, profile.toString())
        dto.put("CustomPrefs", custom).put("Client", CLIENT)
        val (pc, _) = http("POST", prefsUrl(), prefs.accountToken, dto.toString())
        if (pc !in 200..299) throw IOException("HTTP $pc")
    }

    private fun buildProfile(): JSONObject {
        val p = prefs
        val ud = app.userData
        val by = app.library.byPath()
        fun meta(path: String): JSONObject {
            val o = JSONObject().put("p", path)
            val t = by[path]
            val m = ud.meta[path]
            o.put("t", t?.title ?: m?.first ?: "").put("a", t?.artist ?: m?.second ?: "")
            return o
        }
        val services = JSONObject()
        // the account's own token doesn't need saving: a new install gets its own at sign-in
        if (p.jellyfinUrl.isNotBlank() && p.jellyfinApiKey.isNotBlank() && p.jellyfinApiKey != p.accountToken)
            services.put("jellyfin", JSONObject().put("url", p.jellyfinUrl).put("key", p.jellyfinApiKey))
        if (p.plexUrl.isNotBlank()) services.put("plex", JSONObject().put("url", p.plexUrl).put("token", p.plexToken))
        if (p.nasHost.isNotBlank()) services.put("nas", JSONObject().put("host", p.nasHost).put("share", p.nasShare).put("folder", p.nasFolder)
            .put("user", p.nasUsername).put("pass", p.nasPassword).put("domain", p.nasDomain))
        if (p.lidarrUrl.isNotBlank()) services.put("lidarr", JSONObject().put("url", p.lidarrUrl).put("key", p.lidarrApiKey))
        if (p.slskdUrl.isNotBlank()) services.put("slskd", JSONObject().put("url", p.slskdUrl).put("key", p.slskdApiKey).put("path", p.slskdDownloadPath))
        if (p.fileMoverUrl.isNotBlank()) services.put("filemover", JSONObject().put("url", p.fileMoverUrl).put("key", p.fileMoverApiKey))
        if (p.syncHost.isNotBlank()) services.put("synchost", p.syncHost)
        if (p.flacieWebUrl.isNotBlank()) services.put("flacieweb", JSONObject().put("url", p.flacieWebUrl))
        val lists = JSONArray()
        synchronized(ud) {
            ud.playlists.forEach { pl ->
                lists.put(JSONObject().put("id", pl.id).put("n", pl.name).put("m", pl.mtime).put("jf", pl.jfId ?: JSONObject.NULL).put("ip", pl.ip ?: JSONObject.NULL)
                    .put("tracks", JSONArray().also { a -> pl.paths.forEach { a.put(meta(it)) } }))
            }
        }
        // the Jellyfin sign-in travels too, so signing in with only the NAS brings the Jellyfin account back
        val account = if (p.hasJellyfinAccount) JSONObject().put("server", p.accountServer).put("userId", p.accountUserId)
            .put("user", p.accountUserName).put("token", p.accountToken) else JSONObject.NULL
        val built = JSONObject().put("v", 2).put("updated", System.currentTimeMillis()).put("services", services).put("account", account)
            .put("favorites", JSONArray().also { a -> ud.favorites.toList().forEach { a.put(meta(it)) } })
            .put("playlists", lists)
            .put("deleted", JSONArray(ud.deletedPlaylists.toList()))
            .put("hidden", JSONArray(p.hiddenPlaylists.toList()))
        // anything else in the shared profile (FLACie Web keeps the play history there) goes back as it was
        lastRemote?.let { r -> r.keys().forEach { k -> if (!built.has(k)) built.put(k, r.get(k)) } }
        return built
    }

    /** Merges a pulled profile in. Services only fill in what this device doesn't have yet (never overwrites a
     * connection set up here); playlists merge by id, newest edit wins; favourites are a union. Returns how many
     * service connections were restored. */
    private suspend fun apply(r: JSONObject): Int {
        val p = prefs
        var restored = 0
        val s = r.optJSONObject("services") ?: JSONObject()
        fun take(blank: Boolean, key: String, set: (JSONObject) -> Unit) { if (blank) s.optJSONObject(key)?.let { set(it); restored++ } }
        if (!p.hasJellyfinAccount) r.optJSONObject("account")?.let { a ->
            if (a.optString("token").isNotBlank()) {
                p.accountServer = a.optString("server"); p.accountUserId = a.optString("userId"); p.accountUserName = a.optString("user"); p.accountToken = a.optString("token")
                restored++
                rebindToThisDevice()
            }
        }
        take(p.jellyfinUrl.isBlank(), "jellyfin") { p.jellyfinUrl = it.optString("url"); p.jellyfinApiKey = it.optString("key") }
        take(p.plexUrl.isBlank(), "plex") { p.plexUrl = it.optString("url"); p.plexToken = it.optString("token") }
        take(p.nasHost.isBlank(), "nas") { p.nasHost = it.optString("host"); p.nasShare = it.optString("share"); p.nasFolder = it.optString("folder"); p.nasUsername = it.optString("user"); p.nasPassword = it.optString("pass"); p.nasDomain = it.optString("domain") }
        take(p.lidarrUrl.isBlank(), "lidarr") { p.lidarrUrl = it.optString("url"); p.lidarrApiKey = it.optString("key") }
        take(p.slskdUrl.isBlank(), "slskd") { p.slskdUrl = it.optString("url"); p.slskdApiKey = it.optString("key"); it.optString("path").takeIf { v -> v.isNotBlank() }?.let { v -> p.slskdDownloadPath = v } }
        take(p.fileMoverUrl.isBlank(), "filemover") { p.fileMoverUrl = it.optString("url"); p.fileMoverApiKey = it.optString("key") }
        if (p.syncHost.isBlank() && s.optString("synchost").isNotBlank()) p.syncHost = s.optString("synchost")
        s.optJSONObject("flacieweb")?.optString("url")?.takeIf { it.isNotBlank() }?.let { p.flacieWebUrl = it }
        // no Jellyfin connection of its own: the account's server + user token is one
        if (p.jellyfinUrl.isBlank() && p.hasJellyfinAccount) { p.jellyfinUrl = p.accountServer; p.jellyfinApiKey = p.accountToken; restored++ }

        val favs = r.optJSONArray("favorites") ?: JSONArray()
        val incomingFavs = List(favs.length()) { favs.getJSONObject(it) }.map { Triple(it.optString("p"), it.optString("t"), it.optString("a")) }
        val pls = r.optJSONArray("playlists") ?: JSONArray()
        val incoming = List(pls.length()) { i ->
            val o = pls.getJSONObject(i)
            val tr = o.optJSONArray("tracks") ?: JSONArray()
            RemotePlaylist(o.getString("id"), o.optString("n"), o.optLong("m"), if (o.isNull("jf")) null else o.optString("jf").ifBlank { null },
                List(tr.length()) { j -> tr.getJSONObject(j).let { Triple(it.optString("p"), it.optString("t"), it.optString("a")) } }, if (o.isNull("ip")) null else o.optString("ip").ifBlank { null })
        }
        val deleted = r.optJSONArray("deleted")?.let { a -> List(a.length()) { a.getString(it) } } ?: emptyList()
        // old on-device playlists hidden on another install stay hidden here
        r.optJSONArray("hidden")?.let { a -> p.hiddenPlaylists = p.hiddenPlaylists + List(a.length()) { a.getString(it) } }
        kotlinx.coroutines.withContext(Dispatchers.Main) { app.userData.mergeRemote(incomingFavs, incoming, deleted) }
        if (restored > 0) app.library.reconnectAll()
        return restored
    }

    /**
     * A token that came out of the shared profile was issued to whichever device signed in first, and Jellyfin ties sessions to the
     * token's device: Connect and Jams would then talk as that other device. Mint one for this install by approving our own Quick
     * Connect request with the borrowed token (no password involved). If anything fails the borrowed token is kept.
     */
    private fun rebindToThisDevice() {
        val base = prefs.accountServer; val old = prefs.accountToken
        try {
            val (ic, it) = http("POST", "$base/QuickConnect/Initiate", null, null)
            if (ic !in 200..299) return
            val init = JSONObject(it)
            val (ac, _) = http("POST", "$base/QuickConnect/Authorize?code=${init.getString("Code")}", old, null)
            if (ac !in 200..299) return
            val (rc, rt) = http("POST", "$base/Users/AuthenticateWithQuickConnect", null, JSONObject().put("Secret", init.getString("Secret")).toString())
            if (rc !in 200..299) return
            val fresh = JSONObject(rt).getString("AccessToken")
            prefs.accountToken = fresh
            if (prefs.jellyfinApiKey == old) prefs.jellyfinApiKey = fresh
        } catch (_: Exception) { }
    }

    class RemotePlaylist(val id: String, val name: String, val mtime: Long, val jfId: String?, val tracks: List<Triple<String, String, String>>, val ip: String? = null)

    // ---- Jellyfin playlist mirror --------------------------------------------------------------------------------

    private val jfIdRe = Regex("/Audio/([0-9a-fA-F]{32})/stream")

    /** The Jellyfin item id for a playlist entry: straight from a Jellyfin stream path, or by title + artist among
     * the Jellyfin tracks (a local file of a song the server also has). Null = not on Jellyfin. */
    private fun jellyfinIdFor(path: String): String? {
        jfIdRe.find(path)?.let { return it.groupValues[1] }
        val t = app.library.byPath()[path]
        val m = app.userData.meta[path]
        val key = t?.matchKey ?: m?.let { matchKey(it.first, it.second) } ?: return null
        val hit = app.library.jellyfinByKey()[key] ?: return null
        return jfIdRe.find(hit.path)?.groupValues?.get(1)
    }


    /** The account user's own playlists on the server (made in Jellyfin or mirrored from here), as playable paths. */
    private suspend fun pullJellyfinPlaylists() {
        val base = prefs.accountServer; val token = prefs.accountToken; val uid = prefs.accountUserId
        val (c, t) = http("GET", "$base/Users/$uid/Items?IncludeItemTypes=Playlist&Recursive=true&SortBy=SortName", token, null)
        if (c !in 200..299) return
        val arr = JSONObject(t).optJSONArray("Items") ?: return
        val byId = HashMap<String, Track>()
        for (tr in app.library.jellyfinTracks) jfIdRe.find(tr.path)?.let { byId[it.groupValues[1]] = tr }
        val streamBase = prefs.jellyfinUrl.ifBlank { base }.trimEnd('/')
        val out = ArrayList<Triple<String, String, List<Pair<String, Pair<String, String>>>>>()
        for (i in 0 until arr.length()) {
            val pl = arr.getJSONObject(i)
            if (pl.optString("MediaType").let { it.isNotEmpty() && it != "Audio" }) continue
            val id = pl.getString("Id")
            val (ic, it2) = http("GET", "$base/Playlists/$id/Items?userId=$uid", token, null)
            if (ic !in 200..299) continue
            val items = JSONObject(it2).optJSONArray("Items") ?: JSONArray()
            val tracks = (0 until items.length()).map { items.getJSONObject(it) }.filter { it.optString("Type") == "Audio" }.map { o ->
                val tid = o.getString("Id")
                val artist = o.optString("AlbumArtist").ifEmpty { o.optJSONArray("Artists")?.optString(0).orEmpty() }
                (byId[tid]?.path ?: "$streamBase/Audio/$tid/stream?static=true") to (o.optString("Name") to artist)
            }
            out += Triple(id, pl.optString("Name").ifBlank { "Playlist" }, tracks)
        }
        val since = prefs.accountSyncedAt
        kotlinx.coroutines.withContext(Dispatchers.Main) { app.userData.mergeJellyfin(out, since) }
    }

    private fun mirrorPlaylists() {
        val base = prefs.accountServer; val token = prefs.accountToken; val uid = prefs.accountUserId
        // the stored ids point at playlists on the account's server; a different Jellyfin can't be mirrored to
        val ud = app.userData
        val hadDeletes = ud.deletedPlaylistJf.isNotEmpty()
        for (dead in ud.deletedPlaylistJf.toList()) {
            try { http("DELETE", "$base/Items/$dead", token, null) } catch (_: Exception) {}
            ud.deletedPlaylistJf.remove(dead)
        }
        if (hadDeletes) ud.touchedExternally()
        val snapshot = synchronized(ud) { ud.playlists.map { Triple(it, it.name, it.paths.toList()) } }
        var mirrored = 0
        for ((pl, name, paths) in snapshot) {
            val ids = paths.mapNotNull { jellyfinIdFor(it) }.distinct()
            try {
                var jf = pl.jfId
                if (jf != null) {
                    val (c, t) = http("GET", "$base/Playlists/$jf/Items?userId=$uid", token, null)
                    if (c == 404) jf = null
                    else if (c in 200..299) {
                        val items = JSONObject(t).optJSONArray("Items") ?: JSONArray()
                        val have = List(items.length()) { items.getJSONObject(it) }
                        if (have.map { it.optString("Id") } != ids) {
                            val entries = have.map { it.optString("PlaylistItemId") }.filter { it.isNotBlank() }
                            if (entries.isNotEmpty()) http("DELETE", "$base/Playlists/$jf/Items?entryIds=${entries.joinToString(",")}", token, null)
                            if (ids.isNotEmpty()) http("POST", "$base/Playlists/$jf/Items?ids=${ids.joinToString(",")}&userId=$uid", token, null)
                        }
                        http("POST", "$base/Playlists/$jf", token, JSONObject().put("Name", name).toString())
                    }
                }
                if (jf == null && ids.isNotEmpty()) {
                    val body = JSONObject().put("Name", name).put("Ids", JSONArray(ids)).put("UserId", uid).put("MediaType", "Audio")
                    val (c, t) = http("POST", "$base/Playlists", token, body.toString())
                    if (c in 200..299) jf = JSONObject(t).optString("Id").ifBlank { null }
                }
                if (jf != pl.jfId) { pl.jfId = jf; ud.touchedExternally() }
                if (jf != null) mirrored++
            } catch (_: Exception) { /* offline: the profile copy still carries it; retried on the next sync */ }
        }
        lastMirrored = mirrored
    }
    @Volatile var lastMirrored = 0; private set


    // ---- sharing ------------------------------------------------------------------------------------------------

    /** Other people on the account's server, to share a playlist with: every user for an admin, else the ones shown on
     * the server's sign-in screen. Calls back on the main thread with (id, name) pairs, or an error message. */
    fun otherUsers(done: (List<Pair<String, String>>, String?) -> Unit) {
        scope.launch {
            val r = try {
                val base = prefs.accountServer; val token = prefs.accountToken
                var (c, t) = http("GET", "$base/Users", token, null)
                if (c !in 200..299) { val p = http("GET", "$base/Users/Public", token, null); c = p.first; t = p.second }
                if (c !in 200..299) throw IOException("HTTP $c")
                val a = JSONArray(t)
                List(a.length()) { a.getJSONObject(it) }.map { it.optString("Id") to it.optString("Name") }
                    .filter { it.first.isNotBlank() && it.first != prefs.accountUserId } to null
            } catch (e: Exception) { emptyList<Pair<String, String>>() to (e.message ?: "Couldn't list users") }
            kotlinx.coroutines.withContext(Dispatchers.Main) { done(r.first, r.second) }
        }
    }

    /** Shares the Jellyfin copy of a playlist with [userId] (Jellyfin 10.9+); it shows in their Jellyfin and FLACie. */
    fun sharePlaylist(jfId: String, userId: String, canEdit: Boolean, done: (String) -> Unit) {
        scope.launch {
            val msg = try {
                val (c, _) = http("POST", "${prefs.accountServer}/Playlists/$jfId/Users/$userId", prefs.accountToken, JSONObject().put("CanEdit", canEdit).toString())
                if (c in 200..299) "Shared" else if (c == 404) "This server is too old to share playlists (needs Jellyfin 10.9)" else "Couldn't share (HTTP $c)"
            } catch (e: Exception) { "Couldn't share: ${e.message}" }
            kotlinx.coroutines.withContext(Dispatchers.Main) { done(msg) }
        }
    }
    // ---- plumbing -----------------------------------------------------------------------------------------------

    private fun run(msg: String, block: suspend () -> Unit) {
        scope.launch {
            busy = true; status = msg
            try { lock.withLock { block() } } catch (e: Exception) { status = e.message ?: "Failed" } finally { busy = false }
        }
    }

    private fun normalise(server: String): String {
        val s = server.trim().trimEnd('/')
        return if (s.startsWith("http://") || s.startsWith("https://")) s else "https://$s"
    }

    private fun authHeader(token: String?): String =
        "MediaBrowser Client=\"$CLIENT\", Device=\"${android.os.Build.MODEL.replace("\"", "")}\", DeviceId=\"${prefs.deviceId}\", Version=\"$VERSION\"" +
            (token?.let { ", Token=\"$it\"" } ?: "")

    private fun http(method: String, url: String, token: String?, body: String?): Pair<Int, String> {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 8000; conn.readTimeout = 20000
        conn.requestMethod = method
        conn.setRequestProperty("Authorization", authHeader(token))
        if (body != null) {
            conn.doOutput = true
            conn.setRequestProperty("Content-Type", "application/json")
            conn.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
        }
        try {
            val code = conn.responseCode
            val text = (if (code in 200..299) conn.inputStream else conn.errorStream)?.bufferedReader()?.use { it.readText() } ?: ""
            return code to text
        } finally { conn.disconnect() }
    }

    companion object {
        const val CLIENT = "ipodplayer"
        const val PREFS_ID = "ipodplayer"
        const val PREFS_KEY = "ipodplayer.profile"
        const val VERSION = "0.8"
    }
}
