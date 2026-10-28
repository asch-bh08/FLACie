package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** The account menu offers what the web's does (TopBar.razor), in the same order. */
class AccountMenuTest {
    @Test fun anAdministratorSeesTheWebsSixInItsOrder() =
        assertEquals(listOf(AccountPick.ACCOUNT, AccountPick.DASHBOARD, AccountPick.DOWNLOADS, AccountPick.RETRO, AccountPick.SETTINGS, AccountPick.SIGN_OUT),
            accountPicks(admin = true, signedIn = true))

    @Test fun someoneElseHasNoDashboardOrDownloads() =
        assertEquals(listOf(AccountPick.ACCOUNT, AccountPick.RETRO, AccountPick.SETTINGS, AccountPick.SIGN_OUT), accountPicks(admin = false, signedIn = true))

    @Test fun signedOutThereIsNothingToSignOutOf() =
        assertEquals(listOf(AccountPick.ACCOUNT, AccountPick.RETRO, AccountPick.SETTINGS), accountPicks(admin = false, signedIn = false))
}
