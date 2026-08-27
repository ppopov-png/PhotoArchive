package com.example.saftest

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.ContentUris
import android.database.Cursor
import android.net.Uri
import android.os.Bundle
import android.os.Build
import android.provider.MediaStore
import android.provider.OpenableColumns
import android.util.Log
import android.util.Base64
import android.media.MediaMetadataRetriever
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.work.ExistingWorkPolicy
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkInfo
import androidx.work.WorkManager
import androidx.work.workDataOf
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items as gridItems
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Cloud
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material.icons.filled.CreateNewFolder
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.Logout
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.VideoLibrary
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Slider
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import coil.ImageLoader
import coil.decode.VideoFrameDecoder
import coil.request.ImageRequest
import androidx.exifinterface.media.ExifInterface
import kotlinx.coroutines.delay
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlin.math.roundToInt
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.security.MessageDigest
import java.security.SecureRandom
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.text.SimpleDateFormat
import java.util.Date

private const val TAG = "SafTest"
private const val PREFS = "saf_test_preferences"
private const val TOKEN_KEY = "yandex_oauth_token"
private const val UPLOAD_WORK = "photo_archive_upload"
private const val UPLOAD_QUEUE_KEY = "upload_queue"
private const val UPLOAD_DESTINATION_KEY = "upload_destination"
private const val UPLOAD_WORK_SCHEMA_KEY = "upload_work_schema"

private val AppBackground = Color(0xFFF8F9FF)
private val Ink = Color(0xFF15171D)
private val Muted = Color(0xFF7B7F8C)
private val Blue = Color(0xFF4F70F5)
private val Lavender = Color(0xFF9183F4)
private val PaleBlue = Color(0xFFEEF0FF)
private val SoftLavender = Color(0xFFF3F0FF)
private val CardWhite = Color(0xFFFFFFFF)
private val PrimaryGradient = Brush.horizontalGradient(listOf(Blue, Lavender))

data class CloudFolder(val name: String, val path: String)
data class MediaItem(val uri: Uri, val name: String, val mime: String, val date: Long, val relativePath: String, val size: Long = -1L)
private enum class MediaCategory(val title: String) {
    ALL("Все"),
    PHOTOS("Фото"),
    GALLERY("Галерея (DCIM)"),
    CAMERA("Камера"),
    SCREENSHOTS("Скриншоты"),
    DOWNLOADS("Загрузки"),
    VIDEOS("Видео")
}

private fun MediaCategory.matches(item: MediaItem): Boolean = when (this) {
    MediaCategory.ALL -> true
    MediaCategory.PHOTOS -> item.mime.startsWith("image/")
    MediaCategory.GALLERY -> item.relativePath.startsWith("DCIM/", ignoreCase = true)
    MediaCategory.VIDEOS -> item.mime.startsWith("video/")
    MediaCategory.SCREENSHOTS -> item.relativePath.contains("screenshot", ignoreCase = true)
    MediaCategory.DOWNLOADS -> item.relativePath.contains("download", ignoreCase = true)
    MediaCategory.CAMERA -> item.relativePath.contains("dcim/camera", ignoreCase = true) || item.relativePath.contains("camera", ignoreCase = true)
}

private fun groupTitle(timestamp: Long, columns: Int): String {
    val date = Instant.ofEpochSecond(timestamp).atZone(ZoneId.systemDefault()).toLocalDate()
    val pattern = when {
        columns <= 2 -> "yyyy"
        columns <= 4 -> "LLLL yyyy"
        else -> "d MMMM yyyy"
    }
    return date.format(DateTimeFormatter.ofPattern(pattern, Locale("ru"))).replaceFirstChar { it.uppercase() }
}

private fun formatBytes(bytes: Long): String {
    if (bytes < 1024L) return "$bytes Б"
    val units = arrayOf("КБ", "МБ", "ГБ")
    var value = bytes.toDouble()
    var index = -1
    while (value >= 1024.0 && index < units.lastIndex) {
        value /= 1024.0
        index++
    }
    return if (value >= 100.0) "%.0f %s".format(Locale.US, value, units[index]) else "%.1f %s".format(Locale.US, value, units[index])
}
private enum class AppScreen { SPLASH, AUTH, CLOUD, SETUP, MODE, GALLERY, UPLOAD }

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { PhotoArchiveApp() }
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    private fun PhotoArchiveApp() {
        val prefs = remember { getSharedPreferences(PREFS, Context.MODE_PRIVATE) }
        val token = remember { mutableStateOf(prefs.getString(TOKEN_KEY, null)) }
        var screen by remember { mutableStateOf(AppScreen.SPLASH) }
        var message by remember { mutableStateOf("") }
        var busy by remember { mutableStateOf(false) }
        var authCode by remember { mutableStateOf("") }
        var oauthVerifier by remember { mutableStateOf("") }
        var selectedFiles by remember { mutableStateOf<List<Uri>>(emptyList()) }
        var mediaItems by remember { mutableStateOf<List<MediaItem>>(emptyList()) }
        var mediaCategory by remember { mutableStateOf(MediaCategory.ALL) }
        var mediaLoading by remember { mutableStateOf(false) }
        var gridColumns by remember { mutableStateOf(4f) }
        var destination by remember { mutableStateOf(prefs.getString(UPLOAD_DESTINATION_KEY, "/") ?: "/") }
        var folders by remember { mutableStateOf<List<CloudFolder>>(emptyList()) }
        var folderLoading by remember { mutableStateOf(false) }
        var showCreateFolder by remember { mutableStateOf(false) }
        var showMediaDisclosure by remember { mutableStateOf(false) }
        var uploadProgress by remember { mutableStateOf(0f) }
        var uploadFileProgress by remember { mutableStateOf(0f) }
        var uploadFileBytes by remember { mutableStateOf(0L) }
        var uploadFileSize by remember { mutableStateOf(0L) }
        var uploadedCount by remember { mutableStateOf(0) }
        var uploadCurrentName by remember { mutableStateOf("") }
        var uploadRequested by remember { mutableStateOf(false) }
        var chronological by remember { mutableStateOf(true) }
        var uploadWorkName by remember { mutableStateOf(UPLOAD_WORK) }
        val workManager = remember { WorkManager.getInstance(this@MainActivity) }
        val imageLoader = remember {
            ImageLoader.Builder(this@MainActivity)
                .components { add(VideoFrameDecoder.Factory()) }
                .build()
        }

        LaunchedEffect(Unit) {
            val workerSchema = prefs.getInt(UPLOAD_WORK_SCHEMA_KEY, 0)
            if (workerSchema < 7) {
                workManager.cancelAllWork()
                prefs.edit()
                    .remove(UPLOAD_QUEUE_KEY)
                    .remove("active_upload_work")
                    .putInt(UPLOAD_WORK_SCHEMA_KEY, 7)
                    .apply()
                uploadRequested = false
                busy = false
                message = ""
            }
            delay(1100)
            val activeUpload = withContext(Dispatchers.IO) {
                workManager.getWorkInfosForUniqueWork(uploadWorkName).get().firstOrNull { info ->
                    info.state == WorkInfo.State.RUNNING || info.state == WorkInfo.State.ENQUEUED || info.state == WorkInfo.State.BLOCKED
                }
            }
            if (activeUpload != null) {
                val restored = decodeQueue(prefs.getString(UPLOAD_QUEUE_KEY, "[]") ?: "[]")
                mediaItems = restored
                selectedFiles = restored.map { it.uri }
            }
            screen = when {
                token.value == null -> AppScreen.AUTH
                activeUpload != null -> AppScreen.UPLOAD
                else -> AppScreen.CLOUD
            }
        }

        LaunchedEffect(screen) {
            while (true) {
                val info = withContext(Dispatchers.IO) { workManager.getWorkInfosForUniqueWork(uploadWorkName).get().firstOrNull() }
                if (info != null) {
                    val total = info.progress.getInt("total", 0)
                    val done = info.progress.getInt("done", 0)
                    uploadProgress = if (total > 0) done.toFloat() / total else 0f
                    uploadedCount = done
                    uploadCurrentName = info.progress.getString("name").orEmpty()
                    uploadFileBytes = info.progress.getLong("fileBytes", 0L)
                    uploadFileSize = info.progress.getLong("fileSize", 0L)
                    uploadFileProgress = if (uploadFileSize > 0L) (uploadFileBytes.toDouble() / uploadFileSize).coerceIn(0.0, 1.0).toFloat() else 0f
                    when (info.state) {
                        WorkInfo.State.SUCCEEDED -> { busy = false; message = "Все файлы успешно загружены"; prefs.edit().remove(UPLOAD_QUEUE_KEY).remove("active_upload_work").apply() }
                        WorkInfo.State.FAILED -> { busy = false; message = "Ошибка: ${info.outputData.getString("error") ?: "загрузка не выполнена"}"; prefs.edit().remove(UPLOAD_QUEUE_KEY).remove("active_upload_work").apply() }
                        WorkInfo.State.CANCELLED -> { busy = false; message = "Загрузка отменена"; prefs.edit().remove(UPLOAD_QUEUE_KEY).remove("active_upload_work").apply() }
                        WorkInfo.State.RUNNING, WorkInfo.State.ENQUEUED, WorkInfo.State.BLOCKED -> busy = true
                    }
                    if (info.state.isFinished) workManager.pruneWork()
                }
                delay(600)
            }
        }

        fun loadFolders(path: String) {
            val currentToken = token.value ?: return
            destination = path
            folderLoading = true
            Thread {
                try {
                    val result = listFolders(currentToken, path)
                    runOnUiThread { folders = result; folderLoading = false }
                } catch (error: Exception) {
                    Log.e(TAG, "Failed to load folders", error)
                    runOnUiThread { message = error.fullMessage(); folderLoading = false }
                }
            }.start()
        }

        fun loadMedia() {
            mediaLoading = true
            Thread {
                try {
                    val result = queryMedia()
                    runOnUiThread { mediaItems = result; mediaLoading = false }
                } catch (error: Exception) {
                    Log.e(TAG, "Failed to query media", error)
                    runOnUiThread { message = error.fullMessage(); mediaLoading = false }
                }
            }.start()
        }

        val mediaPermissionLauncher = rememberLauncherForActivityResult(
            ActivityResultContracts.RequestMultiplePermissions()
        ) {
            if (hasMediaPermission()) {
                loadMedia()
                screen = AppScreen.GALLERY
            } else message = "Нужно разрешить доступ к фото и видео в настройках телефона"
        }

        fun openGallery() {
            if (hasMediaPermission()) {
                loadMedia()
                screen = AppScreen.GALLERY
            } else {
                screen = AppScreen.GALLERY
                showMediaDisclosure = true
            }
        }

        Surface(Modifier.fillMaxSize(), color = AppBackground) {
            when (screen) {
                AppScreen.SPLASH -> SplashScreen()
                AppScreen.AUTH -> AuthScreen(
                    code = authCode,
                    busy = busy,
                    error = message,
                    onOpenAuth = {
                        oauthVerifier = createCodeVerifier()
                        val challenge = createCodeChallenge(oauthVerifier)
                        val redirect = URLEncoder.encode("https://oauth.yandex.ru/verification_code", "UTF-8")
                        val url = "https://oauth.yandex.ru/authorize?response_type=code" +
                            "&client_id=${BuildConfig.YANDEX_CLIENT_ID}&redirect_uri=$redirect" +
                            "&scope=cloud_api:disk.read%20cloud_api:disk.write%20cloud_api:disk.info" +
                            "&force_confirm=yes&code_challenge=$challenge&code_challenge_method=S256"
                        startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
                    },
                    onCodeChange = { authCode = it },
                    onConfirm = {
                        busy = true
                        Thread {
                            try {
                                val newToken = postToken(authCode.trim(), oauthVerifier)
                                getDiskInfo(newToken)
                                prefs.edit().putString(TOKEN_KEY, newToken).apply()
                                runOnUiThread {
                                    token.value = newToken
                                    busy = false
                                    message = ""
                                    screen = AppScreen.CLOUD
                                }
                            } catch (error: Exception) {
                                Log.e(TAG, "OAuth failed", error)
                                runOnUiThread { busy = false; message = error.fullMessage() }
                            }
                        }.start()
                    }
                )
                AppScreen.CLOUD -> CloudScreen(onYandex = { loadFolders("/"); screen = AppScreen.SETUP }, onGoogle = { })
                AppScreen.SETUP -> SetupScreen(
                    destination = destination,
                    folders = folders,
                    loading = folderLoading,
                    fileCount = selectedFiles.size,
                    onLogout = {
                        prefs.edit().remove(TOKEN_KEY).apply()
                        token.value = null
                        selectedFiles = emptyList()
                        screen = AppScreen.AUTH
                    },
                    onOpenFolder = { loadFolders(it) },
                    onUp = {
                        val parent = destination.trimEnd('/').substringBeforeLast('/', "")
                        loadFolders(if (parent.isBlank()) "/" else parent)
                    },
                    onCreateFolder = { showCreateFolder = true },
                    onPickMedia = { screen = AppScreen.MODE },
                    onContinue = { selectedFiles = emptyList(); message = ""; screen = AppScreen.MODE },
                    uploadActive = busy,
                    onOpenUpload = { screen = AppScreen.UPLOAD },
                    onRefresh = { loadFolders(destination) }
                )
                AppScreen.MODE -> ModeScreen(destination = destination, chronological = chronological, onChronologicalChange = { chronological = it }, onBack = { screen = AppScreen.SETUP }, onPickMedia = { openGallery() })
                AppScreen.GALLERY -> GalleryScreen(
                    items = mediaItems,
                    category = mediaCategory,
                    selected = selectedFiles,
                    columns = gridColumns,
                    loading = mediaLoading,
                    error = message,
                    imageLoader = imageLoader,
                    onBack = { screen = AppScreen.SETUP },
                    onColumnsChange = { gridColumns = it },
                    onCategoryChange = { mediaCategory = it },
                    onToggle = { uri -> selectedFiles = if (selectedFiles.contains(uri)) selectedFiles - uri else selectedFiles + uri },
                    onTapUri = { uri -> selectedFiles = if (selectedFiles.contains(uri)) selectedFiles - uri else selectedFiles + uri },
                    onDragSelection = { uris, remove ->
                        val selectedUris = uris.toSet()
                        selectedFiles = if (remove) selectedFiles.filterNot { selectedUris.contains(it) } else (selectedFiles + selectedUris).distinct()
                    },
                    onToggleGroup = { uris ->
                        val groupUris = uris.toSet()
                        val selectedSet = selectedFiles.toHashSet()
                        val groupSelected = groupUris.isNotEmpty() && groupUris.all(selectedSet::contains)
                        selectedFiles = if (groupSelected) selectedFiles.filterNot(groupUris::contains) else (selectedFiles + groupUris).distinct()
                    },
                    onToggleAll = {
                        val categoryItems = mediaItems.filter { mediaCategory.matches(it) }
                        val selectedSet = selectedFiles.toHashSet()
                        val categorySelected = categoryItems.isNotEmpty() && categoryItems.all { selectedSet.contains(it.uri) }
                        selectedFiles = if (categorySelected) {
                            val categoryUris = categoryItems.asSequence().map { it.uri }.toHashSet()
                            selectedFiles.filterNot(categoryUris::contains)
                        } else {
                            (selectedFiles + categoryItems.map { it.uri }).distinct()
                        }
                    },
                    onDone = {
                        message = ""
                        uploadProgress = 0f
                        uploadedCount = 0
                        uploadCurrentName = ""
                        uploadRequested = true
                        screen = AppScreen.UPLOAD
                    }
                )
                AppScreen.UPLOAD -> UploadScreen(
                    files = mediaItems.filter { selectedFiles.contains(it.uri) },
                    destination = destination,
                    progress = uploadProgress,
                    fileProgress = uploadFileProgress,
                    fileBytes = uploadFileBytes,
                    fileSize = uploadFileSize,
                    uploaded = uploadedCount,
                    busy = busy,
                    currentName = uploadCurrentName,
                    autoStart = uploadRequested,
                    imageLoader = imageLoader,
                    message = message,
                    onBack = { screen = AppScreen.SETUP },
                    onCancel = {
                        workManager.cancelUniqueWork(uploadWorkName)
                        workManager.pruneWork()
                        prefs.edit().remove(UPLOAD_QUEUE_KEY).remove("active_upload_work").apply()
                        uploadRequested = false
                        busy = false
                        message = "Загрузка отменена"
                    },
                    onStart = {
                        val currentToken = token.value
                        if (currentToken.isNullOrBlank()) {
                            uploadRequested = false
                            busy = false
                            message = "Сначала подключите Яндекс Диск"
                            screen = AppScreen.AUTH
                        } else {
                        val uploadItems = mediaItems.filter { selectedFiles.contains(it.uri) && mediaCategory.matches(it) }
                        val queue = JSONArray().apply {
                            uploadItems.forEach { item ->
                                put(JSONObject().apply {
                                    put("uri", item.uri.toString())
                                    put("name", item.name)
                                    put("mime", item.mime)
                                    put("date", item.date)
                                    put("size", item.size)
                                })
                            }
                        }.toString()
                        prefs.edit().putString(UPLOAD_QUEUE_KEY, queue).putString(UPLOAD_DESTINATION_KEY, destination).apply()
                        val workName = UPLOAD_WORK
                        uploadWorkName = workName
                        prefs.edit().putString("active_upload_work", workName).apply()
                        val request = OneTimeWorkRequestBuilder<UploadWorker>()
                            .setInputData(workDataOf(
                                "token" to currentToken,
                                "destination" to destination,
                                "chronological" to chronological
                            ))
                            .build()
                        workManager.enqueueUniqueWork(workName, ExistingWorkPolicy.REPLACE, request)
                        uploadRequested = false
                        busy = true
                        message = ""
                        uploadProgress = 0f
                        uploadedCount = 0
                        uploadCurrentName = ""
                        }
                    }
                )
            }
        }

        if (showCreateFolder) {
            CreateFolderDialog(
                onDismiss = { showCreateFolder = false },
                onCreate = { name ->
                    showCreateFolder = false
                    val currentToken = token.value ?: return@CreateFolderDialog
                    Thread {
                        try {
                            val newPath = createFolder(currentToken, destination, name)
                            runOnUiThread { message = "Папка создана и выбрана"; loadFolders(newPath) }
                        } catch (error: Exception) {
                            runOnUiThread { message = error.fullMessage() }
                        }
                    }.start()
                }
            )
        }

        if (showMediaDisclosure) {
            AlertDialog(
                onDismissRequest = { showMediaDisclosure = false },
                title = { Text("Доступ к медиатеке") },
                text = { Text("ФотоАрхив получает доступ к фото и видео, чтобы по вашему выбору загрузить их в указанную папку Яндекс Диска. Файлы не отправляются без вашего действия.") },
                confirmButton = { TextButton(onClick = { showMediaDisclosure = false; mediaPermissionLauncher.launch(mediaPermissions()) }) { Text("Разрешить доступ") } },
                dismissButton = { TextButton(onClick = { showMediaDisclosure = false }) { Text("Не сейчас") } }
            )
        }
    }

    @Composable
    private fun SplashScreen() {
        Column(Modifier.fillMaxSize().background(AppBackground).padding(28.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
            CloudIllustration(Modifier.size(250.dp))
            Spacer(Modifier.height(20.dp))
            Text("ФотоАрхив", style = MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold, color = Ink)
            Text("Ваши воспоминания в надёжном облаке", color = Muted)
            Spacer(Modifier.height(28.dp))
            CircularProgressIndicator(Modifier.size(30.dp), color = Lavender, strokeWidth = 3.dp)
            Spacer(Modifier.height(12.dp))
            Text("Подготовка медиатеки...", color = Muted, style = MaterialTheme.typography.bodySmall)
        }
    }

    @Composable
    private fun CloudIllustration(modifier: Modifier = Modifier) {
        Box(modifier, contentAlignment = Alignment.Center) {
            Box(Modifier.size(190.dp).clip(CircleShape).background(SoftLavender))
            Icon(Icons.Default.CloudUpload, null, Modifier.size(118.dp), tint = Color(0xFFB9C9FF))
            Box(Modifier.align(Alignment.BottomStart).padding(start = 28.dp, bottom = 28.dp).size(70.dp).clip(RoundedCornerShape(20.dp)).background(Color.White).border(1.dp, Color(0xFFD9DDFF), RoundedCornerShape(20.dp)), contentAlignment = Alignment.Center) {
                Icon(Icons.Default.Image, null, Modifier.size(38.dp), tint = Lavender)
            }
            Box(Modifier.align(Alignment.TopEnd).padding(end = 26.dp, top = 30.dp).size(28.dp).clip(RoundedCornerShape(10.dp)).background(Color(0xFFE5E1FF)))
        }
    }

    @Composable
    private fun PrimaryGradientButton(text: String, onClick: () -> Unit, enabled: Boolean = true, modifier: Modifier = Modifier) {
        Box(modifier.fillMaxWidth().height(58.dp).clip(RoundedCornerShape(28.dp)).background(if (enabled) PrimaryGradient else Brush.linearGradient(listOf(Color(0xFFE2E3EA), Color(0xFFE8E8EF)))).clickable(enabled = enabled, onClick = onClick), contentAlignment = Alignment.Center) {
            Text(text, color = if (enabled) Color.White else Muted, fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.titleMedium)
        }
    }

    @Composable
    private fun SoftCard(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
        Card(modifier, shape = RoundedCornerShape(26.dp), colors = CardDefaults.cardColors(containerColor = CardWhite), elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)) {
            Column(Modifier.padding(20.dp), content = content)
        }
    }

    @Composable
    private fun AuthScreen(code: String, busy: Boolean, error: String, onOpenAuth: () -> Unit, onCodeChange: (String) -> Unit, onConfirm: () -> Unit) {
        var showPrivacy by remember { mutableStateOf(false) }
        Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.Center) {
            Box(Modifier.size(72.dp).clip(CircleShape).background(PaleBlue), contentAlignment = Alignment.Center) { Icon(Icons.Default.Cloud, null, Modifier.size(38.dp), tint = Blue) }
            Spacer(Modifier.height(20.dp))
            Text("Подключите Яндекс Диск", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold, color = Ink)
            Spacer(Modifier.height(8.dp))
            Text("ФотоАрхив будет загружать выбранные фото и видео только в указанную вами папку.", color = Muted)
            Spacer(Modifier.height(24.dp))
            Button(onClick = onOpenAuth, modifier = Modifier.fillMaxWidth().height(52.dp), colors = ButtonDefaults.buttonColors(containerColor = Blue)) { Text("Войти через Яндекс ID") }
            Spacer(Modifier.height(12.dp))
            OutlinedTextField(code, onCodeChange, Modifier.fillMaxWidth(), label = { Text("Код подтверждения") }, singleLine = true)
            Spacer(Modifier.height(8.dp))
            Button(onClick = onConfirm, enabled = code.isNotBlank() && !busy, modifier = Modifier.fillMaxWidth()) { Text("Подтвердить подключение") }
            TextButton(onClick = { showPrivacy = true }, modifier = Modifier.align(Alignment.CenterHorizontally)) { Text("Политика конфиденциальности", color = Blue) }
            if (busy) { Spacer(Modifier.height(16.dp)); LinearProgressIndicator(Modifier.fillMaxWidth(), color = Blue) }
            if (error.isNotBlank()) { Spacer(Modifier.height(12.dp)); Text(error, color = MaterialTheme.colorScheme.error) }
        }
        if (showPrivacy) AlertDialog(onDismissRequest = { showPrivacy = false }, title = { Text("Политика конфиденциальности") }, text = { Text("ФотоАрхив получает доступ к выбранным фото и видео только для загрузки в выбранную папку Яндекс Диска. OAuth-токен хранится локально. Фото не отправляются на собственный сервер и не используются для рекламы. Полный текст политики находится в файле privacy-policy.html проекта.") }, confirmButton = { TextButton(onClick = { showPrivacy = false }) { Text("Понятно") } })
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    private fun CloudScreen(onYandex: () -> Unit, onGoogle: () -> Unit) {
        val busy = false
        val fileProgress = 0f
        val fileBytes = 0L
        val fileSize = 0L
        Column(Modifier.fillMaxSize().background(AppBackground).padding(24.dp), verticalArrangement = Arrangement.Center) {
            Text("Подключение облака", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold, color = Ink)
            Spacer(Modifier.height(6.dp))
            Text("Выберите место для резервной копии", color = Muted)
            Spacer(Modifier.height(26.dp))
            SoftCard(Modifier.fillMaxWidth().clickable(onClick = onYandex)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(54.dp).clip(CircleShape).background(PaleBlue), contentAlignment = Alignment.Center) { Icon(Icons.Default.Cloud, null, Modifier.size(32.dp), tint = Blue) }
                    Spacer(Modifier.width(16.dp)); Column(Modifier.weight(1f)) { Text("Яндекс Диск", fontWeight = FontWeight.Bold, color = Ink, style = MaterialTheme.typography.titleMedium); Text("Подключён", color = Color(0xFF36A66B), style = MaterialTheme.typography.bodySmall) }
                    Box(Modifier.size(28.dp).clip(CircleShape).background(Color(0xFF62C18D)), contentAlignment = Alignment.Center) { Icon(Icons.Default.Check, null, Modifier.size(18.dp), tint = Color.White) }
                }
            }
            Spacer(Modifier.height(14.dp))
            SoftCard(Modifier.fillMaxWidth().alpha(0.62f)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(54.dp).clip(CircleShape).background(Color(0xFFE9EAF0)), contentAlignment = Alignment.Center) { Icon(Icons.Default.Cloud, null, Modifier.size(32.dp), tint = Muted) }
                    Spacer(Modifier.width(16.dp)); Column(Modifier.weight(1f)) { Text("Google Диск", fontWeight = FontWeight.Bold, color = Muted, style = MaterialTheme.typography.titleMedium); Text("Скоро", color = Muted, style = MaterialTheme.typography.bodySmall) }
                    Box(Modifier.size(28.dp).border(2.dp, Color(0xFFC9CBD5), CircleShape))
                }
            }
            Spacer(Modifier.height(20.dp))
            SoftCard(Modifier.fillMaxWidth()) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Benefit(Icons.Default.CheckCircle, "Безопасно", "Шифрование данных")
                    Benefit(Icons.Default.CloudUpload, "Надёжно", "Резервные копии")
                    Benefit(Icons.Default.Cloud, "Удобно", "Доступ с любого устройства")
                }
            }
            if (busy && fileSize > 0L) {
                SoftCard(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Прогресс текущего файла", fontWeight = FontWeight.SemiBold, color = Ink)
                        LinearProgressIndicator({ fileProgress }, Modifier.fillMaxWidth(), color = Lavender)
                        Text("${formatBytes(fileBytes)} из ${formatBytes(fileSize)} · ${(fileProgress * 100).toInt()}%", color = Muted)
                    }
                }
            }
            Spacer(Modifier.height(18.dp))
            Text("Подробнее о подключении", color = Blue, modifier = Modifier.align(Alignment.CenterHorizontally), style = MaterialTheme.typography.bodySmall)
        }
    }

    @Composable
    private fun Benefit(icon: androidx.compose.ui.graphics.vector.ImageVector, title: String, subtitle: String) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.width(92.dp)) {
            Box(Modifier.size(38.dp).clip(CircleShape).background(PaleBlue), contentAlignment = Alignment.Center) { Icon(icon, null, Modifier.size(20.dp), tint = Blue) }
            Spacer(Modifier.height(6.dp)); Text(title, color = Ink, fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.labelMedium); Text(subtitle, color = Muted, style = MaterialTheme.typography.labelSmall, maxLines = 2)
        }
    }

    @Composable
    private fun ModeScreen(destination: String, chronological: Boolean, onChronologicalChange: (Boolean) -> Unit, onBack: () -> Unit, onPickMedia: () -> Unit) {
        Column(Modifier.fillMaxSize().background(AppBackground).padding(24.dp)) {
            IconButton(onClick = onBack) { Icon(Icons.Default.ArrowBack, "Назад") }
            Text("Как загрузить файлы?", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold, color = Ink)
            Text("Папка назначения: $destination", color = Muted)
            Spacer(Modifier.height(24.dp))
            SoftCard(Modifier.fillMaxWidth()) {
                Row(verticalAlignment = Alignment.CenterVertically) { Box(Modifier.size(46.dp).clip(RoundedCornerShape(16.dp)).background(PaleBlue), contentAlignment = Alignment.Center) { Icon(Icons.Default.CloudUpload, null, tint = Blue) }; Spacer(Modifier.width(12.dp)); Column(Modifier.weight(1f)) { Text("Хронологическая сортировка", color = Ink, fontWeight = FontWeight.SemiBold); Text("Год / месяц / день будут созданы автоматически.", color = Muted, style = MaterialTheme.typography.bodySmall) }; androidx.compose.material3.Switch(checked = chronological, onCheckedChange = onChronologicalChange) }
            }
            Spacer(Modifier.height(14.dp))
            SoftCard(Modifier.fillMaxWidth()) { Row(verticalAlignment = Alignment.CenterVertically) { Icon(Icons.Default.CheckCircle, null, tint = Blue); Spacer(Modifier.width(10.dp)); Text("Если выключить — файлы попадут прямо в выбранную папку.", color = Muted, style = MaterialTheme.typography.bodySmall) } }
            Spacer(Modifier.weight(1f))
            PrimaryGradientButton("Выбрать медиафайлы", onPickMedia, modifier = Modifier.navigationBarsPadding())
        }
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    private fun GalleryScreen(items: List<MediaItem>, category: MediaCategory, selected: List<Uri>, columns: Float, loading: Boolean, error: String, imageLoader: ImageLoader, onBack: () -> Unit, onColumnsChange: (Float) -> Unit, onCategoryChange: (MediaCategory) -> Unit, onToggle: (Uri) -> Unit, onTapUri: (Uri) -> Unit, onDragSelection: (List<Uri>, Boolean) -> Unit, onToggleGroup: (List<Uri>) -> Unit, onToggleAll: () -> Unit, onDone: () -> Unit) {
        val visibleItems = remember(items, category) { items.filter { category.matches(it) } }
        val allSelected = visibleItems.isNotEmpty() && visibleItems.all { selected.contains(it.uri) }
        val columnCount = columns.roundToInt()
        val gridState = rememberLazyGridState()
        val groupedItems = remember(visibleItems, columnCount) {
            visibleItems.groupBy { groupTitle(it.date, columnCount) }.toList()
        }
        val mediaIndexByUri = remember(visibleItems) {
            visibleItems.mapIndexed { index, item -> item.uri to index }.toMap()
        }
        var dragAnchor by remember { mutableStateOf<Int?>(null) }
        var dragRemoveMode by remember { mutableStateOf(false) }
        val slotToMediaIndex = remember(groupedItems) {
            buildList {
                groupedItems.forEach { (_, group) ->
                    add(null)
                    group.forEach { item -> mediaIndexByUri[item.uri]?.let(::add) }
                }
            }
        }
        fun indexAt(x: Float, y: Float): Int? {
            val layoutItem = gridState.layoutInfo.visibleItemsInfo.firstOrNull { info ->
                x >= info.offset.x && x < info.offset.x + info.size.width &&
                    y >= info.offset.y && y < info.offset.y + info.size.height
            } ?: return null
            return slotToMediaIndex.getOrNull(layoutItem.index)
        }
        fun rangeForDrag(from: Int, to: Int): List<Int> {
            val fromRow = from / columnCount
            val toRow = to / columnCount
            if (fromRow == toRow) {
                val first = minOf(from, to)
                val last = maxOf(from, to)
                return (first..last).filter { it < visibleItems.size }
            }
            val firstRow = minOf(fromRow, toRow)
            val lastRow = maxOf(fromRow, toRow)
            return (firstRow..lastRow).flatMap { row ->
                (0 until columnCount).map { row * columnCount + it }
            }.filter { it < visibleItems.size }
        }
        Column(Modifier.fillMaxSize().background(AppBackground)) {
            Row(Modifier.fillMaxWidth().padding(start = 8.dp, end = 8.dp, top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onBack) { Text("Отмена", color = Blue) }
                Column(Modifier.weight(1f)) {
                    Text("Медиатека", color = Ink, fontWeight = FontWeight.Bold, modifier = Modifier.align(Alignment.CenterHorizontally))
                }
                TextButton(onClick = onToggleAll, enabled = items.isNotEmpty()) { Text(if (allSelected) "Снять все" else "Выбрать все", color = Blue) }
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("Размер сетки", color = Muted, style = MaterialTheme.typography.bodySmall)
                Slider(value = columns, onValueChange = onColumnsChange, valueRange = 2f..6f, steps = 3, modifier = Modifier.weight(1f), colors = androidx.compose.material3.SliderDefaults.colors(thumbColor = Color.White, activeTrackColor = Color(0xFF829DFF)))
            }
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                MediaCategory.values().forEach { value ->
                    FilterChip(selected = value == category, onClick = { onCategoryChange(value) }, label = { Text(value.title) })
                }
            }
            if (loading) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator(color = Lavender) }
            } else if (visibleItems.isEmpty()) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Icon(Icons.Default.Image, null, Modifier.size(54.dp), tint = Color(0xFF858894))
                        Spacer(Modifier.height(12.dp))
                        Text("Фотографии не найдены", color = Ink)
                        Text(if (error.isBlank()) "Разрешите доступ к фото и видео" else error, color = if (error.isBlank()) Muted else Color(0xFFB34A5A))
                    }
                }
            } else {
                LazyVerticalGrid(
                    state = gridState,
                    columns = GridCells.Fixed(columnCount),
                    modifier = Modifier.weight(1f)
                    .pointerInput(visibleItems, columnCount, gridState) {
                            detectTapGestures { position -> indexAt(position.x, position.y)?.let { visibleItems.getOrNull(it)?.uri?.let(onTapUri) } }
                        }
                        .pointerInput(visibleItems, columnCount, gridState) {
                            detectDragGesturesAfterLongPress(
                                onDragStart = { position ->
                                    indexAt(position.x, position.y)?.let { index ->
                                        dragAnchor = index
                                        dragRemoveMode = visibleItems.getOrNull(index)?.uri?.let { selected.contains(it) } == true
                                        onDragSelection(rangeForDrag(index, index).mapNotNull { visibleItems.getOrNull(it)?.uri }, dragRemoveMode)
                                    }
                                },
                                onDrag = { change, _ ->
                                    change.consume()
                                    val anchor = dragAnchor
                                    val current = indexAt(change.position.x, change.position.y)
                                    if (anchor != null && current != null) onDragSelection(rangeForDrag(anchor, current).mapNotNull { visibleItems.getOrNull(it)?.uri }, dragRemoveMode)
                                },
                                onDragEnd = { dragAnchor = null },
                                onDragCancel = { dragAnchor = null }
                            )
                        },
                    horizontalArrangement = Arrangement.spacedBy(2.dp),
                    verticalArrangement = Arrangement.spacedBy(2.dp)
                ) {
                    groupedItems.forEach { (title, group) ->
                        item(span = { GridItemSpan(maxLineSpan) }) {
                            val groupUris = group.map { it.uri }
                            val groupSelected = groupUris.isNotEmpty() && groupUris.all(selected::contains)
                            Row(Modifier.fillMaxWidth().clickable { onToggleGroup(groupUris) }.padding(horizontal = 8.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                                Box(Modifier.size(25.dp).clip(CircleShape).background(if (groupSelected) Blue else Color.Transparent).border(2.dp, if (groupSelected) Blue else Color(0xFFB8BAC6), CircleShape), contentAlignment = Alignment.Center) { if (groupSelected) Icon(Icons.Default.Check, null, Modifier.size(17.dp), tint = Color.White) }
                                Spacer(Modifier.width(8.dp)); Text(title, color = Ink, fontWeight = FontWeight.Bold)
                            }
                        }
                        gridItems(group, key = { it.uri.toString() }) { item ->
                        val isSelected = selected.contains(item.uri)
                        Box(Modifier.fillMaxWidth().aspectRatio(1f)) {
                            val imageRequest = ImageRequest.Builder(LocalContext.current).data(item.uri).size(256).crossfade(false).memoryCacheKey(item.uri.toString()).diskCacheKey(item.uri.toString()).build()
                            AsyncImage(model = imageRequest, imageLoader = imageLoader, contentDescription = item.name, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                            if (item.mime.startsWith("video/")) Icon(Icons.Default.VideoLibrary, "Видео", tint = Color.White, modifier = Modifier.align(Alignment.BottomStart).padding(6.dp).size(18.dp))
                            Box(Modifier.align(Alignment.TopEnd).padding(7.dp).size(25.dp).clip(CircleShape).background(if (isSelected) Blue else Color.Transparent)) {
                                if (!isSelected) Box(Modifier.fillMaxSize().clip(CircleShape).background(Color.Transparent).border(2.dp, Color.White, CircleShape))
                                else Icon(Icons.Default.Check, null, tint = Color.White, modifier = Modifier.padding(4.dp))
                            }
                        }
                        }
                }
            }
            }
            Row(Modifier.fillMaxWidth().background(CardWhite).padding(16.dp).navigationBarsPadding(), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) { Text("Выбрано: ${selected.size}", color = Ink, fontWeight = FontWeight.SemiBold); Text("Медиафайлы", color = Muted, style = MaterialTheme.typography.bodySmall) }
                Box(Modifier.width(150.dp).height(52.dp).clip(RoundedCornerShape(26.dp)).background(if (selected.isNotEmpty()) PrimaryGradient else Brush.linearGradient(listOf(Color(0xFFE2E3EA), Color(0xFFE8E8EF)))).clickable(enabled = selected.isNotEmpty(), onClick = onDone), contentAlignment = Alignment.Center) { Text("Далее (${selected.size})", color = if (selected.isNotEmpty()) Color.White else Muted, fontWeight = FontWeight.SemiBold) }
            }
        }
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    private fun SetupScreen(destination: String, folders: List<CloudFolder>, loading: Boolean, fileCount: Int, uploadActive: Boolean, onLogout: () -> Unit, onOpenFolder: (String) -> Unit, onUp: () -> Unit, onCreateFolder: () -> Unit, onPickMedia: () -> Unit, onContinue: () -> Unit, onOpenUpload: () -> Unit, onRefresh: () -> Unit) {
        Scaffold(topBar = {
            TopAppBar(
                title = { Row(verticalAlignment = Alignment.CenterVertically) { Icon(Icons.Default.Cloud, null, tint = Blue); Spacer(Modifier.width(8.dp)); Text("Яндекс Диск", fontWeight = FontWeight.Bold) } },
                actions = { Icon(Icons.Default.CheckCircle, null, tint = Color(0xFF28A56A)); IconButton(onClick = onLogout) { Icon(Icons.Default.Logout, "Выйти") } },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent)
            )
        }, bottomBar = {
            PrimaryGradientButton("Выбрать эту папку", onContinue, modifier = Modifier.padding(16.dp).navigationBarsPadding())
        }) { insets ->
            LazyColumn(Modifier.fillMaxSize().padding(insets).padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                item {
                    Text("Куда загрузить?", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold, color = Ink)
                    Text("Выберите папку или создайте новую", color = Muted)
                }
                item {
                    Card(colors = CardDefaults.cardColors(containerColor = PaleBlue), shape = RoundedCornerShape(18.dp), modifier = Modifier.fillMaxWidth()) {
                        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) { Icon(Icons.Default.Folder, null, modifier = Modifier.size(30.dp), tint = Blue); Spacer(Modifier.width(12.dp)); Column { Text("Папка назначения", color = Muted); Text(destination, fontWeight = FontWeight.Bold, color = Ink) } }
                    }
                }
                item {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                        OutlinedButton(onClick = onUp, enabled = destination != "/", modifier = Modifier.weight(1f)) { Icon(Icons.Default.ArrowBack, null); Text(" Назад") }
                        OutlinedButton(onClick = onCreateFolder, modifier = Modifier.weight(1f)) { Icon(Icons.Default.CreateNewFolder, null); Text(" Новая папка") }
                        IconButton(onClick = onRefresh) { Icon(Icons.Default.MoreVert, "Обновить") }
                    }
                }
                item {
                    if (loading) LinearProgressIndicator(Modifier.fillMaxWidth(), color = Blue)
                    else if (folders.isEmpty()) Text("Папка пока пустая — создайте первую папку", color = Muted)
                    else LazyVerticalGrid(columns = GridCells.Fixed(2), modifier = Modifier.height((((folders.size + 1) / 2) * 108).dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        gridItems(folders) { folder -> FolderTile(folder, folder.path == destination) { onOpenFolder(folder.path) } }
                    }
                }
                item {
                    if (uploadActive) {
                        Card(Modifier.fillMaxWidth().clickable(onClick = onOpenUpload), colors = CardDefaults.cardColors(containerColor = Color(0xFFE7F6ED)), shape = RoundedCornerShape(18.dp)) {
                            Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) { CircularProgressIndicator(Modifier.size(24.dp), color = Color(0xFF28A56A), strokeWidth = 2.dp); Spacer(Modifier.width(12.dp)); Column(Modifier.weight(1f)) { Text("Загрузка продолжается", fontWeight = FontWeight.Bold, color = Ink); Text("Открыть статус загрузки", color = Color(0xFF228B5A)) }; Icon(Icons.Default.CloudUpload, null, tint = Color(0xFF28A56A)) }
                        }
                    }
                }
            }
        }
    }

    @Composable
    private fun FolderTile(folder: CloudFolder, selected: Boolean, onClick: () -> Unit) {
        Card(Modifier.fillMaxWidth().height(112.dp).clickable(onClick = onClick), colors = CardDefaults.cardColors(containerColor = if (selected) PaleBlue else CardWhite), shape = RoundedCornerShape(20.dp), elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)) {
            Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.SpaceBetween) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) { Icon(Icons.Default.Folder, null, Modifier.size(30.dp), tint = Lavender); if (selected) Icon(Icons.Default.Check, null, tint = Blue) }
                Column { Text(folder.name, maxLines = 1, fontWeight = FontWeight.SemiBold, color = Ink); Text("Папка", color = Muted, style = MaterialTheme.typography.labelSmall) }
            }
        }
    }

    @Composable
    private fun UploadScreen(files: List<MediaItem>, destination: String, progress: Float, fileProgress: Float, fileBytes: Long, fileSize: Long, uploaded: Int, busy: Boolean, currentName: String, autoStart: Boolean, imageLoader: ImageLoader, message: String, onBack: () -> Unit, onCancel: () -> Unit, onStart: () -> Unit) {
        var confirmCancel by remember { mutableStateOf(false) }
        val previewFiles = remember(files) { files.take(80) }
        LaunchedEffect(files, busy, message, autoStart) {
            if (autoStart && files.isNotEmpty() && !busy) {
                delay(300)
                onStart()
            }
        }
        Column(Modifier.fillMaxSize().padding(24.dp)) {
            if (busy && fileSize > 0L) {
                SoftCard(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Прогресс текущего файла", fontWeight = FontWeight.SemiBold, color = Ink)
                        LinearProgressIndicator({ fileProgress }, Modifier.fillMaxWidth(), color = Lavender)
                        Text("${formatBytes(fileBytes)} из ${formatBytes(fileSize)} · ${(fileProgress * 100).toInt()}%", color = Muted)
                    }
                }
            }
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = onBack) { Icon(Icons.Default.ArrowBack, "Назад") }
                Text("Загрузка", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, color = Ink)
            }
            Spacer(Modifier.height(12.dp))
            if (busy) CloudIllustration(Modifier.fillMaxWidth().height(150.dp))
            Text(if (busy) "Файлы загружаются" else "Готово к загрузке", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold, color = Ink)
            Text(if (currentName.isBlank()) "Загрузка продолжится в фоне" else currentName, color = Muted, maxLines = 1)
            Spacer(Modifier.height(22.dp))
            SoftCard(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    SummaryRow("Файлов", files.size.toString())
                    SummaryRow("Папка", destination)
                    if (busy || progress > 0f) { LinearProgressIndicator({ progress }, Modifier.fillMaxWidth(), color = Blue); Text("Загружено $uploaded из ${files.size}", color = Muted) }
                }
            }
            Spacer(Modifier.height(18.dp))
            LazyVerticalGrid(columns = GridCells.Fixed(4), modifier = Modifier.weight(1f), horizontalArrangement = Arrangement.spacedBy(4.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                gridItems(previewFiles, key = { it.uri.toString() }) { item ->
                    Box(Modifier.aspectRatio(1f).clip(RoundedCornerShape(8.dp)).background(Color(0xFFE8EAF0))) {
                        AsyncImage(model = ImageRequest.Builder(LocalContext.current).data(item.uri).size(160).crossfade(false).build(), imageLoader = imageLoader, contentDescription = item.name, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                        if (busy && item.name == currentName) {
                            Box(Modifier.align(Alignment.Center).size(42.dp).clip(CircleShape).background(Color(0xAA000000)), contentAlignment = Alignment.Center) { CircularProgressIndicator(Modifier.size(25.dp), color = Color.White, strokeWidth = 3.dp) }
                        } else if (uploaded > 0 && previewFiles.indexOf(item) < uploaded) {
                            Icon(Icons.Default.CheckCircle, "Загружено", tint = Color(0xFF38C172), modifier = Modifier.align(Alignment.TopEnd).padding(5.dp))
                        }
                    }
                }
            }
            if (files.size > previewFiles.size) Text("Показаны первые ${previewFiles.size} миниатюр из ${files.size}. Остальные загружаются в фоне.", color = Muted, style = MaterialTheme.typography.bodySmall)
            if (message.isNotBlank()) Text(message, color = if (message.startsWith("Ошибка")) MaterialTheme.colorScheme.error else Color(0xFF228B5A))
            Spacer(Modifier.height(12.dp))
            if (busy) OutlinedButton(onClick = { confirmCancel = true }, modifier = Modifier.fillMaxWidth().navigationBarsPadding(), shape = RoundedCornerShape(28.dp), colors = ButtonDefaults.outlinedButtonColors(contentColor = Lavender)) { Text("Отменить загрузку") }
            if (!busy) {
                val retry = message.startsWith("Ошибка")
                PrimaryGradientButton(
                    if (retry) "Повторить загрузку" else "Начать загрузку",
                    onStart,
                    enabled = files.isNotEmpty() && (message.isBlank() || retry),
                    modifier = Modifier.navigationBarsPadding()
                )
            }
        }
        if (confirmCancel) AlertDialog(onDismissRequest = { confirmCancel = false }, title = { Text("Отменить загрузку?") }, text = { Text("Уже загруженные файлы останутся на Диске.") }, confirmButton = { TextButton(onClick = { confirmCancel = false; onCancel() }) { Text("Отменить") } }, dismissButton = { TextButton(onClick = { confirmCancel = false }) { Text("Продолжить") } })
    }

    @Composable
    private fun SummaryRow(label: String, value: String) { Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) { Text(label, color = Muted); Text(value, fontWeight = FontWeight.Bold, color = Ink) } }

    @Composable
    private fun CreateFolderDialog(onDismiss: () -> Unit, onCreate: (String) -> Unit) {
        var name by remember { mutableStateOf("") }
        AlertDialog(onDismissRequest = onDismiss, title = { Text("Новая папка") }, text = { OutlinedTextField(name, { name = it }, label = { Text("Название папки") }, singleLine = true) }, confirmButton = { TextButton(onClick = { onCreate(name.trim()) }, enabled = name.isNotBlank()) { Text("Создать") } }, dismissButton = { TextButton(onClick = onDismiss) { Text("Отмена") } })
    }

    private fun hasMediaPermission(): Boolean {
        return if (Build.VERSION.SDK_INT >= 33) {
            ContextCompat.checkSelfPermission(this, "android.permission.READ_MEDIA_IMAGES") == PackageManager.PERMISSION_GRANTED ||
                ContextCompat.checkSelfPermission(this, "android.permission.READ_MEDIA_VIDEO") == PackageManager.PERMISSION_GRANTED ||
                (Build.VERSION.SDK_INT >= 34 && ContextCompat.checkSelfPermission(this, "android.permission.READ_MEDIA_VISUAL_USER_SELECTED") == PackageManager.PERMISSION_GRANTED)
        } else {
            ContextCompat.checkSelfPermission(this, "android.permission.READ_EXTERNAL_STORAGE") == PackageManager.PERMISSION_GRANTED
        }
    }

    private fun mediaPermissions(): Array<String> = if (Build.VERSION.SDK_INT >= 33) {
        buildList {
            add("android.permission.READ_MEDIA_IMAGES")
            add("android.permission.READ_MEDIA_VIDEO")
            if (Build.VERSION.SDK_INT >= 34) add("android.permission.READ_MEDIA_VISUAL_USER_SELECTED")
        }.toTypedArray()
    } else {
        arrayOf("android.permission.READ_EXTERNAL_STORAGE")
    }

    private fun queryMedia(): List<MediaItem> {
        val result = mutableListOf<MediaItem>()
        if (Build.VERSION.SDK_INT < 33 || hasPermission("android.permission.READ_MEDIA_IMAGES") || hasPermission("android.permission.READ_MEDIA_VISUAL_USER_SELECTED")) {
            result += queryMediaCollection(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, false)
        }
        if (Build.VERSION.SDK_INT < 33 || hasPermission("android.permission.READ_MEDIA_VIDEO") || hasPermission("android.permission.READ_MEDIA_VISUAL_USER_SELECTED")) {
            result += queryMediaCollection(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, true)
        }
        return result.sortedByDescending { it.date }.take(10000)
    }

    private fun hasPermission(permission: String): Boolean =
        ContextCompat.checkSelfPermission(this, permission) == PackageManager.PERMISSION_GRANTED

    private fun queryMediaCollection(collection: Uri, video: Boolean): List<MediaItem> {
        val projection = arrayOf(
            MediaStore.MediaColumns._ID,
            MediaStore.MediaColumns.DISPLAY_NAME,
            MediaStore.MediaColumns.MIME_TYPE,
            MediaStore.MediaColumns.DATE_ADDED,
            MediaStore.MediaColumns.DATE_TAKEN,
            MediaStore.MediaColumns.RELATIVE_PATH,
            MediaStore.MediaColumns.SIZE
        )
        val result = mutableListOf<MediaItem>()
        // Samsung MediaProvider can reject LIMIT inside sortOrder. Limit the cursor manually.
        val sort = "${MediaStore.MediaColumns.DATE_TAKEN} DESC, ${MediaStore.MediaColumns.DATE_ADDED} DESC"
        contentResolver.query(collection, projection, null, null, sort)?.use { cursor ->
            val idIndex = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns._ID)
            val nameIndex = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.DISPLAY_NAME)
            val mimeIndex = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.MIME_TYPE)
            val addedIndex = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.DATE_ADDED)
            val takenIndex = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.DATE_TAKEN)
            val pathIndex = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.RELATIVE_PATH)
            val sizeIndex = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.SIZE)
            var count = 0
            while (cursor.moveToNext() && count < 5000) {
                val id = cursor.getLong(idIndex)
                val takenMillis = cursor.getLong(takenIndex)
                val dateSeconds = if (takenMillis > 0) takenMillis / 1000 else cursor.getLong(addedIndex)
                result += MediaItem(
                    ContentUris.withAppendedId(collection, id),
                    cursor.getString(nameIndex) ?: "media",
                    cursor.getString(mimeIndex) ?: if (video) "video/*" else "image/*",
                    dateSeconds,
                    cursor.getString(pathIndex) ?: "",
                    cursor.getLong(sizeIndex)
                )
                count++
            }
        }
        Log.i(TAG, "MediaStore ${if (video) "video" else "image"}: ${result.size} items")
        return result
    }

    private fun listFolders(token: String, path: String): List<CloudFolder> {
        val response = apiGet(token, "/v1/disk/resources?path=${encode(path)}&limit=1000")
        val items = JSONObject(response).optJSONObject("_embedded")?.optJSONArray("items") ?: return emptyList()
        return (0 until items.length()).mapNotNull { index -> items.getJSONObject(index).let { if (it.optString("type") == "dir") CloudFolder(it.optString("name"), it.optString("path")) else null } }.sortedBy { it.name.lowercase() }
    }

    private fun createFolder(token: String, parent: String, name: String): String {
        val path = if (parent == "/") "/$name" else "${parent.trimEnd('/')}/$name"
        val connection = apiConnection(token, "PUT", "/v1/disk/resources?path=${encode(path)}")
        val response = readResponse(connection)
        if (connection.responseCode !in 200..299 && connection.responseCode != 409) error("Создание папки HTTP ${connection.responseCode}: $response")
        return path
    }

    private fun uploadOrganizedFile(token: String, basePath: String, item: MediaItem) {
        val date = mediaCaptureDate(item)
        val year = SimpleDateFormat("yyyy", Locale.US).format(date)
        val month = SimpleDateFormat("MM", Locale.US).format(date)
        val day = SimpleDateFormat("dd", Locale.US).format(date)
        val yearPath = childPath(basePath, year)
        val monthPath = childPath(yearPath, month)
        val dayPath = childPath(monthPath, day)

        ensureCloudFolder(token, yearPath)
        ensureCloudFolder(token, monthPath)
        ensureCloudFolder(token, dayPath)
        uploadFile(token, dayPath, item.uri)
        Log.i(TAG, "Uploaded ${item.name} to $dayPath using date $date")
    }

    private fun childPath(parent: String, child: String): String =
        if (parent == "/") "/$child" else "${parent.trimEnd('/')}/$child"

    private fun ensureCloudFolder(token: String, path: String) {
        val connection = apiConnection(token, "PUT", "/v1/disk/resources?path=${encode(path)}")
        val response = readResponse(connection)
        if (connection.responseCode !in 200..299 && connection.responseCode != 409) {
            error("Создание папки $path HTTP ${connection.responseCode}: $response")
        }
    }

    private fun mediaCaptureDate(item: MediaItem): Date {
        val fallback = Date(item.date * 1000L)
        return try {
            if (item.mime.startsWith("image/")) {
                contentResolver.openInputStream(item.uri)?.use { stream ->
                    val exif = ExifInterface(stream)
                    val value = exif.getAttribute(ExifInterface.TAG_DATETIME_ORIGINAL)
                        ?: exif.getAttribute(ExifInterface.TAG_DATETIME)
                    if (!value.isNullOrBlank()) {
                        SimpleDateFormat("yyyy:MM:dd HH:mm:ss", Locale.US).parse(value) ?: fallback
                    } else fallback
                } ?: fallback
            } else if (item.mime.startsWith("video/")) {
                val retriever = MediaMetadataRetriever()
                try {
                    retriever.setDataSource(this, item.uri)
                    val value = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DATE)
                    parseVideoDate(value) ?: fallback
                } finally { retriever.release() }
            } else fallback
        } catch (error: Exception) {
            Log.w(TAG, "Could not read capture date for ${item.name}; using MediaStore date", error)
            fallback
        }
    }

    private fun parseVideoDate(value: String?): Date? {
        if (value.isNullOrBlank()) return null
        val formats = listOf("yyyyMMdd'T'HHmmss.SSS'Z'", "yyyyMMdd'T'HHmmss'Z'", "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'")
        return formats.firstNotNullOfOrNull { format ->
            runCatching { SimpleDateFormat(format, Locale.US).parse(value) }.getOrNull()
        }
    }

    private fun uploadFile(token: String, folder: String, uri: Uri) {
        val name = displayName(uri)
        val path = if (folder == "/") "/$name" else "${folder.trimEnd('/')}/$name"
        val info = JSONObject(apiGet(token, "/v1/disk/resources/upload?path=${encode(path)}&overwrite=true"))
        val connection = URL(info.getString("href")).openConnection() as HttpURLConnection
        connection.requestMethod = "PUT"; connection.doOutput = true
        connection.setRequestProperty("Content-Type", contentResolver.getType(uri) ?: "application/octet-stream")
        contentResolver.openInputStream(uri)?.use { input -> connection.outputStream.use { output -> input.copyTo(output) } } ?: error("Не удалось открыть $name")
        val response = readResponse(connection)
        if (connection.responseCode !in 200..299) error("Загрузка $name HTTP ${connection.responseCode}: $response")
    }

    private fun postToken(code: String, verifier: String): String {
        val connection = apiConnection(null, "POST", "/token", oauth = true)
        val body = "grant_type=authorization_code&code=${encode(code)}&client_id=${encode(BuildConfig.YANDEX_CLIENT_ID)}&code_verifier=${encode(verifier)}"
        connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
        val response = readResponse(connection)
        if (connection.responseCode !in 200..299) error("OAuth HTTP ${connection.responseCode}: $response")
        return JSONObject(response).getString("access_token")
    }

    private fun getDiskInfo(token: String) { apiGet(token, "/v1/disk") }

    private fun apiGet(token: String, path: String): String {
        val connection = apiConnection(token, "GET", path); val response = readResponse(connection)
        if (connection.responseCode !in 200..299) error("Disk API HTTP ${connection.responseCode}: $response")
        return response
    }

    private fun apiConnection(token: String?, method: String, path: String, oauth: Boolean = false): HttpURLConnection {
        val base = if (oauth) "https://oauth.yandex.ru" else "https://cloud-api.yandex.net"
        return (URL(base + path).openConnection() as HttpURLConnection).apply { requestMethod = method; doOutput = method == "POST" || method == "PUT"; if (!oauth && token != null) setRequestProperty("Authorization", "OAuth $token"); if (oauth) setRequestProperty("Content-Type", "application/x-www-form-urlencoded") }
    }

    private fun readResponse(connection: HttpURLConnection): String { val stream = if (connection.responseCode in 200..299) connection.inputStream else connection.errorStream; return stream?.bufferedReader()?.use { it.readText() } ?: "" }
    private fun encode(value: String): String = URLEncoder.encode(value, "UTF-8").replace("+", "%20")
    private fun displayName(uri: Uri): String { var cursor: Cursor? = null; return try { cursor = contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null); if (cursor?.moveToFirst() == true) cursor.getString(0) else uri.lastPathSegment ?: "media_file" } finally { cursor?.close() } }
    private fun createCodeVerifier(): String {
        val bytes = ByteArray(32)
        SecureRandom().nextBytes(bytes)
        return Base64.encodeToString(bytes, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
    }

    private fun createCodeChallenge(verifier: String): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(verifier.toByteArray(Charsets.US_ASCII))
        return Base64.encodeToString(digest, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
    }
    private fun decodeQueue(raw: String): List<MediaItem> {
        return runCatching {
            val array = JSONArray(raw)
            (0 until array.length()).map { index ->
                val item = array.getJSONObject(index)
                MediaItem(Uri.parse(item.getString("uri")), item.optString("name", "media"), item.optString("mime", "application/octet-stream"), item.optLong("date"), "", item.optLong("size", -1L))
            }
        }.getOrDefault(emptyList())
    }
    private fun Throwable.fullMessage(): String = "Ошибка: ${message ?: javaClass.simpleName}"
}
