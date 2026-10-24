package com.ipodemu.playback

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.net.Uri
import android.os.ParcelFileDescriptor
import com.ipodemu.App
import java.io.FileNotFoundException

/**
 * Covers for other apps (Android Auto, a head unit, the system media card): they cannot read this app's private files, so a cover is handed out as
 * content://com.ipodemu.art/<cover key>, read-only, one file per key, nothing else. A cover that is not on the phone yet is fetched while the car waits
 * (CoverWait): the car asks only once, and kept a blank square when told it was not there.
 */
class ArtProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    override fun openFile(uri: Uri, mode: String): ParcelFileDescriptor {
        if (mode != "r") throw FileNotFoundException("read only")
        val key = uri.lastPathSegment ?: throw FileNotFoundException()
        if (key.length > 400 || key.contains('/') || key.contains('\\') || key.contains("..")) throw FileNotFoundException()
        val ctx = context ?: throw FileNotFoundException()
        val art = App.of(ctx).art
        val file = CoverWait.file({ art.bestFile(key) }, { art.load(key, priority = com.ipodemu.library.Priority.VISIBLE) }, 5_000) ?: placeholder(ctx)
        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
    }

    /** The FLACie logo, for an album or artist the server has no picture of: the car otherwise draws an empty square in the grid. Made once, kept in the cache. */
    private fun placeholder(ctx: android.content.Context): java.io.File {
        val f = java.io.File(ctx.cacheDir, "car-placeholder.png")
        if (f.length() > 0) return f
        synchronized(ArtProvider::class.java) {
            if (f.length() > 0) return f
            val b = android.graphics.BitmapFactory.decodeResource(ctx.resources, com.ipodemu.R.mipmap.ic_launcher_art) ?: throw FileNotFoundException()
            val tmp = java.io.File(ctx.cacheDir, "car-placeholder.tmp")
            tmp.outputStream().use { b.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }
            tmp.renameTo(f)
        }
        return f
    }

    override fun getType(uri: Uri): String = "image/*"
    override fun query(uri: Uri, projection: Array<out String>?, selection: String?, selectionArgs: Array<out String>?, sortOrder: String?): Cursor? = null
    override fun insert(uri: Uri, values: ContentValues?): Uri? = null
    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int = 0
    override fun update(uri: Uri, values: ContentValues?, selection: String?, selectionArgs: Array<out String>?): Int = 0

    companion object {
        const val AUTHORITY = "com.ipodemu.art"
        /** The address of a cover for other apps, or null when there is no key. */
        fun uri(key: String?): Uri? = key?.takeIf { it.isNotEmpty() }?.let { Uri.parse("content://$AUTHORITY/" + Uri.encode(it)) }
    }
}
