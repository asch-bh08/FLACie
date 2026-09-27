package com.ipodemu.nav

import android.os.Environment
import android.os.StatFs
import com.ipodemu.App
import com.ipodemu.library.Group
import com.ipodemu.library.Library
import com.ipodemu.library.Track
import com.ipodemu.playback.PlayerController
import com.ipodemu.theme.Themes
import java.text.DateFormat
import java.util.Date

/**
 * Theme-agnostic page factories. Themes compose these into their own menu tree
 * (see IpodTheme.buildRoot), so structure differs per era while behaviour is shared.
 */
class MenuBuilder(private val app: App, private val onThemeChosen: (String) -> Unit) {
    private val lib get() = app.library
    private val player get() = app.player
    private val prefs get() = app.prefs

    /** Titles offered by the current theme's main menu (for Settings > Main Menu). */
    private var rootTitles: List<String> = emptyList()

    fun page(title: String, kind: Kind = Kind.MENU, items: () -> List<Item>) =
        ListPage(title, kind) { ListSourceOf(items()) }

    /** The top-level menu; entries hidden in Settings > Main Menu are filtered out. */
    fun rootPage(items: () -> List<Item>) = page("iPod") {
        val all = items()
        rootTitles = all.map { it.title }
        all.filter { it.title !in prefs.hiddenMenu }
    }

    // ---- top-level entries -------------------------------------------------

    fun musicItem(title: String = "Music", page: () -> ListPage) =
        Item(title, icon = Icon.MUSIC) { it.push(page()) }

    fun voiceMemosItem(title: String = "Voice Memos") =
        Item(title, icon = Icon.VOICE) { it.push(memosPage()) }

    fun settingsItem() = Item("Settings", icon = Icon.SETTINGS) { it.push(settingsPage()) }

    fun shuffleItem() = Item("Shuffle Songs", icon = Icon.SHUFFLE, chevron = false) { nav ->
        val songs = lib.songs()
        if (songs.isNotEmpty()) { player.shuffleAll(songs); nav.showNowPlaying() }
    }

    /** Only listed while something is queued, as on the real device. */
    fun nowPlayingItems(): List<Item> =
        if (player.hasQueue) listOf(Item("Now Playing", icon = Icon.NOW_PLAYING) { it.showNowPlaying() }) else emptyList()

    fun statusItems(): List<Item> = when {
        lib.tracks.isEmpty() && lib.scanning -> listOf(Item("Scanning... ${lib.scanCount}", chevron = false))
        lib.tracks.isEmpty() -> listOf(Item("No Music", chevron = false))
        else -> emptyList()
    }

    // ---- library pages -----------------------------------------------------

    fun playlistsItem() = Item("Playlists", icon = Icon.PLAYLISTS) { it.push(playlistsPage()) }
    fun artistsItem() = Item("Artists", icon = Icon.ARTISTS) { it.push(artistsPage()) }
    fun albumsItem() = Item("Albums", icon = Icon.ALBUMS) { it.push(albumsPage()) }
    fun songsItem() = Item("Songs", icon = Icon.SONGS) { it.push(songsPage("Songs", lib.songs())) }
    fun genresItem() = Item("Genres", icon = Icon.GENRES) { it.push(genresPage()) }

    fun songsPage(title: String, tracks: List<Track>) = ListPage(title, Kind.SONGS) {
        // Every list gets a Shuffle row on top, whichever iPod is selected.
        val off = if (tracks.size > 1) 1 else 0
        LazySource(tracks.size + off) { i ->
            if (off == 1 && i == 0) {
                Item("Shuffle", artKey = tracks.firstNotNullOfOrNull { it.artKey }, icon = Icon.SHUFFLE, detail = songCount(tracks.size), chevron = false) { nav ->
                    player.play(tracks, tracks.indices.random(), shuffle = true); nav.showNowPlaying()
                }
            } else {
                val t = tracks[i - off]
            Item(t.title, subtitle = t.artist, artKey = t.artKey, detail = listOf(t.album, fmtTime(t.durationMs)).filter { it.isNotEmpty() }.joinToString("  ·  "), chevron = false) { nav -> playFrom(nav, tracks, i - off) }
            }
        }
    }

    fun memosPage() = ListPage("Voice Memos", Kind.MEMOS) {
        val memos = lib.memos
        if (memos.isEmpty()) ListSourceOf(listOf(Item("No Memos", chevron = false)))
        else LazySource(memos.size) { i ->
            val t = memos[i]
            val date = DateFormat.getDateInstance(DateFormat.SHORT).format(Date(t.mtime))
            Item(t.title, subtitle = "$date  ${fmtTime(t.durationMs)}", chevron = false) { nav -> playFrom(nav, memos, i) }
        }
    }

    /** Playlists mirror the folders under Music/ (plus any .m3u files). */
    fun playlistsPage() = groupPage("Playlists", Kind.ALBUMS, { lib.playlists() }) { g -> songsPage(g.name, g.tracks) }

    fun artistsPage() = groupPage("Artists", Kind.ARTISTS, { lib.artists() }) { g ->
        val albums = g.tracks.groupBy { it.albumKey }.values.map { Group(it[0].album, it, it.firstNotNullOfOrNull { t -> t.artKey }) }
        if (albums.size <= 1) songsPage(g.name, g.tracks)
        else ListPage(g.name, Kind.ALBUMS) {
            val all = Item("All Songs", artKey = g.artKey, detail = songCount(g.tracks.size), chevron = true) { nav -> nav.push(songsPage(g.name, g.tracks)) }
            val rest = albums.map { a -> Item(a.name, artKey = a.artKey, detail = songCount(a.tracks.size)) { nav -> nav.push(songsPage(a.name, a.tracks.sortedWith(Library.trackOrder))) } }
            ListSourceOf(listOf(all) + rest)
        }
    }

    fun albumsPage() = groupPage("Albums", Kind.ALBUMS, { lib.albums() }, artistSub = true) { g -> songsPage(g.name, g.tracks) }

    fun genresPage() = groupPage("Genres", Kind.ALBUMS, { lib.genres() }) { g -> songsPage(g.name, g.tracks) }

    private fun groupPage(
        title: String, kind: Kind, groups: () -> List<Group>, artistSub: Boolean = false, open: (Group) -> ListPage,
    ) = ListPage(title, kind) {
        val gs = groups()
        if (gs.isEmpty()) ListSourceOf(statusItems().ifEmpty { listOf(Item("None", chevron = false)) })
        else LazySource(gs.size) { i ->
            val g = gs[i]
            val sub = if (artistSub) g.tracks.firstOrNull()?.artist else null
            Item(g.name, subtitle = sub, artKey = g.artKey, detail = songCount(g.tracks.size)) { nav -> nav.push(open(g)) }
        }
    }

    private fun playFrom(nav: Nav, tracks: List<Track>, i: Int) {
        player.play(tracks, i, shuffle = false)
        nav.showNowPlaying()
    }

    // ---- settings ----------------------------------------------------------

    private fun choice(title: String, values: List<String>, get: () -> Int, set: (Int) -> Unit) =
        Item(title, value = { values[get().coerceIn(0, values.lastIndex)] }, chevron = false) { nav ->
            set((get() + 1) % values.size); nav.redraw()
        }

    /** Settings is grouped so common changes are a couple of taps away instead of one long list. */
    fun settingsPage(): ListPage = ListPage("Settings", Kind.SETTINGS) {
        ListSourceOf(listOf(
            // the single Appearance screen (Look, Mode, Colour, Theme, Display) is shared with the Player views
            Item("Appearance") { app.ui.pickerOpen = true },
            Item("Playback") { it.push(playbackPage()) },
            Item("Controls") { it.push(controlsPage()) },
            Item("Main Menu") { it.push(mainMenuPage()) },
            Item("About") { it.push(aboutPage()) },
            Item("Rescan Library", value = { if (lib.scanning) "${lib.scanCount}..." else null }, chevron = false) {
                lib.rescan(); it.redraw()
            },
            Item("Sync", value = { lib.syncDeviceLabel ?: "Local Library" }) { app.ui.syncSetupOpen = true },
            Item("Reset Settings") { it.push(resetPage()) },
        ))
    }

    private fun playbackPage(): ListPage = ListPage("Playback", Kind.SETTINGS) {
        val limits = listOf(100, 85, 70, 50)
        val sleeps = listOf(0, 15, 30, 60, 90, 120)
        ListSourceOf(listOf(
            choice("Shuffle", listOf("Off", "Songs"), { if (prefs.shuffle) 1 else 0 }) { prefs.shuffle = it == 1; player.applyModes() },
            choice("Repeat", listOf("Off", "All", "One"), { prefs.repeat }) { prefs.repeat = it; player.applyModes() },
            Item("Equalizer", value = { prefs.eq }) { it.push(eqPage()) },
            choice("Volume Limit", limits.map { if (it == 100) "Off" else "$it%" },
                { limits.indexOf(prefs.volumeLimit).coerceAtLeast(0) }) { prefs.volumeLimit = limits[it]; player.applyVolumeLimit() },
            choice("Sleep Timer", sleeps.map { if (it == 0) "Off" else "$it min" },
                { sleeps.indexOf(player.sleepMinutes).coerceAtLeast(0) }) { player.setSleepTimer(sleeps[it]) },
        ))
    }

    private fun controlsPage(): ListPage = ListPage("Controls", Kind.SETTINGS) {
        ListSourceOf(listOf(
            choice("Wheel Speed", listOf("Very Slow", "Slow", "Medium", "Fast", "Very Fast"), { prefs.wheelSensitivity }) { prefs.wheelSensitivity = it },
            choice("Wheel Accel", listOf("Off", "Low", "Normal", "High"), { prefs.wheelAccel }) { prefs.wheelAccel = it },
            choice("Clicker", listOf("Off", "Sound", "Haptic", "Both"),
                { (if (prefs.clickSound) 1 else 0) + (if (prefs.haptics) 2 else 0) }) { prefs.clickSound = it and 1 != 0; prefs.haptics = it and 2 != 0 },
            choice("Click Volume", listOf("Quiet", "Medium", "Loud"), { prefs.clickVolume }) { prefs.clickVolume = it },
            choice("Face Buttons", listOf("Xbox", "Nintendo"), { if (prefs.swapFaceButtons) 1 else 0 }) { prefs.swapFaceButtons = it == 1 },
        ))
    }

    private fun aboutPage() = ListPage("About", Kind.SETTINGS) {
        val free = try {
            val s = StatFs(Environment.getExternalStorageDirectory().path)
            String.format("%.1f GB", s.availableBytes / 1e9)
        } catch (_: Exception) { "?" }
        fun info(k: String, v: String) = Item(k, value = { v }, chevron = false)
        ListSourceOf(listOf(
            info("Songs", "${lib.songs().size}"),
            info("Playlists", "${lib.playlists().size}"),
            info("Voice Memos", "${lib.memos.size}"),
            info("Free Space", free),
            info("Version", "0.2"),
        ))
    }

    private fun eqPage() = ListPage("Equalizer", Kind.SETTINGS) {
        ListSourceOf(PlayerController.EQ_NAMES.map { n ->
            Item(n, chevron = false, checked = { prefs.eq == n }) { nav -> prefs.eq = n; player.applyEq(); nav.redraw() }
        })
    }

    private fun mainMenuPage() = ListPage("Main Menu", Kind.SETTINGS) {
        val fixed = setOf("Music", "Settings", "Now Playing")
        val names = rootTitles.filter { it !in fixed && !it.startsWith("Scanning") && it != "No Music" }
        ListSourceOf(names.map { n ->
            Item(n, chevron = false, checked = { n !in prefs.hiddenMenu }) { nav ->
                prefs.hiddenMenu = if (n in prefs.hiddenMenu) prefs.hiddenMenu - n else prefs.hiddenMenu + n
                nav.redraw()
            }
        })
    }

    private fun resetPage() = ListPage("Reset Settings", Kind.SETTINGS) {
        ListSourceOf(listOf(
            Item("Cancel", chevron = false) { it.pop() },
            Item("Reset", chevron = false) { nav ->
                prefs.reset(); player.applyModes(); player.applyEq(); player.applyVolumeLimit(); player.setSleepTimer(0)
                nav.pop()
            },
        ))
    }

    companion object {
        fun songCount(n: Int) = if (n == 1) "1 song" else "$n songs"
        fun fmtTime(ms: Long): String {
            val s = (ms / 1000).coerceAtLeast(0)
            val h = s / 3600
            return if (h > 0) "%d:%02d:%02d".format(h, s % 3600 / 60, s % 60) else "%d:%02d".format(s / 60, s % 60)
        }
    }
}
