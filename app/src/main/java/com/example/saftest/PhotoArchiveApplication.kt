package com.example.saftest

import android.app.Application
import androidx.work.WorkManager

private const val PREFS_NAME = "saf_test_preferences"
private const val WORK_SCHEMA_KEY = "upload_work_schema"
private const val CURRENT_WORK_SCHEMA = 7

class PhotoArchiveApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        val prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE)
        if (prefs.getInt(WORK_SCHEMA_KEY, 0) < CURRENT_WORK_SCHEMA) {
            WorkManager.getInstance(this).cancelAllWork()
            WorkManager.getInstance(this).pruneWork()
            prefs.edit()
                .remove("upload_queue")
                .remove("active_upload_work")
                .putInt(WORK_SCHEMA_KEY, CURRENT_WORK_SCHEMA)
                .apply()
        }
    }
}
