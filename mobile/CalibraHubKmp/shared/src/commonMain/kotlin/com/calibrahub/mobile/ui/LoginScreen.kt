package com.calibrahub.mobile.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ListItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.data.ApiResult
import com.calibrahub.mobile.data.CalibraApiClient
import com.calibrahub.mobile.data.CompanyDto
import io.ktor.client.HttpClient
import kotlinx.coroutines.launch

@Composable
fun LoginScreen(
    initialBaseUrl: String,
    httpClient: HttpClient,
    onLoginSuccess: (baseUrl: String, client: CalibraApiClient, displayName: String?) -> Unit,
) {
    var baseUrl by remember { mutableStateOf(initialBaseUrl) }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var isLoading by remember { mutableStateOf(false) }
    var errorMessage by remember { mutableStateOf<String?>(null) }
    var companies by remember { mutableStateOf<List<CompanyDto>>(emptyList()) }

    val scope = rememberCoroutineScope()

    fun doLogin(companyId: Int?) {
        isLoading = true
        errorMessage = null
        scope.launch {
            val apiClient = CalibraApiClient(baseUrl = baseUrl, client = httpClient)
            when (val result = apiClient.login(email = email, password = password, companyId = companyId)) {
                is ApiResult.Success -> {
                    isLoading = false
                    val body = result.data
                    if (body.ok) {
                        onLoginSuccess(baseUrl, apiClient, body.displayName)
                    } else {
                        errorMessage = body.error ?: "Hatali giris / erisim yok"
                    }
                }
                is ApiResult.Failure -> {
                    isLoading = false
                    errorMessage = result.message
                }
            }
        }
    }

    fun onGirisClick() {
        isLoading = true
        errorMessage = null
        companies = emptyList()
        scope.launch {
            val apiClient = CalibraApiClient(baseUrl = baseUrl, client = httpClient)
            when (val result = apiClient.loginCompanies(email = email, password = password)) {
                is ApiResult.Success -> {
                    isLoading = false
                    val list = result.data
                    when {
                        list.isEmpty() -> errorMessage = "Hatali giris / erisim yok"
                        list.size == 1 -> doLogin(list.first().id)
                        else -> companies = list
                    }
                }
                is ApiResult.Failure -> {
                    isLoading = false
                    errorMessage = result.message
                }
            }
        }
    }

    Column(modifier = Modifier.fillMaxSize().padding(24.dp)) {
        Text("CalibraHub KMP POC — Giris")
        Spacer(modifier = Modifier.height(16.dp))
        OutlinedTextField(
            value = baseUrl,
            onValueChange = { baseUrl = it },
            label = { Text("Sunucu adresi (Base URL)") },
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedTextField(
            value = email,
            onValueChange = { email = it },
            label = { Text("E-posta") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedTextField(
            value = password,
            onValueChange = { password = it },
            label = { Text("Sifre") },
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(16.dp))
        Button(
            onClick = { onGirisClick() },
            enabled = !isLoading && email.isNotBlank() && password.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("Giris")
        }
        Spacer(modifier = Modifier.height(16.dp))

        if (isLoading) {
            Row(horizontalArrangement = Arrangement.Center, modifier = Modifier.fillMaxWidth()) {
                CircularProgressIndicator()
            }
        }

        errorMessage?.let {
            Text(text = it)
        }

        if (companies.isNotEmpty()) {
            Text("Birden fazla sirket bulundu, secin:")
            LazyColumn(modifier = Modifier.fillMaxWidth()) {
                items(companies) { company ->
                    ListItem(
                        headlineContent = { Text(company.name) },
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Button(
                        onClick = { doLogin(company.id) },
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Text("Sec: ${company.name}")
                    }
                }
            }
        }
    }
}
