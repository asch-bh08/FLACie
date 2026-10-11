package com.ipodemu.player

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.Handler
import android.os.Looper
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** True when the device has a network with internet access (Wi-Fi or mobile); follows it coming and going. */
@Composable
fun rememberOnline(): Boolean {
    val ctx = LocalContext.current.applicationContext
    val cm = remember { ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager }
    fun now() = try { cm.getNetworkCapabilities(cm.activeNetwork)?.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) == true } catch (_: Exception) { true }
    var online by remember { mutableStateOf(now()) }
    DisposableEffect(cm) {
        val main = Handler(Looper.getMainLooper())
        val cb = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) { main.post { online = now() } }
            override fun onLost(network: Network) { main.post { online = now() } }
            override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) { main.post { online = now() } }
        }
        try { cm.registerDefaultNetworkCallback(cb) } catch (_: Exception) {}
        onDispose { try { cm.unregisterNetworkCallback(cb) } catch (_: Exception) {} }
    }
    return online
}

/** A small "Offline" tag with a hint that the library on this device still works. */
@Composable
fun OfflineChip(modifier: Modifier = Modifier, text: String = "Offline: your downloaded and local music still plays") {
    val sc = LocalScheme.current
    Row(
        modifier.clip(RoundedCornerShape(50)).background(Color(0x33FFC857)).padding(horizontal = 12.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        GlyphIcon(Glyph.CLOSE, Modifier.size(12.dp), Color(0xFFFFC857))
        Txt(text, size = 12.5f, weight = FontWeight.SemiBold, color = Color(0xFFFFC857), maxLines = 2)
    }
}
