package com.ipodemu.player

import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.input.key.Key
import com.ipodemu.library.removalIndex
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class FocusAndRemovalTest {
    @Test fun upAndDownAlwaysLeaveTheSearchField() {
        assertEquals(FocusDirection.Down, leaveDirection(Key.DirectionDown, 3, 3, 10))
        assertEquals(FocusDirection.Up, leaveDirection(Key.DirectionUp, 0, 5, 10))
    }

    @Test fun leftAndRightLeaveOnlyAtTheEdges() {
        assertEquals(FocusDirection.Left, leaveDirection(Key.DirectionLeft, 0, 0, 10))
        assertNull(leaveDirection(Key.DirectionLeft, 4, 4, 10))
        assertNull(leaveDirection(Key.DirectionLeft, 0, 3, 10))   // a selection: the key just collapses it
        assertEquals(FocusDirection.Right, leaveDirection(Key.DirectionRight, 10, 10, 10))
        assertNull(leaveDirection(Key.DirectionRight, 4, 4, 10))
    }

    @Test fun anEmptyFieldLeavesInEveryDirection() {
        for ((k, d) in listOf(Key.DirectionLeft to FocusDirection.Left, Key.DirectionRight to FocusDirection.Right, Key.DirectionUp to FocusDirection.Up, Key.DirectionDown to FocusDirection.Down))
            assertEquals(d, leaveDirection(k, 0, 0, 0))
    }

    @Test fun otherKeysStayInTheField() {
        assertNull(leaveDirection(Key.A, 0, 0, 0))
        assertNull(leaveDirection(Key.Enter, 0, 0, 0))
    }

    @Test fun pendingRemovalFindsTheSongWhereItWas() {
        assertEquals(1, removalIndex(listOf("a", "b", "c"), 1, "b"))
    }

    @Test fun pendingRemovalFollowsTheSongIfTheListShifted() {
        assertEquals(0, removalIndex(listOf("b", "c"), 1, "b"))
        assertEquals(-1, removalIndex(listOf("a", "c"), 1, "b"))
    }
}
