package com.example.saftest

import android.app.Service
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.ContentUris
import android.content.Intent
import android.graphics.Bitmap
import android.os.Build
import android.os.IBinder
import android.provider.MediaStore
import android.util.Size
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.Executors

/** Local ADB-only bridge: the desktop receives small MediaStore thumbnails, not original files. */
class ThumbnailBridgeService : Service() {
    private var server: ServerSocket? = null
    private val workers = Executors.newCachedThreadPool()

    override fun onCreate() {
        super.onCreate()
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel("photoarchive_bridge", "ФотоАрхив", NotificationManager.IMPORTANCE_LOW))
        startForeground(8765, Notification.Builder(this, "photoarchive_bridge").setContentTitle("ФотоАрхив").setContentText("Подключение к компьютеру").setSmallIcon(android.R.drawable.stat_sys_upload).setOngoing(true).build())
        server = ServerSocket(8765, 8, java.net.InetAddress.getLoopbackAddress())
        workers.execute {
            while (!server!!.isClosed) try {
                val socket = server!!.accept()
                workers.execute { acceptOne(socket) }
            } catch (_: Exception) { break }
        }
    }

    private fun acceptOne(socket: Socket) {
        socket.use {
            val input = DataInputStream(BufferedInputStream(it.getInputStream()))
            val output = DataOutputStream(BufferedOutputStream(it.getOutputStream()))
            val request = input.readUTF()
            if (request.startsWith("LIST\t")) {
                val parts = request.split('\t')
                val offset = parts.getOrNull(1)?.toIntOrNull() ?: 0
                val limit = (parts.getOrNull(2)?.toIntOrNull() ?: 80).coerceIn(1, 100)
                output.writeUTF(listMedia(offset, limit))
            } else {
                val data = thumbnail(request)
                output.writeInt(data.size)
                output.write(data)
            }
            output.flush()
        }
    }

    private fun listMedia(offset: Int, limit: Int): String {
        val projection = arrayOf(MediaStore.MediaColumns.DATA, MediaStore.MediaColumns.DATE_MODIFIED, MediaStore.MediaColumns.MIME_TYPE)
        val result = StringBuilder()
        val sort = "${MediaStore.MediaColumns.DATE_MODIFIED} DESC LIMIT $limit OFFSET $offset"
        val selection = "${MediaStore.MediaColumns.MIME_TYPE} LIKE 'image/%' OR ${MediaStore.MediaColumns.MIME_TYPE} LIKE 'video/%'"
        contentResolver.query(MediaStore.Files.getContentUri("external"), projection, selection, null, sort)?.use { cursor ->
            val pathIndex = cursor.getColumnIndex(MediaStore.MediaColumns.DATA)
            while (cursor.moveToNext()) if (pathIndex >= 0) {
                val path = cursor.getString(pathIndex) ?: continue
                if (path.matches(Regex(".*\\.(jpg|jpeg|png|webp|heic|gif|mp4|mov|mkv|webm|3gp)$", RegexOption.IGNORE_CASE))) result.append(path).append('\n')
            }
        }
        return result.toString()
    }

    private fun thumbnail(path: String): ByteArray {
        if (Build.VERSION.SDK_INT < 29) return ByteArray(0)
        val resolver = contentResolver
        val projection = arrayOf(MediaStore.MediaColumns._ID, MediaStore.MediaColumns.DATA)
        val tables = listOf(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, MediaStore.Video.Media.EXTERNAL_CONTENT_URI)
        val name = path.substringAfterLast('/')
        for (base in tables) resolver.query(base, projection, "${MediaStore.MediaColumns.DATA} = ? OR ${MediaStore.MediaColumns.DISPLAY_NAME} = ?", arrayOf(path, name), null)?.use { cursor ->
            if (cursor.moveToFirst()) {
                val id = cursor.getLong(0)
                val uri = ContentUris.withAppendedId(base, id)
                val bitmap = resolver.loadThumbnail(uri, Size(240, 240), null)
                return java.io.ByteArrayOutputStream().use { bytes -> bitmap.compress(Bitmap.CompressFormat.JPEG, 82, bytes); bitmap.recycle(); bytes.toByteArray() }
            }
        }
        return ByteArray(0)
    }

    override fun onDestroy() { server?.close(); workers.shutdownNow(); super.onDestroy() }
    override fun onBind(intent: Intent?): IBinder? = null
}
