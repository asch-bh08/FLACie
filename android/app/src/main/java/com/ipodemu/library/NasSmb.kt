package com.ipodemu.library

import jcifs.CIFSContext
import jcifs.config.PropertyConfiguration
import jcifs.context.BaseContext
import jcifs.smb.NtlmPasswordAuthenticator
import java.io.IOException
import java.util.Properties

/** Builds jcifs-ng SMB auth contexts from the app's saved NAS credentials (Prefs). Shared by
 * NasDirectClient (browsing/scanning a share) and PlayerController's data source (playback), so
 * both always authenticate identically. */
object NasSmb {
    private val AUDIO_EXT = setOf("mp3", "flac", "m4a", "aac", "wav", "ogg", "oga", "wma", "opus", "aiff", "alac")

    fun context(username: String, password: String, domain: String): CIFSContext {
        val props = Properties().apply {
            // Modern NAS boxes (Synology, QNAP, TrueNAS) default to SMB2/3; SMB1 is legacy and often disabled server-side.
            setProperty("jcifs.smb.client.minVersion", "SMB202")
            setProperty("jcifs.smb.client.maxVersion", "SMB311")
        }
        val base: CIFSContext = BaseContext(PropertyConfiguration(props))
        // A blank username means "guest share" -- most home NAS boxes (Synology's Guest access, etc.)
        // expect a guest-type login rather than a real account, not just an empty username/password.
        val auth = if (username.isBlank()) NtlmPasswordAuthenticator(NtlmPasswordAuthenticator.AuthenticationType.GUEST)
            else NtlmPasswordAuthenticator(domain.ifBlank { null }, username, password)
        return base.withCredentials(auth)
    }

    /** smb://host/share/percent-encoded/path -- built once here so a user-typed folder path (which
     * may contain spaces) becomes a well-formed SMB URL both when scanning and when reopening a
     * saved Track.path for playback. */
    fun rootUrl(host: String, share: String, folder: String): String {
        val h = host.trim().trim('/')
        val s = share.trim().trim('/')
        val sb = StringBuilder("smb://").append(h).append('/').append(encodeSegment(s)).append('/')
        if (folder.isNotBlank()) {
            for (seg in folder.trim('/').split('/')) sb.append(encodeSegment(seg)).append('/')
        }
        return sb.toString()
    }

    private fun encodeSegment(seg: String) = java.net.URLEncoder.encode(seg, "UTF-8").replace("+", "%20")

    fun isAudio(name: String): Boolean = name.substringAfterLast('.', "").lowercase() in AUDIO_EXT

    /** Moves a file within the same SMB share -- used to file a Soulseek download straight into the
     * library folder Jellyfin scans, without going through Lidarr (which refuses to import a single
     * track when it's expecting a whole album). Creates the destination folder if needed.
     *
     * The source file is found via a directory listing rather than by constructing its SmbFile URL
     * directly -- confirmed live that jcifs-ng's `exists()`/`renameTo()` can misbehave on a hand-built
     * URL for certain filenames (a real file a raw SMB client reads fine at the same moment) while
     * `listFiles()` -- the same mechanism NAS library scanning already relies on -- resolves the
     * identical file correctly. */
    fun moveFile(username: String, password: String, domain: String, host: String, share: String, fromFolder: String, fromName: String, toFolder: String, toName: String) {
        var lastError: Exception? = null
        repeat(3) { attempt ->
            try {
                val ctx = context(username, password, domain)
                val fromDir = jcifs.smb.SmbFile(rootUrl(host, share, fromFolder), ctx)
                val from = fromDir.listFiles()?.firstOrNull { it.name.trimEnd('/').equals(fromName, ignoreCase = true) }
                    ?: throw IOException("source not found via listing: $fromFolder/$fromName")
                val toDir = jcifs.smb.SmbFile(rootUrl(host, share, toFolder), ctx)
                if (!toDir.exists()) toDir.mkdirs()
                val to = jcifs.smb.SmbFile(rootUrl(host, share, toFolder) + encodeSegment(toName), ctx)
                from.renameTo(to)
                return
            } catch (e: Exception) {
                lastError = e
                Thread.sleep(1000)
            }
        }
        throw lastError ?: IOException("move failed")
    }
}
