package com.ipodemu.library

/** Asks the server for a cover through the gate: WebP first, JPEG when the server cannot make WebP; If-None-Match when a copy is already saved. */
class CoverFetcher(private val gate: JellyfinGate) {
    @Volatile var webpOk = true; private set

    suspend fun fetch(base: String, token: String, id: String, full: Boolean, tag: String?, etag: String?, priority: Priority): GateResponse {
        val h = HashMap<String, String>()
        h["Authorization"] = JellyfinAuth.header(token)
        if (!etag.isNullOrEmpty()) h["If-None-Match"] = etag
        val kind = if (full) "cover-full" else "cover"
        val r = gate.execute(GateRequest("GET", JellyfinCovers.url(base, id, full, tag, webpOk), h, kind = kind), priority)
        if (webpOk && r.code in WEBP_REFUSED) {
            val j = gate.execute(GateRequest("GET", JellyfinCovers.url(base, id, full, tag, false), h, kind = kind), priority)
            if (j.ok || j.code == 304) { webpOk = false; return j }
        }
        return r
    }

    companion object { val WEBP_REFUSED = setOf(400, 406, 415, 500, 501) }
}
