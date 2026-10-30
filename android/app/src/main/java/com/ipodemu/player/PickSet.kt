package com.ipodemu.player

import com.ipodemu.library.Track

/**
 * The rows ticked in a list (Select mode). Immutable: every tick gives a new set, so Compose sees the change. The ids are whatever names a row (a path, an artist and title).
 * Also what "Download N" does with a pick that mixes songs already in the library with catalogue results that are not. Pure, unit-tested.
 */
class PickSet(private val ids: Set<String> = emptySet()) {
    val count: Int get() = ids.size
    operator fun contains(id: String) = id in ids
    fun toggle(id: String) = PickSet(if (id in ids) ids - id else ids + id)
    fun allOf(all: Collection<String>) = all.isNotEmpty() && ids.containsAll(all)
    /** The Select all button: everything ticked, or (when it all is already) nothing. */
    fun selectAllOrNone(all: Collection<String>) = if (allOf(all)) PickSet() else PickSet(ids + all)
    fun asList(): List<String> = ids.toList()

    /** [keep]: picked songs that are in the library (kept on the phone); [fetch]: the ids with no copy yet (asked for from the catalogue). */
    class Split(val keep: List<Track>, val fetch: List<String>)

    companion object {
        fun downloadLabel(n: Int) = if (n > 0) "Download $n" else "Download"

        fun split(picked: List<String>, owned: (String) -> Track?): Split {
            val keep = ArrayList<Track>(); val fetch = ArrayList<String>(); val seen = HashSet<String>()
            for (id in picked) {
                if (!seen.add(id)) continue
                val t = owned(id)
                if (t != null) keep += t else fetch += id
            }
            return Split(keep, fetch)
        }

        private fun songs(n: Int) = if (n == 1) "1 song" else "$n songs"
        fun summary(keep: Int, fetch: Int): String = when {
            keep > 0 && fetch > 0 -> "Keeping ${songs(keep)} on this phone and getting $fetch more"
            keep > 0 -> "Keeping ${songs(keep)} on this phone"
            fetch > 0 -> "Getting ${songs(fetch)}"
            else -> "Nothing to download"
        }
    }
}
