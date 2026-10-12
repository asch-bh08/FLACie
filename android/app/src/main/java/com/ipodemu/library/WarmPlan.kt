package com.ipodemu.library

/** Which covers to make ready when a song starts: the previous song and the next four at row size, the previous and next two at Now Playing size. Blank keys are left out. */
object WarmPlan {
    fun rows(keys: List<String?>, index: Int): List<String> = (index - 1..index + 4).mapNotNull { keys.getOrNull(it) }
    fun large(keys: List<String?>, index: Int): List<String> = (index - 1..index + 2).mapNotNull { keys.getOrNull(it) }
}
