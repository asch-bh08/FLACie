package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Test

/** Search rows and cards ask the catalogue for a small cover, not the 1000 px one (covers took ages on a weak signal; owner, 1 Nov). */
class RemoteArtSizeTest {
    @Test fun a_deezer_cover_is_asked_for_at_250_px() =
        assertEquals("https://e-cdns-images.dzcdn.net/images/cover/abc/250x250-000000-80-0-0.jpg",
            CoverLookup.small("https://e-cdns-images.dzcdn.net/images/cover/abc/1000x1000-000000-80-0-0.jpg"))

    @Test fun an_itunes_cover_is_asked_for_at_250_px() =
        assertEquals("https://is1-ssl.mzstatic.com/image/thumb/x/250x250bb.jpg", CoverLookup.small("https://is1-ssl.mzstatic.com/image/thumb/x/600x600bb.jpg"))

    @Test fun any_other_address_is_left_alone() =
        assertEquals("https://example.com/a.jpg", CoverLookup.small("https://example.com/a.jpg"))
}
