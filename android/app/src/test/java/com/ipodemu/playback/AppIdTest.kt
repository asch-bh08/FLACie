package com.ipodemu.playback

import com.ipodemu.BuildConfig
import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * The app's id on the phone is FLACie's own (app.flacie, next to FLACie Retro's app.flacie.retro), not "ipodemu". The covers that other
 * apps (the car, the lock screen) read live under that id too: two apps can't share one address, so a fixed old one would stop the app installing.
 */
class AppIdTest {
    @Test fun appIdIsFlacies() = assertEquals("app.flacie", BuildConfig.APPLICATION_ID)

    @Test fun coversLiveUnderTheAppId() = assertEquals("app.flacie.art", ArtProvider.AUTHORITY)
}
