package com.ipodemu.playback

import org.json.JSONObject

/**
 * The AI DJ's shared types, the Kotlin side of `ipodsync/src/FLACie.Core/Dj/DjContracts.cs` (the server, the web and Windows use that one).
 * Change them only together with it. Pure, unit-tested.
 */

/** Where a song is playing, for the DJ's "Where" ticks. The phone counts as [AUTO], not [PHONE], while Android Auto is connected. */
enum class DjSurface(val wire: String) { PHONE("phone"), AUTO("auto"), WEB("web"), WINDOWS("windows") }

/** How often the DJ speaks: Rare is about 1 song in 7, Often about 1 in 3, Every song before each song (the most of the daily allowance). */
enum class DjChatty(val wire: String, val label: String) {
    RARE("rare", "Rare"), OFTEN("often", "Often"), EVERY("every", "Every song");
    fun next(): DjChatty = entries[(ordinal + 1) % entries.size]
    /** An unknown word (a newer server) is Often. */
    companion object { fun of(wire: String?): DjChatty = entries.firstOrNull { it.wire.equals(wire, true) } ?: OFTEN }
}

/** How the DJ sounds: Classic radio (the default), Chill (softer, a little less often) or Hype (livelier, a little more often). */
enum class DjStyle(val wire: String, val label: String) {
    CLASSIC("classic", "Classic radio"), CHILL("chill", "Chill"), HYPE("hype", "Hype");
    fun next(): DjStyle = entries[(ordinal + 1) % entries.size]
    companion object { fun of(wire: String?): DjStyle = entries.firstOrNull { it.wire.equals(wire, true) } ?: CLASSIC }
}

/**
 * One person's DJ settings, kept on the server (per account) so the phone, the car, the web and the Windows app all follow them. The phone keeps the
 * last copy it was sent (Prefs) for the Now Playing button. [playlists]: true = DJ on for that playlist, false = off, no entry = follow the rest; the key
 * is the Jellyfin playlist id when it has one, else the app's own id. [voiceReady]: the server can make clips (Cloudflare is set up).
 */
data class DjSettings(
    val enabled: Boolean = false, val chatty: DjChatty = DjChatty.OFTEN,
    val phone: Boolean = true, val auto: Boolean = true, val web: Boolean = true, val windows: Boolean = true,
    val backup: Boolean = true, val playlists: Map<String, Boolean> = emptyMap(), val voiceReady: Boolean = false,
    /** Keep talking when the listener skips (off: the DJ stays quiet for a couple of songs after a skip). The server decides with it; the phone shows and sets it. */
    val talkAfterSkips: Boolean = true,
    /** The DJ's tone, kept on the server like the rest. */
    val style: DjStyle = DjStyle.CLASSIC,
    /** The person chose the phone's own voice as the DJ's voice (every line, no clip). Off: only the server's voice, never the phone's. */
    val deviceVoice: Boolean = false,
    /** How loud the DJ's voice is, 0-100 % of the music's volume. */
    val volume: Int = DEFAULT_VOLUME,
    /** How loud the music stays under the DJ, 0-100 %. */
    val duck: Int = DEFAULT_DUCK,
    /** Per song, by [com.ipodemu.library.matchKey] (title and artist): true = always introduce it, false = never talk over it; no entry = the usual rules. */
    val songs: Map<String, Boolean> = emptyMap(),
    /** Show the DJ's words on screen while it talks (Now Playing and the mini bar). */
    val captions: Boolean = true,
    /** A short soft swoosh just before the DJ speaks. */
    val cue: Boolean = true,
) {
    /** The clip player's volume (0..1). */
    val voiceLevel: Float get() = volume.coerceIn(0, 100) / 100f
    /** The music's level under the DJ (0..1). */
    val duckLevel: Float get() = duck.coerceIn(0, 100) / 100f
    fun on(s: DjSurface): Boolean = when (s) { DjSurface.PHONE -> phone; DjSurface.AUTO -> auto; DjSurface.WEB -> web; DjSurface.WINDOWS -> windows }

    /** The GET shape, as the server sends it (also what the phone caches). */
    fun toJson(): JSONObject = JSONObject().put("enabled", enabled).put("chatty", chatty.wire).put("phone", phone).put("auto", auto).put("web", web)
        .put("windows", windows).put("backup", backup).put("talkAfterSkips", talkAfterSkips).put("style", style.wire).put("playlists", JSONObject().also { o -> playlists.forEach { (k, v) -> o.put(k, v) } })
        .put("deviceVoice", deviceVoice).put("volume", volume).put("duck", duck).put("songs", JSONObject().also { o -> songs.forEach { (k, v) -> o.put(k, if (v) "always" else "never") } })
        .put("voiceReady", voiceReady).put("captions", captions).put("cue", cue)

    companion object {
        const val DEFAULT_VOLUME = 90
        /** 20 % since 2026-10-27 (it was 30 %, and the voice got lost in busy songs); the server moves anyone still on 30 % across once. */
        const val DEFAULT_DUCK = 20
        /** The server's `/api/dj/settings` answer; a field it leaves out keeps its default. Throws on text that is not JSON. */
        fun parse(text: String): DjSettings {
            val o = JSONObject(text); val d = DjSettings()
            val pl = o.optJSONObject("playlists")
            val so = o.optJSONObject("songs")
            return DjSettings(
                o.optBoolean("enabled", d.enabled), DjChatty.of(o.optString("chatty")),
                o.optBoolean("phone", d.phone), o.optBoolean("auto", d.auto), o.optBoolean("web", d.web), o.optBoolean("windows", d.windows),
                o.optBoolean("backup", d.backup),
                pl?.keys()?.asSequence()?.filter { pl.opt(it) is Boolean }?.associateWith { pl.getBoolean(it) } ?: emptyMap(),
                o.optBoolean("voiceReady", d.voiceReady),
                o.optBoolean("talkAfterSkips", d.talkAfterSkips),
                DjStyle.of(o.optString("style")),
                o.optBoolean("deviceVoice", d.deviceVoice),
                o.optInt("volume", d.volume).coerceIn(0, 100),
                o.optInt("duck", d.duck).coerceIn(0, 100),
                so?.keys()?.asSequence()?.mapNotNull { k -> when (so.optString(k)) { "always" -> k to true; "never" -> k to false; else -> null } }?.toMap() ?: emptyMap(),
                o.optBoolean("captions", d.captions),
                o.optBoolean("cue", d.cue),
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

    /** A song's key in [DjSettings.songs]: its match key (title and artist), "" without a title. The same as `DjRules.SongKey` in DjContracts.cs. */
    fun songKey(title: String, artist: String): String = if (title.isBlank()) "" else com.ipodemu.library.matchKey(title, artist)

    /** The person's choice for this song: true = always introduce it, false = never talk over it, null = the usual rules. */
    fun songChoice(s: DjSettings, title: String, artist: String): Boolean? = songKey(title, artist).takeIf { it.isNotEmpty() }?.let { s.songs[it] }

    /** POST /api/dj/settings' body that sets ([choice]) or clears (null) the choice for a song; the server works out the key the same way. */
    fun songPatch(title: String, artist: String, choice: Boolean?): JSONObject =
        JSONObject().put("song", JSONObject().put("title", title).put("artist", artist).put("choice", when (choice) { true -> "always"; false -> "never"; null -> JSONObject.NULL }))

    /** A song's menu: the three DJ choices with the one in force marked, as (label, choice, selected). */
    fun songMenu(s: DjSettings, title: String, artist: String): List<Triple<String, Boolean?, Boolean>> {
        val now = songChoice(s, title, artist)
        return listOf(Triple("DJ: always introduce this song", true, now == true), Triple("DJ: never talk over this song", false, now == false), Triple("DJ: default", null, now == null))
    }

    /** The settings as they will be once [songPatch] is saved (shown at once, before the server answers). */
    fun withSong(s: DjSettings, title: String, artist: String, choice: Boolean?): DjSettings {
        val k = songKey(title, artist)
        if (k.isEmpty()) return s
        return s.copy(songs = if (choice == null) s.songs - k else s.songs + (k to choice))
    }
}

/**
 * What the DJ says before a song (`POST /api/dj/next` with speak = true). [text] is spoken, [caption] is the written form. [clip] is the server-relative
 * MP3 ("/api/dj/clip/<id>.mp3"), null when no clip could be made; [backup] = say [text] with the phone's own voice instead. [introMs]: how long the
 * song plays before the singing, when the server is sure of it, for [DjTiming]; null = not known (the clip starts with the song, as before).
 */
data class DjCue(val text: String, val caption: String, val clip: String?, val backup: Boolean, val introMs: Int? = null) {
    companion object {
        /** The server's answer: null for `{"speak":false}`, for a cue with nothing to say, or for text that is not JSON. */
        fun parse(text: String): DjCue? = try {
            val o = JSONObject(text)
            if (!o.optBoolean("speak")) null else {
                fun str(k: String) = if (o.isNull(k)) "" else o.optString(k)
                // introMs: how long the song plays before the singing, when the server is sure (DjTiming); null = not known
                val intro = if (o.isNull("introMs")) null else o.optInt("introMs", -1).takeIf { it >= 0 }
                val cue = DjCue(str("text"), str("caption"), str("clip").takeIf { it.isNotBlank() }, o.optBoolean("backup"), intro)
                cue.takeIf { it.clip != null || (it.backup && it.text.isNotBlank()) }
            }
        } catch (_: Exception) { null }
    }
}
