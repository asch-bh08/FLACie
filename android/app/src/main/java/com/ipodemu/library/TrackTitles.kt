package com.ipodemu.library

/**
 * Cleans the title of a song whose file has no real tags. Jellyfin names such an item after its file ("06 - Jason Mraz -
 * Everything Is Sound"), with no artist. This reads the artist and title out of that name, but only when the title really
 * is the file name (a tagged title is never touched, so "99 Problems", "1979" or "7 Rings" stay as they are).
 *
 * The same rules, with the same test table, live in FLACie.Core/TrackTitles.cs: keep the two in step.
 */
object TrackTitles {
    /** Bump whenever [clean] / [cleanAll] change what they produce: a saved Jellyfin list stamped with another number is cleaned again once when it is loaded. */
    const val RULES = 1

    /** What a clean did: the new title and artist. [title] equals the input name when nothing was recognised. */
    data class Result(val title: String, val artist: String, val cleaned: Boolean)

    private val sepClass = "[-–—]"
    private val dashSplit = Regex("\\s+$sepClass\\s+")
    private val spaces = Regex("\\s+")
    private val discTrack = Regex("^\\(?\\s*(\\d{1,2})[-.](\\d{2,3})\\s*\\)?(?:\\s*[-.]\\s+|\\s+|\\.\\s*)(?=\\p{L})")
    private val spaced = Regex("^(\\d{1,3})\\s*(?:$sepClass|\\.|\\))\\s+(?=\\S)")
    private val tight = Regex("^(\\d{2})[-_.](?=\\p{L})")
    private val bare = Regex("^(\\d{1,3})\\s+(?=\\p{L})")
    private val onlyNumber = Regex("^\\d{1,3}$")
    private val hiRes = Regex("\\s*[\\[(]\\s*hi-?res\\s*[\\])]\\s*$", RegexOption.IGNORE_CASE)
    private val tld = "(?:com|net|org|ru|info|me|fm|to|cc|biz|co|uk|io|tv|ws|su|ua|pl|de|xyz|club|site|top)"
    private val spamBracket = Regex("\\s*[\\[(]\\s*(?:https?://)?(?:www\\.)?[\\p{L}\\p{N}\\-]+(?:\\.[\\p{L}\\p{N}\\-]+)*\\.$tld\\s*[\\])]\\s*$", RegexOption.IGNORE_CASE)
    private val spamPlain = Regex("\\s*(?:$sepClass\\s*)?(?:https?://)?www\\.[\\p{L}\\p{N}\\-.]+\\.[a-z]{2,6}\\s*$", RegexOption.IGNORE_CASE)
    private val yearTail = Regex("\\s*\\(\\d{4}\\)\\s*$")
    private val variousRe = Regex("^(various( artists)?|va|unknown( artist)?|<unknown>)$", RegexOption.IGNORE_CASE)

    /** File name without folders and extension; "" for a path with no file part. */
    fun baseName(path: String): String {
        val p = path.substringBefore('?').replace('\\', '/').trimEnd('/')
        val f = p.substringAfterLast('/')
        val dot = f.lastIndexOf('.')
        return if (dot > 0 && f.length - dot - 1 in 1..5 && f.substring(dot + 1).all { it.isLetterOrDigit() }) f.substring(0, dot) else f
    }

    private fun folders(path: String): List<String> =
        path.substringBefore('?').replace('\\', '/').split('/').filter { it.isNotEmpty() }.dropLast(1).takeLast(2)

    private fun fold(s: String) = s.replace('_', ' ').replace(spaces, " ").trim().lowercase()
    private fun usable(artist: String) = artist.isNotBlank() && !variousRe.matches(artist.trim())

    /** Whether the item's title is just its file name: the one sign that Jellyfin found no title tag. */
    fun isFileName(name: String, path: String): Boolean {
        val base = baseName(path)
        return base.isNotBlank() && name.isNotBlank() && fold(name) == fold(base)
    }

    /** Whether a name starts like a track number ("06 - ", "06. ", "06 ", "1-06 "), for deciding if a folder's files are numbered. */
    fun hasNumberPrefix(name: String): Boolean {
        val t = name.replace('_', ' ').trim()
        return discTrack.containsMatchIn(t) || spaced.containsMatchIn(t) || tight.containsMatchIn(t) || bare.containsMatchIn(t)
    }

    private fun stripNoise(s: String): String {
        var t = s
        repeat(3) { t = t.replace(hiRes, "").replace(spamBracket, "").replace(spamPlain, "").trim() }
        return t
    }

    /**
     * @param name the item's title as Jellyfin gives it
     * @param path the file's path on the server ("" when unknown: then nothing is changed)
     * @param artist the item's artist ("" or "Unknown" when there is none)
     * @param album the item's album, used to drop an "Album - " part
     * @param siblingsNumbered other files in the same folder also start with track numbers (makes "12 Title" safe to read)
     * @param knownArtists primary artist names (see [primaryArtist]) known from properly tagged songs, to tell which side of "X - Y" is the artist
     */
    fun clean(name: String, path: String, artist: String, album: String = "", siblingsNumbered: Boolean = false, knownArtists: Set<String> = emptySet()): Result {
        val same = Result(name, artist, false)
        if (!isFileName(name, path)) return same
        var t = stripNoise(name.replace('_', ' ').replace(spaces, " ").trim())
        if (t.isEmpty()) return same

        val dirs = folders(path).map { fold(it) }
        val artistMissing = !usable(artist) || fold(artist) in dirs
        val albumMissing = album.isBlank() || fold(album) in dirs

        val known = knownArtists
        fun isKnown(s: String) = primaryArtist(s) in known

        // the track number
        var numbered = false
        discTrack.find(t)?.let { t = t.substring(it.range.last + 1); numbered = true }
        if (!numbered) {
            val m = spaced.find(t) ?: tight.find(t)
            if (m != null && t.length > m.range.last + 1) { t = t.substring(m.range.last + 1); numbered = true }
        }
        if (!numbered) {
            bare.find(t)?.let { m ->
                val padded = m.groupValues[1].startsWith("0")
                // "50 Cent - In Da Club": when the whole front part is a known artist, the number is part of the name
                if (!isKnown(t.split(dashSplit)[0]) && (siblingsNumbered || (padded && (artistMissing || albumMissing)))) { t = t.substring(m.range.last + 1); numbered = true }
            }
        }
        t = t.trim().trimStart('-', '–', '—', '.', ' ').trimEnd('-', '–', '—', ' ')
        if (t.isEmpty()) return same

        var title = t
        var credit = artist
        val parts = t.split(dashSplit).map { it.trim() }.filter { it.isNotEmpty() }
        val pa = if (usable(artist)) primaryArtist(artist) else ""

        if (parts.size >= 3) {
            val numAt = (1 until parts.size - 1).firstOrNull { onlyNumber.matches(parts[it]) }
            if (numAt != null) {
                // "Artist - Album - 06 - Title"
                title = parts.drop(numAt + 1).joinToString(" - ")
                if (artistMissing || !usable(artist)) credit = parts[0]
            } else if (parts[1].replace(yearTail, "").equals(album.replace(yearTail, "").trim(), ignoreCase = true) && album.isNotBlank()) {
                title = parts.drop(2).joinToString(" - ")
                if (artistMissing) credit = parts[0]
            } else if ((pa.isNotEmpty() && primaryArtist(parts[0]) == pa) || isKnown(parts[0]) || (!usable(artist) && looksLikeName(parts[0]))) {
                title = parts.drop(1).joinToString(" - ")
                if (!usable(artist)) credit = parts[0]
            }
        } else if (parts.size == 2) {
            val a = parts[0]; val b = parts[1]
            val aKnown = isKnown(a); val bKnown = isKnown(b)
            when {
                pa.isNotEmpty() && primaryArtist(a) == pa -> title = b
                pa.isNotEmpty() && primaryArtist(b) == pa -> title = a
                pa.isNotEmpty() && !(aKnown || bKnown) -> title = t   // credited elsewhere and nothing says which side is which: leave it
                bKnown && !aKnown -> { title = a; if (!usable(artist)) credit = b }
                looksLikeName(a) -> { title = b; if (!usable(artist)) credit = a }
                else -> title = t
            }
            // a short bare number as the whole title ("Artist - 22") is more likely a misread than a song, unless the artist is a known one
            if (onlyNumber.matches(title) && !(aKnown || (pa.isNotEmpty() && primaryArtist(a) == pa))) { title = t; credit = artist }
        }
        title = title.trim()
        if (title.isEmpty()) return same
        val changed = title != name || credit != artist
        return Result(title, credit, changed)
    }

    private fun looksLikeName(s: String) = s.isNotBlank() && s.length <= 60 && !onlyNumber.matches(s) && s.any { it.isLetter() }

    /** Primary artist names of songs that do have real tags, in the form [clean] compares with. */
    fun knownArtistsOf(tracks: List<Track>): Set<String> {
        val out = HashSet<String>()
        for (t in tracks) {
            if (t.source != TrackSource.JELLYFIN || isFileName(t.title, t.filePath)) continue
            for (a in arrayOf(t.artist, t.albumArtist)) if (usable(a)) primaryArtist(a).takeIf { it.isNotEmpty() }?.let { out += it }
        }
        return out
    }

    /** [clean] for a whole Jellyfin library: knows which artists exist and which folders hold numbered files. Other sources pass through. */
    fun cleanAll(tracks: List<Track>, extraKnown: Set<String> = emptySet()): List<Track> {
        val known = knownArtistsOf(tracks) + extraKnown
        val numbered = HashMap<String, HashSet<String>>()
        fun dir(t: Track) = t.filePath.replace('\\', '/').substringBeforeLast('/', "")
        for (t in tracks) if (t.source == TrackSource.JELLYFIN && isFileName(t.title, t.filePath) && hasNumberPrefix(t.title.replace('_', ' ')))
            numbered.getOrPut(dir(t)) { HashSet() }.add(t.title.trim().takeWhile { it.isDigit() || it == '-' || it == '.' }.ifEmpty { t.title })
        return tracks.map { t ->
            if (t.source != TrackSource.JELLYFIN || t.origTitle.isNotEmpty()) return@map t
            val sib = (numbered[dir(t)]?.size ?: 0) >= 2
            val r = clean(t.title, t.filePath, t.artist, t.album, sib, known)
            if (!r.cleaned) t else t.copy(
                title = r.title, artist = r.artist,
                albumArtist = if (t.albumArtist.isBlank() || t.albumArtist == t.artist || !usable(t.albumArtist)) r.artist else t.albumArtist,
                origTitle = t.title,
            )
        }
    }
}
