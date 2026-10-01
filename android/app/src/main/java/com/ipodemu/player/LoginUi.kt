package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp

/**
 * First screen of a new install and after signing out. Signing in with a Jellyfin user or a NAS login makes that the
 * account: the profile (services, playlists, favourites, preferences) is kept there and in any other account added
 * later, so either one brings everything back. A guest uses this device's files only; nothing is synced.
 */
@Composable
fun LoginScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val acct = app.account
    var page by remember { mutableStateOf(0) } // 0 choose, 1 Jellyfin, 2 NAS, 3 guest
    LaunchedEffect(acct.signedIn) { if (acct.signedIn) ui.setLogin("account") }
    androidx.activity.compose.BackHandler(enabled = page != 0) { acct.cancelQuickConnect(); page = 0 }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier.widthIn(max = 520.dp).fillMaxSize().imePadding().verticalScroll(rememberScrollState()).padding(horizontal = 24.dp, vertical = 28.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Box(Modifier.size(64.dp).clip(RoundedCornerShape(16.dp)).background(LocalScheme.current.accent), contentAlignment = Alignment.Center) {
                GlyphIcon(Glyph.NOTE, Modifier.size(34.dp), LocalScheme.current.accent.readableInk())
            }
            Txt("FLACie", size = 32f, weight = FontWeight.Bold, color = Color.White)
            when (page) {
                0 -> {
                    Txt("Sign in to keep your music, playlists and settings in sync on every device.", size = 16f, color = Color(0xCCFFFFFF), maxLines = 3)
                    LoginChoice("Sign in with Jellyfin", "Use a Jellyfin server account. Streams your server anywhere.", primary = true) { page = 1 }
                    LoginChoice("Sign in with a NAS", "Use the login for a network share (SMB). Plays the music on it and keeps your profile there.") { page = 2 }
                    LoginChoice("Continue as guest", "This device's music only, no internet needed. Nothing is synced.") { page = 3 }
                }
                1 -> {
                    Txt("Sign in with Jellyfin", size = 20f, weight = FontWeight.SemiBold, color = Color.White)
                    JellyfinSignInForm()
                    GlossPill("Back", { acct.cancelQuickConnect(); page = 0 }, icon = Glyph.BACK, height = 40.dp)
                }
                2 -> {
                    Txt("Sign in with a NAS", size = 20f, weight = FontWeight.SemiBold, color = Color.White)
                    NasSignInForm()
                    GlossPill("Back", { page = 0 }, icon = Glyph.BACK, height = 40.dp)
                }
                else -> {
                    Txt("Continue as guest?", size = 20f, weight = FontWeight.SemiBold, color = Color.White)
                    Txt("Guest mode plays the music stored on this device and nothing else: no servers, no downloads, no syncing. Anything set up earlier on this device is removed from it. Sign in at any time from Settings to use servers.", size = 15f, color = Color(0xCCFFFFFF), maxLines = 8)
                    GlossPill("Start as guest", { app.library.forgetAllServices(); ui.setLogin("guest") }, primary = true, height = 44.dp)
                    GlossPill("Back", { page = 0 }, icon = Glyph.BACK, height = 40.dp)
                }
            }
        }
    }
}

/** NAS sign-in: the login is checked against the share before anything is saved. */
@Composable
fun NasSignInForm() {
    val app = LocalApp.current
    val acct = app.account
    var host by remember { mutableStateOf("") }
    var share by remember { mutableStateOf("") }
    var folder by remember { mutableStateOf("") }
    var user by remember { mutableStateOf("") }
    var pass by remember { mutableStateOf("") }
    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Field("Server address", host, { host = it }, "192.168.1.50 or nas.local", KeyboardType.Uri)
        Field("Share", share, { share = it }, "Music", KeyboardType.Text)
        Field("Music folder in the share (optional)", folder, { folder = it }, "", KeyboardType.Text)
        Field("Username", user, { user = it }, "", KeyboardType.Text)
        Field("Password", pass, { pass = it }, "", KeyboardType.Password, secret = true)
        GlossPill(if (acct.busy) "Checking..." else "Sign in", {
            if (host.isNotBlank() && share.isNotBlank() && !acct.busy) acct.signInWithNas(host, share, folder, user, pass, "")
        }, primary = true, height = 44.dp)
        acct.status?.let { Txt(it, size = 14f, color = if (acct.busy) Color(0xCCFFFFFF) else Color(0xFFFFB0B0), maxLines = 3) }
    }
}

@Composable
private fun LoginChoice(title: String, detail: String, primary: Boolean = false, onClick: () -> Unit) {
    val sc = LocalScheme.current
    Column(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp))
            .background(if (primary) sc.accent.copy(alpha = 0.18f) else Color(0x14FFFFFF))
            .clickable(onClick = onClick)
            .padding(18.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Txt(title, size = 18f, weight = FontWeight.SemiBold, color = Color.White)
        Txt(detail, size = 14f, color = Color(0xBBFFFFFF), maxLines = 4)
    }
}
