package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.Access
import com.ipodemu.Need

/** The words for each permission: what it is for, shown BEFORE the system prompt. */
internal object PermissionText {
    const val MEDIA_REASON = "FLACie needs access to the music on this phone."
    const val MEDIA_DETAIL = "It only reads the audio files so they show up in your library. Nothing leaves the phone."
    const val NOTIF_REASON = "Show playback controls?"
    const val NOTIF_DETAIL = "Android needs your permission for the notification with play, pause and skip, and for the lock screen controls."
    const val MEDIA_DENIED = "FLACie can't read the music on this phone because access is turned off. Servers keep working. You can turn it on in the app's settings."
}

/** Full-page "why we ask" shown before a system permission prompt that was not triggered by a button that already said why. Gamepad: A on the focused button. */
@Composable
fun PermissionReasonScreen(need: Need) {
    val ui = LocalApp.current.ui
    val focus = remember { FocusRequester() }
    LaunchedEffect(need) { try { focus.requestFocus() } catch (_: Exception) {} }
    androidx.activity.compose.BackHandler { ui.permissions?.decline(need) }
    val (title, detail) = if (need == Need.MEDIA) PermissionText.MEDIA_REASON to PermissionText.MEDIA_DETAIL else PermissionText.NOTIF_REASON to PermissionText.NOTIF_DETAIL
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            Modifier.widthIn(max = 520.dp).verticalScroll(rememberScrollState()).padding(horizontal = 24.dp, vertical = 24.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp), horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            // the FLACie logo, as on the sign-in screen (it was a plain music note)
            androidx.compose.foundation.Image(androidx.compose.ui.res.painterResource(com.ipodemu.R.mipmap.ic_launcher_art), contentDescription = null,
                modifier = Modifier.size(72.dp).clip(RoundedCornerShape(18.dp)))
            Txt(title, size = 22f, weight = FontWeight.SemiBold, color = Color.White, maxLines = 6)
            Txt(detail, size = 16f, color = Color(0xCCFFFFFF), maxLines = 12)
            GlossPill("Continue", { ui.permissions?.request(need) }, Modifier.fillMaxWidth().focusRequester(focus), primary = true, height = 48.dp)
            GlossPill("Not now", { ui.permissions?.decline(need) }, Modifier.fillMaxWidth(), height = 44.dp)
        }
    }
}

/** A card that says this phone's music can't be read yet (not asked / refused) with the button that fixes it. */
@Composable
fun MediaAccessNotice(modifier: Modifier = Modifier) {
    val ui = LocalApp.current.ui
    val sc = LocalScheme.current
    Column(
        modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(sc.accent.copy(alpha = 0.16f)).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (ui.mediaAccess == Access.DENIED) {
            Txt(PermissionText.MEDIA_DENIED, size = 15f, color = Color.White, maxLines = 10)
            GlossPill("Open app settings", { ui.permissions?.openAppSettings() }, primary = true, height = 44.dp)
        } else {
            Txt(PermissionText.MEDIA_REASON, size = 15f, color = Color.White, maxLines = 10)
            GlossPill("Allow access", { ui.permissions?.request(Need.MEDIA) }, primary = true, height = 44.dp)
        }
    }
}
