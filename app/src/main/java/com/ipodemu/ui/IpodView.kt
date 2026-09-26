package com.ipodemu.ui

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.Canvas
import android.graphics.RectF
import android.hardware.display.DisplayManager
import android.view.Surface
import android.os.BatteryManager
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.VelocityTracker
import android.view.View
import android.view.WindowManager
import com.ipodemu.App
import com.ipodemu.input.Input
import com.ipodemu.input.Key
import com.ipodemu.input.KeyMapper
import com.ipodemu.input.WheelPhysics
import com.ipodemu.input.Zone
import com.ipodemu.nav.ListPage
import com.ipodemu.nav.MenuBuilder
import com.ipodemu.nav.Navigator
import com.ipodemu.nav.NowPlayingPage
import com.ipodemu.nav.Page
import com.ipodemu.playback.FileSpecs
import com.ipodemu.theme.CardLayout
import com.ipodemu.theme.DrawCtx
import com.ipodemu.theme.IpodTheme
import com.ipodemu.theme.NowPlayingModel
import com.ipodemu.theme.StatusInfo
import com.ipodemu.theme.Themes
import kotlin.math.abs
import kotlin.math.exp
import kotlin.math.floor
import kotlin.math.hypot
import kotlin.math.ln
import kotlin.math.min

/**
 * Single custom view: draws the faceplate (screen, click wheel, touch keys) and owns every input path.
 * Touch wheel, wheel taps, touch screen, touch keys, D-pad, hat axes and face buttons all funnel into [dispatch].
 */
class IpodView(context: Context) : View(context) {
    private val app = App.of(context)
    private val prefs = app.prefs
    private val player = app.player
    private val handler = Handler(Looper.getMainLooper())
    private val feedback = Feedback(context, prefs, this)
    private val physics = WheelPhysics()

    private var theme: IpodTheme = Themes.create(prefs)
    private val menu = MenuBuilder(app) { row -> applyTheme(row) }
    private val nav = Navigator(theme.buildRoot(menu))

    // layout
    private val screenRect = RectF()
    private val panel = RectF()
    private val bezel = RectF()
    private val bodyRect = RectF()
    private val screenClip = android.graphics.Path()
    private val queueRect = RectF()
    private val specsRect = RectF()
    private val keyRects = LinkedHashMap<Key, RectF>().apply { Key.values().forEach { put(it, RectF()) } }
    private var wheelCx = 0f
    private var wheelCy = 0f
    private var wheelR = 1f
    private var screenScale = 1f
    private var isFull = false
    /** Only the RG Rotate has the deployable controller; everywhere else fullscreen is manual (Settings > Layout). */
    private val isRotateDevice = android.os.Build.MODEL.contains("rotate", ignoreCase = true)
    /** Set by the controller-deployed detector; only used when Settings > Layout is Auto. */
    var controllerDeployed = false
        private set(v) { if (field != v) { field = v; refreshLayout() } }

    /**
     * RG Rotate: deploying the controller turns the screen 90 degrees and the system flips the display rotation
     * (closed = ROTATION_90 or 270, deployed = ROTATION_0 or 180), even while the panel is flat on a table.
     */
    /** Called by the activity when another app hands us an audio file. */
    fun openNowPlaying() { nav.showNowPlaying(); invalidate() }

    fun updateHinge() {
        if (!isRotateDevice) { controllerDeployed = false; return }
        val r = (context as? Activity)?.windowManager?.defaultDisplay?.rotation ?: return
        controllerDeployed = r == Surface.ROTATION_0 || r == Surface.ROTATION_180
    }

    private val displayListener = object : DisplayManager.DisplayListener {
        override fun onDisplayChanged(displayId: Int) { updateHinge() }
        override fun onDisplayAdded(displayId: Int) {}
        override fun onDisplayRemoved(displayId: Int) {}
    }

    // animation state
    private var lastFrame = SystemClock.uptimeMillis()
    private var transStart = 0L
    private var transKind = Navigator.Transition.NONE
    private var transPrev: Page? = null
    private var hudUntil = 0L
    private var pressed: Zone? = null
    private var pressedKey: Key? = null
    private var glowX = 0f
    private var glowY = 0f
    private var glowOn = false
    private var lastNavAt = 0L
    private var flingV = 0f
    private var lastHasQueue = player.hasQueue
    private var batteryAt = 0L
    private var battery = StatusInfo(false, 100, false)

    // backlight
    private var dimmed = false
    private var lastInput = SystemClock.uptimeMillis()
    private var swallowGesture = false

    private val idleCheck = object : Runnable {
        override fun run() {
            val s = prefs.backlightSec
            if (s > 0 && !dimmed && SystemClock.uptimeMillis() - lastInput > s * 1000L) {
                dimmed = true; applyDisplay(); invalidate()
            }
            handler.postDelayed(this, 1000)
        }
    }

    init {
        isFocusable = true
        isFocusableInTouchMode = true
        keepScreenOn = true
        nav.onNavigate = { kind, prev ->
            val top = nav.top
            if (top is ListPage && kind != Navigator.Transition.POP) { top.free = false; top.scroll = theme.targetScroll(top) }
            flingV = 0f
            lastNavAt = SystemClock.uptimeMillis()
            if (kind == Navigator.Transition.NONE) transKind = Navigator.Transition.NONE
            else { transKind = kind; transPrev = prev; transStart = SystemClock.uptimeMillis() }
            invalidate()
        }
        nav.onRedraw = { applyDisplay(); refreshLayout(); invalidate() }

        var pending = false
        app.library.onChange = {
            if (!pending) {
                pending = true
                handler.postDelayed({ pending = false; nav.refreshLists() }, 600)
            }
        }
        player.onChange = {
            if (player.hasQueue != lastHasQueue) { lastHasQueue = player.hasQueue; nav.refreshLists() }
            invalidate()
        }
        app.art.onLoaded = { invalidate() }
        setOnApplyWindowInsetsListener { _, ins ->
            val i = ins.getInsets(android.view.WindowInsets.Type.systemBars() or android.view.WindowInsets.Type.displayCutout())
            insets.set(i.left, i.top, i.right, i.bottom)
            computeLayout(); invalidate(); ins
        }
        app.library.start()
        handler.postDelayed(idleCheck, 1000)
        (context.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager).registerDisplayListener(displayListener, handler)
        updateHinge()
        applyDisplay()
    }

    fun release() {
        player.onChange = null; app.library.onChange = null; app.art.onLoaded = null
        (context.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager).unregisterDisplayListener(displayListener)
        handler.removeCallbacksAndMessages(null)
        feedback.release()
        setBrightness(-1f)
    }

    /** Rebuilds the skin from prefs and lands back in Settings on the row that was just changed. */
    private fun applyTheme(row: String) {
        app.ui.refreshFromPrefs()
        theme = Themes.create(prefs)
        computeLayout()
        val settings = menu.settingsPage()
        nav.reset(theme.buildRoot(menu), listOf(settings))
        transKind = Navigator.Transition.NONE
        invalidate()
    }

    // ---- backlight / brightness ---------------------------------------------

    /** Records user activity. Returns true if the screen was blanked, in which case the input only wakes it. */
    private fun noteInput(): Boolean {
        lastInput = SystemClock.uptimeMillis()
        if (!dimmed) return false
        dimmed = false; applyDisplay(); invalidate()
        return true
    }

    private fun applyDisplay() {
        setBrightness(if (dimmed) 0.005f else prefs.brightness)
    }

    private fun setBrightness(v: Float) {
        val w = (context as? Activity)?.window ?: return
        val lp = w.attributes
        lp.screenBrightness = if (v < 0f) WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE else v
        w.attributes = lp
    }

    // ---- layout ------------------------------------------------------------

    override fun onSizeChanged(w: Int, h: Int, ow: Int, oh: Int) { computeLayout() }

    private fun wantFull() = when (prefs.layoutMode) { 1 -> false; 2 -> true; else -> controllerDeployed }

    /** Re-run layout if the fullscreen decision changed (setting toggled or controller deployed/stowed). */
    fun refreshLayout() {
        if (wantFull() != isFull) { computeLayout(); invalidate() }
    }

    private val insets = android.graphics.Rect()
    private val area = RectF()

    /**
     * Fit the theme's design size into a screen rect of any aspect ratio: wider than the design -> match heights and
     * let the logical width grow; taller -> match widths and let the logical height grow. Themes lay out from
     * logicalW/logicalH, so phones, foldables and squares all get a natural list.
     */
    private fun fitLogical(sw: Float, sh: Float, designW: Int, designH: Int) {
        val designAspect = designW.toFloat() / designH
        if (sw / sh >= designAspect) {
            screenScale = sh / designH; theme.logicalH = designH.toFloat(); theme.logicalW = sw / screenScale
        } else {
            screenScale = sw / designW; theme.logicalW = designW.toFloat(); theme.logicalH = sh / screenScale
        }
    }

    private fun computeLayout() {
        val w = width.toFloat(); val h = height.toFloat()
        if (w == 0f) return
        val d = theme.device
        area.set(insets.left.toFloat(), insets.top.toFloat(), w - insets.right, h - insets.bottom)
        val aw = area.width(); val ah = area.height()
        if (aw <= 0f || ah <= 0f) return
        val s = min(aw, ah)
        isFull = wantFull()
        panel.set(0f, 0f, w, h)
        screenClip.reset()
        wheelCx = 0f; wheelCy = 0f; wheelR = -1f
        physics.setGeometry(0f, 0f, -1f)
        queueRect.setEmpty(); specsRect.setEmpty(); keyRects.values.forEach { it.setEmpty() }

        if (isFull) {
            // Controller deployed (or forced): no wheel, no cards, the screen takes the whole usable display.
            screenRect.set(area); bezel.set(screenRect)
            fitLogical(aw, ah, d.screenW, d.screenH)
            return
        }
        val b = theme.body
        if (b != null) {
            // Device skin: a physical iPod body centred in the usable area; only the screen and wheel are live.
            val bh = min(ah * 0.965f, aw * 0.98f / b.aspect); val bw = bh * b.aspect
            bodyRect.set(area.centerX() - bw / 2, area.centerY() - bh / 2, area.centerX() + bw / 2, area.centerY() + bh / 2)
            screenRect.set(
                bodyRect.left + b.screen.left * bw, bodyRect.top + b.screen.top * bh,
                bodyRect.left + b.screen.right * bw, bodyRect.top + b.screen.bottom * bh,
            )
            screenScale = screenRect.width() / d.screenW
            theme.logicalW = d.screenW.toFloat(); theme.logicalH = d.screenH.toFloat()
            wheelCx = bodyRect.left + b.wheelCx * bw; wheelCy = bodyRect.top + b.wheelCy * bh; wheelR = b.wheelR * bw
            physics.setGeometry(wheelCx, wheelCy, wheelR)
            screenClip.addRoundRect(screenRect, 6f, 6f, android.graphics.Path.Direction.CW)
            return
        }

        // Modern (flat) layout.
        val pad = s * 0.022f
        if (aw <= ah * 1.3f) {
            // Portrait / square / foldable: screen across the top, wheel underneath with info cards either side.
            wheelR = min(s * 0.19f, ah * 0.2f)
            val sh = ah - 2 * wheelR - 3 * pad - 5f
            screenRect.set(area.left, area.top, area.right, area.top + sh)
            wheelCx = area.centerX(); wheelCy = area.bottom - pad - wheelR
            val top = screenRect.bottom + 5f + pad; val bottom = area.bottom - pad
            val lx1 = wheelCx - wheelR - pad; val rx0 = wheelCx + wheelR + pad
            if (lx1 - (area.left + pad) >= 110f) {
                queueRect.set(area.left + pad, top, lx1, bottom)
                specsRect.set(rx0, top, area.right - pad, bottom)
                keyRects.getValue(Key.SHUFFLE).set(rx0 + 14f, bottom - 14f - 42f, area.right - pad - 14f, bottom - 14f)
            }
        } else {
            // Landscape: screen fills the left, the wheel sits in a column on the right with Shuffle beneath it.
            wheelR = min(ah * 0.3f, aw * 0.15f)
            val colW = 2 * wheelR + 2 * pad
            screenRect.set(area.left, area.top, area.right - colW - 5f, area.bottom)
            wheelCx = area.right - colW / 2; wheelCy = area.centerY() - pad
            val py = wheelCy + wheelR + pad
            if (py + 40f <= area.bottom - pad) keyRects.getValue(Key.SHUFFLE).set(wheelCx - wheelR * 0.9f, py, wheelCx + wheelR * 0.9f, py + 40f)
        }
        bezel.set(screenRect)
        fitLogical(screenRect.width(), screenRect.height(), d.screenW, d.screenH)
        physics.setGeometry(wheelCx, wheelCy, wheelR)
    }

    // ---- drawing -----------------------------------------------------------

    override fun onDraw(c: Canvas) {
        val now = SystemClock.uptimeMillis()
        val dt = ((now - lastFrame).coerceIn(1, 50)) / 1000f
        lastFrame = now
        c.drawColor(theme.backdrop)
        val ctx = drawCtx(now)
        val device = !isFull && theme.body != null
        if (!isFull) {
            if (device) theme.drawBody(c, bodyRect, screenRect) else theme.drawFrame(c, panel, bezel)
            theme.drawWheel(c, wheelCx, wheelCy, wheelR, pressed)
            if (glowOn) drawGlow(c)
            if (!device) {
                theme.drawQueueCard(c, queueRect, ctx)
                theme.drawSpecsCard(c, specsRect, ctx)
                for ((k, r) in keyRects) theme.drawKey(c, r, k, pressedKey == k, prefs.shuffle, true)
            }
        }

        var animating = animateScroll(nav.top, dt)
        val prev = transPrev
        var e = 1f
        val transitioning = transKind != Navigator.Transition.NONE && prev != null && now - transStart < TRANS_MS
        if (transitioning) {
            val t = (now - transStart).toFloat() / TRANS_MS
            e = 1f - (1f - t) * (1f - t) * (1f - t)
            animateScroll(prev!!, dt)
            animating = true
        } else { transKind = Navigator.Transition.NONE; transPrev = null }

        c.save()
        if (device) c.clipPath(screenClip) else c.clipRect(screenRect)
        c.save()
        c.translate(screenRect.left, screenRect.top)
        c.scale(screenScale, screenScale)
        val sw = theme.logicalW
        if (transitioning) {
            when (transKind) {
                Navigator.Transition.PUSH -> { page(c, prev!!, -e * sw, ctx); page(c, nav.top, (1 - e) * sw, ctx) }
                else -> { page(c, nav.top, -(1 - e) * sw, ctx); page(c, prev!!, e * sw, ctx) }
            }
        } else page(c, nav.top, 0f, ctx)
        c.restore()
        if (device) theme.drawGlass(c, screenRect)
        c.restore()

        if (dimmed) c.drawColor(0xFF000000.toInt())

        when {
            animating -> postInvalidateOnAnimation()
            hudUntil > now -> postInvalidateDelayed(hudUntil - now + 20)
            nav.top is NowPlayingPage && player.isPlaying -> postInvalidateDelayed(250)
            prefs.timeInTitle -> postInvalidateDelayed(20_000)
        }
    }

    /** Soft light under the finger so the wheel visibly follows the touch. */
    private fun drawGlow(c: Canvas) {
        val r = wheelR * 0.3f
        com.ipodemu.theme.Gfx.radial(c, glowX, glowY, r, 0x66FFFFFF, 0x00FFFFFF)
    }

    private fun page(c: Canvas, p: Page, dx: Float, ctx: DrawCtx) {
        c.save(); c.translate(dx, 0f)
        theme.drawScreen(c, p, ctx)
        c.restore()
    }

    private fun animateScroll(p: Page, dt: Float): Boolean {
        if (p !is ListPage) return false
        if (p.free) {
            if (p !== nav.top || abs(flingV) < 0.4f) { if (p === nav.top) flingV = 0f; return false }
            val max = theme.maxScroll(p)
            p.scroll += flingV * dt
            if (p.scroll < 0f) { p.scroll = 0f; flingV = 0f } else if (p.scroll > max) { p.scroll = max; flingV = 0f }
            flingV *= exp(-3.2f * dt)
            return true
        }
        val target = theme.targetScroll(p)
        val diff = target - p.scroll
        if (abs(diff) < 0.004f) { p.scroll = target; return false }
        p.scroll += diff * (1f - exp(-SCROLL_RATE * dt))
        return true
    }

    private var specsKey: String? = null
    private var specs: FileSpecs? = null

    private fun drawCtx(now: Long): DrawCtx {
        if (now - batteryAt > 20_000) {
            batteryAt = now
            val i = context.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
            val lvl = i?.getIntExtra(BatteryManager.EXTRA_LEVEL, 100) ?: 100
            val scale = i?.getIntExtra(BatteryManager.EXTRA_SCALE, 100) ?: 100
            val st = i?.getIntExtra(BatteryManager.EXTRA_STATUS, -1) ?: -1
            battery = StatusInfo(false, lvl * 100 / scale.coerceAtLeast(1), st == BatteryManager.BATTERY_STATUS_CHARGING)
        }
        val clock = if (prefs.timeInTitle) android.text.format.DateFormat.format("h:mm a", System.currentTimeMillis()).toString() else null
        val status = StatusInfo(player.isPlaying, battery.battery, battery.charging, clock)
        val np = NowPlayingModel(
            player.current, player.isPlaying, player.positionMs, player.durationMs, nav.nowPlaying.mode,
            player.volume, player.queueIndex, player.queue.size, prefs.shuffle, prefs.repeat, hudUntil > now,
        )
        val cur = player.current
        val fmt = player.audioFormat()
        val sk = cur?.path + "|" + fmt?.sampleRate + "|" + fmt?.pcmEncoding
        if (sk != specsKey) { specsKey = sk; specs = cur?.let { FileSpecs.of(it, fmt) } }
        return DrawCtx(app.art, status, np, specs, player.upNext(4))
    }

    // ---- dispatch (shared by every input path) -----------------------------

    fun dispatch(input: Input) {
        when (input) {
            is Input.Scroll -> scroll(input.n)
            is Input.Page -> (nav.top as? ListPage)?.let {
                if (nav.scroll(input.dir * 7) != 0) feedback.tick()
            }
            Input.Select -> {
                feedback.press()
                when (val t = nav.top) {
                    is NowPlayingPage -> {
                        t.mode = if (t.mode == NowPlayingPage.Mode.VOLUME) NowPlayingPage.Mode.SCRUB else NowPlayingPage.Mode.VOLUME
                        hud()
                    }
                    else -> nav.select()
                }
            }
            Input.Back -> { feedback.press(); nav.pop() }
            Input.BackHold -> { feedback.press(); nav.toRoot() }
            Input.PlayPause -> { feedback.press(); player.toggle() }
            Input.Next -> { feedback.press(); player.next() }
            Input.Prev -> { feedback.press(); player.prev() }
            is Input.Seek -> { player.seekBy(input.dir * 4000L); hudIfNowPlaying() }
            Input.NowPlaying -> if (player.hasQueue) { feedback.press(); nav.showNowPlaying() }
            Input.ToggleShuffle -> { feedback.press(); prefs.shuffle = !prefs.shuffle; player.applyModes() }
        }
        invalidate()
    }

    private fun scroll(n: Int) {
        when (val t = nav.top) {
            is ListPage -> { flingV = 0f; if (nav.scroll(n) != 0) feedback.tick() }
            is NowPlayingPage -> {
                if (t.mode == NowPlayingPage.Mode.VOLUME) player.volume = player.volume + n / 32f
                else player.seekBy(n * maxOf(1000L, player.durationMs / 120))
                feedback.tick(); hud()
            }
        }
    }

    private fun hud() { hudUntil = SystemClock.uptimeMillis() + 2200 }
    private fun hudIfNowPlaying() { if (nav.top is NowPlayingPage) hud() }

    /** Long lists get extra gain so a 10k-song library is reachable without a marathon spin. */
    private fun listGain(): Float {
        val p = nav.top as? ListPage ?: return 1f
        val n = p.source.size
        if (prefs.wheelAccel == 0 || n <= 60) return 1f
        return (1f + 0.25f * ln(n / 60f)).coerceAtMost(2f)
    }

    private fun tapZone(z: Zone) = dispatch(
        when (z) {
            Zone.CENTER -> Input.Select
            Zone.MENU -> Input.Back
            Zone.NEXT -> Input.Next
            Zone.PREV -> Input.Prev
            Zone.PLAY_PAUSE -> Input.PlayPause
        }
    )

    // ---- touch: wheel, keys, screen ----------------------------------------

    private enum class Touch { NONE, WHEEL, SCREEN, KEY, QUEUE }
    private var touch = Touch.NONE
    private var seekHeld = false
    private val holdRunnable = object : Runnable {
        override fun run() {
            when (val z = physics.pendingZone) {
                Zone.NEXT, Zone.PREV -> { seekHeld = true; dispatch(Input.Seek(if (z == Zone.NEXT) 1 else -1)); handler.postDelayed(this, 130) }
                Zone.MENU -> { seekHeld = true; dispatch(Input.BackHold) }
                else -> {}
            }
        }
    }

    // screen gesture state
    private var vt: VelocityTracker? = null
    private var sx = 0f
    private var sy = 0f
    private var sDrag = false
    private var sScroll0 = 0f
    private var sMode = 0 // 0 undecided, 1 scrub, 2 volume
    private var sPos0 = 0L
    private var sVol0 = 0f
    private var lastSeekAt = 0L

    private fun queueTap(x: Float, y: Float) {
        if (!queueRect.contains(x, y)) return
        val i = ((y - queueRect.top - CardLayout.HEADER) / CardLayout.ROW).toInt()
        if (y < queueRect.top + CardLayout.HEADER || i < 0) return
        player.upNext(i + 1).getOrNull(i)?.let { feedback.press(); player.skipTo(it.first); invalidate() }
    }

    private fun keyAt(x: Float, y: Float): Key? = keyRects.entries.firstOrNull { it.value.contains(x, y) }?.key

    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (e.actionMasked == MotionEvent.ACTION_DOWN) {
            touch = Touch.NONE
            swallowGesture = noteInput()
            if (swallowGesture) return true
            val x = e.x; val y = e.y
            val k = keyAt(x, y)
            when {
                k != null -> { touch = Touch.KEY; pressedKey = k; invalidate() }
                queueRect.contains(x, y) -> { touch = Touch.QUEUE }
                startWheel(e) -> touch = Touch.WHEEL
                screenRect.contains(x, y) -> { touch = Touch.SCREEN; screenDown(e) }
            }
            return true
        }
        if (swallowGesture) return true
        when (touch) {
            Touch.WHEEL -> wheelEvent(e)
            Touch.SCREEN -> screenEvent(e)
            Touch.KEY -> keyEvent(e)
            Touch.QUEUE -> if (e.actionMasked == MotionEvent.ACTION_UP) queueTap(e.x, e.y)
            Touch.NONE -> {}
        }
        if (e.actionMasked == MotionEvent.ACTION_UP || e.actionMasked == MotionEvent.ACTION_CANCEL) touch = Touch.NONE
        return true
    }

    private fun startWheel(e: MotionEvent): Boolean {
        val deg = floatArrayOf(56f, 44f, 34f, 26f, 20f)[prefs.wheelSensitivity.coerceIn(0, 4)]
        val gain = floatArrayOf(1f, 1.5f, 2.2f, 3.2f)[prefs.wheelAccel.coerceIn(0, 3)]
        physics.config = physics.config.copy(degPerDetent = deg, maxGain = gain)
        if (!physics.down(e.x, e.y, e.eventTime)) return false
        glowX = e.x; glowY = e.y; glowOn = physics.pendingZone != Zone.CENTER
        seekHeld = false
        pressed = physics.pendingZone
        handler.postDelayed(holdRunnable, 500)
        invalidate()
        return true
    }

    private fun wheelEvent(e: MotionEvent) {
        when (e.actionMasked) {
            MotionEvent.ACTION_MOVE -> {
                for (h in 0 until e.historySize) feed(e.getHistoricalX(h), e.getHistoricalY(h), e.getHistoricalEventTime(h))
                feed(e.x, e.y, e.eventTime)
            }
            MotionEvent.ACTION_UP -> {
                handler.removeCallbacks(holdRunnable)
                glowOn = false
                val z = physics.up(e.x, e.y, e.eventTime)
                pressed = null
                if (z != null && !seekHeld) tapZone(z)
                seekHeld = false
                invalidate()
            }
            MotionEvent.ACTION_CANCEL -> { glowOn = false; handler.removeCallbacks(holdRunnable); physics.cancel(); pressed = null; invalidate() }
        }
    }

    private fun feed(x: Float, y: Float, t: Long) {
        glowX = x; glowY = y
        val n = physics.move(x, y, t, listGain())
        if (physics.isRotating) { handler.removeCallbacks(holdRunnable); pressed = null }
        if (n != 0) dispatch(Input.Scroll(n))
    }

    private fun keyEvent(e: MotionEvent) {
        val k = pressedKey ?: return
        val r = keyRects.getValue(k)
        when (e.actionMasked) {
            MotionEvent.ACTION_MOVE -> if (!r.contains(e.x, e.y)) { pressedKey = null; invalidate() }
            MotionEvent.ACTION_UP -> {
                pressedKey = null
                if (r.contains(e.x, e.y)) dispatch(
                    when (k) { Key.SHUFFLE -> Input.ToggleShuffle }
                )
                invalidate()
            }
            MotionEvent.ACTION_CANCEL -> { pressedKey = null; invalidate() }
        }
    }

    private fun screenDown(e: MotionEvent) {
        sx = e.x; sy = e.y; sDrag = false; sMode = 0
        vt?.recycle(); vt = VelocityTracker.obtain().also { it.addMovement(e) }
        val top = nav.top
        if (top is ListPage) { sScroll0 = top.scroll; flingV = 0f }
        else { sPos0 = player.positionMs; sVol0 = player.volume }
    }

    private fun screenEvent(e: MotionEvent) {
        vt?.addMovement(e)
        val top = nav.top
        when (e.actionMasked) {
            MotionEvent.ACTION_MOVE -> {
                val dx = e.x - sx; val dy = e.y - sy
                if (!sDrag && hypot(dx, dy) > min(width, height) * 0.018f) {
                    sDrag = true
                    if (top is ListPage) {
                        // a mostly-horizontal drag on a list is a swipe-back gesture, not a scroll
                        if (abs(dx) > abs(dy) * 1.4f) sMode = 3 else { top.free = true; sScroll0 = top.scroll }
                    } else sMode = if (abs(dx) > abs(dy)) 1 else 2
                }
                if (!sDrag) return
                if (top is ListPage) {
                    if (sMode == 3) return
                    val rows = dy / (theme.rowPitch * screenScale)
                    top.scroll = (sScroll0 - rows).coerceIn(0f, theme.maxScroll(top))
                } else if (sMode == 1 && player.durationMs > 0) {
                    val now = SystemClock.uptimeMillis()
                    if (now - lastSeekAt > 60) {
                        lastSeekAt = now
                        player.seekTo(sPos0 + (dx / width * player.durationMs).toLong()); hud()
                    }
                } else if (sMode == 2) {
                    player.volume = sVol0 - dy / (screenRect.height() * 0.8f); hud()
                }
                invalidate()
            }
            MotionEvent.ACTION_UP -> {
                if (!sDrag) screenTap(e.x, e.y)
                else if (top is ListPage && sMode == 3) { if (e.x - sx > width * 0.22f) dispatch(Input.Back) }
                else if (top is ListPage) {
                    vt?.computeCurrentVelocity(1000)
                    flingV = (-(vt?.yVelocity ?: 0f) / (theme.rowPitch * screenScale)).coerceIn(-70f, 70f)
                    invalidate()
                }
                vt?.recycle(); vt = null
            }
            MotionEvent.ACTION_CANCEL -> { vt?.recycle(); vt = null }
        }
    }

    private fun screenTap(x: Float, y: Float) {
        val lx = (x - screenRect.left) / screenScale
        val ly = (y - screenRect.top) / screenScale
        when (val top = nav.top) {
            is ListPage -> {
                if (SystemClock.uptimeMillis() - lastNavAt < 280) return // ignore the second tap of a double-tap
                if (ly < theme.listTop) { dispatch(Input.Back); return }
                if (lx > theme.listRight(top)) return
                val idx = floor((ly - theme.listTop) / theme.rowPitch + top.scroll).toInt()
                if (idx in 0 until top.source.size) {
                    top.selected = idx; top.free = false
                    feedback.press(); nav.select(); invalidate()
                }
            }
            is NowPlayingPage -> {
                if (ly < theme.listTop) { dispatch(Input.Back); return }
                if (ly > theme.logicalH - 58f && player.durationMs > 0) {
                    player.seekTo((((lx - 16f) / (theme.logicalW - 32f)).coerceIn(0f, 1f) * player.durationMs).toLong()); hud()
                } else dispatch(Input.PlayPause)
                invalidate()
            }
        }
    }

    // ---- hardware buttons --------------------------------------------------

    private enum class Dir { UP, DOWN, LEFT, RIGHT }
    private var heldDir: Dir? = null
    private var dirRepeats = 0
    private var dirSeeked = false
    private var backDown = false
    private var backHeld = false

    private val dirRepeat = object : Runnable {
        override fun run() {
            val d = heldDir ?: return
            dirRepeats++
            when (d) {
                Dir.UP, Dir.DOWN -> KeyMapper.scrollStep(dirRepeats)?.let { dispatch(Input.Scroll(if (d == Dir.UP) -it else it)) }
                Dir.LEFT, Dir.RIGHT -> { dirSeeked = true; dispatch(Input.Seek(if (d == Dir.RIGHT) 1 else -1)) }
            }
            handler.postDelayed(this, if (d == Dir.UP || d == Dir.DOWN) 55L else 120L)
        }
    }

    private fun dirDown(d: Dir) {
        if (heldDir == d) return
        if (heldDir != null) dirUp(heldDir!!)
        heldDir = d; dirRepeats = 0; dirSeeked = false
        if (d == Dir.UP || d == Dir.DOWN) dispatch(Input.Scroll(if (d == Dir.UP) -1 else 1))
        handler.postDelayed(dirRepeat, 400)
    }

    private fun dirUp(d: Dir) {
        if (heldDir != d) return
        handler.removeCallbacks(dirRepeat)
        heldDir = null
        if (!dirSeeked) when (d) { Dir.LEFT -> dispatch(Input.Prev); Dir.RIGHT -> dispatch(Input.Next); else -> {} }
    }

    private val backHold = Runnable { if (backDown) { backHeld = true; dispatch(Input.BackHold) } }

    fun onKeyDownHw(keyCode: Int, e: KeyEvent): Boolean {
        if (e.repeatCount == 0 && noteInput()) return true
        when (keyCode) {
            KeyEvent.KEYCODE_DPAD_UP -> { if (e.repeatCount == 0) dirDown(Dir.UP); return true }
            KeyEvent.KEYCODE_DPAD_DOWN -> { if (e.repeatCount == 0) dirDown(Dir.DOWN); return true }
            KeyEvent.KEYCODE_DPAD_LEFT -> { if (e.repeatCount == 0) dirDown(Dir.LEFT); return true }
            KeyEvent.KEYCODE_DPAD_RIGHT -> { if (e.repeatCount == 0) dirDown(Dir.RIGHT); return true }
        }
        val input = KeyMapper.map(keyCode, prefs.swapFaceButtons) ?: return false
        when (input) {
            Input.Back -> if (e.repeatCount == 0) { backDown = true; backHeld = false; handler.postDelayed(backHold, 600) }
            is Input.Page -> if (e.repeatCount % 3 == 0) dispatch(input)
            else -> if (e.repeatCount == 0) dispatch(input)
        }
        return true
    }

    fun onKeyUpHw(keyCode: Int, e: KeyEvent): Boolean {
        when (keyCode) {
            KeyEvent.KEYCODE_DPAD_UP -> { dirUp(Dir.UP); return true }
            KeyEvent.KEYCODE_DPAD_DOWN -> { dirUp(Dir.DOWN); return true }
            KeyEvent.KEYCODE_DPAD_LEFT -> { dirUp(Dir.LEFT); return true }
            KeyEvent.KEYCODE_DPAD_RIGHT -> { dirUp(Dir.RIGHT); return true }
        }
        val input = KeyMapper.map(keyCode, prefs.swapFaceButtons) ?: return false
        if (input == Input.Back) {
            handler.removeCallbacks(backHold)
            if (backDown && !backHeld) dispatch(Input.Back)
            backDown = false; backHeld = false
        }
        return true
    }

    /** Gamepads that report the D-pad as a hat axis instead of DPAD key events. */
    fun onHat(e: MotionEvent): Boolean {
        if (e.source and android.view.InputDevice.SOURCE_JOYSTICK == 0 || e.actionMasked != MotionEvent.ACTION_MOVE) return false
        val hx = e.getAxisValue(MotionEvent.AXIS_HAT_X)
        val hy = e.getAxisValue(MotionEvent.AXIS_HAT_Y)
        val want = when {
            hy < -0.5f -> Dir.UP
            hy > 0.5f -> Dir.DOWN
            hx < -0.5f -> Dir.LEFT
            hx > 0.5f -> Dir.RIGHT
            else -> null
        }
        if (want == heldDir) return want != null
        if (want != null && noteInput()) return true
        if (want == null) heldDir?.let { dirUp(it) } else dirDown(want)
        return true
    }

    companion object {
        private const val TRANS_MS = 240L
        private const val SCROLL_RATE = 20f
    }
}
