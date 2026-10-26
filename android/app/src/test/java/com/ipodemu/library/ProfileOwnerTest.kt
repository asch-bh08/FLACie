package com.ipodemu.library

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Test

/** The family shared one NAS copy of the profile (profile-guest.json): a sign-in must never take another account's copy, or its history merges in. */
class ProfileOwnerTest {
    private fun copy(userId: String?, updated: Long, vararg plays: Long) = JSONObject().put("v", 2).put("updated", updated)
        .put("account", if (userId == null) JSONObject.NULL else JSONObject().put("userId", userId).put("user", "x"))
        .put("history", JSONArray().also { a -> plays.forEach { a.put(JSONObject().put("p", "p$it").put("t", "T").put("a", "A").put("w", it)) } })

    @Test fun a_newer_NAS_copy_from_another_account_is_not_taken() {
        val mine = copy("c3aaeb3c051b4f6593afef2947748e3d", 10, 1)
        val dads = copy("3c4493eaa5244feb9b948a17db3fda45", 20, 2)
        assertSame(mine, ProfileOwner.choose(mine, dads, "c3aaeb3c051b4f6593afef2947748e3d"))
    }

    @Test fun the_account_id_matches_with_or_without_dashes_and_in_any_case() {
        val nas = copy("C3AAEB3C-051B-4F65-93AF-EF2947748E3D", 20)
        assertSame(nas, ProfileOwner.choose(copy("c3aaeb3c051b4f6593afef2947748e3d", 10), nas, "c3aaeb3c051b4f6593afef2947748e3d"))
    }

    @Test fun a_NAS_copy_without_an_account_is_not_taken_by_a_Jellyfin_sign_in() {
        val mine = copy("u1", 10)
        assertSame(mine, ProfileOwner.choose(mine, copy(null, 20), "u1"))
        assertNull(ProfileOwner.choose(null, copy(null, 20), "u1"))
    }

    @Test fun a_NAS_sign_in_takes_the_NAS_copy() {
        val nas = copy("u1", 20)
        assertSame(nas, ProfileOwner.choose(null, nas, ""))
    }
}
