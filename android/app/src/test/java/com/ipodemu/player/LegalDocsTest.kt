package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Settings > About > Licences and Privacy show the files the build copies into assets/legal (app/build.gradle.kts, legalFiles). */
class LegalDocsTest {
    @Test fun licences_are_the_notices_then_flacies_own_licence() =
        assertEquals(listOf("legal/THIRD-PARTY-NOTICES.md", "legal/LICENSE.txt", "legal/licenses/LGPL-2.1.txt", "legal/licenses/Apache-2.0.txt"), LegalDocs.LICENCES.map { it.second })

    @Test fun privacy_is_the_privacy_notice() = assertEquals(listOf("legal/PRIVACY.md"), LegalDocs.PRIVACY.map { it.second })

    @Test fun text_is_split_into_paragraphs_at_blank_lines_so_a_long_licence_scrolls_smoothly() {
        val p = LegalDocs.paragraphs("# Title\r\n\r\nOne line\nnext line\n\n\n\nTwo\n")
        assertEquals(listOf("# Title", "One line\nnext line", "Two"), p)
    }

    @Test fun nothing_to_show_is_said_plainly() = assertTrue(LegalDocs.paragraphs("").single().isNotBlank())
}
