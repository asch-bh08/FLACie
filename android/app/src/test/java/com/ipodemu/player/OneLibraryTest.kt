package com.ipodemu.player

import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Test

/** ONE APP consolidate: Jellyfin, Plex, NAS and this phone's songs are one library with source tags. The per-source lists open Songs with that
 *  source's chip on, not a separate list of their own. */
class OneLibraryTest {
    @Test fun aSourceListIsSongsWithItsChipOn() {
        assertEquals(TrackSource.JELLYFIN, OneLibrary.sourceOf(LibKind.JELLYFIN))
        assertEquals(TrackSource.PLEX, OneLibrary.sourceOf(LibKind.PLEX))
        assertEquals(TrackSource.NAS, OneLibrary.sourceOf(LibKind.NAS))
    }

    @Test fun everyOtherListHasNoPresetSource() {
        for (k in listOf(LibKind.SONGS, LibKind.ALBUMS, LibKind.ARTISTS, LibKind.GENRES, LibKind.PLAYLISTS, LibKind.MEMOS)) assertEquals(null, OneLibrary.sourceOf(k))
    }

    @Test fun aSourceListIsTitledSongs() = assertEquals("Songs", OneLibrary.title(LibKind.NAS))
}
