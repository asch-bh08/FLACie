package com.ipodemu.library

import com.ipodemu.Prefs
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * "Paste a Spotify or Deezer link": FLACie Web reads the link and fetches the songs (`POST /api/import/link`, the same code the web's Import page runs);
 * the phone only recognises a link, hands it over and follows the job in `GET /api/import` like a file import. The recognising here mirrors the server's
 * (PlaylistLinks.cs) so a bad link is turned down at once with the server's own words; anything it lets through the server checks again.
 */
object LinkImport {
    /** What the server says for a link it can't read; the phone says the same before asking. */
    const val NOT_A_LINK = "That isn't a Spotify or Deezer playlist or album link. Copy it with Share > Copy link."
    const val NEEDS_WEB = "Importing needs FLACie Web: open it in a browser once, signed in to the same Jellyfin account."

    // the same shapes as PlaylistLinks.cs (a Spotify playlist or album, optionally /intl-xx/ or /embed/; a Deezer one, optionally with a language)
    private val spotifyUrl = Regex("""open\.spotify\.com/(?:intl-[a-z]{2}/)?(?:embed/)?(playlist|album)/([A-Za-z0-9]{10,40})""", RegexOption.IGNORE_CASE)
    private val spotifyUri = Regex("""spotify:(playlist|album):([A-Za-z0-9]{10,40})""", RegexOption.IGNORE_CASE)
    private val deezerUrl = Regex("""deezer\.com/(?:[a-z]{2}/)?(playlist|album)/(\d{3,20})""", RegexOption.IGNORE_CASE)

    /** The address to send for the first playlist or album link found in [text] (a pasted link, or a whole shared message), else null. Spotify's `spotify:playlist:ID` becomes its web address. */
    fun recognise(text: String?): String? {
        if (text.isNullOrBlank()) return null
        spotifyUrl.find(text)?.let { return "https://open.spotify.com/${it.groupValues[1].lowercase()}/${it.groupValues[2]}" }
        deezerUrl.find(text)?.let { return "https://www.deezer.com/${it.groupValues[1].lowercase()}/${it.groupValues[2]}" }
        spotifyUri.find(text)?.let { return "https://open.spotify.com/${it.groupValues[1].lowercase()}/${it.groupValues[2]}" }
        return null
    }

    /** The body of `POST /api/import/link`. */
    fun body(url: String, replace: Boolean = false): String = JSONObject().put("url", url).put("replace", replace).toString()

    /** The job the server started (same shape as `POST /api/import`). */
    fun parseJob(t: String): FlacieWebClient.Job = JSONObject(t).let {
        FlacieWebClient.Job(it.optString("id"), it.optString("file"), it.optString("state"), it.optInt("total"), it.optInt("done"), it.optInt("failed"),
            it.optString("note").takeIf { n -> n.isNotBlank() && n != "null" })
    }

    /** The words to show for a refused request: the server's `error`, else a plain line for the status. */
    fun errorText(code: Int, body: String): String {
        val said = try { JSONObject(body).optString("error") } catch (_: Exception) { "" }
        return said.ifBlank {
            when (code) {
                401 -> "FLACie Web doesn't know this account. Open FLACie Web in a browser once, signed in to the same Jellyfin."
                429 -> "Too many requests just now. Try again in a minute."
                in 500..599 -> "FLACie Web had a problem answering. Try again in a moment."
                else -> "Couldn't import that link (HTTP $code)."
            }
        }
    }

    /** Hands [url] to FLACie Web; returns the new job, or throws an IOException carrying the words to show. Blocking: call it off the main thread. */
    fun start(prefs: Prefs, url: String, replace: Boolean = false): FlacieWebClient.Job {
        val c = URL(prefs.flacieWebUrl.trimEnd('/') + "/api/import/link").openConnection() as HttpURLConnection
        c.requestMethod = "POST"; c.connectTimeout = 10_000; c.readTimeout = 60_000   // the server reads the link first (Deezer pages one by one)
        c.setRequestProperty("X-Emby-Token", prefs.accountToken)
        c.setRequestProperty("X-Jellyfin-Server", prefs.accountServer.trimEnd('/'))
        try {
            val b = body(url, replace).toByteArray()
            c.doOutput = true; c.setRequestProperty("Content-Type", "application/json"); c.setFixedLengthStreamingMode(b.size)
            c.outputStream.use { it.write(b) }
            val code = c.responseCode
            val text = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() } ?: ""
            if (code !in 200..299) throw java.io.IOException(errorText(code, text))
            return parseJob(text)
        } finally { c.disconnect() }
    }
}
