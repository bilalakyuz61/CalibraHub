package com.calibrahub.app.ui.chat

import android.content.Intent
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.AttachFile
import androidx.compose.material.icons.filled.OpenInNew
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import com.calibrahub.app.app
import com.calibrahub.app.data.MessageDto
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.net.URLEncoder
import java.nio.charset.StandardCharsets

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ChatDetailScreen(phone: String, onBack: () -> Unit) {
    val context = LocalContext.current
    val repo    = context.app.repository
    val session = context.app.session
    val scope   = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }
    val listState = rememberLazyListState()

    var messages by remember { mutableStateOf<List<MessageDto>>(emptyList()) }
    var composer by remember { mutableStateOf("") }
    var sending  by remember { mutableStateOf(false) }
    var loading  by remember { mutableStateOf(true) }
    var baseUrl  by remember { mutableStateOf("") }
    var contactDisplayName by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(Unit) { baseUrl = session.currentBaseUrl().trimEnd('/') }

    // 3sn polling — yeni mesajları yakala
    LaunchedEffect(phone) {
        // Sohbete girdiğimizde okundu işaretle
        repo.markRead(phone)
        while (true) {
            repo.messages(phone).fold(
                onSuccess = {
                    messages = it
                    loading = false
                    // contactDisplayName için son gelen mesaj ismi yoksa sohbet listesinden çek
                },
                onFailure = { /* sessiz */ }
            )
            delay(3000)
        }
    }

    // Yeni mesaj geldiğinde otomatik en alta kaydır
    LaunchedEffect(messages.size) {
        if (messages.isNotEmpty()) listState.animateScrollToItem(messages.lastIndex)
    }

    val filePicker = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        if (uri == null) return@rememberLauncherForActivityResult
        scope.launch {
            val resolver = context.contentResolver
            val mime = resolver.getType(uri) ?: "application/octet-stream"
            val fileName = uri.lastPathSegment?.substringAfterLast('/') ?: "attachment.bin"
            val cacheFile = java.io.File(context.cacheDir, fileName)
            resolver.openInputStream(uri)?.use { input ->
                cacheFile.outputStream().use { input.copyTo(it) }
            }
            sending = true
            repo.sendMedia(phone, cacheFile, mime, caption = composer.ifBlank { null }).fold(
                onSuccess = { composer = "" },
                onFailure = { e ->
                    snackbarHostState.showSnackbar("Dosya gönderilemedi: ${e.message ?: ""}")
                }
            )
            cacheFile.delete()
            sending = false
        }
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(contactDisplayName ?: phone, style = MaterialTheme.typography.titleSmall)
                        Text(phone, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Geri")
                    }
                },
                actions = {
                    IconButton(onClick = {
                        // WhatsApp Business app'i aç — varsa Intent ile redirect
                        val text = composer.takeIf { it.isNotBlank() } ?: ""
                        val encoded = URLEncoder.encode(text, StandardCharsets.UTF_8.toString())
                        val uri = Uri.parse("https://wa.me/${phone}?text=${encoded}")
                        val pkgs = listOf("com.whatsapp.w4b", "com.whatsapp")
                        var opened = false
                        for (pkg in pkgs) {
                            try {
                                val intent = Intent(Intent.ACTION_VIEW, uri).apply { setPackage(pkg) }
                                context.startActivity(intent)
                                opened = true
                                break
                            } catch (_: Exception) { /* paket yok, sıradakini dene */ }
                        }
                        if (!opened) scope.launch {
                            snackbarHostState.showSnackbar("WhatsApp app yüklü değil.")
                        }
                    }) {
                        Icon(Icons.Default.OpenInNew, contentDescription = "WhatsApp Business'ta aç")
                    }
                }
            )
        }
    ) { padding ->
        Column(modifier = Modifier.padding(padding).fillMaxSize()) {

            if (loading) {
                Box(modifier = Modifier.weight(1f).fillMaxSize(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
            } else if (messages.isEmpty()) {
                Box(modifier = Modifier.weight(1f).fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                    Text("Henüz mesaj yok.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            } else {
                LazyColumn(
                    state = listState,
                    modifier = Modifier.weight(1f).fillMaxWidth().padding(horizontal = 8.dp)
                ) {
                    items(messages, key = { it.id }) { m ->
                        MessageBubble(m, baseUrl)
                        Spacer(Modifier.height(4.dp))
                    }
                }
            }

            HorizontalDivider(color = MaterialTheme.colorScheme.outline)

            // Composer
            Row(
                modifier = Modifier.fillMaxWidth().padding(8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                IconButton(
                    onClick = { filePicker.launch(arrayOf("*/*")) },
                    enabled = !sending
                ) {
                    Icon(Icons.Default.AttachFile, contentDescription = "Dosya ekle")
                }

                OutlinedTextField(
                    value = composer,
                    onValueChange = { composer = it },
                    placeholder = { Text("Mesaj yaz…") },
                    enabled = !sending,
                    modifier = Modifier.weight(1f),
                    maxLines = 4
                )

                IconButton(
                    onClick = {
                        if (composer.isBlank()) return@IconButton
                        val text = composer
                        composer = ""
                        scope.launch {
                            sending = true
                            repo.sendText(phone, text).fold(
                                onSuccess = { /* polling getirir */ },
                                onFailure = { e ->
                                    composer = text   // hata olursa metni geri koy
                                    snackbarHostState.showSnackbar("Mesaj gönderilemedi: ${e.message ?: ""}")
                                }
                            )
                            sending = false
                        }
                    },
                    enabled = composer.isNotBlank() && !sending
                ) {
                    if (sending) CircularProgressIndicator(modifier = Modifier.size(20.dp))
                    else         Icon(Icons.AutoMirrored.Filled.Send, contentDescription = "Gönder",
                                       tint = MaterialTheme.colorScheme.primary)
                }
            }
        }
    }
}

@Composable
private fun MessageBubble(m: MessageDto, baseUrl: String) {
    val outgoing = m.direction == 1
    val bg  = if (outgoing) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant
    val fg  = if (outgoing) MaterialTheme.colorScheme.onPrimaryContainer else MaterialTheme.colorScheme.onSurface

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = if (outgoing) Arrangement.End else Arrangement.Start
    ) {
        Column(
            modifier = Modifier
                .padding(2.dp)
                .widthIn(max = 320.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(bg)
                .padding(8.dp)
        ) {
            when (m.mediaType) {
                "image", "sticker" -> {
                    if (!m.mediaPath.isNullOrBlank()) {
                        AsyncImage(
                            model = "$baseUrl${m.mediaPath}",
                            contentDescription = m.mediaFilename ?: "Resim",
                            modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(8.dp))
                        )
                        Spacer(Modifier.height(4.dp))
                    }
                }
                "video", "audio" -> {
                    Text(
                        text = if (m.mediaType == "video") "🎥 Video" else "🎤 Ses kaydı",
                        color = fg,
                        style = MaterialTheme.typography.bodyMedium
                    )
                    if (!m.mediaPath.isNullOrBlank()) {
                        Text(
                            text = "$baseUrl${m.mediaPath}",
                            color = MaterialTheme.colorScheme.secondary,
                            style = MaterialTheme.typography.bodySmall,
                            maxLines = 1, overflow = TextOverflow.Ellipsis
                        )
                    }
                }
                "document" -> {
                    Text("📄 ${m.mediaFilename ?: "Belge"}", color = fg)
                    if (m.mediaSize != null) {
                        Text(
                            text = "${m.mediaSize / 1024} KB",
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            style = MaterialTheme.typography.bodySmall
                        )
                    }
                }
                else -> { /* sadece body göster */ }
            }
            if (!m.body.isNullOrBlank()) {
                Text(m.body, color = fg)
            }
            if (!m.receivedAt.isNullOrBlank()) {
                Text(
                    text = m.receivedAt.takeLast(8).take(5),   // HH:mm
                    style = MaterialTheme.typography.labelSmall,
                    color = fg.copy(alpha = 0.55f),
                    modifier = Modifier.align(Alignment.End)
                )
            }
        }
    }
}
