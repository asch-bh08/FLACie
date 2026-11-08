package com.ipodemu.library

/**
 * The one line above the mini player that says what is downloading right now, so nobody has to dig through menus to find out (owner, 1 Nov). Pure.
 * [fetching]: songs being fetched into the library (label, its latest message); [offlineLeft]: songs still to save on the phone; [offlinePercent]: the one
 * being saved now (-1 when none). Null when nothing is going on. The server's downloads are for admins only (owner, 7 Nov); what the phone saves for
 * offline is the person's own, so it shows for everyone.
 */
object DownloadActivity {
    private val percent = Regex("""\d+\s?%""")

    fun line(fetching: List<Pair<String, String>>, offlineLeft: Int, offlinePercent: Int, admin: Boolean = true): String? {
        @Suppress("NAME_SHADOWING") val fetching = if (admin) fetching else emptyList()
        val parts = ArrayList<String>()
        if (fetching.isNotEmpty()) {
            val (label, message) = fetching.first()
            val pc = percent.find(message)?.value?.replace(" ", "")
            parts += if (fetching.size == 1) "Getting ${label.substringBefore(" · ")}" + (pc?.let { " · $it" } ?: "")
            else "Getting ${fetching.size} songs" + (pc?.let { " · $it" } ?: "")
        }
        if (offlineLeft > 0) parts += "Saving ${if (offlineLeft == 1) "1 song" else "$offlineLeft songs"} for offline" + (if (offlinePercent >= 0 && fetching.isEmpty()) " · $offlinePercent%" else "")
        return parts.takeIf { it.isNotEmpty() }?.joinToString(" · ")
    }
}
