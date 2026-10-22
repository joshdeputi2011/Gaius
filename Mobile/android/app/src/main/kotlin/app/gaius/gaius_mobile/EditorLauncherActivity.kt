package app.gaius.gaius_mobile

import android.app.Activity
import android.content.Intent
import android.os.Bundle

class EditorLauncherActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val kind = when (intent.component?.className?.substringAfterLast('.')) {
            "WriteLauncher" -> "document"
            "SheetsLauncher" -> "spreadsheet"
            "SlidesLauncher" -> "presentation"
            "PdfLauncher" -> "pdf"
            "ImageLauncher" -> "image"
            else -> null
        }
        if (kind != null) startActivity(Intent(this, DocumentActivity::class.java).apply { addFlags(Intent.FLAG_ACTIVITY_NEW_DOCUMENT or Intent.FLAG_ACTIVITY_MULTIPLE_TASK); putExtra("appKind", kind) })
        else startActivity(Intent(this, MainActivity::class.java))
        finish()
    }
}
