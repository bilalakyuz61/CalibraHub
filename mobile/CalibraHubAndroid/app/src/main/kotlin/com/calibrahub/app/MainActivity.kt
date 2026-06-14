package com.calibrahub.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.calibrahub.app.ui.chat.ChatDetailScreen
import com.calibrahub.app.ui.chat.ChatListScreen
import com.calibrahub.app.ui.login.LoginScreen
import com.calibrahub.app.ui.theme.CalibraTheme

class MainActivity : ComponentActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            CalibraTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    AppNav()
                }
            }
        }
    }
}

@Composable
private fun AppNav() {
    val navController = rememberNavController()

    // İlk açılışta whoAmI() çağırıp cookie hala geçerli mi diye bak.
    // Geçerliyse direkt chatler ekranına atla; değilse login.
    var startRoute by remember { mutableStateOf<String?>(null) }
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val repo = ctx.app.repository

    LaunchedEffect(Unit) {
        startRoute = repo.whoAmI().fold(
            onSuccess = { name -> if (name != null) "chats" else "login" },
            onFailure = { "login" }
        )
    }

    if (startRoute == null) return   // İlk auth check sürerken boş ekran (split second)

    NavHost(navController = navController, startDestination = startRoute!!) {
        composable("login") {
            LoginScreen(
                onLoggedIn = {
                    navController.navigate("chats") {
                        popUpTo("login") { inclusive = true }
                    }
                }
            )
        }
        composable("chats") {
            ChatListScreen(
                onOpenChat = { phone -> navController.navigate("chat/${phone}") },
                onLogout   = {
                    navController.navigate("login") {
                        popUpTo("chats") { inclusive = true }
                    }
                }
            )
        }
        composable("chat/{phone}") { entry ->
            val phone = entry.arguments?.getString("phone") ?: ""
            ChatDetailScreen(
                phone = phone,
                onBack = { navController.popBackStack() }
            )
        }
    }
}
