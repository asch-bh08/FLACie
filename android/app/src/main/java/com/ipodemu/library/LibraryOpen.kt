package com.ipodemu.library

/** F3: the phone opens from the library it saved, then asks the network. [shown] runs as soon as the saved list is up, so nothing waits on the network. */
object LibraryOpen {
    suspend fun run(loadSaved: suspend () -> Unit, shown: () -> Unit, refresh: suspend () -> Unit) {
        loadSaved()
        shown()
        refresh()
    }
}
