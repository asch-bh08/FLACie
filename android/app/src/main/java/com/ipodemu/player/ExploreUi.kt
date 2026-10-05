package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ipodemu.library.hiRes
import com.ipodemu.library.AudioFacts
import com.ipodemu.library.FlacieWebClient
import com.ipodemu.library.Group
import com.ipodemu.library.SearchRank
import com.ipodemu.library.Track
import com.ipodemu.library.matchKey
import com.ipodemu.library.sortKey
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

private val LOSSLESS = setOf("flac", "alac", "wav", "aiff", "aif", "ape", "wv", "dsf", "tta")
private fun ext(t: Track) = (t.filePath.ifEmpty { t.path.substringBefore('?') }).substringAfterLast('.', "").lowercase()
private fun decadeOf(y: Int) = if (y >= 1900) "${y / 10 * 10}s" else ""

private val EXPLORE_SORTS = mapOf(
    "Songs" to listOf("az" to "Title A–Z", "za" to "Title Z–A", "artist" to "Artist", "new" to "Recently added", "year" to "Newest year", "oldest" to "Oldest year", "long" to "Longest"),
    "Albums" to listOf("az" to "Name A–Z", "za" to "Name Z–A", "artist" to "Artist", "new" to "Recently added", "year" to "Newest year", "oldest" to "Oldest year", "long" to "Most songs"),
    "Artists" to listOf("az" to "Name A–Z", "za" to "Name Z–A", "long" to "Most songs"),
    "Genres" to listOf("az" to "Name A–Z", "za" to "Name Z–A", "long" to "Most songs"),
)

private val DEPTHS = listOf("16" to "16-bit", "24" to "24-bit", "32" to "32-bit")
private val RATES = listOf("lo" to "44.1 kHz or lower", "48" to "48 kHz", "96" to "88.2 to 96 kHz", "192" to "176.4 kHz and up")
private fun rateBand(hz: Int, band: String) = when (band) { "lo" -> hz in 1..44_100; "48" -> hz in 44_101..48_000; "96" -> hz in 48_001..96_000; "192" -> hz > 96_000; else -> true }

/** Explore: the library cut five ways (songs, albums, artists, genres, charts) with a text filter, A to Z, genre, decade, lossless or lossy and a sort. */
@Composable
fun ExploreScreen(nav: PlayerNav, snap: PlayerSnap) {
    LocalLibRev.current
    AudioFacts.version
    val app = LocalApp.current
    val sc = LocalScheme.current
    val lib = app.library
    app.userData.rev
    var tab by rememberSaveable { mutableStateOf("Songs") }
    var query by rememberSaveable { mutableStateOf("") }
    var letter by rememberSaveable { mutableStateOf("") }
    var genre by rememberSaveable { mutableStateOf("") }
    var decade by rememberSaveable { mutableStateOf("") }
    var quality by rememberSaveable { mutableStateOf("") }
    var depth by rememberSaveable { mutableStateOf("") }
    var rate by rememberSaveable { mutableStateOf("") }
    var sort by rememberSaveable { mutableStateOf("az") }
    val sorts = EXPLORE_SORTS[tab]
    if (sorts != null && sorts.none { it.first == sort }) sort = "az"
    val libRev = LocalLibRev.current
    val songs = remember(libRev) { lib.songs() }
    val choices = remember(libRev) {
        val gs = songs.filter { it.genre.isNotBlank() }.groupBy { it.genre.trim().lowercase() }.filter { it.value.size >= 3 }.map { it.value.first().genre.trim() }.sortedBy { it.lowercase() }
        gs to songs.map { decadeOf(it.year) }.filter { it.isNotEmpty() }.distinct().sortedDescending()
    }
    val filtering = query.isNotBlank() || letter.isNotEmpty() || genre.isNotEmpty() || decade.isNotEmpty() || quality.isNotEmpty() || depth.isNotEmpty() || rate.isNotEmpty()
    val words = remember(query) { SearchRank.words(query) }
    fun hit(vararg fields: String): Boolean { if (words.isEmpty()) return true; val glue = SearchRank.words(fields.joinToString(" ")).joinToString(""); return words.all { glue.contains(it) } }
    fun match(t: Track) = (genre.isEmpty() || t.genre.trim().equals(genre, true)) && (decade.isEmpty() || decadeOf(t.year) == decade) &&
        (quality.isEmpty() || (if (quality == "hires") t.hiRes else (quality == "lossless") == (AudioFacts.of(t)?.lossless ?: (ext(t) in LOSSLESS)))) &&
        (depth.isEmpty() || AudioFacts.of(t)?.let { it.depth > 0 && it.depth.toString() == depth } == true) &&
        (rate.isEmpty() || AudioFacts.of(t)?.let { rateBand(it.rate, rate) } == true)
    fun letterOk(name: String) = letter.isEmpty() || letterOf(sortKey(name)) == letter

    // the pickers live in one tidy Filters sheet instead of a row of seven pills
    fun pick(title: String, subtitle: String?, options: List<Pair<String, String>>, current: String, set: (String) -> Unit) {
        nav.sheet = SheetSpec(title, subtitle, options.map { (id, label) -> SheetItem(label, if (id == current) Glyph.CHECK else Glyph.LIST) { set(id) } })
    }
    val pendingNote = if (AudioFacts.pending > 0) "Still reading ${AudioFacts.pending} songs on the server" else null
    fun qualityName(q: String) = when (q) { "lossless" -> "Lossless"; "lossy" -> "Lossy"; "hires" -> "Hi-Res"; else -> "Any" }
    fun openFilters() {
        nav.sheet = SheetSpec("Filters", null, listOf(
            SheetItem("Genre · " + genre.ifEmpty { "Any" }, Glyph.NOTE) { pick("Genre", null, listOf("" to "Any") + choices.first.map { it to it }, genre) { genre = it } },
            SheetItem("Decade · " + decade.ifEmpty { "Any" }, Glyph.CLOCK) { pick("Decade", null, listOf("" to "Any") + choices.second.map { it to it }, decade) { decade = it } },
            SheetItem("Quality · " + qualityName(quality), Glyph.STAR) { pick("Quality", null, listOf("" to "Any", "lossless" to "Lossless (FLAC, ALAC…)", "lossy" to "Lossy (MP3, AAC…)", "hires" to "✦ Hi-Res"), quality) { quality = it } },
            SheetItem("Bit depth · " + (DEPTHS.firstOrNull { it.first == depth }?.second ?: "Any"), Glyph.INFO) { pick("Bit depth", pendingNote, listOf("" to "Any") + DEPTHS, depth) { depth = it } },
            SheetItem("Sample rate · " + (RATES.firstOrNull { it.first == rate }?.second ?: "Any"), Glyph.INFO) { pick("Sample rate", pendingNote, listOf("" to "Any") + RATES, rate) { rate = it } },
        ) + (if (genre.isNotEmpty() || decade.isNotEmpty() || quality.isNotEmpty() || depth.isNotEmpty() || rate.isNotEmpty()) listOf(SheetItem("Clear all filters", Glyph.CLOSE) { genre = ""; decade = ""; quality = ""; depth = ""; rate = "" }) else emptyList()))
    }
    val activeFilters = listOf(genre, decade, quality, depth, rate).count { it.isNotEmpty() }

    Column(Modifier.fillMaxSize()) {
        ModernTopBar("Explore", null) { IconAction(Glyph.HEART, "Favorites and sources", { nav.push(Screen.Sources) }) }
        // the five views, as one quiet row
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            for (t in listOf("Songs", "Albums", "Artists", "Genres", "Charts")) GlossPill(t, { tab = t }, primary = tab == t, height = 38.dp)
        }
        if (tab == "Charts") { ChartsPanel(nav, snap); return@Column }
        // one line: the filter box, Filters (with how many are on) and Sort
        Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 10.dp, bottom = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Row(Modifier.weight(1f).height(42.dp).clip(RoundedCornerShape(21.dp)).background(Palette.surface2).padding(horizontal = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                GlyphIcon(Glyph.SEARCH, Modifier.size(18.dp), sc.onBgDim)
                Box(Modifier.padding(start = 10.dp).weight(1f)) {
                    if (query.isEmpty()) Txt("Filter this list", size = 15f, color = sc.onBgDim)
                    BasicTextField(query, { query = it }, singleLine = true, cursorBrush = SolidColor(sc.accent), textStyle = TextStyle(color = sc.onBg, fontSize = 15.sp), modifier = Modifier.fillMaxWidth())
                }
                if (query.isNotEmpty()) Box(Modifier.size(28.dp).clip(RoundedCornerShape(14.dp)).clickable { query = "" }, contentAlignment = Alignment.Center) { GlyphIcon(Glyph.CLOSE, Modifier.size(14.dp), sc.onBgDim) }
            }
            GlossPill(if (activeFilters > 0) "Filters · $activeFilters" else "Filters", { openFilters() }, icon = Glyph.LIST, primary = activeFilters > 0, height = 42.dp)
            IconAction(Glyph.DOWN, "Sort: " + (sorts?.firstOrNull { it.first == sort }?.second ?: ""), {
                nav.sheet = SheetSpec("Sort", null, (sorts ?: emptyList()).map { (id, label) -> SheetItem(label, if (id == sort) Glyph.CHECK else Glyph.LIST) { sort = id } })
            }, size = 42.dp)
        }
        // what is on, each one tappable to turn off (only shown when something is)
        if (activeFilters > 0 || letter.isNotEmpty()) Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (letter.isNotEmpty()) GlossPill("Letter $letter", { letter = "" }, icon = Glyph.CLOSE, height = 32.dp)
            if (genre.isNotEmpty()) GlossPill(genre, { genre = "" }, icon = Glyph.CLOSE, height = 32.dp)
            if (decade.isNotEmpty()) GlossPill(decade, { decade = "" }, icon = Glyph.CLOSE, height = 32.dp)
            if (quality.isNotEmpty()) GlossPill(qualityName(quality), { quality = "" }, icon = Glyph.CLOSE, height = 32.dp)
            if (depth.isNotEmpty()) GlossPill(DEPTHS.firstOrNull { it.first == depth }?.second ?: depth, { depth = "" }, icon = Glyph.CLOSE, height = 32.dp)
            if (rate.isNotEmpty()) GlossPill(RATES.firstOrNull { it.first == rate }?.second ?: rate, { rate = "" }, icon = Glyph.CLOSE, height = 32.dp)
        }
        Box(Modifier.fillMaxSize()) {
        // the first-letter index: a slim rail down the right edge, over the list
        Column(
            Modifier.align(Alignment.CenterEnd).padding(end = 3.dp, top = 6.dp, bottom = 10.dp).fillMaxHeight().width(24.dp).clip(RoundedCornerShape(12.dp)).background(Color(0x99111114)).padding(vertical = 4.dp),
            horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.SpaceBetween,
        ) {
            for (l in listOf("") + listOf("#") + ('A'..'Z').map { it.toString() }) {
                val on = letter == l
                Box(Modifier.weight(1f).fillMaxWidth().background(if (on) sc.accent else Color.Transparent, RoundedCornerShape(6.dp)).clickable { letter = if (on) "" else l }, contentAlignment = Alignment.Center) {
                    Txt(if (l.isEmpty()) "All" else l, size = if (l.isEmpty()) 8.5f else 10.5f, weight = FontWeight.ExtraBold, color = if (on) Color.White else sc.onBgDim)
                }
            }
        }
        Box(Modifier.fillMaxSize().padding(end = 32.dp)) {
        when (tab) {
            "Songs" -> {
                val list = remember(songs, query, letter, genre, decade, quality, depth, rate, sort) {
                    songs.filter { letterOk(it.title) && match(it) && hit(it.title, it.artist, it.album) }.let { l ->
                        when (sort) {
                            "za" -> l.sortedByDescending { sortKey(it.title) }; "artist" -> l.sortedWith(compareBy({ sortKey(it.artist) }, { it.album }, { it.discNo }, { it.trackNo }))
                            "new" -> l.sortedByDescending { it.mtime }; "year" -> l.sortedByDescending { it.year }; "oldest" -> l.filter { it.year > 0 }.sortedBy { it.year }
                            "long" -> l.sortedByDescending { it.durationMs }; else -> l
                        }
                    }
                }
                if (list.isEmpty()) EmptyState(if (filtering) "Nothing matches these filters" else "No songs yet")
                else SongList(list, nav, snap, showArt = true, header = {
                    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                        GlossPill("Shuffle", { app.player.play(list, list.indices.random(), true) }, icon = Glyph.SHUFFLE, primary = true, height = 34.dp)
                        Txt(if (filtering) "${list.size} of ${songs.size} songs" else songCount(list.size), Modifier.weight(1f), size = 13f, color = sc.onBgDim, align = androidx.compose.ui.text.style.TextAlign.End)
                    }
                })
            }
            "Albums" -> {
                val list = remember(libRev, query, letter, genre, decade, quality, depth, rate, sort) {
                    lib.albums().filter { g -> letterOk(g.name) && hit(g.name, g.tracks.first().albumArtist.ifEmpty { g.tracks.first().artist }) && g.tracks.any { match(it) } }.let { l ->
                        when (sort) {
                            "za" -> l.sortedByDescending { sortKey(it.name) }; "artist" -> l.sortedBy { sortKey(it.tracks.first().albumArtist.ifEmpty { it.tracks.first().artist }) }
                            "new" -> l.sortedByDescending { g -> g.tracks.maxOf { it.mtime } }; "year" -> l.sortedByDescending { g -> g.tracks.maxOf { it.year } }
                            "oldest" -> l.filter { g -> g.tracks.any { it.year > 0 } }.sortedBy { g -> g.tracks.filter { it.year > 0 }.minOf { it.year } }
                            "long" -> l.sortedByDescending { it.tracks.size }; else -> l
                        }
                    }
                }
                if (list.isEmpty()) EmptyState(if (filtering) "Nothing matches these filters" else "No albums yet") else AlbumGrid(list, nav)
            }
            "Artists" -> {
                val list = remember(libRev, query, letter, genre, decade, quality, depth, rate, sort) {
                    lib.artists().filter { letterOk(it.name) && hit(it.name) && it.tracks.any { t -> match(t) } }.let { l -> when (sort) { "za" -> l.sortedByDescending { sortKey(it.name) }; "long" -> l.sortedByDescending { it.tracks.size }; else -> l } }
                }
                if (list.isEmpty()) EmptyState(if (filtering) "Nothing matches these filters" else "No artists yet") else GroupList(list, nav, circle = true) { Screen.Detail(DetailKind.ARTIST, it.name) }
            }
            else -> {
                val list = remember(libRev, query, letter, genre, decade, quality, depth, rate, sort) {
                    lib.genres().filter { letterOk(it.name) && hit(it.name) && it.tracks.any { t -> match(t) } }.let { l -> when (sort) { "za" -> l.sortedByDescending { sortKey(it.name) }; "long" -> l.sortedByDescending { it.tracks.size }; else -> l } }
                }
                if (list.isEmpty()) EmptyState(if (filtering) "Nothing matches these filters" else "No genres yet") else GroupList(list, nav, circle = false) { Screen.Detail(DetailKind.GENRE, it.name) }
            }
        }
}        }
    }
}

/** The charts the server reads (Top Songs, Pop, Hip-Hop…): play the ones you have, download the ones you don't. */
@Composable
private fun ChartsPanel(nav: PlayerNav, snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val web = remember { FlacieWebClient(app.prefs) }
    if (!web.available) {
        EmptyState("Charts come from FLACie Web. Open FLACie Web in a browser, signed in to the same Jellyfin account, and this phone finds it by itself.")
        return
    }
    var charts by remember { mutableStateOf<List<Pair<Int, String>>>(emptyList()) }
    var current by rememberSaveable { mutableStateOf(0) }
    var songs by remember { mutableStateOf<List<FlacieWebClient.ChartSong>?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(Unit) { try { charts = withContext(Dispatchers.IO) { web.settings().available } } catch (_: Exception) { } }
    LaunchedEffect(current) {
        songs = null; error = null
        try { songs = withContext(Dispatchers.IO) { web.chart(current) } } catch (e: Exception) { error = e.message ?: "The chart couldn't be loaded" }
    }
    val libRev = LocalLibRev.current
    val owned = remember(libRev) { app.library.songs().associateBy { matchKey(it.title, it.artist) } }
    Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        for ((id, name) in charts) GlossPill(name, { current = id }, primary = current == id, height = 34.dp)
    }
    val list = songs
    when {
        error != null -> EmptyState(error!!)
        list == null -> EmptyState("Loading the chart…")
        list.isEmpty() -> EmptyState("The chart couldn't be loaded right now")
        else -> {
            val have = list.count { owned.containsKey(matchKey(it.title, it.artist)) }
            LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
                item { Txt("$have of ${list.size} are in your library", Modifier.padding(horizontal = 16.dp, vertical = 6.dp), size = 13f, color = sc.onBgDim) }
                itemsIndexed(list, key = { _, s -> "${s.rank}${s.title}" }) { _, s ->
                    val mine = owned[matchKey(s.title, s.artist)]
                    val status = app.library.downloads[app.library.songKey(s.artist, s.title)]
                    IpodRow({ if (mine != null) { app.player.play(listOf(mine), 0, null) } else app.library.requestDownload(s.artist, s.title, "", 0) }, height = 58.dp,
                        leading = { Box(Modifier.width(34.dp), contentAlignment = Alignment.Center) { Txt("${s.rank}", size = 15f, weight = FontWeight.Bold, color = sc.onBgDim) } },
                        trailing = { Txt(if (mine != null) "Play" else status?.message?.take(16) ?: "Download", size = 13f, color = sc.accent) }) { _ ->
                        Column { Txt(s.title, size = 16f, weight = FontWeight.Medium); Txt(s.artist, size = 13f, color = sc.onBgDim) }
                    }
                }
            }
        }
    }
}
