package com.ipodemu.library

/** Which stored favourite a song on this phone stands for: its own path, or a favourite kept under another device's path (the web, another
 * phone) whose title and artist ([UserData.meta]) are the same song. The Favourites list already matched them this way; the heart did not. */
object FavoriteMatch {
    fun storedFor(path: String, key: String, favorites: Collection<String>, meta: Map<String, Pair<String, String>>): String? {
        if (path in favorites) return path
        return favorites.firstOrNull { p -> meta[p]?.let { (t, a) -> t.isNotEmpty() && matchKey(t, a) == key } == true }
    }
}
