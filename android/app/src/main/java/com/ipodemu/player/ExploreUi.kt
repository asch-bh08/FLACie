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

/** Explore: the library cut five ways (songs, albums, artists, genres, charts) with a text filter, A to Z, genre, decade, lossless or lossy and a sort. */
@Composable
fun ExploreScreen(nav: PlayerNav, snap: PlayerSnap) {
    LocalLibRev.current
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
    var sort by rememberSaveable { mutableStateOf("az") }
    val sorts = EXPLORE_SORTS[tab]
    if (sorts != null && sorts.none { it.first == sort }) sort = "az"
    val libRev = LocalLibRev.current
    val songs = remember(libRev) { lib.songs() }
    val choices = remember(libRev) {
        val gs = songs.filter { it.genre.isNotBlank() }.groupBy { it.genre.trim().lowercase() }.filter { it.value.size >= 3 }.map { it.value.first().genre.trim() }.sortedBy { it.lowercase() }
        gs to songs.map { decadeOf(it.year) }.filter { it.isNotEmpty() }.distinct().sortedDescending()
    }
    val filtering = query.isNotBlank() || letter.isNotEmpty() || genre.isNotEmpty() || decade.isNotEmpty() || quality.isNotEmpty()
    val words = remember(query) { SearchRank.words(query) }
    fun hit(vararg fields: String): Boolean { if (words.isEmpty()) return true; val glue = SearchRank.words(fields.joinToString(" ")).joinToString(""); return words.all { glue.contains(it) } }
    fun match(t: Track) = (genre.isEmpty() || t.genre.trim().equals(genre, true)) && (decade.isEmpty() || decadeOf(t.year) == decade) &&
        (quality.isEmpty() || (if (quality == "hires") t.hiRes else (quality == "lossless") == (ext(t) in LOSSLESS)))
    fun letterOk(name: String) = letter.isEmpty() || letterOf(sortKey(name)) == letter

    Column(Modifier.fillMaxSize()) {
        ModernTopBar("Explore", null)
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            for (t in listOf("Songs", "Albums", "Artists", "Genres", "Charts")) GlossPill(t, { tab = t }, primary = tab == t, height = 36.dp)
            GlossPill("Favorites, sources…", { nav.push(Screen.Sources) }, icon = Glyph.HEART, height = 36.dp)
        }
        if (tab == "Charts") { ChartsPanel(nav, snap); return@Column }
        // filter row: the text box, then the pickers
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Row(Modifier.width(190.dp).height(36.dp).clip(RoundedCornerShape(18.dp)).background(Color(0x1FFFFFFF)).padding(horizontal = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                GlyphIcon(Glyph.SEARCH, Modifier.size(16.dp), sc.onBgDim)
                Box(Modifier.padding(start = 8.dp).weight(1f)) {
                    if (query.isEmpty()) Txt("Filter this list", size = 14f, color = sc.onBgDim)
                    BasicTextField(query, { query = it }, singleLine = true, cursorBrush = SolidColor(sc.accent), textStyle = TextStyle(color = sc.onBg, fontSize = 14.sp), modifier = Modifier.fillMaxWidth())
                }
            }
            GlossPill("Sort: " + (sorts?.firstOrNull { it.first == sort }?.second ?: ""), {
                nav.sheet = SheetSpec("Sort", null, (sorts ?: emptyList()).map { (id, label) -> SheetItem(label, if (id == sort) Glyph.CHECK else Glyph.LIST) { sort = id } })
            }, height = 34.dp)
            GlossPill("Genre: " + genre.ifEmpty { "Any" }, {
                nav.sheet = SheetSpec("Genre", null, listOf(SheetItem("Any", if (genre.isEmpty()) Glyph.CHECK else Glyph.LIST) { genre = "" }) + choices.first.map { g -> SheetItem(g, if (g == genre) Glyph.CHECK else Glyph.LIST) { genre = g } })
            }, primary = genre.isNotEmpty(), height = 34.dp)
            GlossPill("Decade: " + decade.ifEmpty { "Any" }, {
                nav.sheet = SheetSpec("Decade", null, listOf(SheetItem("Any", if (decade.isEmpty()) Glyph.CHECK else Glyph.LIST) { decade = "" }) + choices.second.map { d -> SheetItem(d, if (d == decade) Glyph.CHECK else Glyph.LIST) { decade = d } })
            }, primary = decade.isNotEmpty(), height = 34.dp)
            GlossPill("Quality: " + when (quality) { "lossless" -> "Lossless"; "lossy" -> "Lossy"; "hires" -> "Hi-Res"; else -> "Any" }, {
                nav.sheet = SheetSpec("Quality", null, listOf("" to "Any", "lossless" to "Lossless (FLAC, ALAC…)", "lossy" to "Lossy (MP3, AAC…)", "hires" to "✦ Hi-Res").map { (id, label) -> SheetItem(label, if (id == quality) Glyph.CHECK else Glyph.LIST) { quality = id } })
            }, primary = quality.isNotEmpty(), height = 34.dp)
            if (filtering) GlossPill("Clear", { query = ""; letter = ""; genre = ""; decade = ""; quality = "" }, icon = Glyph.CLOSE, height = 34.dp)
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
                val list = remember(songs, query, letter, genre, decade, quality, sort) {
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
                        GlossPill("Shuffle", { app.player.play(list, list.indices.random(), true); nav.nowPlaying = true }, icon = Glyph.SHUFFLE, primary = true, height = 34.dp)
                        Txt(if (filtering) "${list.size} of ${songs.size} songs" else songCount(list.size), Modifier.weight(1f), size = 13f, color = sc.onBgDim, align = androidx.compose.ui.text.style.TextAlign.End)
                    }
                })
            }
            "Albums" -> {
                val list = remember(libRev, query, letter, genre, decade, quality, sort) {
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
                val list = remember(libRev, query, letter, genre, decade, quality, sort) {
                    lib.artists().filter { letterOk(it.name) && hit(it.name) && it.tracks.any { t -> match(t) } }.let { l -> when (sort) { "za" -> l.sortedByDescending { sortKey(it.name) }; "long" -> l.sortedByDescending { it.tracks.size }; else -> l } }
                }
                if (list.isEmpty()) EmptyState(if (filtering) "Nothing matches these filters" else "No artists yet") else GroupList(list, nav, circle = true) { Screen.Detail(DetailKind.ARTIST, it.name) }
            }
            else -> {
                val list = remember(libRev, query, letter, genre, decade, quality, sort) {
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
                    IpodRow({ if (mine != null) { app.player.play(listOf(mine), 0, null); nav.nowPlaying = true } else app.library.requestDownload(s.artist, s.title, "", 0) }, height = 58.dp,
                        leading = { Box(Modifier.width(34.dp), contentAlignment = Alignment.Center) { Txt("${s.rank}", size = 15f, weight = FontWeight.Bold, color = sc.onBgDim) } },
                        trailing = { Txt(if (mine != null) "Play" else status?.message?.take(16) ?: "Download", size = 13f, color = sc.accent) }) { _ ->
                        Column { Txt(s.title, size = 16f, weight = FontWeight.Medium); Txt(s.artist, size = 13f, color = sc.onBgDim) }
                    }
                }
            }
        }
    }
}
