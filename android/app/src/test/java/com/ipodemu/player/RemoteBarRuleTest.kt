package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** Owner, 5 Nov: pausing the other device from its bar must not make the bar vanish. */
class RemoteBarRuleTest {
    @Test fun aPlayingDeviceIsShown() = assertEquals("a", RemoteBarRule.pick(listOf("a" to false, "b" to true), null))

    @Test fun theDeviceJustPausedStaysForPlay() = assertEquals("a", RemoteBarRule.pick(listOf("a" to true), "a"))

    @Test fun aPausedDeviceNeverShownIsNotOffered() = assertNull(RemoteBarRule.pick(listOf("a" to true), null))

    @Test fun whenItStopsTheBarGoes() = assertNull(RemoteBarRule.pick(emptyList(), "a"))

    @Test fun anotherDeviceTakingOverReplacesIt() = assertEquals("b", RemoteBarRule.pick(listOf("a" to true, "b" to false), "a"))
}
