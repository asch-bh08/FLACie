package com.ipodemu.nav

class Navigator(root: ListPage) : Nav {
    enum class Transition { NONE, PUSH, POP }

    val stack = arrayListOf<Page>(root)
    val top: Page get() = stack.last()
    val nowPlaying = NowPlayingPage()

    /** (transition, page that was on top before). */
    var onNavigate: ((Transition, Page) -> Unit)? = null
    var onRedraw: (() -> Unit)? = null

    override fun push(page: Page) {
        val prev = top
        if (page is NowPlayingPage) page.mode = NowPlayingPage.Mode.VOLUME
        stack.add(page)
        onNavigate?.invoke(Transition.PUSH, prev)
    }

    override fun pop(): Boolean {
        if (stack.size <= 1) return false
        val prev = stack.removeAt(stack.lastIndex)
        onNavigate?.invoke(Transition.POP, prev)
        return true
    }

    override fun toRoot() {
        if (stack.size <= 1) return
        val prev = top
        while (stack.size > 1) stack.removeAt(stack.lastIndex)
        onNavigate?.invoke(Transition.POP, prev)
    }

    override fun showNowPlaying() {
        if (top is NowPlayingPage) return
        nowPlaying.mode = NowPlayingPage.Mode.VOLUME
        push(nowPlaying)
    }

    override fun reset(root: ListPage, then: List<Page>) {
        val prev = top
        stack.clear(); stack.add(root); stack.addAll(then)
        onNavigate?.invoke(Transition.NONE, prev)
    }

    override fun redraw() { onRedraw?.invoke() }

    fun refreshLists() {
        for (p in stack) if (p is ListPage) p.refresh()
        redraw()
    }

    /** Move selection in the top list. Returns detents actually applied (0 at the ends). */
    fun scroll(n: Int): Int {
        val p = top as? ListPage ?: return 0
        p.free = false
        val target = (p.selected + n).coerceIn(0, (p.source.size - 1).coerceAtLeast(0))
        val moved = target - p.selected
        p.selected = target
        return moved
    }

    fun select() {
        when (val p = top) {
            is ListPage -> if (p.source.size > 0) p.source[p.selected].onSelect(this)
            is NowPlayingPage -> {}
        }
    }
}
