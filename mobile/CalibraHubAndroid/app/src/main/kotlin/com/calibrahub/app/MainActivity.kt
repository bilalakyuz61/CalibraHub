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
import com.calibrahub.app.ui.production.WorkOrderDetailScreen
import com.calibrahub.app.ui.production.WorkOrderListScreen
import com.calibrahub.app.ui.theme.CalibraTheme
import com.calibrahub.app.ui.warehouse.CountScreen
import com.calibrahub.app.ui.warehouse.DeliveryDocType
import com.calibrahub.app.ui.warehouse.DeliveryScreen
import com.calibrahub.app.ui.warehouse.DraftCountsScreen
import com.calibrahub.app.ui.warehouse.OpenOrderDetailScreen
import com.calibrahub.app.ui.warehouse.OpenOrderListScreen
import com.calibrahub.app.ui.warehouse.StockDocMode
import com.calibrahub.app.ui.warehouse.StockDocScreen
import com.calibrahub.app.ui.warehouse.StockQueryScreen
import com.calibrahub.app.ui.warehouse.TransferScreen
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
                onOpenStockIn    = { navController.navigate("warehouse_stock_in") },
                onOpenStockOut   = { navController.navigate("warehouse_stock_out") },
                onOpenDeliveryPurchase = { navController.navigate("warehouse_delivery/purchase") },
                onOpenDeliverySales    = { navController.navigate("warehouse_delivery/sales") },
                onOpenTransfer   = { navController.navigate("warehouse_transfer") },
                onOpenCount      = { navController.navigate("warehouse_count") },
                onOpenOpenOrdersSales    = { navController.navigate("warehouse_open_orders/sales") },
                onOpenOpenOrdersPurchase = { navController.navigate("warehouse_open_orders/purchase") },
                onOpenDraftCounts        = { navController.navigate("warehouse_draft_counts") },
                onBack = { navController.popBackStack() }
            )
        }
        composable("warehouse_stock_query") {
            StockQueryScreen(onBack = { navController.popBackStack() })
        }
        composable("warehouse_stock_in") {
            StockDocScreen(mode = StockDocMode.IN, onBack = { navController.popBackStack() })
        }
        composable("warehouse_stock_out") {
            StockDocScreen(mode = StockDocMode.OUT, onBack = { navController.popBackStack() })
        }
        // docType path segmenti "purchase"|"sales" — DeliveryScreen'in tek composable'ına
        // WarehouseHomeScreen'deki iki ayrı karttan navigate edilir (StockDocMode'un aynı deseni).
        composable("warehouse_delivery/{docType}") { entry ->
            val docTypeArg = entry.arguments?.getString("docType")
            val docType = if (docTypeArg == "sales") DeliveryDocType.SALES else DeliveryDocType.PURCHASE
            DeliveryScreen(docType = docType, onBack = { navController.popBackStack() })
        }
        composable("warehouse_transfer") {
            TransferScreen(onBack = { navController.popBackStack() })
        }
        composable("warehouse_count") {
            CountScreen(onBack = { navController.popBackStack() })
        }
        // FAZ C(a) — Açık Siparişler (2026-07-17). docType path segmenti "purchase"|"sales" AYNI
        // DeliveryScreen deseni; detay route'u docType'ı da taşır (OpenOrderDetailDto sözleşmesinde
        // docType YOK, bkz. OpenOrderDetailScreen üstü KDoc).
        composable("warehouse_open_orders/{docType}") { entry ->
            val docTypeArg = entry.arguments?.getString("docType")
            val docType = if (docTypeArg == "sales") DeliveryDocType.SALES else DeliveryDocType.PURCHASE
            OpenOrderListScreen(
                docType = docType,
                onOpenDetail = { id -> navController.navigate("warehouse_open_order_detail/${docType.apiValue}/$id") },
                onBack = { navController.popBackStack() }
            )
        }
        composable("warehouse_open_order_detail/{docType}/{id}") { entry ->
            val docTypeArg = entry.arguments?.getString("docType")
            val docType = if (docTypeArg == "sales") DeliveryDocType.SALES else DeliveryDocType.PURCHASE
            val id = entry.arguments?.getString("id")?.toIntOrNull() ?: 0
            OpenOrderDetailScreen(
                orderId = id,
                docType = docType,
                onBack = { navController.popBackStack() }
            )
        }
        // FAZ C(b) — Taslak Sayımlar (2026-07-17).
        composable("warehouse_draft_counts") {
            DraftCountsScreen(onBack = { navController.popBackStack() })
        }
        composable("production_home") {
            ProductionHomeScreen(
                onOpenWorkOrders = { navController.navigate("production_work_orders") },
                onBack = { navController.popBackStack() }
            )
        }
        composable("production_work_orders") {
            WorkOrderListScreen(
                onOpenDetail = { id -> navController.navigate("production_work_order_detail/$id") },
                onBack = { navController.popBackStack() }
            )
        }
        composable("production_work_order_detail/{id}") { entry ->
            val id = entry.arguments?.getString("id")?.toIntOrNull() ?: 0
            WorkOrderDetailScreen(
                workOrderId = id,
                onBack = { navController.popBackStack() }
            )
        }
    }
}
