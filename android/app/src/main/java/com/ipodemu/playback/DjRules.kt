package com.ipodemu.playback

import org.json.JSONObject

/**
 * The AI DJ's shared types, the Kotlin side of `ipodsync/src/FLACie.Core/Dj/DjContracts.cs` (the server, the web and Windows use that one).
 * Change them only together with it. Pure, unit-tested.
 */

/** Where a song is playing, for the DJ's "Where" ticks. The phone counts as [AUTO], not [PHONE], while Android Auto is connected. */
enum class DjSurface(val wire: String) { PHONE("phone"), AUTO("auto"), WEB("web"), WINDOWS("windows") }

/** How often the DJ speaks: Rare is about 1 song in 7, Often about 1 in 3. */
enum class DjChatty(val wire: String) { RARE("rare"), OFTEN("often") }

/**
 * One person's DJ settings, kept on the server (per account) so the phone, the car, the web and the Windows app all follow them. The phone keeps the
 * last copy it was sent (Prefs) for the Now Playing button. [playlists]: true = DJ on for that playlist, false = off, no entry = follow the rest; the key
 * is the Jellyfin playlist id when it has one, else the app's own id. [voiceReady]: the server can make clips (Cloudflare is set up).
 */
data class DjSettings(
    val enabled: Boolean = false, val chatty: DjChatty = DjChatty.OFTEN,
    val phone: Boolean = true, val auto: Boolean = true, val web: Boolean = true, val windows: Boolean = true,
    val backup: Boolean = true, val playlists: Map<String, Boolean> = emptyMap(), val voiceReady: Boolean = false,
) {
    fun on(s: DjSurface): Boolean = when (s) { DjSurface.PHONE -> phone; DjSurface.AUTO -> auto; DjSurface.WEB -> web; DjSurface.WINDOWS -> windows }

    /** The GET shape, as the server sends it (also what the phone caches). */
    fun toJson(): JSONObject = JSONObject().put("enabled", enabled).put("chatty", chatty.wire).put("phone", phone).put("auto", auto).put("web", web)
        .put("windows", windows).put("backup", backup).put("playlists", JSONObject().also { o -> playlists.forEach { (k, v) -> o.put(k, v) } }).put("voiceReady", voiceReady)

    companion object {
        /** The server's `/api/dj/settings` answer; a field it leaves out keeps its default. Throws on text that is not JSON. */
        fun parse(text: String): DjSettings {
            val o = JSONObject(text); val d = DjSettings()
            val pl = o.optJSONObject("playlists")
            return DjSettings(
                o.optBoolean("enabled", d.enabled), if (o.optString("chatty").equals("rare", true)) DjChatty.RARE else DjChatty.OFTEN,
                o.optBoolean("phone", d.phone), o.optBoolean("auto", d.auto), o.optBoolean("web", d.web), o.optBoolean("windows", d.windows),
                o.optBoolean("backup", d.backup),
                pl?.keys()?.asSequence()?.filter { pl.opt(it) is Boolean }?.associateWith { pl.getBoolean(it) } ?: emptyMap(),
                o.optBoolean("voiceReady", d.voiceReady),
            )
        }
        /** The cached copy, or the defaults (DJ off) when there is none or it can't be read. */
        fun parseOrDefault(text: String?): DjSettings = if (text.isNullOrBlank()) DjSettings() else try { parse(text) } catch (_: Exception) { DjSettings() }
    }
}

/**
 * Which switch wins, top to bottom: the Now Playing "this queue" switch, then the playlist's switch, then On/Off with this device's "Where" tick.
 * The same as `DjRules.EffectiveOn` in DjContracts.cs; keep the two the same.
 */
object DjRules {
    fun effectiveOn(s: DjSettings, surface: DjSurface, playlistId: String?, queueDj: Boolean?): Boolean =
        queueDj ?: (if (!playlistId.isNullOrEmpty()) s.playlists[playlistId] else null) ?: (s.enabled && s.on(surface))
}

/**
 * What the DJ says before a song (`POST /api/dj/next` with speak = true). [text] is spoken, [caption] is the written form. [clip] is the server-relative
 * MP3 ("/api/dj/clip/<id>.mp3"), null when no clip could be made; [backup] = say [text] with the phone's own voice instead.
 */
data class DjCue(val text: String, val caption: String, val clip: String?, val backup: Boolean) {
    companion object {
        /** The server's answer: null for `{"speak":false}`, for a cue with nothing to say, or for text that is not JSON. */
        fun parse(text: String): DjCue? = try {
            val o = JSONObject(text)
            if (!o.optBoolean("speak")) null else {
                fun str(k: String) = if (o.isNull(k)) "" else o.optString(k)
                val cue = DjCue(str("text"), str("caption"), str("clip").takeIf { it.isNotBlank() }, o.optBoolean("backup"))
                cue.takeIf { it.clip != null || (it.backup && it.text.isNotBlank()) }
            }
        } catch (_: Exception) { null }
    }
}
