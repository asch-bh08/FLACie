package com.ipodemu.library

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.Environment
import android.os.storage.StorageManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import kotlin.coroutines.resume

/**
 * ipodsync's verified engine, built into this app as a native library (libipodsync.so -- NativeAOT C#, see
 * ipodsync's src/IpodSync.Engine and tools/build-engine-android.sh). One JSON request in, one JSON reply out; the
 * reply bodies are the same ones IpodSync.Web returns. Writes go through the same pipeline as the PC and the
 * ipodsync app: backup, write, read-back, re-verify, automatic restore; plus the remote-edit rules (op allow-list,
 * dry-run confirm token). arm64 only: on other ABIs [available] is false and the option is hidden.
 */
object IpodEngine {
    val available: Boolean by lazy {
        try { System.loadLibrary("e_sqlite3"); System.loadLibrary("ipodsync"); true } catch (_: Throwable) { false }
    }

    // null only if the engine failed before it could even build an error reply
    @JvmStatic private external fun call(request: ByteArray): ByteArray?

    /** Blocking (a write takes seconds): call off the main thread. Returns {status, body}. */
    fun request(req: JSONObject): JSONObject {
        val out = call(req.toString().toByteArray(Charsets.UTF_8)) ?: throw java.io.IOException("the iPod engine failed")
        return JSONObject(String(out, Charsets.UTF_8))
    }

    /** Known-answer test of the engine's crypto; the engine itself also refuses to write if this fails. */
    fun selfTest(): Boolean = try { request(JSONObject().put("op", "selftest")).optJSONObject("body")?.optBoolean("ok") == true } catch (_: Throwable) { false }
}

/** Where Sync mode reads and writes an iPod: ipodsync on a PC over HTTP, or the engine on this device. */
interface IpodLink {
    val label: String
    suspend fun devices(): List<SyncDevice>
    suspend fun load(root: String): IpodDb
    suspend fun apply(root: String, changeSet: JSONObject, commit: Boolean, confirmToken: String?): ApplyResult
}

class HttpLink(private val host: String) : IpodLink {
    private val edit = SyncEditClient()
    override val label get() = host
    override suspend fun devices() = SyncClient().devices(host)
    override suspend fun load(root: String) = edit.load(host, root)
    override suspend fun apply(root: String, changeSet: JSONObject, commit: Boolean, confirmToken: String?) = edit.apply(host, root, changeSet, commit, confirmToken)
}

/**
 * The iPod plugged into this phone/handheld over USB-OTG. Android mounts it as a removable volume
 * (/storage/XXXX-XXXX); with "All files access" that's an ordinary path the engine can open. The database signature
 * (hash58) needs the iPod's FirewireGuid, which is its USB serial number -- read through Android's USB permission
 * prompt (the USB interface itself is never claimed, so the OS mount is left alone).
 */
class EngineLink(private val ctx: Context) : IpodLink {
    override val label get() = "this device"

    /** Same folder the ipodsync app uses, so all of the iPod's backups sit together. */
    private val backupRoot: String
        get() = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOCUMENTS), "ipodsync/ipod-backups").absolutePath

    override suspend fun devices(): List<SyncDevice> = withContext(Dispatchers.IO) {
        if (!Environment.isExternalStorageManager()) return@withContext listOf(SyncDevice(NEEDS_ACCESS, "Allow \"All files access\" so ipodplayer can read and write the iPod's USB storage", false, needsUserAction = true))
        val sm = ctx.getSystemService(StorageManager::class.java)
        sm.storageVolumes.mapNotNull { v ->
            val dir = v.directory ?: return@mapNotNull null
            if (v.isPrimary || v.state != Environment.MEDIA_MOUNTED || !File(dir, "iPod_Control").isDirectory) return@mapNotNull null
            SyncDevice(dir.absolutePath, v.getDescription(ctx), File(dir, "iPod_Control/iTunes").isDirectory)
        }
    }

    override suspend fun load(root: String): IpodDb = withContext(Dispatchers.IO) {
        val r = IpodEngine.request(JSONObject().put("op", "library").put("root", root))
        if (r.optInt("status") != 200) throw java.io.IOException(r.optJSONObject("body")?.optString("error") ?: "engine error")
        SyncEditClient.parseLibrary(r.getJSONObject("body"))
    }

    override suspend fun apply(root: String, changeSet: JSONObject, commit: Boolean, confirmToken: String?): ApplyResult {
        val serials = appleUsbSerials()
        return withContext(Dispatchers.IO) {
            val req = JSONObject().put("op", "apply").put("root", root).put("changeSet", changeSet).put("commit", commit)
                .put("confirm", confirmToken ?: JSONObject.NULL).put("firewire", JSONArray(serials)).put("backupRoot", backupRoot)
            val r = IpodEngine.request(req)
            SyncEditClient.parseApply(r.optJSONObject("body") ?: JSONObject(), r.optInt("status"), commit)
        }
    }

    /** Serial numbers of Apple USB devices attached (for an iPod that is its FirewireGuid), asking once per device. */
    private suspend fun appleUsbSerials(): List<String> {
        val um = ctx.getSystemService(UsbManager::class.java) ?: return emptyList()
        val out = ArrayList<String>()
        for (d in um.deviceList.values.filter { it.vendorId == 0x05AC }) {
            if (!um.hasPermission(d) && !askPermission(um, d)) continue
            try { d.serialNumber?.takeIf { it.isNotBlank() }?.let { out += it } } catch (_: SecurityException) {}
        }
        return out
    }

    private suspend fun askPermission(um: UsbManager, d: UsbDevice): Boolean = withTimeoutOrNull(60_000) {
        suspendCancellableCoroutine { cont ->
            val action = ctx.packageName + ".USB_PERMISSION"
            val receiver = object : BroadcastReceiver() {
                override fun onReceive(c: Context, i: Intent) {
                    try { ctx.unregisterReceiver(this) } catch (_: Exception) {}
                    // re-check rather than trust the extra, which some Android builds drop from the broadcast
                    if (cont.isActive) cont.resume(um.hasPermission(d))
                }
            }
            if (Build.VERSION.SDK_INT >= 33) ctx.registerReceiver(receiver, IntentFilter(action), Context.RECEIVER_NOT_EXPORTED)
            else ctx.registerReceiver(receiver, IntentFilter(action))
            // mutable: the system attaches EXTRA_DEVICE / EXTRA_PERMISSION_GRANTED to it
            val pi = PendingIntent.getBroadcast(ctx, 0, Intent(action).setPackage(ctx.packageName), PendingIntent.FLAG_MUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            um.requestPermission(d, pi)
            cont.invokeOnCancellation { try { ctx.unregisterReceiver(receiver) } catch (_: Exception) {} }
        }
    } ?: false

    companion object {
        const val NEEDS_ACCESS = "needs-access"
        fun allFilesAccessIntent(ctx: Context) = Intent(android.provider.Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, android.net.Uri.parse("package:" + ctx.packageName))
    }
}
