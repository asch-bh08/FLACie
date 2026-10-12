package com.ipodemu.library

/**
 * Up next shows, besides the songs that are in the queue, the Autoplay suggestions that are still being fetched (they are not queue items yet, so
 * nothing in the player can mark them). This is the pure plan for merging the two for display only:
 *
 *  - queue songs keep their own queue index, order and actions untouched (the pending rows are never part of the queue, never playable or removable);
 *  - a divider "Autoplay · up next after this" sits above the first queued song that Autoplay added, or, when none was, above the pending rows;
 *  - a pending suggestion is shown while it has something to say (running, finished but not queued yet, or failed within the window) and is dropped
 *    the moment its song is in the queue (no duplicate row at the switch) or when its tag has aged out.
 */
object UpNextMerge {
    const val DIVIDER = "Autoplay · up next after this"

    /** An Autoplay suggestion asked for from the downloaders. [key] is its [matchKey]. */
    class Pending(val key: String, val title: String, val artist: String, val album: String, val durationMs: Long, val artUrl: String?)
    class PendingRow(val pending: Pending, val tag: DownloadTag)

    /**
     * [dividerBefore]: the queue index of the first upcoming Autoplay song the divider goes above (null = none); [dividerAtEnd]: the divider goes after
     * the last queue row because only pending rows are Autoplay's; [pending]: the rows to show after the queue rows, in the order they will play.
     */
    class Plan(val dividerBefore: Int?, val dividerAtEnd: Boolean, val pending: List<PendingRow>)

    /**
     * [upcoming] = the songs after the playing one as (queue index, track); [queueKeys] = the match keys of every song in the queue;
     * [isAutoplay] = whether Autoplay added that song; [tagOf] = the download tag of a pending key at this moment.
     */
    fun plan(upcoming: List<Pair<Int, Track>>, queueKeys: Set<String>, isAutoplay: (Track) -> Boolean, pending: List<Pending>, tagOf: (String) -> DownloadTag): Plan {
        val first = upcoming.firstOrNull { isAutoplay(it.second) }?.first
        val rows = pending.mapNotNull { p ->
            if (p.key in queueKeys) null
            else tagOf(p.key).let { t -> if (t is DownloadTag.None || t is DownloadTag.NewDownload) null else PendingRow(p, t) }
        }
        return Plan(first, first == null && rows.isNotEmpty(), rows)
    }
}
