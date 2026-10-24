package com.ipodemu.playback

import android.app.PendingIntent
import android.content.Intent
import android.os.Bundle
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.CommandButton
import androidx.media3.session.DefaultMediaNotificationProvider
import androidx.media3.session.LibraryResult
import androidx.media3.session.MediaLibraryService
import androidx.media3.session.MediaSession
import androidx.media3.session.SessionCommand
import androidx.media3.session.SessionResult
import com.google.common.collect.ImmutableList
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import com.google.common.util.concurrent.SettableFuture
import com.ipodemu.App
import com.ipodemu.R
import com.ipodemu.ui.MainActivity
import java.util.concurrent.Executors
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

/**
 * Makes the app a proper Android media player: a MediaSession (lock screen, quick-settings media card, Bluetooth and
 * headset buttons) plus a playback notification, kept alive as a foreground service while music plays. Tapping the notification reopens the iPod UI.
 * It is also a media LIBRARY: Android Auto, a head unit or a voice command can browse Recently played, Favourites, Playlists, Albums, Artists and Downloaded
 * ([BrowseTree], a few songs at a time) and play from there; covers reach them through [ArtProvider]. Two custom buttons ride along: favourite (the heart follows the
 * song) and speed (taps cycle 1x, 1.25x ... 3x, 1x).
 */
@UnstableApi
class PlaybackService : MediaLibraryService() {
    private var session: MediaLibrarySession? = null
    private var unobserve: (() -> Unit)? = null
    private var lastLayoutKey: String? = null
    /** Browsing reads the library: never on the main thread. */
    private val io = Executors.newSingleThreadExecutor { r -> Thread(r, "browse").also { it.isDaemon = true } }

    private val favCommand = SessionCommand(CMD_FAVORITE, Bundle.EMPTY)
    private val speedCommand = SessionCommand(CMD_SPEED, Bundle.EMPTY)

    private fun <T> async(block: () -> T): ListenableFuture<T> {
        val f = SettableFuture.create<T>()
        io.execute { try { f.set(block()) } catch (e: Throwable) { f.setException(e) } }
        return f
    }

    private fun nodeItem(n: BrowseNode): MediaItem {
        // a folder drawn as a cover (album, playlist, artist, even one with no picture yet) is only opened, not played straight off: Android Auto draws a grid of covers only for folders that
        // can't be played, and showed albums and playlists as plain lists before. Its songs still play (and queue the rest) from inside it; voice still plays it whole
        val md = MediaMetadata.Builder().setTitle(n.title).setIsBrowsable(n.browsable).setIsPlayable(n.playable && !(n.browsable && (n.grid || n.cover)))
            .setMediaType(if (n.browsable) MediaMetadata.MEDIA_TYPE_FOLDER_MIXED else MediaMetadata.MEDIA_TYPE_MUSIC)
        n.subtitle?.let { md.setSubtitle(it); if (!n.browsable) md.setArtist(it) }
        (ArtProvider.uri(n.artKey) ?: n.icon?.let { android.net.Uri.parse("android.resource://$packageName/drawable/$it") })?.let { md.setArtworkUri(it) }
        // what the car draws: this folder's contents as big covers or a list, the heading a row sits under, and a tick on a song kept on the phone
        val extras = Bundle()
        if (n.browsable) { extras.putInt(STYLE_BROWSABLE, if (n.grid) STYLE_GRID else STYLE_LIST); extras.putInt(STYLE_PLAYABLE, STYLE_LIST) }
        n.group?.let { extras.putString(STYLE_GROUP, it) }
        if (n.cover) extras.putInt(STYLE_SINGLE, STYLE_GRID)
        if (n.offline) extras.putLong(DOWNLOAD_STATUS, 2L)
        md.setExtras(extras)
        return MediaItem.Builder().setMediaId(n.id).setMediaMetadata(md.build()).build()
    }

    /** For the whole tree: songs as a list with covers, folders as a list unless they say otherwise, and search on. */
    private fun rootParams(): LibraryParams = LibraryParams.Builder().setExtras(Bundle().apply {
        // without CONTENT_STYLE_SUPPORTED the car ignores every hint (albums and playlists came out as plain lists)
        // folders as covers by default (the car took no notice of the hint on the Albums and Playlists tabs themselves); Home asks for a list on its own item
        putBoolean(STYLE_SUPPORTED, true); putInt(STYLE_BROWSABLE, STYLE_GRID); putInt(STYLE_PLAYABLE, STYLE_LIST); putBoolean(SEARCH_SUPPORTED, true)
    }).build()

    private val library = object : MediaLibrarySession.Callback {
        override fun onConnect(session: MediaSession, controller: MediaSession.ControllerInfo): MediaSession.ConnectionResult {
            val commands = MediaSession.ConnectionResult.DEFAULT_SESSION_AND_LIBRARY_COMMANDS.buildUpon().add(favCommand).add(speedCommand).build()
            return MediaSession.ConnectionResult.AcceptedResultBuilder(session).setAvailableSessionCommands(commands).setCustomLayout(layout()).build()
        }

        override fun onCustomCommand(session: MediaSession, controller: MediaSession.ControllerInfo, customCommand: SessionCommand, args: Bundle): ListenableFuture<SessionResult> {
            val app = App.of(this@PlaybackService)
            // the player's own thread is the main thread, where this runs
            when (customCommand.customAction) {
                CMD_FAVORITE -> app.player.current?.let { app.userData.toggleFavorite(it.path) }
                CMD_SPEED -> app.player.setSpeed(SpeedPolicy.cycle(app.player.speed))
            }
            refreshLayout(force = true)
            return Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
        }

        override fun onGetLibraryRoot(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, params: LibraryParams?): ListenableFuture<LibraryResult<MediaItem>> =
            Futures.immediateFuture(LibraryResult.ofItem(nodeItem(BrowseNode(BrowseTree.ROOT, "FLACie", null, browsable = true, playable = false, artKey = null)), rootParams()))

        override fun onGetChildren(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, parentId: String, page: Int, pageSize: Int, params: LibraryParams?): ListenableFuture<LibraryResult<ImmutableList<MediaItem>>> =
            async {
                val app = App.of(this@PlaybackService)
                val nodes = BrowseTree.children(parentId, page, pageSize, AppBrowseSource(app))
                opened.add(parentId)
                // the covers of this page are fetched in the background, so the next look finds them
                app.art.warmAround(nodes.mapNotNull { it.artKey }, emptyList())
                LibraryResult.ofItemList(ImmutableList.copyOf(nodes.map(::nodeItem)), params)
            }

        override fun onGetItem(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, mediaId: String): ListenableFuture<LibraryResult<MediaItem>> =
            async {
                val src = AppBrowseSource(App.of(this@PlaybackService))
                val node = BrowseTree.root().firstOrNull { it.id == mediaId }
                    ?: BrowseTree.resolve(mediaId, src)?.let { r -> r.tracks.getOrNull(r.index) }?.let { t -> BrowseNode(mediaId, t.title, t.artist.ifEmpty { null }, false, true, t.artKey, t.path) }
                if (node == null) LibraryResult.ofError(LibraryResult.RESULT_ERROR_BAD_VALUE) else LibraryResult.ofItem(nodeItem(node), null)
            }

        override fun onSearch(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, query: String, params: LibraryParams?): ListenableFuture<LibraryResult<Void>> =
            Futures.immediateFuture(LibraryResult.ofVoid())

        override fun onGetSearchResult(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, query: String, page: Int, pageSize: Int, params: LibraryParams?): ListenableFuture<LibraryResult<ImmutableList<MediaItem>>> =
            async { LibraryResult.ofItemList(ImmutableList.copyOf(BrowseTree.searchNodes(query, page, pageSize, AppBrowseSource(App.of(this@PlaybackService))).map(::nodeItem)), params) }

        /** A request to play something: a browse id (handled by the player, see PlayerController.sessionPlayer) or a voice request ("play X"), which becomes the first matching song. */
        override fun onSetMediaItems(mediaSession: MediaSession, controller: MediaSession.ControllerInfo, mediaItems: MutableList<MediaItem>, startIndex: Int, startPositionMs: Long): ListenableFuture<MediaSession.MediaItemsWithStartPosition> =
            async {
                val src = AppBrowseSource(App.of(this@PlaybackService))
                val out = mediaItems.map { item ->
                    val q = item.requestMetadata.searchQuery
                    if (item.mediaId.isEmpty() && !q.isNullOrBlank()) {
                        val first = BrowseTree.searchPlay(q, src)?.let { r -> r.tracks[r.index] }
                        if (first != null) item.buildUpon().setMediaId(BrowseTree.songId("search:$q", first.path)).build() else item
                    } else item
                }
                MediaSession.MediaItemsWithStartPosition(out, startIndex, startPositionMs)
            }
    }

    override fun onCreate() {
        super.onCreate()
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val app = App.of(this)
        session = MediaLibrarySession.Builder(this, app.player.sessionPlayer, library).setSessionActivity(open).build()
        // Register the session so the notification manager tracks the player even with no external controller bound.
        addSession(session!!)
        setMediaNotificationProvider(DefaultMediaNotificationProvider(this).apply { setSmallIcon(R.drawable.ic_stat_flacie) })
        // the heart and the speed label follow the song and the speed
        unobserve = app.player.observe { refreshLayout(force = false) }
        refreshLayout(force = true)
        // the car keeps the lists it was first given: when the library or the account's lists change (loaded after the car connected, a sync, a new favourite) it is
        // told to look again, at most once a second (a library load changes it many times)
        val unLib = app.library.observe { listsChanged() }
        // only the phone's screens used to load the library: a car that woke FLACie on its own had nothing to show. It loads here too (a second start while
        // it is loading does nothing)
        app.library.start()
        val revs = scope.launch { androidx.compose.runtime.snapshotFlow { app.userData.rev }.collect { listsChanged() } }
        val unPlayer = unobserve
        unobserve = { unPlayer?.invoke(); unLib(); revs.cancel() }
    }

    private val scope = kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.SupervisorJob() + kotlinx.coroutines.Dispatchers.Main)
    private val main = android.os.Handler(android.os.Looper.getMainLooper())
    /** The lists the car has asked for, in order, so they can be refreshed too. */
    private val opened = java.util.Collections.synchronizedSet(LinkedHashSet<String>())
    private val tellCar = Runnable {
        val s = session ?: return@Runnable
        val src = AppBrowseSource(App.of(this))
        // the tabs, and every list the car has opened: a playlist opened while the library was still loading showed "No items" and kept it
        val ids = (BrowseTree.LIVE + opened.toList().takeLast(60)).distinct()
        io.execute { for (id in ids) { val n = try { BrowseTree.children(id, 0, BrowseTree.MAX_PAGE, src).size } catch (_: Exception) { 0 }; s.notifyChildrenChanged(id, n, null) } }
    }
    private fun listsChanged() { main.removeCallbacks(tellCar); main.postDelayed(tellCar, 1000) }

    private fun layout(): ImmutableList<CommandButton> {
        val app = App.of(this)
        val fav = isFavorite(app)
        val speed = app.player.speed
        return ImmutableList.of(
            CommandButton.Builder().setDisplayName(if (fav) "Remove from favourites" else "Favourite").setIconResId(if (fav) R.drawable.ic_fav_on else R.drawable.ic_fav)
                .setSessionCommand(favCommand).build(),
            CommandButton.Builder().setDisplayName("Speed " + SpeedPolicy.label(speed)).setIconResId(R.drawable.ic_speed).setSessionCommand(speedCommand).build(),
        )
    }

    /** The service can start (to hold the restored queue) before the favourites are loaded: until then, not a favourite. */
    private fun isFavorite(app: App): Boolean = try { app.player.current?.let { app.userData.isFavorite(it.path) } == true } catch (_: UninitializedPropertyAccessException) { false }

    /** Sets the custom buttons again when what they show (the heart, the speed) has changed. */
    private fun refreshLayout(force: Boolean) {
        val s = session ?: return
        val app = App.of(this)
        val key = (app.player.current?.path ?: "") + "|" + isFavorite(app) + "|" + app.player.speed
        if (!force && key == lastLayoutKey) return
        lastLayoutKey = key
        s.setCustomLayout(layout())
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaLibrarySession? = session

    /** Swiping the app away from recents stops the music, clears the notification and ends the service. */
    override fun onTaskRemoved(rootIntent: Intent?) {
        App.of(this).player.persistNow()   // the swipe-away stops the music, but the queue comes back (paused) next time
        val p = App.of(this).player.exo
        p.pause()
        p.stop()
        p.clearMediaItems()
        stopSelf()
    }

    override fun onDestroy() {
        unobserve?.invoke(); unobserve = null
        main.removeCallbacks(tellCar); scope.cancel()
        session?.release()
        session = null
        io.shutdown()
        super.onDestroy()
    }

    companion object {
        const val CMD_FAVORITE = "com.ipodemu.FAVORITE"
        const val CMD_SPEED = "com.ipodemu.SPEED"
        // Android Auto's content-style hints (the keys of androidx.media.utils.MediaConstants)
        private const val STYLE_BROWSABLE = "android.media.browse.CONTENT_STYLE_BROWSABLE_HINT"
        private const val STYLE_PLAYABLE = "android.media.browse.CONTENT_STYLE_PLAYABLE_HINT"
        private const val STYLE_GROUP = "android.media.browse.CONTENT_STYLE_GROUP_TITLE_HINT"
        private const val STYLE_SUPPORTED = "android.media.browse.CONTENT_STYLE_SUPPORTED"
        private const val STYLE_SINGLE = "android.media.browse.CONTENT_STYLE_SINGLE_ITEM_HINT"
        private const val STYLE_LIST = 1; private const val STYLE_GRID = 2
        private const val SEARCH_SUPPORTED = "android.media.browse.SEARCH_SUPPORTED"
        private const val DOWNLOAD_STATUS = "android.media.extra.DOWNLOAD_STATUS"
    }
}
