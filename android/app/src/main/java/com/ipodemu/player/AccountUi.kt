package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * Settings > Account (also the avatar on Home): sign in with a Jellyfin user to restore every saved service connection
 * and playlist on this install -- see library/AccountSync.kt. Quick Connect is the default (approve a 6-digit code from
 * any signed-in Jellyfin app, no typing a password on a handheld); username + password is the fallback.
 */
@Composable
fun AccountScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val acct = app.account
    val prefs = app.prefs
    fun close() { acct.cancelQuickConnect(); ui.accountOpen = false }
    BackHandler { close() }
    var server by remember { mutableStateOf(prefs.accountServer.ifEmpty { prefs.jellyfinUrl }) }
    var user by remember { mutableStateOf("") }
    var pass by remember { mutableStateOf("") }
    var usePassword by remember { mutableStateOf(false) }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier.widthIn(max = 560.dp).fillMaxSize().imePadding().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.BACK, "Back", { close() }, tint = Color.White)
                Txt("Account", Modifier.padding(start = 4.dp), size = 22f, weight = FontWeight.Bold, color = Color.White)
            }
            if (acct.signedIn) {
                Txt("Signed in as ${prefs.accountUserName}", size = 18f, weight = FontWeight.SemiBold, color = Color.White)
                Txt(prefs.accountServer, size = 14f, color = Color(0x99FFFFFF))
                Txt("Your service connections, playlists and favourites are saved to this account and restored when you sign in on another device. Playlists are also kept as real Jellyfin playlists. Passwords and API keys are stored in your Jellyfin user settings, so anyone with admin access to that server can read them.", size = 14f, color = Color(0xCCFFFFFF), maxLines = 7)
                Txt(acct.status ?: syncedLabel(prefs.accountSyncedAt), size = 14f, color = Color(0xFF7CE0A0), maxLines = 3)
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    GlossPill(if (acct.busy) "Syncing..." else "Sync now", { acct.sync() }, primary = true)
                    GlossPill("Sign out", { acct.signOut() })
                }
                return@Column
            }
            Txt("Sign in with your Jellyfin account to bring back every service you set up (Jellyfin, Plex, NAS, Lidarr, Soulseek) and your playlists.", size = 14f, color = Color(0xCCFFFFFF), maxLines = 5)
            Field("Jellyfin server", server, { server = it }, "https://jellyfin.example.ts.net", KeyboardType.Uri)
            if (!usePassword) {
                val code = acct.quickCode
                if (code != null) {
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Color(0x1AFFFFFF)).padding(18.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                        Txt("Enter this code in a signed-in Jellyfin app", size = 14f, color = Color(0xCCFFFFFF), align = TextAlign.Center)
                        Txt("(Settings > Quick Connect)", size = 13f, color = Color(0x99FFFFFF), align = TextAlign.Center)
                        Txt(code.chunked(3).joinToString(" "), Modifier.padding(top = 8.dp), size = 40f, weight = FontWeight.Bold, color = Color.White)
                    }
                    GlossPill("Cancel", { acct.cancelQuickConnect() })
                } else GlossPill(if (acct.busy) "Signing in..." else "Sign in with Quick Connect", { if (server.isNotBlank()) acct.startQuickConnect(server) }, primary = true)
                GlossPill("Use username and password instead", { acct.cancelQuickConnect(); usePassword = true })
            } else {
                Field("Username", user, { user = it }, "", KeyboardType.Text)
                Field("Password", pass, { pass = it }, "", KeyboardType.Password, secret = true)
                GlossPill(if (acct.busy) "Signing in..." else "Sign in", { if (server.isNotBlank() && user.isNotBlank()) acct.signInWithPassword(server, user.trim(), pass) }, primary = true)
                GlossPill("Use Quick Connect instead", { usePassword = false })
            }
            acct.status?.let { Txt(it, size = 14f, color = if (acct.busy || acct.quickCode != null) Color(0xCCFFFFFF) else Color(0xFFFFB0B0), maxLines = 3) }
        }
    }
}

@Composable
private fun Field(label: String, value: String, onChange: (String) -> Unit, hint: String, type: KeyboardType, secret: Boolean = false) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Txt(label, size = 13f, color = Color(0x99FFFFFF))
        Box(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp)) {
            if (value.isEmpty() && hint.isNotEmpty()) Txt(hint, size = 16f, color = Color(0x66FFFFFF))
            BasicTextField(
                value, onChange, singleLine = true, cursorBrush = SolidColor(Color.White),
                textStyle = TextStyle(color = Color.White, fontSize = 16.sp),
                keyboardOptions = KeyboardOptions(keyboardType = type, autoCorrect = false),
                visualTransformation = if (secret) PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None,
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}
