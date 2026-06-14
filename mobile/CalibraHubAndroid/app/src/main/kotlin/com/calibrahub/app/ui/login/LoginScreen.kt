package com.calibrahub.app.ui.login

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.calibrahub.app.app
import com.calibrahub.app.data.CompanyDto
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(onLoggedIn: () -> Unit) {
    val context = LocalContext.current
    val repo    = context.app.repository
    val session = context.app.session
    val scope   = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var email    by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var baseUrl  by remember { mutableStateOf("") }
    var showPwd  by remember { mutableStateOf(false) }
    var loading  by remember { mutableStateOf(false) }
    var showServerSettings by remember { mutableStateOf(false) }

    // Şirket dropdown — birden çok şirketli sistemler için
    var companies by remember { mutableStateOf<List<CompanyDto>>(emptyList()) }
    var selectedCompanyId by remember { mutableStateOf<Int?>(null) }
    var companyDropdownExpanded by remember { mutableStateOf(false) }

    // Mevcut base URL + şirket listesi
    LaunchedEffect(Unit) {
        baseUrl = session.currentBaseUrl()
        repo.companies().onSuccess { list ->
            companies = list
            if (list.size == 1) selectedCompanyId = list.first().id
        }
    }

    Scaffold(snackbarHost = { SnackbarHost(snackbarHostState) }) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .padding(24.dp)
                .fillMaxSize(),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                text = "CalibraHub",
                style = MaterialTheme.typography.headlineLarge,
                color = MaterialTheme.colorScheme.primary
            )
            Spacer(Modifier.height(4.dp))
            Text(
                text = "WhatsApp Companion",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(48.dp))

            // Şirket dropdown — sadece 2+ şirket varsa göster
            if (companies.size > 1) {
                ExposedDropdownMenuBox(
                    expanded = companyDropdownExpanded,
                    onExpandedChange = { companyDropdownExpanded = !companyDropdownExpanded },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    OutlinedTextField(
                        value = companies.firstOrNull { it.id == selectedCompanyId }?.name ?: "Şirket seçin",
                        onValueChange = {},
                        label = { Text("Şirket") },
                        readOnly = true,
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = companyDropdownExpanded) },
                        modifier = Modifier.menuAnchor().fillMaxWidth()
                    )
                    ExposedDropdownMenu(
                        expanded = companyDropdownExpanded,
                        onDismissRequest = { companyDropdownExpanded = false }
                    ) {
                        companies.forEach { c ->
                            DropdownMenuItem(
                                text = { Text(c.name) },
                                onClick = {
                                    selectedCompanyId = c.id
                                    companyDropdownExpanded = false
                                }
                            )
                        }
                    }
                }
                Spacer(Modifier.height(12.dp))
            }

            OutlinedTextField(
                value = email,
                onValueChange = { email = it },
                label = { Text("E-posta") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(12.dp))

            OutlinedTextField(
                value = password,
                onValueChange = { password = it },
                label = { Text("Parola") },
                singleLine = true,
                visualTransformation = if (showPwd) VisualTransformation.None else PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                trailingIcon = {
                    IconButton(onClick = { showPwd = !showPwd }) {
                        if (showPwd) Icon(Icons.Default.VisibilityOff, contentDescription = "Parolayı gizle")
                        else         Icon(Icons.Default.Visibility, contentDescription = "Parolayı göster")
                    }
                },
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(24.dp))

            Button(
                onClick = {
                    scope.launch {
                        loading = true
                        repo.login(email.trim(), password, selectedCompanyId).fold(
                            onSuccess = { onLoggedIn() },
                            onFailure = { e ->
                                snackbarHostState.showSnackbar("Giriş başarısız: ${e.message ?: "bilinmeyen hata"}")
                            }
                        )
                        loading = false
                    }
                },
                enabled = !loading && email.isNotBlank() && password.isNotBlank() &&
                          (companies.size <= 1 || selectedCompanyId != null),
                modifier = Modifier.fillMaxWidth()
            ) {
                if (loading) CircularProgressIndicator(modifier = Modifier.size(20.dp), color = MaterialTheme.colorScheme.onPrimary)
                else         Text("Giriş yap")
            }

            Spacer(Modifier.height(32.dp))
            TextButton(onClick = { showServerSettings = !showServerSettings }) {
                Text(if (showServerSettings) "Sunucu ayarlarını gizle" else "Sunucu ayarları")
            }

            if (showServerSettings) {
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = baseUrl,
                    onValueChange = { baseUrl = it },
                    label = { Text("Backend URL") },
                    supportingText = {
                        Text("Emulator için: http://10.0.2.2:61001/")
                    },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(8.dp))
                OutlinedButton(
                    onClick = {
                        scope.launch {
                            session.setBaseUrl(baseUrl.trim())
                            snackbarHostState.showSnackbar("Sunucu adresi kaydedildi.")
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                ) { Text("Kaydet") }
            }
        }
    }
}
