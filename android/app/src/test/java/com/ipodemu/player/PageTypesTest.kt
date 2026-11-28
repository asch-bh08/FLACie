package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** ONE APP 1k, the one frame on the phone (docs/plans/one-app-consolidate.md): every screen in the stack is a Standard page with the shared top
 *  bar, the tab bar and the mini player; the five tab roots have no back button; a page keeps its tab lit, as on the web (PageFrame.TabFor). */
class PageTypesTest {
    @Test fun theFiveRootsAreTheTabsAndHaveNoBack() {
        for (s in listOf(Screen.Home, Screen.Search, Screen.Sources, Screen.Devices, Screen.Settings)) assertEquals(s.toString(), true, PageTypes.isTabRoot(s))
        for (s in listOf(Screen.Jam, Screen.Account, Screen.Lib(LibKind.SONGS), Screen.Detail(DetailKind.ALBUM, "a"))) assertEquals(s.toString(), false, PageTypes.isTabRoot(s))
    }

    @Test fun everyStackScreenIsStandard() {
        for (s in listOf(Screen.Home, Screen.Devices, Screen.Jam, Screen.Account, Screen.DownloadLog, Screen.Headphones, Screen.Queue, Screen.Legal(false)))
            assertEquals(PageType.STANDARD, PageTypes.typeOf(s))
    }

    @Test fun aPageKeepsItsTabLitAsOnTheWeb() {
        assertEquals(Tab.LIBRARY, PageTypes.tabFor(Screen.Lib(LibKind.SONGS)))
        assertEquals(Tab.LIBRARY, PageTypes.tabFor(Screen.Detail(DetailKind.ALBUM, "a")))
        assertEquals(Tab.SEARCH, PageTypes.tabFor(Screen.Library))
        assertEquals(Tab.DEVICES, PageTypes.tabFor(Screen.Jam))
        assertEquals(Tab.SETTINGS, PageTypes.tabFor(Screen.Account))
        assertEquals(Tab.SETTINGS, PageTypes.tabFor(Screen.Headphones))
        assertEquals(Tab.SETTINGS, PageTypes.tabFor(Screen.DownloadLog))
        assertEquals(Tab.HOME, PageTypes.tabFor(Screen.Home))
    }

    @Test fun theDevicesTabOpensTheDevicesPage() = assertEquals(Screen.Devices, Tab.DEVICES.root)

    @Test fun anOpenFlagPushesItsPageAndLeavingThePageClearsIt() {
        val stack = mutableListOf<Screen>(Screen.Home)
        PageTypes.open(stack, Screen.Jam)
        assertEquals(listOf(Screen.Home, Screen.Jam), stack)
        PageTypes.open(stack, Screen.Jam)   // already on top: not twice
        assertEquals(listOf(Screen.Home, Screen.Jam), stack)
        PageTypes.close(stack, Screen.Jam)
        assertEquals(listOf(Screen.Home), stack)
        PageTypes.close(stack, Screen.Home)   // a root is never closed
        assertEquals(listOf(Screen.Home), stack)
    }
}
