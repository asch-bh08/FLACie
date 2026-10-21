package com.ipodemu.library

/**
 * Merging favourites between devices, by the same rule as FLACie Web (FavoriteSync.cs): each favourite carries when it was made ("w", ms) and each
 * removal is kept with its time (the profile's "favgone"); per song (title and artist) the latest of the two wins. Before, devices only added each
 * other's favourites together, so a song unfavourited on one device came straight back from any other that still had it.
 */
object FavoriteSync {
    data class Fav(val p: String, val t: String, val a: String, val w: Long)
    const val KEEP_REMOVALS = 1000

    private fun key(f: Fav) = if (f.t.isNotBlank()) matchKey(f.t, f.a) else "p:" + f.p

    /** The favourites to keep (in order, local first) and the removals to remember. */
    fun merge(local: List<Fav>, localGone: List<Fav>, remote: List<Fav>, remoteGone: List<Fav>): Pair<List<Fav>, List<Fav>> {
        val gone = HashMap<String, Fav>()
        for (g in localGone + remoteGone) { val k = key(g); val had = gone[k]; if (had == null || g.w > had.w) gone[k] = g }
        val favs = LinkedHashMap<String, Fav>()
        // the same song favourited on two devices: one entry, under this device's path, with the later time
        for (f in local + remote) { val k = key(f); val had = favs[k]; if (had == null) favs[k] = f else if (f.w > had.w) favs[k] = f.copy(p = had.p) }
        val out = favs.filter { (k, f) -> gone[k]?.let { f.w > it.w } ?: true }.values.toList()
        return out to gone.values.sortedByDescending { it.w }.take(KEEP_REMOVALS)
    }
}
