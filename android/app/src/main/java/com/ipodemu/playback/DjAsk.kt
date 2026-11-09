package com.ipodemu.playback

import org.json.JSONObject

/**
 * "Talk to the DJ" on the phone: the server (POST /api/dj/ask) understands what was said, so there is one parser for every player; the phone only carries
 * the answer out. The same as `DjAskAnswer` / `DjAskRunner` in FLACie.Core. Only the recognised words are sent, never sound.
 */
/** SEARCH: the search box's mic heard words that are not a request (they fill the search). */
enum class DjAskDo { NONE, SEARCH, PLAY, UPNEXT, NEXT, PAUSE, RESUME, VOLUMEUP, VOLUMEDOWN, DJON, DJOFF,
    /** The F9 steps: playlist edits (by the playlist's name), a question back, shuffle on or off. */
    ADDTOPLAYLIST, REMOVEFROMPLAYLIST, CREATEPLAYLIST, ASK, SHUFFLE, RENAMEPLAYLIST }

/** A song in an answer: the server's path, and its file, title, artist and album so this library can find its own copy (the album tells the live
 *  "Out of Space" from the album one, owner 9 Nov). */
data class DjAskSong(val path: String, val file: String = "", val title: String = "", val artist: String = "", val album: String = "")

/** One step of an answer (F9): [playlist] is the playlist's name; [say] the question of an ASK step; RENAMEPLAYLIST renames [playlist] to [title]. */
data class DjAskStep(
    val doThis: DjAskDo, val songs: List<DjAskSong> = emptyList(), val title: String = "", val shuffle: Boolean = false, val replace: Boolean = false,
    val playlist: String = "", val say: String = "",
) {
    companion object {
        fun parse(o: JSONObject): DjAskStep = DjAskStep(
            DjAskDo.entries.firstOrNull { it.name.equals(o.optString("do"), ignoreCase = true) } ?: DjAskDo.NONE, DjAskAnswer.songsOf(o),
            o.optString("title"), o.optBoolean("shuffle"), o.optBoolean("replace"), o.optString("playlist"), o.optString("say"))
    }
}

data class DjAskAnswer(
    val doThis: DjAskDo, val say: String, val songs: List<DjAskSong> = emptyList(), val title: String = "", val shuffle: Boolean = false,
    val replace: Boolean = false, val waitArtist: String? = null, val waitTitle: String? = null, val clip: String? = null, val deviceVoice: Boolean = false,
    /** The DJ asked the person something back (the server's intent "Clarify"): the mic listens for the reply. */
    val asked: Boolean = false,
    /** The server's name for what it understood ("Answer", "Clarify", "Goodbye", "ThumbsDown"...): the conversation's turn-taking reads it. */
    val intent: String = "",
    /** The steps to carry out in order (F9); null for an old single-action answer, which is then its one step. */
    val steps: List<DjAskStep>? = null,
    /** A song being fetched for a playlist: added to [waitAddTo] when it lands, then that playlist shuffled with it first when [waitShuffle] (6 Nov). */
    val waitAddTo: String? = null, val waitShuffle: Boolean = false,
) {
    /** What the answer does, for the conversation: [doThis], else (an answer made of steps may say "none" on top) the step that decides the music, else the first real one. */
    val mainDo: DjAskDo get() = if (doThis != DjAskDo.NONE) doThis else steps?.let { st ->
        (st.firstOrNull { it.doThis in MUSIC } ?: st.firstOrNull { it.doThis != DjAskDo.NONE && it.doThis != DjAskDo.ASK })?.doThis
    } ?: DjAskDo.NONE

    /** The steps in order: the answer's own, or its single action. */
    val allSteps: List<DjAskStep> get() = steps ?: if (doThis == DjAskDo.NONE || doThis == DjAskDo.SEARCH) emptyList() else listOf(DjAskStep(doThis, songs, title, shuffle, replace))

    companion object {
        private val MUSIC = setOf(DjAskDo.PLAY, DjAskDo.NEXT, DjAskDo.RESUME, DjAskDo.PAUSE)

        /** The songs of an answer or a step: the full "songs", else the bare "paths". */
        fun songsOf(o: JSONObject): List<DjAskSong> =
            o.optJSONArray("songs")?.let { a -> (0 until a.length()).mapNotNull { i -> a.optJSONObject(i)?.let { s -> DjAskSong(s.optString("path"), s.optString("file"), s.optString("title"), s.optString("artist"), s.optString("album")) } } }
                ?: o.optJSONArray("paths")?.let { a -> (0 until a.length()).map { DjAskSong(a.optString(it)) } } ?: emptyList()

        val SORRY = DjAskAnswer(DjAskDo.NONE, "Sorry, I couldn't reach the DJ.")

        fun parse(text: String): DjAskAnswer {
            val o = JSONObject(text)
            val d = DjAskDo.entries.firstOrNull { it.name.equals(o.optString("do"), ignoreCase = true) } ?: DjAskDo.NONE
            val songs = songsOf(o)
            val steps = o.optJSONArray("steps")?.let { a -> (0 until a.length()).mapNotNull { i -> a.optJSONObject(i)?.let(DjAskStep::parse) } }
            val w = o.optJSONObject("wait")
            return DjAskAnswer(d, o.optString("say"), songs, o.optString("title"), o.optBoolean("shuffle"), o.optBoolean("replace"),
                w?.optString("artist"), w?.optString("title"), o.optString("clip").takeIf { it.isNotBlank() && it != "null" }, o.optBoolean("deviceVoice"),
                o.optString("intent") == "Clarify" || steps?.any { it.doThis == DjAskDo.ASK && it.say.isNotBlank() } == true, o.optString("intent"), steps,
                w?.optString("addTo")?.takeIf { it.isNotBlank() && it != "null" }, w?.optBoolean("shuffle") ?: false)
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
    // The F9 steps (a player that can't do one leaves it alone). Playlists are named; a missing one is made.
    fun addToPlaylist(name: String, list: List<T>) {}
    fun removeFromPlaylist(name: String, list: List<T>) {}
    fun createPlaylist(name: String, list: List<T>) {}
    fun renamePlaylist(name: String, to: String) {}
    /** Says [question] if the answer's own line did not (the mic then listens for the reply). */
    fun ask(question: String) {}
    fun setShuffle(on: Boolean) {}
}

object DjAskRunner {
    /** A fetched song has landed: the order to play, the song first, then (when shuffling) the rest of its playlist in a random order. */
    fun landedOrder(song: String, playlist: List<String>, shuffle: Boolean): List<String> =
        if (!shuffle) listOf(song) else listOf(song) + playlist.filter { it != song }.distinct().shuffled()

    const val VOLUME_STEP = 0.15f

    /** Carries [a]'s steps out in order. True when it changed anything. */
    fun <T> run(p: DjAskPlayer<T>, a: DjAskAnswer): Boolean {
        var any = false
        for (step in a.allSteps) any = run(p, step) || any
        return any
    }

    fun <T> run(p: DjAskPlayer<T>, a: DjAskStep): Boolean {
        fun found() = a.songs.mapNotNull(p::find)
        return when (a.doThis) {
            DjAskDo.PLAY -> found().takeIf { it.isNotEmpty() }?.let { p.play(it, a.title, a.shuffle); true } ?: false
            DjAskDo.UPNEXT -> found().takeIf { it.isNotEmpty() }?.let { p.upNext(it, a.replace); true } ?: false
            DjAskDo.ADDTOPLAYLIST -> if (a.playlist.isBlank()) false else found().takeIf { it.isNotEmpty() }?.let { p.addToPlaylist(a.playlist, it); true } ?: false
            DjAskDo.REMOVEFROMPLAYLIST -> if (a.playlist.isBlank()) false else found().takeIf { it.isNotEmpty() }?.let { p.removeFromPlaylist(a.playlist, it); true } ?: false
            DjAskDo.CREATEPLAYLIST -> if (a.playlist.isBlank()) false else { p.createPlaylist(a.playlist, found()); true }
            DjAskDo.RENAMEPLAYLIST -> if (a.playlist.isBlank() || a.title.isBlank()) false else { p.renamePlaylist(a.playlist, a.title); true }
            DjAskDo.ASK -> if (a.say.isBlank()) false else { p.ask(a.say); true }
            DjAskDo.SHUFFLE -> { p.setShuffle(a.shuffle); true }
            DjAskDo.NEXT -> { p.next(); true }
            DjAskDo.PAUSE -> { p.pause(); true }
            DjAskDo.RESUME -> { p.resume(); true }
            DjAskDo.VOLUMEUP -> { p.nudgeVolume(VOLUME_STEP); true }
            DjAskDo.VOLUMEDOWN -> { p.nudgeVolume(-VOLUME_STEP); true }
            DjAskDo.DJON -> { p.setDj(true); true }
            DjAskDo.DJOFF -> { p.setDj(false); true }
            DjAskDo.NONE, DjAskDo.SEARCH -> false
        }
    }

    /** How a song in an answer is found here: the same path, else the same file on the server, else the same title and artist. */
    fun <T> resolve(s: DjAskSong, byPath: (String) -> T?, byFile: (String) -> T?, bySong: (String, String, String) -> T?): T? =
        byPath(s.path) ?: s.file.takeIf { it.isNotBlank() }?.let(byFile) ?: if (s.title.isNotBlank()) bySong(s.title, s.artist, s.album) else null
}
