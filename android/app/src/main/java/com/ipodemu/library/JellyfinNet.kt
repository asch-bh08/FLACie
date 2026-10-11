package com.ipodemu.library

import kotlinx.coroutines.CancellableContinuation
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import okhttp3.Call
import okhttp3.Callback
import okhttp3.Dispatcher
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import java.io.IOException
import java.util.concurrent.TimeUnit
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/** OkHttp underneath the gate: one shared connection pool, a call that is cancelled when its coroutine is. */
fun okGateTransport(client: OkHttpClient): GateTransport = { req ->
    suspendCancellableCoroutine { cont: CancellableContinuation<GateResponse> ->
        val b = Request.Builder().url(req.url)
        req.headers.forEach { (k, v) -> b.header(k, v) }
        if (req.method == "GET") b.get()
        else b.method(req.method, (req.body ?: ByteArray(0)).toRequestBody((req.contentType ?: "application/json").toMediaTypeOrNull()))
        val call = client.newCall(b.build())
        cont.invokeOnCancellation { call.cancel() }
        call.enqueue(object : Callback {
            override fun onFailure(call: Call, e: IOException) { if (cont.isActive) cont.resumeWithException(e) }
            override fun onResponse(call: Call, response: Response) {
                try {
                    response.use { r ->
                        val bytes = r.body?.bytes() ?: ByteArray(0)
                        val h = HashMap<String, String>()
                        for (n in r.headers.names()) r.header(n)?.let { h[n.lowercase()] = it }
                        if (cont.isActive) cont.resume(GateResponse(r.code, bytes, h))
                    }
                } catch (e: IOException) { if (cont.isActive) cont.resumeWithException(e) }
            }
        })
    }
}

/**
 * The app's single gate to Jellyfin ([JellyfinGate] over OkHttp). Everything that talks to the Jellyfin server over HTTP goes through [get],
 * [post] or [gate]; the live WebSocket and the player's media reads use the gate's permit instead (see [JellyfinGate.withPermit]).
 */
object JellyfinNet {
    private val client: OkHttpClient by lazy {
        OkHttpClient.Builder().connectTimeout(8, TimeUnit.SECONDS).readTimeout(25, TimeUnit.SECONDS).callTimeout(60, TimeUnit.SECONDS)
            .dispatcher(Dispatcher().apply { maxRequests = 64; maxRequestsPerHost = 32 }).followRedirects(true).build()
    }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    val gate: JellyfinGate by lazy {
        JellyfinGate(GateConfig(), okGateTransport(client), scope, busy = { com.ipodemu.library.tags.UserActivity.recent() })
    }

    /** Starts the 10 s `FLACieGate` log line (adb logcat -s FLACieGate); safe to call more than once. */
    @Volatile private var logging = false
    fun startLogging() {
        if (logging) return
        logging = true
        scope.launch {
            var last = 0L
            while (true) {
                delay(10_000)
                val s = gate.stats()
                if (s.total != last || s.inFlight > 0) { android.util.Log.d("FLACieGate", s.line()); last = s.total }
            }
        }
    }

    suspend fun get(url: String, token: String?, priority: Priority, kind: String, heavy: Boolean = false, headers: Map<String, String> = emptyMap()): GateResponse =
        gate.execute(GateRequest("GET", url, auth(token) + headers, kind = kind, heavy = heavy), priority)

    suspend fun send(method: String, url: String, headers: Map<String, String>, body: ByteArray?, contentType: String?, priority: Priority, kind: String): GateResponse =
        gate.execute(GateRequest(method, url, headers, body, contentType, kind), priority)

    private fun auth(token: String?): Map<String, String> = if (token == null) emptyMap() else mapOf("Authorization" to JellyfinAuth.header(token))
}
