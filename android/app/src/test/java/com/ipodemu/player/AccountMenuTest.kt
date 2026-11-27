package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** More (the account menu) offers what the web's does (TopBar.razor, ONE APP 1f), in the same order: the places as a grid, Sign out apart at the end. */
class AccountMenuTest {
    @Test fun anAdministratorSeesTheWebsOrder() =
        assertEquals(listOf(AccountPick.ACCOUNT, AccountPick.SETTINGS, AccountPick.CAR, AccountPick.RETRO, AccountPick.DASHBOARD, AccountPick.DOWNLOADS, AccountPick.SIGN_OUT),
            accountPicks(admin = true, signedIn = true))

    @Test fun someoneElseHasNoDashboardOrDownloads() =
        assertEquals(listOf(AccountPick.ACCOUNT, AccountPick.SETTINGS, AccountPick.CAR, AccountPick.RETRO, AccountPick.SIGN_OUT), accountPicks(admin = false, signedIn = true))

    @Test fun signedOutThereIsNothingToSignOutOf() =
        assertEquals(listOf(AccountPick.ACCOUNT, AccountPick.SETTINGS, AccountPick.CAR, AccountPick.RETRO), accountPicks(admin = false, signedIn = false))
}
