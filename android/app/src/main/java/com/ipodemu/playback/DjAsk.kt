package com.ipodemu.playback

import org.json.JSONObject

/**
 * "Talk to the DJ" on the phone: the server (POST /api/dj/ask) understands what was said, so there is one parser for every player; the phone only carries
 * the answer out. The same as `DjAskAnswer` / `DjAskRunner` in FLACie.Core. Only the recognised words are sent, never sound.
 */
/** SEARCH: the search box's mic heard words that are not a request (they fill the search). */
enum class DjAskDo { NONE, SEARCH, PLAY, UPNEXT, NEXT, PAUSE, RESUME, VOLUMEUP, VOLUMEDOWN, DJON, DJOFF }

/** A song in an answer: the server's path, and its file, title and artist so this library can find its own copy. */
data class DjAskSong(val path: String, val file: String = "", val title: String = "", val artist: String = "")

data class DjAskAnswer(
    val doThis: DjAskDo, val say: String, val songs: List<DjAskSong> = emptyList(), val title: String = "", val shuffle: Boolean = false,
    val replace: Boolean = false, val waitArtist: String? = null, val waitTitle: String? = null, val clip: String? = null, val deviceVoice: Boolean = false,
    /** The DJ asked the person something back (the server's intent "Clarify"): the mic listens for the reply. */
    val asked: Boolean = false,
    /** The server's name for what it understood ("Answer", "Clarify", "Goodbye", "ThumbsDown"...): the conversation's turn-taking reads it. */
    val intent: String = "",
) {
    companion object {
        val SORRY = DjAskAnswer(DjAskDo.NONE, "Sorry, I couldn't reach the DJ.")

        fun parse(text: String): DjAskAnswer {
            val o = JSONObject(text)
            val d = DjAskDo.entries.firstOrNull { it.name.equals(o.optString("do"), ignoreCase = true) } ?: DjAskDo.NONE
            val songs = o.optJSONArray("songs")?.let { a -> (0 until a.length()).mapNotNull { i -> a.optJSONObject(i)?.let { s -> DjAskSong(s.optString("path"), s.optString("file"), s.optString("title"), s.optString("artist")) } } }
                ?: o.optJSONArray("paths")?.let { a -> (0 until a.length()).map { DjAskSong(a.optString(it)) } } ?: emptyList()
            val w = o.optJSONObject("wait")
            return DjAskAnswer(d, o.optString("say"), songs, o.optString("title"), o.optBoolean("shuffle"), o.optBoolean("replace"),
                w?.optString("artist"), w?.optString("title"), o.optString("clip").takeIf { it.isNotBlank() && it != "null" }, o.optBoolean("deviceVoice"),
                o.optString("intent") == "Clarify", o.optString("intent"))
        }

        /** The request body: the words, the song playing (by title and artist: the server's paths are not the phone's), where, and the hour. */
        fun body(text: String, title: String?, artist: String?, surface: DjSurface, hour: Int, search: Boolean = false): String = JSONObject()
            .put("text", text.take(300)).apply { if (search) put("search", true) }
            .apply { if (title != null && artist != null) put("currentSong", JSONObject().put("title", title).put("artist", artist)) }
            .put("surface", surface.name).put("hour", hour).toString()
    }
}

/** The player an answer is carried out on (PlayerController; a fake in the tests). */
interface DjAskPlayer<T> {
    /** This library's copy of a song in an answer, or null. */
    fun find(s: DjAskSong): T?
    fun play(list: List<T>, title: String, shuffle: Boolean)
    fun upNext(list: List<T>, replace: Boolean)
    fun next(); fun pause(); fun resume()
    fun nudgeVolume(delta: Float)
    fun setDj(on: Boolean)
}

object DjAskRunner {
    const val VOLUME_STEP = 0.15f

    /** Carries [a] out. True when it changed anything. */
    fun <T> run(p: DjAskPlayer<T>, a: DjAskAnswer): Boolean = when (a.doThis) {
        DjAskDo.PLAY -> a.songs.mapNotNull(p::find).takeIf { it.isNotEmpty() }?.let { p.play(it, a.title, a.shuffle); true } ?: false
        DjAskDo.UPNEXT -> a.songs.mapNotNull(p::find).takeIf { it.isNotEmpty() }?.let { p.upNext(it, a.replace); true } ?: false
        DjAskDo.NEXT -> { p.next(); true }
        DjAskDo.PAUSE -> { p.pause(); true }
        DjAskDo.RESUME -> { p.resume(); true }
        DjAskDo.VOLUMEUP -> { p.nudgeVolume(VOLUME_STEP); true }
        DjAskDo.VOLUMEDOWN -> { p.nudgeVolume(-VOLUME_STEP); true }
        DjAskDo.DJON -> { p.setDj(true); true }
        DjAskDo.DJOFF -> { p.setDj(false); true }
        DjAskDo.NONE, DjAskDo.SEARCH -> false
    }

    /** How a song in an answer is found here: the same path, else the same file on the server, else the same title and artist. */
    fun <T> resolve(s: DjAskSong, byPath: (String) -> T?, byFile: (String) -> T?, bySong: (String, String) -> T?): T? =
        byPath(s.path) ?: s.file.takeIf { it.isNotBlank() }?.let(byFile) ?: if (s.title.isNotBlank()) bySong(s.title, s.artist) else null
}
