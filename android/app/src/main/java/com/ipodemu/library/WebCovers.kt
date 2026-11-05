package com.ipodemu.library

import java.net.HttpURLConnection
import java.net.URL

/**
 * Covers from FLACie Web's /art, so the phone and the web show the same picture. The phone only looks a cover up itself when this gives nothing:
 * the web is not connected, can't be reached, or answered with its made (svg) cover, which the phone can't draw yet.
 */
class WebCovers(
    private val base: () -> String,
    private val headers: () -> Map<String, String>,
    /** One GET: the answer, or null on a timeout or connection error. */
    private val http: (url: String, headers: Map<String, String>) -> GateResponse?,
    private val now: () -> Long = System::currentTimeMillis,
) {
    @Volatile private var downUntil = 0L

    val connected: Boolean get() = base().isNotBlank()

    /** The picture bytes (a 200, or a 304 when the saved copy is still right), or null: ask the old way. */
    fun fetch(key: String, tag: String?, full: Boolean, etag: String?): GateResponse? {
        if (!connected || now() < downUntil) return null
        val h = headers() + (if (etag != null) mapOf("If-None-Match" to etag) else emptyMap())
        val r = http(base().trimEnd('/') + path(key, tag, full), h)
        if (r == null) { downUntil = now() + DOWN_MS; return null }   // one timeout, then the phone's own lookup for a while
        if (r.code == 304) return r
        if (r.code !in 200..299 || r.body.isEmpty()) return null
        if (r.headers["content-type"].orEmpty().contains("svg", ignoreCase = true)) return null
        return r
    }

    companion object {
        const val DOWN_MS = 30_000L

        /** The web's address of a cover: catalog keys carry the lookup rules' mark ("~r2"), Jellyfin keys their picture tag; "fmt=png" asks for a raster made cover. */
        fun path(key: String, tag: String?, full: Boolean): String {
            val k = when {
                key.startsWith("it") && !key.contains("~r") -> "$key~r2"
                key.startsWith("jf") && tag != null && !key.contains('~') -> "$key~$tag"
                else -> key
            }
            return "/art/" + java.net.URLEncoder.encode(k, "UTF-8").replace("+", "%20").replace("%7E", "~") + "?fmt=png" + (if (full) "&s=full" else "")
        }

        /** The real GET: short timeouts, since a web that doesn't answer should cost the phone seconds, not minutes. */
        fun get(url: String, headers: Map<String, String>): GateResponse? = try {
            val c = URL(url).openConnection() as HttpURLConnection
            c.connectTimeout = 4000; c.readTimeout = 15000; c.instanceFollowRedirects = true
            headers.forEach { (k, v) -> c.setRequestProperty(k, v) }
            try {
                val code = c.responseCode
                val body = if (code in 200..299) c.inputStream.use { it.readBytes() } else ByteArray(0)
                GateResponse(code, body, c.headerFields.filterKeys { it != null }.mapKeys { it.key.lowercase() }.mapValues { it.value.firstOrNull().orEmpty() })
            } finally { c.disconnect() }
        } catch (_: Exception) { null }
    }
}
