package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** How a page change moves (as the web's pages since b125): deeper slides in from the right, a tab change cross-fades with a small rise,
 *  Back fades in (the swipe-back drag already moved the page), and nothing moves with Animations off. */
class PageMoveTest {
    @Test fun going_deeper_slides_in() = assertEquals(PageMove.PUSH, PageMove.of(motion = true, fromDepth = 1, toDepth = 2, sameTop = false))
    @Test fun going_back_fades_in() = assertEquals(PageMove.BACK, PageMove.of(motion = true, fromDepth = 3, toDepth = 2, sameTop = false))
    @Test fun a_tab_change_cross_fades() = assertEquals(PageMove.TAB, PageMove.of(motion = true, fromDepth = 1, toDepth = 1, sameTop = false))
    @Test fun the_same_page_does_not_move() = assertEquals(PageMove.NONE, PageMove.of(motion = true, fromDepth = 2, toDepth = 2, sameTop = true))
    @Test fun nothing_moves_with_animations_off() = assertEquals(PageMove.NONE, PageMove.of(motion = false, fromDepth = 1, toDepth = 2, sameTop = false))
    @Test fun a_push_starts_off_to_the_side_and_see_through() {
        val s = PageMove.PUSH.start
        assertEquals(0.28f, s.slide0, 0.001f)
        org.junit.Assert.assertTrue(s.alpha0 < 1f)
    }
    @Test fun none_starts_where_it_ends() {
        val s = PageMove.NONE.start
        assertEquals(1f, s.alpha0, 0f); assertEquals(0f, s.slide0, 0f); assertEquals(0f, s.rise0, 0f)
    }
}
