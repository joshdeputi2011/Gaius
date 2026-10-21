package app.gaius.gaius_mobile

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.net.Uri
import android.provider.OpenableColumns
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.io.File
import java.util.concurrent.Executors

class MainActivity : FlutterActivity() {
    private val worker = Executors.newSingleThreadExecutor()
    private var windows: MethodChannel? = null
    private var consumedInitial = false
    private var pendingSave: MethodChannel.Result? = null
    private var pendingBytes: ByteArray? = null
    private var pendingName = "document.pdf"
    private val createRequest = 8101
    private val replaceRequest = 8102

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        windows = MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "app.gaius/windows").also { channel ->
            channel.setMethodCallHandler { call, result ->
                when (call.method) {
                    "newWindow" -> {
                        startActivity(Intent(this, MainActivity::class.java).apply {
                            addFlags(Intent.FLAG_ACTIVITY_NEW_DOCUMENT or Intent.FLAG_ACTIVITY_MULTIPLE_TASK)
                            putExtra("documentId", call.argument<String>("documentId"))
                        })
                        result.success(null)
                    }
                    "initialFile" -> {
                        if (consumedInitial) result.success(null)
                        else {
                            consumedInitial = true
                            val id = intent.getStringExtra("documentId")
                            if (id != null) result.success(mapOf("documentId" to id))
                            else readIncoming(intent, { result.success(it) }, { result.error("open_failed", it, null) })
                        }
                    }
                    else -> result.notImplemented()
                }
            }
        }
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "app.gaius/files").setMethodCallHandler { call, result ->
            if (call.method != "save") result.notImplemented()
            else if (pendingSave != null) result.error("busy", "A save dialog is already open.", null)
            else {
                val bytes = call.argument<ByteArray>("bytes")
                if (bytes == null) { result.error("invalid", "No file data.", null); return@setMethodCallHandler }
                pendingSave = result
                pendingBytes = bytes
                pendingName = call.argument<String>("name") ?: "document.pdf"
                val replace = call.argument<Boolean>("replace") == true
                val picker = Intent(if (replace) Intent.ACTION_OPEN_DOCUMENT else Intent.ACTION_CREATE_DOCUMENT).apply {
                    addCategory(Intent.CATEGORY_OPENABLE)
                    type = if (pendingName.endsWith(".pdf", true)) "application/pdf" else "application/octet-stream"
                    putExtra(Intent.EXTRA_TITLE, pendingName)
                    addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
                }
                try { startActivityForResult(picker, if (replace) replaceRequest else createRequest) }
                catch (e: Exception) { finishSave(false, e.message) }
            }
        }
    }
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        consumedInitial = true
        val id = intent.getStringExtra("documentId")
        if (id != null) windows?.invokeMethod("openDocument", id)
        else readIncoming(intent, { if (it != null) windows?.invokeMethod("openFile", it) }, { windows?.invokeMethod("fileError", it) })
    }
    private fun readIncoming(intent: Intent, done: (Map<String, String>?) -> Unit, fail: (String) -> Unit) {
        @Suppress("DEPRECATION")
        val uri = intent.data ?: if (intent.action == Intent.ACTION_SEND) intent.getParcelableExtra<Uri>(Intent.EXTRA_STREAM) else null
        if (uri == null) { done(null); return }
        worker.execute {
            try {
                var name = uri.lastPathSegment?.substringAfterLast('/') ?: "document"
                contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
                    if (cursor.moveToFirst()) name = cursor.getString(0) ?: name
                }
                val safeName = name.replace(Regex("[^a-zA-Z0-9._ -]"), "_").take(180)
                val folder = File(cacheDir, "incoming").apply { mkdirs() }
                // Clean old imported copies; working documents live in application support.
                folder.listFiles()?.filter { System.currentTimeMillis() - it.lastModified() > 86400000 }?.forEach { it.delete() }
                val target = File(folder, "${java.util.UUID.randomUUID()}-$safeName")
                try {
                    contentResolver.openInputStream(uri)?.use { source -> target.outputStream().use { output ->
                        val buffer = ByteArray(65536); var count: Int; var total = 0L
                        while (source.read(buffer).also { count = it } != -1) {
                            total += count
                            if (total > 40L * 1024 * 1024) throw IllegalArgumentException("Choose a file smaller than 40 MB.")
                            output.write(buffer, 0, count)
                        }
                    } } ?: throw IllegalArgumentException("Unable to read the selected file.")
                } catch (e: Exception) { target.delete(); throw e }
                runOnUiThread { done(mapOf("path" to target.absolutePath, "name" to name)) }
            } catch (e: Exception) { runOnUiThread { fail(e.message ?: "Unable to open file.") } }
        }
    }
    @Deprecated("Android activity result API is bridged to Flutter's method channel")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != createRequest && requestCode != replaceRequest) return
        val uri = data?.data
        if (resultCode != Activity.RESULT_OK || uri == null) { finishSave(false); return }
        if (requestCode == replaceRequest) {
            AlertDialog.Builder(this).setTitle("Replace this file?")
                .setMessage("The selected file will be overwritten with the processed result. This cannot be undone.")
                .setNegativeButton("Cancel") { _, _ -> finishSave(false) }
                .setPositiveButton("Replace") { _, _ -> writeResult(uri) }
                .setOnCancelListener { finishSave(false) }.show()
        } else writeResult(uri)
    }
    private fun writeResult(uri: Uri) {
        val bytes = pendingBytes ?: return
        worker.execute {
            try {
                contentResolver.openOutputStream(uri, "wt")?.use { it.write(bytes); it.flush() }
                    ?: throw IllegalStateException("The selected provider does not allow writing.")
                runOnUiThread { finishSave(true) }
            } catch (e: Exception) { runOnUiThread { finishSave(false, e.message) } }
        }
    }
    private fun finishSave(saved: Boolean, error: String? = null) {
        val result = pendingSave
        pendingSave = null; pendingBytes = null
        if (error == null) result?.success(saved) else result?.error("save_failed", error, null)
    }
    override fun onDestroy() { worker.shutdown(); super.onDestroy() }
}
