package com.ipodemu.library

import com.ipodemu.library.tags.RandomAccess
import com.ipodemu.library.tags.ReadFailure
import jcifs.smb.SmbFile
import jcifs.smb.SmbRandomAccessFile
import java.io.Closeable

/** Range reads of one SMB file for the tag reader: one open handle, seek + read, never the whole file. */
class NasSmbAccess(private val file: SmbFile, knownSize: Long = -1L) : RandomAccess, ReadFailure, Closeable {
    private var raf: SmbRandomAccessFile? = null
    @Volatile override var failed = false; private set

    override val size: Long = if (knownSize >= 0) knownSize else try { file.length() } catch (_: Exception) { failed = true; 0L }

    override fun read(offset: Long, length: Int): ByteArray {
        if (failed || length <= 0 || offset < 0) return ByteArray(0)
        return try {
            val r = raf ?: SmbRandomAccessFile(file, "r").also { raf = it }
            r.seek(offset)
            val buf = ByteArray(length)
            var n = 0
            while (n < length) {
                val k = r.read(buf, n, length - n)
                if (k <= 0) break
                n += k
            }
            if (n == length) buf else buf.copyOf(n)
        } catch (_: Exception) {
            failed = true
            ByteArray(0)
        }
    }

    override fun close() { try { raf?.close() } catch (_: Exception) {}; raf = null }
}
