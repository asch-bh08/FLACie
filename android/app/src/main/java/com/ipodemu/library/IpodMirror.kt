package com.ipodemu.library

/** A playlist as it is on a connected iPod right now: its name and its songs (title + artist) in order. */
class IpodSnap(val name: String, val songs: List<Pair<String, String>>)

/**
 * Keeps the account's playlists in step with the playlists on real iPods. Reading an iPod (or writing to it) replaces the account's copy of each
 * of its playlists with what the iPod holds: same name, same songs, same order, so counts and renames follow the iPod. An account playlist with
 * the same name that came from a folder or by hand (ignoring a quality tag such as "(FLAC)") is taken over rather than duplicated. A playlist that
 * was on this iPod before and isn't now is removed from the account too. Pure logic, so it is unit-tested without a phone.
 */
object IpodMirror {
    class Plan(val id: String, val name: String, val paths: List<String>, val meta: Map<String, Pair<String, String>>, val key: String)

    private val qualityTag = Regex("""\s*\((f?lac|alac|mp3|aac|wav|ogg|opus|hi-?res)\)\s*$""", RegexOption.IGNORE_CASE)
    fun baseName(n: String) = n.replace(qualityTag, "").trim().lowercase()
    fun keyOf(device: String, name: String) = "$device|$name"

    /** [resolve] maps a title and artist to a path this device can play, or null when the library lacks the song. */
    fun plan(device: String, snaps: List<IpodSnap>, resolve: (String, String) -> String?): List<Plan> = snaps.map { s ->
        val meta = HashMap<String, Pair<String, String>>()
        val paths = s.songs.mapIndexed { i, (title, artist) ->
            resolve(title, artist) ?: "ipod:$device/${keyOf("", s.name).hashCode().toUInt().toString(36)}/$i".also { meta[it] = title to artist }
        }
        Plan("ip" + keyOf(device, s.name).hashCode().toUInt().toString(36), s.name, paths, meta, keyOf(device, s.name))
    }

    /** Which existing playlist a plan lands on: the one already linked to this iPod playlist, else a same-named unlinked one, else none (a new one). */
    fun target(existing: List<UserPlaylist>, p: Plan): UserPlaylist? =
        existing.firstOrNull { it.ip == p.key } ?: existing.firstOrNull { it.ip == null && baseName(it.name) == baseName(p.name) }
}
