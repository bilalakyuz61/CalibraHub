package com.calibrahub.app.ui.chat

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Logout
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.ConversationDto
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ChatListScreen(
    onOpenChat: (phone: String) -> Unit,
    onLogout: () -> Unit
) {
    val context = LocalContext.current
    val repo    = context.app.repository
    val scope   = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var conversations by remember { mutableStateOf<List<ConversationDto>>(emptyList()) }
    var query   by remember { mutableStateOf("") }
    var loading by remember { mutableStateOf(true) }

    // 3 saniyelik polling — web UI ile aynı kadans
    LaunchedEffect(Unit) {
        while (true) {
            repo.conversations().fold(
                onSuccess = { conversations = it; loading = false },
                onFailure = { e ->
                    loading = false
                    snackbarHostState.showSnackbar("Yenileme hatası: ${e.message ?: "ağ"}")
                }
            )
            delay(3000)
        }
    }

    val filtered = remember(conversations, query) {
        if (query.isBlank()) conversations
        else conversations.filter {
            (it.displayName ?: "").contains(query, ignoreCase = true) ||
            (it.phone).contains(query) ||
            (it.contactCode ?: "").contains(query, ignoreCase = true)
        }
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = { Text("Sohbetler") },
                actions = {
                    IconButton(onClick = {
                        scope.launch {
                            repo.logout()
                            onLogout()
                        }
                    }) { Icon(Icons.Default.Logout, contentDescription = "Çıkış") }
                }
            )
        }
    ) { padding ->
        Column(modifier = Modifier.padding(padding).fillMaxSize()) {

            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                placeholder = { Text("Ara…") },
                leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth().padding(12.dp)
            )

            if (loading) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
            } else if (filtered.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                    Text(
                        text = if (conversations.isEmpty()) "Henüz sohbet yok."
                               else "Aramaya uyan sohbet bulunamadı.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    items(filtered, key = { it.phone }) { c ->
                        ConversationRow(c, onClick = { onOpenChat(c.phone) })
                        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
                    }
                }
            }
        }
    }
}

@Composable
private fun ConversationRow(c: ConversationDto, onClick: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        // Avatar — adın ilk harfi
        val initial = (c.displayName ?: c.phone).trim().firstOrNull()?.uppercaseChar() ?: '?'
        Box(
            modifier = Modifier
                .size(44.dp)
                .clip(CircleShape)
                .background(MaterialTheme.colorScheme.primary),
            contentAlignment = Alignment.Center
        ) {
            Text(
                text = initial.toString(),
                color = MaterialTheme.colorScheme.onPrimary,
                fontWeight = FontWeight.Bold
            )
        }
        Spacer(Modifier.width(12.dp))

        Column(modifier = Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = c.displayName ?: c.phone,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                if ((c.unreadCount) > 0) {
                    Badge(containerColor = MaterialTheme.colorScheme.primary) {
                        Text(if (c.unreadCount > 99) "99+" else c.unreadCount.toString())
                    }
                }
            }
            val preview = c.lastMessage?.takeIf { it.isNotBlank() }
                ?: when (c.lastMediaType) {
                    "image"    -> "📷 Resim"
                    "video"    -> "🎥 Video"
                    "audio"    -> "🎤 Ses"
                    "document" -> "📄 Belge"
                    "sticker"  -> "🪄 Sticker"
                    else       -> "—"
                }
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (!c.contactCode.isNullOrBlank()) {
                    Text(
                        text = c.contactCode,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.secondary,
                        modifier = Modifier.padding(end = 6.dp)
                    )
                }
                Text(
                    text = preview,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            }
        }
    }
}
