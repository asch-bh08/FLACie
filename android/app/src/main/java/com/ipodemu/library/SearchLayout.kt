package com.ipodemu.library

/**
 * How the phone's Search lays out what it found, the same as FLACie Web's (SearchLayout.cs, ResultSections.razor). Owner, 25 Nov: the search page
 * was dated and hard to use; on the web "heads will roll" showed playlists and downloads before the song the library has.
 *
 * The song asked for comes first and big (Top result), then the library's Songs, Artists, Albums and Playlists (yours, then the ready-made ones),
 * and the catalog ("More music to download") last. When the library already has the song asked for, the catalog's copies of it are folded under one
 * small "Other copies to download" line. A search with no Top result (a genre, "2010s") puts the ready-made playlists first: they are the answer.
 */
object SearchLayout {
    enum class Section { TOP, SONGS, ARTISTS, ALBUMS, PLAYLISTS, MADE, DOWNLOAD }

    /** The filter chips, in the order the page shows them: "" is All. */
    val KINDS = listOf("" to "All", "songs" to "Songs", "artists" to "Artists", "albums" to "Albums", "playlists" to "Playlists", "downloadable" to "Downloadable")

    /** The sections shown, in order, for what was found and the chip picked ([only]: "", "songs", "artists", "albums", "playlists" or "downloadable"). */
    fun order(top: Boolean, songs: Boolean, artists: Boolean, albums: Boolean, playlists: Boolean, made: Boolean, download: Boolean, only: String = ""): List<Section> {
        val out = ArrayList<Section>()
        when (only) {
            "songs" -> { if (songs || top) out += Section.SONGS; if (download) out += Section.DOWNLOAD }
            "artists" -> if (artists) out += Section.ARTISTS
            "albums" -> { if (albums) out += Section.ALBUMS; if (download) out += Section.DOWNLOAD }
            "playlists" -> { if (playlists) out += Section.PLAYLISTS; if (made) out += Section.MADE }
            "downloadable" -> if (download) out += Section.DOWNLOAD
            else -> {
                if (made && !top) out += Section.MADE
                if (top) out += Section.TOP
                if (songs) out += Section.SONGS
                if (artists) out += Section.ARTISTS
                if (albums) out += Section.ALBUMS
                if (playlists) out += Section.PLAYLISTS
                if (made && top) out += Section.MADE
                if (download) out += Section.DOWNLOAD
            }
        }
        return out
    }

    /** What the Top result is: the best match of any kind (owner, 26 Nov: "that could be a playlist if I search for 2026"). The web's SearchLayout.PickTop. */
    enum class TopKind { NONE, ARTIST, ALBUM, PLAYLIST, SONG }

    /** [songExact]: the best song is named exactly what was typed, so it wins over everything. Otherwise an artist named like the search, an album, a
     *  playlist named so, then the best song. The phone is always narrow: the Top result comes first and full width, then the rest (the web puts the
     *  Playlists beside it from 900 px up, 50/50). */
    fun pickTop(artist: Boolean, album: Boolean, playlist: Boolean, song: Boolean, songExact: Boolean): TopKind = when {
        songExact && song -> TopKind.SONG
        artist -> TopKind.ARTIST
        album -> TopKind.ALBUM
        playlist -> TopKind.PLAYLIST
        song -> TopKind.SONG
        else -> TopKind.NONE
    }

    /** The song whatever the version and whoever sings it: "Heads Will Roll", "Heads Will Roll (A-Trak Remix)" and "Heads Will Roll - Live" are one. */
    fun titleCore(title: String): String = songKey(title, "").substringBefore('|')

    /** The library's best song when it is the song asked for: every word of its name was typed (the artist may be typed too).
     *  "heads will roll" asks for Heads Will Roll; "heads" might mean another song, so nothing is folded for it. */
    fun askedSong(query: String, bestTitle: String?): String? {
        if (bestTitle.isNullOrBlank()) return null
        val typed = SearchRank.words(query).toHashSet()
        val name = SearchRank.words(titleCore(bestTitle))
        return if (name.isNotEmpty() && name.all { it in typed }) bestTitle else null
    }

    /** The catalog's songs split in two: the rows shown, and the copies of the song the library has (folded away). Without one, all are shown. */
    fun <T> foldCopies(catalog: List<T>, titleOf: (T) -> String, ownedTitle: String?): Pair<List<T>, List<T>> {
        if (ownedTitle.isNullOrBlank()) return catalog to emptyList()
        val core = titleCore(ownedTitle)
        return catalog.partition { titleCore(titleOf(it)) != core }
    }
}
