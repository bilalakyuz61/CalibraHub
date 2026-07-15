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
import com.calibrahub.app.ui.home.HomeScreen
import com.calibrahub.app.ui.login.LoginScreen
import com.calibrahub.app.ui.production.ProductionHomeScreen
import com.calibrahub.app.ui.theme.CalibraTheme
import com.calibrahub.app.ui.warehouse.StockQueryScreen
import com.calibrahub.app.ui.warehouse.WarehouseHomeScreen

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
    // Geçerliyse direkt modül seçici (home) ekranına atla; değilse login.
    var startRoute by remember { mutableStateOf<String?>(null) }
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val repo = ctx.app.repository

    LaunchedEffect(Unit) {
        startRoute = repo.whoAmI().fold(
            onSuccess = { name -> if (name != null) "home" else "login" },
            onFailure = { "login" }
        )
    }

    if (startRoute == null) return   // İlk auth check sürerken boş ekran (split second)

    // Logout: home/chats/warehouse/production her neredeyse, TÜM back stack'i temizleyip
    // login'e döner (graph.id'ye kadar inclusive pop — stale authenticated ekran kalmaz).
    val clearStackToLogin: () -> Unit = {
        navController.navigate("login") {
            popUpTo(navController.graph.id) { inclusive = true }
        }
    }

    NavHost(navController = navController, startDestination = startRoute!!) {
        composable("login") {
            LoginScreen(
                onLoggedIn = {
                    navController.navigate("home") {
                        popUpTo("login") { inclusive = true }
                    }
                }
            )
        }
        composable("home") {
            HomeScreen(
                onOpenChats      = { navController.navigate("chats") },
                onOpenWarehouse  = { navController.navigate("warehouse_home") },
                onOpenProduction = { navController.navigate("production_home") },
                onLogout         = clearStackToLogin
            )
        }
        composable("chats") {
            ChatListScreen(
                onOpenChat = { phone -> navController.navigate("chat/${phone}") },
                onLogout   = clearStackToLogin
            )
        }
        composable("chat/{phone}") { entry ->
            val phone = entry.arguments?.getString("phone") ?: ""
            ChatDetailScreen(
                phone = phone,
                onBack = { navController.popBackStack() }
            )
        }
        composable("warehouse_home") {
            WarehouseHomeScreen(
                onOpenStockQuery = { navController.navigate("warehouse_stock_query") },
                onBack = { navController.popBackStack() }
            )
        }
        composable("warehouse_stock_query") {
            StockQueryScreen(onBack = { navController.popBackStack() })
        }
        composable("production_home") {
            ProductionHomeScreen(onBack = { navController.popBackStack() })
        }
    }
}
