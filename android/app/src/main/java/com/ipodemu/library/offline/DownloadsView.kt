package com.ipodemu.library.offline

/** The small mark a song, playlist or album carries: nothing, on its way, or on the phone. Spotify's green arrow, here in the app's own accent. */
enum class DownloadMark { NONE, DOWNLOADING, DOWNLOADED }

/**
 * Which mark to show, from the in-memory offline list only (keyed by [OfflineMath.keyOf], never a disk look-up), so a row can ask while it is drawn. Pure, unit-tested.
 */
object DownloadMarks {
    /** A finished song is Downloaded; a queued or running one shows the small busy mark; a failed or unknown one shows nothing (the Downloads section says what failed). */
    fun ofSong(state: OfflineState?): DownloadMark = when (state) {
        OfflineState.DONE -> DownloadMark.DOWNLOADED
        OfflineState.QUEUED, OfflineState.DOWNLOADING -> DownloadMark.DOWNLOADING
        else -> DownloadMark.NONE
    }

    /**
     * A playlist or album: Downloaded only when every song that can be downloaded is on the phone; the busy mark only when all of them are at least on their way
     * (so a list that merely shares a song with another download stays calm and shows nothing). Songs that can't be downloaded (files already on the phone, other servers) are left out
     * of the count, so they never hold the mark back; a list with none that can be downloaded has no mark.
     */
    fun ofList(paths: Collection<String>, state: (String) -> OfflineState?): DownloadMark {
        val keys = HashMap<String, OfflineState?>()
        for (p in paths) { val s = state(p); if (s != null || OfflineMath.keyOf(p) != p) keys.putIfAbsent(OfflineMath.keyOf(p), s) }
        if (keys.isEmpty()) return DownloadMark.NONE
        val states = keys.values
        return when {
            states.all { it == OfflineState.DONE } -> DownloadMark.DOWNLOADED
            states.all { it != null } && states.any { it == OfflineState.QUEUED || it == OfflineState.DOWNLOADING } -> DownloadMark.DOWNLOADING
            else -> DownloadMark.NONE
        }
    }
}

/** A playlist or an album as the Downloads section sees it: its songs' paths and how to show it. [subtitle] is what the row says when it has nothing better ("Playlist", the artist). */
class ListSpec(val id: String, val name: String, val subtitle: String, val artKey: String?, val paths: List<String>)

/** A playlist or album with every song on the phone, ready to be a row. */
class DownloadedList(val id: String, val name: String, val subtitle: String, val artKey: String?, val songs: Int, val bytes: Long) {
    /** "12 songs · 380 MB" */
    val detail: String get() = DownloadsOverview.storageLine(songs, bytes)
}

/** What the Downloads section (phone and car) shows and says. Pure, unit-tested. */
object DownloadsOverview {
    /** The lists out of [specs] whose every downloadable song is finished, with their size, by name (case ignored). [entry] looks a song up by its path. */
    fun lists(specs: List<ListSpec>, entry: (String) -> OfflineEntry?): List<DownloadedList> =
        specs.mapNotNull { s ->
            if (DownloadMarks.ofList(s.paths) { entry(it)?.state } != DownloadMark.DOWNLOADED) return@mapNotNull null
            val done = s.paths.mapNotNull { entry(it) }.filter { it.state == OfflineState.DONE }.distinctBy { it.key }
            DownloadedList(s.id, s.name, s.subtitle, s.artKey, done.size, done.sumOf { it.bytes })
        }.sortedBy { it.name.lowercase() }

    /** Every finished song, by title. */
    fun songs(entries: List<OfflineEntry>): List<OfflineEntry> = entries.filter { it.state == OfflineState.DONE }.sortedBy { it.title.lowercase() }

    /** "Downloading 12 of 54", or "Waiting for Wi-Fi · 12 of 54" when it can't go on; null when nothing is on its way. */
    fun progressLine(done: Int, pending: Int, waitReason: String?): String? {
        if (pending <= 0) return null
        val of = "$done of ${done + pending}"
        return if (waitReason == null) "Downloading $of" else "$waitReason · $of"
    }

    /** "3 songs could not download", or null for none. */
    fun failedLine(failed: Int): String? = if (failed <= 0) null else "${OfflineMath.songs(failed)} could not download"

    /** "14 songs · 1.2 GB", the total on the section's header and under each list. */
    fun storageLine(songs: Int, bytes: Long): String = if (songs <= 0) "Nothing downloaded yet" else "${OfflineMath.songs(songs)} · ${OfflineMath.formatBytes(bytes)}"
}
