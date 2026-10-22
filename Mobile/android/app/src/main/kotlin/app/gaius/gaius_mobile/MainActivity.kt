package app.gaius.gaius_mobile

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.content.ComponentName
import android.content.pm.PackageManager
import android.app.ActivityManager
import android.graphics.Bitmap
import android.graphics.Canvas
import android.os.Bundle
import android.net.Uri
import android.provider.OpenableColumns
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.io.File
import java.util.concurrent.Executors

open class MainActivity : FlutterActivity() {
    private val worker = Executors.newSingleThreadExecutor()
    private var windows: MethodChannel? = null
    private var consumedInitial = false
    private var pendingSave: MethodChannel.Result? = null
    private var pendingBytes: ByteArray? = null
    private var pendingName = "document.pdf"
    private val createRequest = 8101
    private val replaceRequest = 8102
    private val launchers = listOf("WriteLauncher", "SheetsLauncher", "SlidesLauncher", "PdfLauncher", "ImageLauncher")

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (routeExternalFile(intent)) { finish(); return }
        if (javaClass == MainActivity::class.java && incomingUri(intent) == null && !intent.hasExtra("documentId")) {
            startActivity(Intent(this, HomeActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)); finish(); return
        }
        updateTask(intent.getStringExtra("appKind"))
    }
    override fun onResume() { super.onResume(); updateTask(intent.getStringExtra("appKind")) }
    private fun incomingUri(source: Intent): Uri? {
        @Suppress("DEPRECATION")
        return source.data ?: if (source.action == Intent.ACTION_SEND) source.getParcelableExtra<Uri>(Intent.EXTRA_STREAM) else null
    }
    private fun routeExternalFile(source: Intent): Boolean {
        if (this is DocumentActivity || !getSharedPreferences("gaius_settings", MODE_PRIVATE).getBoolean("separateApps", false)) return false
        val uri = incomingUri(source) ?: return false
        val mime = source.type ?: contentResolver.getType(uri) ?: ""
        val kind = when { mime == "application/pdf" -> "pdf"; mime.startsWith("image/") -> "image"; mime.contains("spreadsheet") || mime.contains("excel") || mime.contains("csv") -> "spreadsheet"; mime.contains("presentation") || mime.contains("powerpoint") -> "presentation"; else -> "document" }
        startActivity(Intent(source).setClass(this, DocumentActivity::class.java).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_DOCUMENT or Intent.FLAG_ACTIVITY_MULTIPLE_TASK); putExtra("appKind", kind) })
        return true
    }
    private fun updateTask(kind: String?) {
        val labels = mapOf("document" to "Write", "spreadsheet" to "Sheets", "presentation" to "Slides", "pdf" to "PDF", "image" to "Image")
        val icons = mapOf("document" to R.drawable.gaius_write, "spreadsheet" to R.drawable.gaius_sheets, "presentation" to R.drawable.gaius_slides, "pdf" to R.drawable.gaius_pdf, "image" to R.drawable.gaius_image)
        val separate = getSharedPreferences("gaius_settings", MODE_PRIVATE).getBoolean("separateApps", false)
        val name = if (this !is HomeActivity && separate && labels.containsKey(kind)) "Gaius ${labels[kind]}" else "Gaius Office"
        val icon = if (this !is HomeActivity && separate) icons[kind] ?: R.drawable.gaius_launcher else R.drawable.gaius_launcher
        val drawable = getDrawable(icon)!!; val bitmap = Bitmap.createBitmap(108, 108, Bitmap.Config.ARGB_8888); drawable.setBounds(0, 0, 108, 108); drawable.draw(Canvas(bitmap))
        @Suppress("DEPRECATION")
        setTaskDescription(ActivityManager.TaskDescription(name, bitmap, 0xffedf0f5.toInt()))
    }
    private fun setSeparateApps(separate: Boolean) {
        // Enable the replacement entry points first, so the app always has a launcher.
        val enabled = if (separate) launchers else listOf("SuiteLauncher")
        val disabled = if (separate) listOf("SuiteLauncher") else launchers
        for (name in enabled) packageManager.setComponentEnabledSetting(ComponentName(this, "$packageName.$name"), PackageManager.COMPONENT_ENABLED_STATE_ENABLED, PackageManager.DONT_KILL_APP)
        for (name in disabled) packageManager.setComponentEnabledSetting(ComponentName(this, "$packageName.$name"), PackageManager.COMPONENT_ENABLED_STATE_DISABLED, PackageManager.DONT_KILL_APP)
        getSharedPreferences("gaius_settings", MODE_PRIVATE).edit().putBoolean("separateApps", separate).apply()
        updateTask(intent.getStringExtra("appKind"))
    }

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        windows = MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "app.gaius/windows").also { channel ->
            channel.setMethodCallHandler { call, result ->
                when (call.method) {
                    "newWindow" -> {
                        val id = call.argument<String>("documentId")
                        if (id == null) startActivity(Intent(this, HomeActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP))
                        else startActivity(Intent(this, DocumentActivity::class.java).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_DOCUMENT or Intent.FLAG_ACTIVITY_MULTIPLE_TASK); putExtra("documentId", id); putExtra("appKind", call.argument<String>("kind")) })
                        result.success(null)
                    }
                    "showHome" -> { startActivity(Intent(this, HomeActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)); result.success(null) }
                    "getLaunchMode" -> result.success(getSharedPreferences("gaius_settings", MODE_PRIVATE).getBoolean("separateApps", false))
                    "setLaunchMode" -> { try { setSeparateApps(call.argument<Boolean>("separate") == true); result.success(null) } catch (e: Exception) { result.error("launcher_failed", e.message, null) } }
                    "setEditorKind" -> { val kind = call.argument<String>("kind"); if (this !is HomeActivity) intent.putExtra("appKind", kind); updateTask(kind); result.success(null) }
                    "initialFile" -> {
                        if (consumedInitial) result.success(null)
                        else {
                            consumedInitial = true
                            val id = intent.getStringExtra("documentId")
                            val kind = intent.getStringExtra("appKind")
                            if (id != null) result.success(mapOf("documentId" to id, "documentWindow" to (this !is HomeActivity)))
                            else if (kind != null && incomingUri(intent) == null) result.success(mapOf("appKind" to kind, "documentWindow" to true))
                            else readIncoming(intent, { result.success((it ?: emptyMap()) + mapOf("documentWindow" to (this !is HomeActivity))) }, { result.error("open_failed", it, null) })
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
        if (routeExternalFile(intent)) return
        setIntent(intent)
        consumedInitial = true
        val id = intent.getStringExtra("documentId")
        if (id != null) windows?.invokeMethod("openDocument", id)
        else readIncoming(intent, { if (it != null) windows?.invokeMethod("openFile", it) else windows?.invokeMethod("showHome", null) }, { windows?.invokeMethod("fileError", it) })
    }
    private fun readIncoming(intent: Intent, done: (Map<String, String>?) -> Unit, fail: (String) -> Unit) {
        val uri = incomingUri(intent)
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
