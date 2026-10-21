package com.ipodemu.library

import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException

/**
 * Finds Jellyfin servers on the home network, the way Jellyfin's own apps do: a UDP broadcast of "who is JellyfinServer?" to port 7359, which every
 * server answers with its name and address. The sign-in screen offers what it finds, so a first-time user need not type an address.
 */
object JellyfinDiscovery {
    data class Found(val name: String, val address: String)

    /** One server's reply, or null for anything that is not one. */
    fun parse(reply: String): Found? {
        val o = try { JSONObject(reply) } catch (_: Exception) { return null }
        val address = o.optString("Address").takeIf { it.startsWith("http") } ?: return null
        return Found(o.optString("Name").ifBlank { address }, address.trimEnd('/'))
    }

    /** Asks the network and collects the answers for [waitMs] (blocking: call it off the main thread). Never throws. */
    fun find(waitMs: Int = 1500): List<Found> {
        val out = LinkedHashMap<String, Found>()
        try {
            DatagramSocket().use { s ->
                s.broadcast = true; s.soTimeout = 250
                val ask = "who is JellyfinServer?".toByteArray()
                s.send(DatagramPacket(ask, ask.size, InetAddress.getByName("255.255.255.255"), 7359))
                val end = System.currentTimeMillis() + waitMs
                val buf = ByteArray(4096)
                while (System.currentTimeMillis() < end) {
                    try {
                        val p = DatagramPacket(buf, buf.size); s.receive(p)
                        parse(String(p.data, 0, p.length))?.let { out.putIfAbsent(it.address, it) }
                    } catch (_: SocketTimeoutException) { }
                }
            }
        } catch (_: Exception) { }
        return out.values.toList()
    }
}
