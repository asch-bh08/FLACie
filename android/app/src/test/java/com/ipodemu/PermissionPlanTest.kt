package com.ipodemu

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PermissionPlanTest {
    private val none = emptySet<String>()

    @Test fun permissionNamesFollowApiLevel() {
        assertEquals(PermissionPlan.READ_EXTERNAL_STORAGE, PermissionPlan.permission(Need.MEDIA, 31))
        assertEquals(PermissionPlan.READ_MEDIA_AUDIO, PermissionPlan.permission(Need.MEDIA, 33))
        assertNull(PermissionPlan.permission(Need.NOTIFICATIONS, 32))
        assertEquals(PermissionPlan.POST_NOTIFICATIONS, PermissionPlan.permission(Need.NOTIFICATIONS, 36))
    }

    @Test fun accessStates() {
        assertEquals(Access.ASK, PermissionPlan.access(Need.MEDIA, 31, none, false))
        assertEquals(Access.DENIED, PermissionPlan.access(Need.MEDIA, 31, none, true))
        assertEquals(Access.GRANTED, PermissionPlan.access(Need.MEDIA, 31, setOf(PermissionPlan.READ_EXTERNAL_STORAGE), false))
        // a permission of the wrong API generation does not count
        assertEquals(Access.ASK, PermissionPlan.access(Need.MEDIA, 36, setOf(PermissionPlan.READ_EXTERNAL_STORAGE), false))
        assertEquals(Access.NOT_NEEDED, PermissionPlan.access(Need.NOTIFICATIONS, 31, none, false))
    }

    @Test fun onboardingOnlyBeforeLoginMode() {
        assertTrue(PermissionPlan.showOnboarding(""))
        assertFalse(PermissionPlan.showOnboarding("account"))
        assertFalse(PermissionPlan.showOnboarding("guest"))
    }

    @Test fun notificationsNeverAtOnboardingOrWithoutPlayback() {
        assertFalse(PermissionPlan.askNotificationsNow("", 36, none, false, playing = true))
        assertFalse(PermissionPlan.askNotificationsNow("account", 36, none, false, playing = false))
        assertTrue(PermissionPlan.askNotificationsNow("account", 36, none, false, playing = true))
        assertTrue(PermissionPlan.askNotificationsNow("guest", 33, none, false, playing = true))
    }

    @Test fun noRepeatPrompts() {
        assertFalse(PermissionPlan.askNotificationsNow("account", 36, setOf(PermissionPlan.POST_NOTIFICATIONS), false, playing = true))
        assertFalse(PermissionPlan.askNotificationsNow("account", 36, none, true, playing = true))
        assertFalse(PermissionPlan.askNotificationsNow("account", 31, none, false, playing = true))
        assertFalse(PermissionPlan.askMediaNow("guest", 31, setOf(PermissionPlan.READ_EXTERNAL_STORAGE), false))
        assertFalse(PermissionPlan.askMediaNow("guest", 31, none, true))
    }

    @Test fun mediaIsOnlyAskedForThisDeviceMusic() {
        assertTrue(PermissionPlan.askMediaNow("guest", 36, none, false))
        assertFalse(PermissionPlan.askMediaNow("account", 36, none, false))
        assertFalse(PermissionPlan.askMediaNow("", 36, none, false))
    }

    @Test fun mediaNotice() {
        assertTrue(PermissionPlan.showMediaNotice("guest", Access.DENIED))
        assertTrue(PermissionPlan.showMediaNotice("guest", Access.ASK))
        assertFalse(PermissionPlan.showMediaNotice("guest", Access.GRANTED))
        assertFalse(PermissionPlan.showMediaNotice("account", Access.DENIED))
    }
}
