package com.ipodemu.library

/**
 * How this app proves itself to Jellyfin. The `Authorization: MediaBrowser ... Token="..."` header is accepted by every Jellyfin version
 * (10.10 and 12 alike), a user's session token or an admin-made API key. The older `X-Emby-Token` header and `api_key=` query are refused by
 * Jellyfin 12 (HTTP 401), so nothing here uses them; a URL that has to carry the key itself (a socket) uses `ApiKey=`, which both versions take.
 */
object JellyfinAuth {
    fun header(token: String, deviceId: String = "flacie-android"): String =
        "MediaBrowser Client=\"ipodplayer\", Device=\"FLACie\", DeviceId=\"$deviceId\", Version=\"1\", Token=\"$token\""
}
