package com.ipodemu.nav

enum class Icon { NONE, MUSIC, VOICE, EXTRAS, BROWSE, PLAYLISTS, ARTISTS, ALBUMS, SONGS, GENRES, SETTINGS, SHUFFLE, NOW_PLAYING }

/** What a list contains; themes use it to pick row style (e.g. art thumbnails on albums). */
enum class Kind { MENU, SONGS, ALBUMS, ARTISTS, MEMOS, SETTINGS, GENERIC }

class Item(
    val title: String,
    val subtitle: String? = null,
    val artKey: String? = null,
    val icon: Icon = Icon.NONE,
    val chevron: Boolean = true,
    val value: (() -> String?)? = null,
    val checked: (() -> Boolean)? = null,
    /** Extra line shown in the artwork pane (album + length, song count...). */
    val detail: String? = null,
    val onSelect: (Nav) -> Unit = {},
)

interface ListSource {
    val size: Int
    operator fun get(i: Int): Item
}

class ListSourceOf(private val items: List<Item>) : ListSource {
    override val size get() = items.size
    override fun get(i: Int) = items[i]
}

/** Builds items on demand and memoizes them, so 20k-song lists cost nothing until drawn. */
class LazySource(override val size: Int, private val make: (Int) -> Item) : ListSource {
    private val cache = arrayOfNulls<Item>(size)
    override fun get(i: Int): Item = cache[i] ?: make(i).also { cache[i] = it }
}

sealed class Page

class ListPage(
    val title: String,
    val kind: Kind = Kind.GENERIC,
    private val provider: () -> ListSource,
) : Page() {
    var source: ListSource = provider(); private set
    var selected = 0
    /** Smoothed first-visible-row position (fractional), owned by the renderer/view. */
    var scroll = 0f
    /** True while the list is positioned by touch (drag/fling) rather than following the selection. */
    var free = false

    fun refresh() {
        source = provider()
        selected = selected.coerceIn(0, (source.size - 1).coerceAtLeast(0))
    }

    companion object {
        fun of(title: String, kind: Kind, items: List<Item>) = ListPage(title, kind) { ListSourceOf(items) }
    }
}

class NowPlayingPage : Page() {
    enum class Mode { VOLUME, SCRUB }
    var mode = Mode.VOLUME
}

interface Nav {
    fun push(page: Page)
    fun pop(): Boolean
    fun toRoot()
    fun showNowPlaying()
    /** Replace the whole stack (used when the theme, and therefore the menu tree, changes). */
    fun reset(root: ListPage, then: List<Page> = emptyList())
    fun redraw()
}
