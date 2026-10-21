package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Test

/** Favourites between devices, by the web's rule (FavoriteSync.cs): per song the latest of favouriting and removing wins. */
class FavoriteSyncTest {
    private fun f(t: String, w: Long, p: String = "/m/$t") = FavoriteSync.Fav(p, t, "Artist", w)
    private fun titles(l: List<FavoriteSync.Fav>) = l.map { it.t }.sorted()

    @Test fun both_sides_kept() = assertEquals(listOf("One", "Two"), titles(FavoriteSync.merge(listOf(f("One", 10)), emptyList(), listOf(f("Two", 20)), emptyList()).first))

    @Test fun a_later_removal_elsewhere_removes_it() {
        val (favs, gone) = FavoriteSync.merge(listOf(f("One", 10), f("Two", 10)), emptyList(), emptyList(), listOf(f("One", 50)))
        assertEquals(listOf("Two"), titles(favs)); assertEquals(listOf("One"), titles(gone))
    }

    @Test fun favourited_again_after_the_removal_stays() =
        assertEquals(listOf("One"), titles(FavoriteSync.merge(listOf(f("One", 90)), emptyList(), emptyList(), listOf(f("One", 50))).first))

    @Test fun the_same_song_under_another_path_is_one() =
        assertEquals(1, FavoriteSync.merge(listOf(f("One", 10)), emptyList(), listOf(f("One", 5, "jf:abc")), emptyList()).first.size)
}
