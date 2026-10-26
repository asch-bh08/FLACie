package com.ipodemu.library

import org.json.JSONObject

/**
 * Which copy of the profile a sync starts from: the newer of the account's own copy and the NAS copy. The NAS copy is a file named after the NAS
 * login, which the family share (everyone had the same NAS connection with no username, so all wrote profile-guest.json), so with a Jellyfin
 * account it is only taken when it says it belongs to that account. Taking the newest regardless gave hms Dad's play history (2026-10-25).
 */
object ProfileOwner {
    private fun id(s: String?) = s.orEmpty().replace("-", "").lowercase()

    fun choose(own: JSONObject?, nas: JSONObject?, jellyfinUserId: String?): JSONObject? {
        val fits = nas?.takeIf { jellyfinUserId.isNullOrBlank() || id(it.optJSONObject("account")?.optString("userId")) == id(jellyfinUserId) }
        if (own == null || fits == null) return own ?: fits
        return if (fits.optLong("updated") > own.optLong("updated")) fits else own
    }
}
