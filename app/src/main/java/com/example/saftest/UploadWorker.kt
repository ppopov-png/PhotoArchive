package com.example.saftest

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.ServiceInfo
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.util.Log
import androidx.exifinterface.media.ExifInterface
import androidx.work.CoroutineWorker
import androidx.work.Data
import androidx.work.ForegroundInfo
import androidx.work.WorkerParameters
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.runInterruptible
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.MediaType.Companion.toMediaType
import okio.BufferedSink
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.Collections
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.TimeUnit

private const val WORK_TOKEN = "token"
private const val WORK_DESTINATION = "destination"
private const val WORK_FILES = "files"
private const val WORK_CHRONOLOGICAL = "chronological"
private const val TAG = "PhotoArchiveUpload"
private const val API_INTERVAL_MS = 250L
private const val NOTIFICATION_CHANNEL = "photo_archive_uploads"
private const val NOTIFICATION_ID = 4101

class UploadWorker(appContext: Context, params: WorkerParameters) : CoroutineWorker(appContext, params) {
    private val uploadClient = OkHttpClient.Builder()
        .connectTimeout(30, TimeUnit.SECONDS)
        .readTimeout(120, TimeUnit.SECONDS)
        .writeTimeout(0, TimeUnit.SECONDS)
        .callTimeout(0, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .build()
    private val apiThrottle = Mutex()
    private var nextApiRequestAt = 0L
    private val remoteCheckedFolders = mutableSetOf<String>()
    private val remoteExistingFiles = mutableSetOf<String>()
    override suspend fun doWork(): Result {
        val token = inputData.getString(WORK_TOKEN) ?: return Result.failure(errorData("Нет токена Яндекс Диска"))
        val destination = inputData.getString(WORK_DESTINATION) ?: "/"
        val queueJson = inputData.getString(WORK_FILES)
            ?: applicationContext.getSharedPreferences("saf_test_preferences", Context.MODE_PRIVATE)
                .getString("upload_queue", "")
                .orEmpty()
        val files = runCatching { JSONArray(queueJson) }
            .getOrElse { return Result.failure(errorData(it.message ?: "Некорректная очередь файлов")) }
        val chronological = inputData.getBoolean(WORK_CHRONOLOGICAL, true)
        val preparedFolders = Collections.synchronizedSet(mutableSetOf<String>())
        val checkedExistingFiles = Collections.synchronizedSet(mutableSetOf<String>())
        val folderMutex = Mutex()
        val completed = AtomicInteger(0)
        val failedFiles = Collections.synchronizedList(mutableListOf<String>())
        val total = files.length()
        Log.i(TAG, "Worker initializing foreground: files=$total")
        setForeground(createForegroundInfo(0, total))
        Log.i(TAG, "Worker started: files=$total, destination=$destination, chronological=$chronological")

        return try {
            // Upload strictly one file at a time. This avoids races between
            // folder creation, duplicate checks and direct upload connections.
            for (index in 0 until total) {
                currentCoroutineContext().ensureActive()
                val file = decodeFile(files.getJSONObject(index))
                try {
                    uploadOneWithRetry(token, destination, chronological, preparedFolders, checkedExistingFiles, folderMutex, file)
                } catch (cancelled: CancellationException) {
                    Log.i(TAG, "Worker cancelled while processing ${file.name}")
                    throw cancelled
                } catch (error: Exception) {
                    // One broken provider item must not stop the whole queue.
                    failedFiles += file.name
                    Log.e(TAG, "File failed, continuing queue: ${file.name}", error)
                }
                val done = completed.incrementAndGet()
                setProgress(progressData(done, total, file, "Готово"))
            }
            Result.success(progressData(total, total, null, "Загрузка завершена"))
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (error: Exception) {
            Log.e(TAG, "Worker failed: ${error.javaClass.name}: ${error.message}", error)
            if (isStopped) Result.failure(errorData("Загрузка отменена"))
            else Result.retry()
        }
    }

    private fun createForegroundInfo(done: Int, total: Int): ForegroundInfo {
        val manager = applicationContext.getSystemService(NotificationManager::class.java)
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            manager.createNotificationChannel(
                NotificationChannel(NOTIFICATION_CHANNEL, "Загрузка ФотоАрхива", NotificationManager.IMPORTANCE_LOW)
            )
        }
        val notification = Notification.Builder(applicationContext, NOTIFICATION_CHANNEL)
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentTitle("ФотоАрхив: загрузка")
            .setContentText(if (total > 0) "Загружено $done из $total" else "Подготовка загрузки")
            .setProgress(total, done, total == 0)
            .setOngoing(true)
            .build()
        return ForegroundInfo(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
    }

    private fun decodeFile(item: JSONObject) = WorkerMedia(Uri.parse(item.getString("uri")), item.optString("name", "media"), item.optString("mime", "application/octet-stream"), item.optLong("date"), item.optLong("size", -1L))

    private suspend fun uploadOneWithRetry(token: String, destination: String, chronological: Boolean, preparedFolders: MutableSet<String>, checkedExistingFiles: MutableSet<String>, folderMutex: Mutex, file: WorkerMedia) {
        var lastError: Exception? = null
        repeat(4) { attempt ->
            try {
                uploadOne(token, destination, chronological, preparedFolders, checkedExistingFiles, folderMutex, file)
                return
            } catch (error: Exception) {
                lastError = error
                if (attempt < 3) {
                    val waitMs = when {
                        error.message?.contains("HTTP 429") == true -> 10_000L
                        error.message?.contains("HTTP 5") == true -> (attempt + 1) * 5_000L
                        else -> (attempt + 1) * 2_000L
                    }
                    Log.w(TAG, "Retry ${attempt + 1}/3 for ${file.name} in ${waitMs}ms", error)
                    delay(waitMs)
                }
            }
        }
        throw lastError ?: error("Не удалось загрузить ${file.name}")
    }

    private suspend fun uploadOne(token: String, destination: String, chronological: Boolean, preparedFolders: MutableSet<String>, checkedExistingFiles: MutableSet<String>, folderMutex: Mutex, file: WorkerMedia) {
        val folder = if (chronological) {
            val date = captureDate(file)
            val yearPath = childPath(destination, SimpleDateFormat("yyyy", Locale.US).format(date))
            val monthPath = childPath(yearPath, SimpleDateFormat("MM", Locale.US).format(date))
            val dayPath = childPath(monthPath, SimpleDateFormat("dd", Locale.US).format(date))
            folderMutex.withLock {
                listOf(yearPath, monthPath, dayPath).forEach { path ->
                    if (!preparedFolders.contains(path)) {
                        ensureFolder(token, path)
                        preparedFolders.add(path)
                    }
                }
            }
            dayPath
        } else destination
        uploadFileStreaming(token, folder, checkedExistingFiles, file)
    }

    private suspend fun uploadFileStreaming(token: String, folder: String, checkedExistingFiles: MutableSet<String>, item: WorkerMedia) {
        val path = if (folder == "/") "/${item.name}" else "${folder.trimEnd('/')}/${item.name}"
        if (remoteCheckedFolders.add(folder)) {
            runCatching { loadRemoteFiles(token, folder) }
                .onFailure { remoteCheckedFolders.remove(folder) }
                .getOrThrow()
        }
        Log.i(TAG, "Checking cached remote path: $path")
        if (checkedExistingFiles.contains(path) || remoteExistingFiles.contains(path)) {
            checkedExistingFiles.add(path)
            Log.i(TAG, "Skip existing file: $path")
            return
        }
        // Do not blindly trust MediaStore.SIZE here. On some Samsung builds it
        // can be stale for videos. A wrong Content-Length makes the Yandex
        // uploader wait forever for bytes that will never arrive.
        val contentLength = resolveContentLength(item)
        val uploadUrl = JSONObject(apiGet(token, "/v1/disk/resources/upload?path=${encode(path)}&overwrite=true")).getString("href")
        Log.i(TAG, "Upload URL received: file=${item.name}, host=${URL(uploadUrl).host}, size=$contentLength")
        val requestBody = object : RequestBody() {
            override fun contentType() = item.mime.toMediaType()
            override fun contentLength() = contentLength
            override fun writeTo(sink: BufferedSink) {
                val input = applicationContext.contentResolver.openInputStream(item.uri)
                    ?: error("Unable to open ${item.name}")
                input.use { source ->
                    val buffer = ByteArray(256 * 1024)
                    var sent = 0L
                    var nextLog = 8L * 1024L * 1024L
                    var nextProgress = 1024L * 1024L
                    while (true) {
                        val read = source.read(buffer)
                        if (read < 0) break
                        if (read == 0) {
                            // A few ContentProvider implementations may return
                            // a transient zero-length read. Avoid a hot loop.
                            Thread.yield()
                            continue
                        }
                        sink.write(buffer, 0, read)
                        sent += read
                        if (sent >= nextProgress) {
                            setProgressAsync(fileProgressData(0, 1, item, "Загрузка", sent, contentLength))
                            nextProgress += 1024L * 1024L
                        }
                        if (sent >= nextLog) {
                            Log.i(TAG, "Upload body progress: ${item.name}, bytes=$sent/$contentLength")
                            nextLog += 8L * 1024L * 1024L
                        }
                    }
                    if (contentLength >= 0L && sent != contentLength) {
                        error("Размер потока изменился для ${item.name}: ожидалось $contentLength, передано $sent")
                    }
                    sink.flush()
                    Log.i(TAG, "Upload body sent: ${item.name}, bytes=$sent")
                }
            }
        }
        val request = Request.Builder()
            .url(uploadUrl)
            .header("Expect", "")
            .put(requestBody)
            .build()
        runInterruptible(Dispatchers.IO) {
            uploadClient.newCall(request).execute().use { response ->
                val responseText = response.body?.string().orEmpty()
                Log.i(TAG, "Upload response ${response.code}: ${item.name}")
                if (!response.isSuccessful) error("Upload ${item.name} HTTP ${response.code}: $responseText")
            }
        }
    }

    private suspend fun loadRemoteFiles(token: String, folder: String) {
        val response = apiGet(token, "/v1/disk/resources?path=${encode(folder)}&limit=1000&fields=_embedded.items.path,_embedded.items.type")
        runCatching {
            val items = JSONObject(response).optJSONObject("_embedded")?.optJSONArray("items") ?: return
            for (index in 0 until items.length()) {
                val item = items.optJSONObject(index) ?: continue
                if (item.optString("type") == "file") {
                    item.optString("path").takeIf { it.isNotBlank() }?.let(remoteExistingFiles::add)
                }
            }
            Log.i(TAG, "Cached remote files: folder=$folder, total=${remoteExistingFiles.count { it.startsWith(folder.trimEnd('/') + "/") }}")
        }.onFailure { error ->
            Log.w(TAG, "Could not cache remote files for $folder; per-file checks remain available", error)
        }
    }

    private fun resolveContentLength(item: WorkerMedia): Long {
        val descriptorLength = runCatching {
            applicationContext.contentResolver.openAssetFileDescriptor(item.uri, "r")?.use { it.length }
        }.getOrNull() ?: -1L
        if (descriptorLength >= 0L) {
            Log.i(TAG, "Resolved content length from provider: ${item.name}=$descriptorLength")
            return descriptorLength
        }
        if (item.size > 0L) {
            Log.i(TAG, "Using MediaStore content length: ${item.name}=${item.size}")
            return item.size
        }
        Log.w(TAG, "Content length unknown; OkHttp will stream chunked: ${item.name}")
        return -1L
    }

    private fun captureDate(item: WorkerMedia): Date {
        val fallback = Date(item.date * 1000L)
        return try {
            if (item.mime.startsWith("image/")) {
                applicationContext.contentResolver.openInputStream(item.uri)?.use { stream ->
                    val exif = ExifInterface(stream)
                    val value = exif.getAttribute(ExifInterface.TAG_DATETIME_ORIGINAL) ?: exif.getAttribute(ExifInterface.TAG_DATETIME)
                    if (value.isNullOrBlank()) fallback else SimpleDateFormat("yyyy:MM:dd HH:mm:ss", Locale.US).parse(value) ?: fallback
                } ?: fallback
            } else if (item.mime.startsWith("video/")) {
                val retriever = MediaMetadataRetriever()
                try {
                    retriever.setDataSource(applicationContext, item.uri)
                    parseVideoDate(retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DATE)) ?: fallback
                } finally { retriever.release() }
            } else fallback
        } catch (_: Exception) { fallback }
    }

    private fun parseVideoDate(value: String?): Date? = value?.let { raw ->
        listOf("yyyyMMdd'T'HHmmss.SSS'Z'", "yyyyMMdd'T'HHmmss'Z'", "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'").firstNotNullOfOrNull { format -> runCatching { SimpleDateFormat(format, Locale.US).parse(raw) }.getOrNull() }
    }

    private suspend fun ensureFolder(token: String, path: String) {
        val connection = apiConnection(token, "PUT", "/v1/disk/resources?path=${encode(path)}")
        try {
            val body = runInterruptible(Dispatchers.IO) { readResponse(connection) }
            if (connection.responseCode !in 200..299 && connection.responseCode != 409) error("Создание папки $path HTTP ${connection.responseCode}: $body")
        } finally {
            connection.disconnect()
        }
    }

    private suspend fun uploadFile(token: String, folder: String, checkedExistingFiles: MutableSet<String>, item: WorkerMedia) {
        val path = if (folder == "/") "/${item.name}" else "${folder.trimEnd('/')}/${item.name}"
        Log.i(TAG, "Checking remote path: $path")
        if (checkedExistingFiles.contains(path) || diskFileExists(token, path)) {
            checkedExistingFiles.add(path)
            Log.i(TAG, "Skip existing file: $path")
            return
        }
        val contentLength = if (item.size >= 0) item.size else applicationContext.contentResolver
            .openAssetFileDescriptor(item.uri, "r")
            ?.use { it.length }
            ?: -1L
        Log.i(TAG, "Uploading ${item.name}, size=$contentLength, path=$path")
        val info = JSONObject(apiGet(token, "/v1/disk/resources/upload?path=${encode(path)}&overwrite=true"))
        val uploadUrl = info.getString("href")
        Log.i(TAG, "Upload URL received: file=${item.name}, host=${runCatching { URL(uploadUrl).host }.getOrDefault("unknown")}, size=$contentLength")
        val connection = (URL(uploadUrl).openConnection() as HttpURLConnection).apply {
            requestMethod = "PUT"
            doOutput = true
            connectTimeout = 30_000
            readTimeout = 120_000
            useCaches = false
            if (contentLength >= 0) {
                setFixedLengthStreamingMode(contentLength)
            } else {
                // Fallback only for providers that cannot report a file length.
                setChunkedStreamingMode(64 * 1024)
            }
            setRequestProperty("Content-Type", item.mime)
        }
        try {
            Log.i(TAG, "Upload body started: ${item.name}")
            val sentBytes = runInterruptible(Dispatchers.IO) {
                applicationContext.contentResolver.openInputStream(item.uri)?.use { input ->
                    connection.outputStream.use { output -> input.copyTo(output, 64 * 1024) }
                } ?: error("Не удалось открыть ${item.name}")
            }
            Log.i(TAG, "Upload body sent: ${item.name}, bytes=$sentBytes")
            val body = readResponse(connection)
            Log.i(TAG, "Upload response ${connection.responseCode}: ${item.name}")
            if (connection.responseCode !in 200..299) error("Загрузка ${item.name} HTTP ${connection.responseCode}: $body")
        } catch (error: Exception) {
            Log.e(TAG, "Upload failed: file=${item.name}, path=$path, size=$contentLength", error)
            throw error
        } finally {
            connection.disconnect()
        }
    }

    private suspend fun diskFileExists(token: String, path: String): Boolean {
        val connection = apiConnection(token, "GET", "/v1/disk/resources?path=${encode(path)}")
        try {
            val body = runInterruptible(Dispatchers.IO) { readResponse(connection) }
            return when (connection.responseCode) {
                in 200..299 -> true
                404 -> false
                else -> error("Проверка файла $path HTTP ${connection.responseCode}: $body")
            }
        } finally {
            connection.disconnect()
        }
    }

    private suspend fun apiGet(token: String, path: String): String {
        val connection = apiConnection(token, "GET", path)
        try {
            val body = runInterruptible(Dispatchers.IO) { readResponse(connection) }
            if (connection.responseCode !in 200..299) error("Disk API HTTP ${connection.responseCode}: $body")
            return body
        } finally {
            connection.disconnect()
        }
    }

    private suspend fun apiConnection(token: String, method: String, path: String): HttpURLConnection {
        apiThrottle.withLock {
            val waitMs = nextApiRequestAt - System.currentTimeMillis()
            if (waitMs > 0) delay(waitMs)
            nextApiRequestAt = System.currentTimeMillis() + API_INTERVAL_MS
        }
        Log.i(TAG, "API request: $method $path")
        return (URL("https://cloud-api.yandex.net$path").openConnection() as HttpURLConnection).apply {
            requestMethod = method
            // Folder creation uses PUT without a request body. Enabling output
            // here makes HttpURLConnection wait for a body before reading the
            // response and can stall the very first upload.
            doOutput = false
            connectTimeout = 30_000
            readTimeout = 120_000
            setRequestProperty("Authorization", "OAuth $token")
        }
    }

    private fun readResponse(connection: HttpURLConnection): String {
        val stream = if (connection.responseCode in 200..299) connection.inputStream else connection.errorStream
        return stream?.bufferedReader()?.use { it.readText() }.orEmpty()
    }

    private fun encode(value: String): String = URLEncoder.encode(value, "UTF-8").replace("+", "%20")
    private fun childPath(parent: String, child: String): String = if (parent == "/") "/$child" else "${parent.trimEnd('/')}/$child"
    private fun progressData(done: Int, total: Int, file: WorkerMedia?, status: String) = Data.Builder().putInt("done", done).putInt("total", total).putString("uri", file?.uri.toString()).putString("name", file?.name.orEmpty()).putString("status", status).build()
    private fun fileProgressData(done: Int, total: Int, file: WorkerMedia, status: String, bytes: Long, size: Long) = Data.Builder().putInt("done", done).putInt("total", total).putString("uri", file.uri.toString()).putString("name", file.name).putString("status", status).putLong("fileBytes", bytes).putLong("fileSize", size).build()
    private fun errorData(message: String) = Data.Builder().putString("error", message).build()
}

private data class WorkerMedia(val uri: Uri, val name: String, val mime: String, val date: Long, val size: Long = -1L)
