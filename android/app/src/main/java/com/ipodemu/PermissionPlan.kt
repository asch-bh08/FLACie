package com.ipodemu

/** What FLACie may need the user to allow, and when. Pure decisions only, so they can be unit tested; MainActivity does the asking. */
enum class Need { MEDIA, NOTIFICATIONS }

/** GRANTED: nothing to do. ASK: not asked yet (show the reason first, then the system prompt). DENIED: asked and refused (offer app settings).
 * NOT_NEEDED: this Android version has no such permission. */
enum class Access { GRANTED, ASK, DENIED, NOT_NEEDED }

/** What the screens can ask MainActivity to do about permissions (MainActivity implements it and keeps [UiState.mediaAccess] etc. current). */
interface PermissionActions {
    /** Show the system prompt for [need] (the screen has already shown its reason). */
    fun request(need: Need)
    /** The user chose not to allow it now: remember that (no repeat prompt) and close the reason card. */
    fun decline(need: Need)
    /** Open this app's page in the system settings, for a permission that was refused. */
    fun openAppSettings()
    /** Something started playing: if the notification permission is still to be asked, raise the reason card. */
    fun playbackStarted()
}

object PermissionPlan {
    const val READ_MEDIA_AUDIO = "android.permission.READ_MEDIA_AUDIO"
    const val READ_EXTERNAL_STORAGE = "android.permission.READ_EXTERNAL_STORAGE"
    const val POST_NOTIFICATIONS = "android.permission.POST_NOTIFICATIONS"

    /** The manifest permission behind a need on this Android version, or null when that version does not have one. */
    fun permission(need: Need, sdk: Int): String? = when (need) {
        Need.MEDIA -> if (sdk >= 33) READ_MEDIA_AUDIO else READ_EXTERNAL_STORAGE
        Need.NOTIFICATIONS -> if (sdk >= 33) POST_NOTIFICATIONS else null
    }

    /** [granted] is the set of permissions the app currently holds; [asked] is whether FLACie has already shown the system prompt for it. */
    fun access(need: Need, sdk: Int, granted: Set<String>, asked: Boolean): Access {
        val p = permission(need, sdk) ?: return Access.NOT_NEEDED
        return if (p in granted) Access.GRANTED else if (asked) Access.DENIED else Access.ASK
    }

    /** The sign-in / source choice shows first on a new install (and after signing out), before any system prompt. */
    fun showOnboarding(loginMode: String): Boolean = loginMode.isEmpty()

    /** Reason card for the notification permission: when something starts playing and it has not been asked yet, never at launch. */
    fun askNotificationsNow(loginMode: String, sdk: Int, granted: Set<String>, asked: Boolean, playing: Boolean): Boolean =
        playing && !showOnboarding(loginMode) && access(Need.NOTIFICATIONS, sdk, granted, asked) == Access.ASK

    /** Reason card for the music permission without a user action: only for someone using this device's music (guest) who has not been asked yet.
     * Servers do not need it, so account installs are never prompted; they can allow it from Settings. */
    fun askMediaNow(loginMode: String, sdk: Int, granted: Set<String>, asked: Boolean): Boolean =
        loginMode == "guest" && access(Need.MEDIA, sdk, granted, asked) == Access.ASK

    /** Whether the Home screen should explain that this phone's music cannot be read (guest without access). */
    fun showMediaNotice(loginMode: String, access: Access): Boolean =
        loginMode == "guest" && (access == Access.ASK || access == Access.DENIED)
}
