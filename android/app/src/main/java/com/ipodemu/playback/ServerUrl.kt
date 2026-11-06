package com.ipodemu.playback

/** Which saved server an address belongs to: [base] and then a path, a query or nothing, never more of a host name or a port (FLACie Web on
 *  the NAS on :8443 is not the file mover on the same NAS, and was sent the file mover's key). */
object ServerUrl {
    fun under(uri: String, base: String): Boolean {
        val b = base.trimEnd('/')
        if (b.isBlank() || !uri.startsWith(b)) return false
        return uri.length == b.length || uri[b.length] == '/' || uri[b.length] == '?'
    }
}
