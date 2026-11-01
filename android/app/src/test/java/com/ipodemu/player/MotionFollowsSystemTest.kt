package com.ipodemu.player

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Animations play only when the app's own switch is on AND the phone's "Remove animations" (animator duration scale 0) is off: the 1.0 bar says
 *  reduced motion is respected, and on Android that setting is where people ask for it. */
class MotionFollowsSystemTest {
    @Test fun on_when_both_allow() = assertTrue(Tweaks.motionOn(appSwitch = true, systemScale = 1f))
    @Test fun a_slower_system_scale_still_animates() = assertTrue(Tweaks.motionOn(appSwitch = true, systemScale = .5f))
    @Test fun off_when_the_phone_removes_animations() = assertFalse(Tweaks.motionOn(appSwitch = true, systemScale = 0f))
    @Test fun off_when_the_app_switch_is_off() = assertFalse(Tweaks.motionOn(appSwitch = false, systemScale = 1f))
}
