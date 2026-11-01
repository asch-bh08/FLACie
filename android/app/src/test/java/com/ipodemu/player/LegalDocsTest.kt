package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Settings > About > Licences and Privacy show the files the build copies into assets/legal (app/build.gradle.kts, legalFiles), as plain text:
 *  the files are Markdown, and the first version showed its #s, table bars and [link](address) brackets on the phone. */
class LegalDocsTest {
    @Test fun licences_are_the_notices_then_flacies_own_licence() =
        assertEquals(listOf("legal/THIRD-PARTY-NOTICES.md", "legal/LICENSE.txt", "legal/licenses/LGPL-2.1.txt", "legal/licenses/Apache-2.0.txt"), LegalDocs.LICENCES.map { it.second })

    @Test fun privacy_is_the_privacy_notice() = assertEquals(listOf("legal/PRIVACY.md"), LegalDocs.PRIVACY.map { it.second })

    @Test fun paragraphs_split_at_blank_lines_and_wrapped_lines_join() =
        assertEquals(listOf(LegalDocs.Para("One line next line"), LegalDocs.Para("Two")), LegalDocs.paragraphs("One line\nnext line\r\n\r\n\n\nTwo\n"))

    @Test fun headings_lose_their_hashes_and_are_marked() =
        assertEquals(listOf(LegalDocs.Para("Libraries under the LGPL", heading = true), LegalDocs.Para("Text")), LegalDocs.paragraphs("## Libraries under the LGPL\n\nText"))

    @Test fun links_code_and_bold_read_as_words() =
        assertEquals(listOf(LegalDocs.Para("See LICENSE and the licenses folder in assets/retro, provided as is.")),
            LegalDocs.paragraphs("See [LICENSE](LICENSE) and the [licenses folder](licenses/) in `assets/retro`, provided **as is**."))

    @Test fun table_rows_become_lines_and_the_rule_goes() =
        assertEquals(listOf(LegalDocs.Para("Library · Licence\nSMBLibrary · LGPL-3.0")), LegalDocs.paragraphs("| Library | Licence |\n|---|---|\n| SMBLibrary | LGPL-3.0 |"))

    @Test fun list_items_stay_on_their_own_lines() =
        assertEquals(listOf(LegalDocs.Para("• one\n• two")), LegalDocs.paragraphs("- one\n- two"))

    @Test fun nothing_to_show_is_said_plainly() = assertTrue(LegalDocs.paragraphs("").single().text.isNotBlank())
}
