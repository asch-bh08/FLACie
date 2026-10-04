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
import androidx.compose.foundation.layout.size
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
                val viaNas = prefs.accountKind == "nas"
                val who = if (viaNas) prefs.nasUsername.ifBlank { "guest" } else prefs.accountUserName.ifBlank { "Signed in" }
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    Box(Modifier.size(64.dp).clip(androidx.compose.foundation.shape.CircleShape).background(Color(0xFFE0457B)), contentAlignment = Alignment.Center) {
                        Txt(who.take(1).uppercase(), size = 28f, weight = FontWeight.Bold, color = Color.White)
                    }
                    Column {
                        Txt(who, size = 22f, weight = FontWeight.Bold, color = Color.White, maxLines = 1)
                        Txt(if (viaNas) "Signed in with a NAS" else "Signed in with Jellyfin", size = 14f, color = Color(0x99FFFFFF))
                    }
                }
                // the numbers: from the server when FLACie Web is reachable (its library is the whole account), else what this phone holds
                val web = remember { com.ipodemu.library.FlacieWebClient(prefs) }
                var stats by remember { mutableStateOf<com.ipodemu.library.FlacieWebClient.AccountStats?>(null) }
                androidx.compose.runtime.LaunchedEffect(Unit) {
                    stats = try { if (web.available) kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { web.account() } else null } catch (_: Exception) { null }
                }
                val st = stats?.takeIf { it.songs > 0 } ?:com.ipodemu.library.FlacieWebClient.AccountStats(app.library.songs().size, app.library.albums().size, app.library.artists().size, app.userData.playlists.size, app.userData.favorites.size)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("Songs" to st.songs, "Albums" to st.albums, "Artists" to st.artists, "Playlists" to st.playlists, "Favourites" to st.favourites).forEach { (label, n) ->
                        Column(Modifier.weight(1f).clip(RoundedCornerShape(14.dp)).background(Color(0x14FFFFFF)).padding(vertical = 10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                            Txt("$n", size = 18f, weight = FontWeight.Bold, color = Color.White, maxLines = 1)
                            Txt(label, size = 10f, color = Color(0x99FFFFFF), maxLines = 1)
                        }
                    }
                }
                Txt("Connected", size = 16f, weight = FontWeight.SemiBold, color = Color.White)
                listOf(
                    Triple("Jellyfin", prefs.hasJellyfinAccount, if (prefs.hasJellyfinAccount) "${prefs.accountUserName} on ${prefs.accountServer.removePrefix("https://").removePrefix("http://")}" else "Not connected"),
                    Triple("NAS", prefs.hasNasAccount, if (prefs.hasNasAccount) "${prefs.nasHost}/${prefs.nasShare}" else "Not connected"),
                    Triple("Downloads", app.library.lidarrConnected, if (app.library.lidarrConnected) "Set up" else "Not set up"),
                    Triple("FLACie Web", web.available, if (web.available) prefs.flacieWebUrl.removePrefix("https://").removePrefix("http://") else "Not found yet"),
                ).forEach { (name, on, detail) ->
                    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Color(0x14FFFFFF)).padding(horizontal = 14.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Box(Modifier.size(10.dp).clip(androidx.compose.foundation.shape.CircleShape).background(if (on) Color(0xFF7CE0A0) else Color(0x55FFFFFF)))
                        Column(Modifier.weight(1f)) {
                            Txt(name, size = 15f, weight = FontWeight.SemiBold, color = Color.White, maxLines = 1)
                            Txt(detail, size = 13f, color = Color(0x99FFFFFF), maxLines = 1)
                        }
                    }
                }
                val kept = listOfNotNull("this NAS".takeIf { prefs.hasNasAccount }, "Jellyfin (${prefs.accountUserName})".takeIf { prefs.hasJellyfinAccount }).joinToString(" and ")
                Txt("Your connected services, playlists, favourites and preferences are kept in $kept, so signing in with either on another device brings them back. They include the services' passwords and API keys, which the server's admins can read.", size = 14f, color = Color(0xCCFFFFFF), maxLines = 7)
                Txt(acct.status ?: syncedLabel(prefs.accountSyncedAt), size = 14f, color = Color(0xFF7CE0A0), maxLines = 3)
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    GlossPill(if (acct.busy) "Syncing..." else "Sync now", { acct.sync() }, primary = true)
                    // signing out leads back to the login screen
                    GlossPill("Sign out", { acct.signOut(); ui.setLogin(""); ui.accountOpen = false })
                }
                // add the other kind of account, so the profile is kept in both
                if (!prefs.hasJellyfinAccount) {
                    Txt("Also keep it in a Jellyfin account", size = 16f, weight = FontWeight.SemiBold, color = Color.White)
                    JellyfinSignInForm()
                }
                return@Column
            }
            Txt("You're using FLACie as a guest", size = 18f, weight = FontWeight.SemiBold, color = Color.White)
            Txt("Guest mode plays this device's music only. Sign in with Jellyfin or a NAS to stream, download and keep your playlists and settings in sync.", size = 14f, color = Color(0xCCFFFFFF), maxLines = 5)
            GlossPill("Sign in", { ui.setLogin(""); ui.accountOpen = false }, primary = true)
        }
    }
}

/** Server field plus Quick Connect (default) or username and password; used by Settings > Account and the login screen. */
@Composable
fun JellyfinSignInForm() {
    val app = LocalApp.current
    val acct = app.account
    val prefs = app.prefs
    var server by remember { mutableStateOf(prefs.accountServer.ifEmpty { prefs.jellyfinUrl }) }
    var user by remember { mutableStateOf("") }
    var pass by remember { mutableStateOf("") }
    var usePassword by remember { mutableStateOf(false) }
    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
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

@Composable
internal fun Field(label: String, value: String, onChange: (String) -> Unit, hint: String, type: KeyboardType, secret: Boolean = false) {
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
