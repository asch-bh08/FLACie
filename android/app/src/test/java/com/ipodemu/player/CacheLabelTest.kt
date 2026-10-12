package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

class CacheLabelTest {
    @Test fun sizesReadNaturally() {
        assertEquals("0 KB", cacheSizeLabel(0))
        assertEquals("1 KB", cacheSizeLabel(1))
        assertEquals("812 KB", cacheSizeLabel(812L * 1024))
        assertEquals("12.4 MB", cacheSizeLabel((12.4 * 1048576).toLong()))
        assertEquals("1.0 GB", cacheSizeLabel(1073741824L))
        assertEquals("3.5 GB", cacheSizeLabel((3.5 * 1073741824).toLong()))
        assertEquals("0 KB", cacheSizeLabel(-5))
    }
}
