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
 * content://com.ipodemu.art/<cover key>, read-only, one file per key, nothing else. A cover that is not on the phone yet is simply not there (the browse list
 * asks for it to be fetched, and the next look finds it).
 */
class ArtProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    override fun openFile(uri: Uri, mode: String): ParcelFileDescriptor {
        if (mode != "r") throw FileNotFoundException("read only")
        val key = uri.lastPathSegment ?: throw FileNotFoundException()
        if (key.length > 400 || key.contains('/') || key.contains('\\') || key.contains("..")) throw FileNotFoundException()
        val ctx = context ?: throw FileNotFoundException()
        val file = App.of(ctx).art.bestFile(key) ?: throw FileNotFoundException()
        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
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
