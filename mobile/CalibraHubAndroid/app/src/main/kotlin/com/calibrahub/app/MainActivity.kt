package com.calibrahub.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.Surface
import androidx.compose.material3.rememberDrawerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.calibrahub.app.ui.chat.ChatDetailScreen
import com.calibrahub.app.ui.chat.ChatListScreen
import com.calibrahub.app.ui.home.HomeScreen
import com.calibrahub.app.ui.login.LoginScreen
import com.calibrahub.app.ui.nav.AppDrawerContent
import com.calibrahub.app.ui.nav.AppRoutes
import com.calibrahub.app.ui.production.ProductionHomeScreen
import com.calibrahub.app.ui.production.WorkOrderDetailScreen
import com.calibrahub.app.ui.production.WorkOrderListScreen
import com.calibrahub.app.ui.purchase.PurchaseHomeScreen
import com.calibrahub.app.ui.sales.SalesHomeScreen
import com.calibrahub.app.ui.shipping.ShippingHomeScreen
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
import kotlinx.coroutines.launch

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

/**
 * Sol menü (navigation drawer) + tüm NavHost — 2026-07-17 sol menü migration'ı. TEK
 * [ModalNavigationDrawer] instance'ı burada kurulur ve NavHost'un TAMAMINI sarar (LoginScreen
 * dahil) — ama [gesturesEnabled] (edge-swipe) ve hamburger giriş noktası yalnız 7 kök modül
 * route'unda aktif olduğu için LoginScreen/alt ekranlarda görsel/işlevsel HİÇBİR fark yok
 * (drawerState hep Closed kalır, kimse açmaz).
 */
@Composable
private fun AppNav() {
    val navController = rememberNavController()
    val drawerState = rememberDrawerState(DrawerValue.Closed)
    val scope = rememberCoroutineScope()

    // İlk açılışta whoAmI() çağırıp cookie hala geçerli mi diye bak.
    // Geçerliyse direkt modül seçici (home) ekranına atla; değilse login.
    var startRoute by remember { mutableStateOf<String?>(null) }
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val repo = ctx.app.repository
    val session = ctx.app.session

    LaunchedEffect(Unit) {
        startRoute = repo.whoAmI().fold(
            onSuccess = { name -> if (name != null) AppRoutes.HOME else "login" },
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

    val navBackStackEntry by navController.currentBackStackEntryAsState()
    val currentRoute = navBackStackEntry?.destination?.route

    // Drawer başlığında gösterilen kullanıcı adı — SessionManager'da login() sırasında set edilir
    // (bkz. WhatsAppRepository.login). "home" rotasına her girişte tazelenir: hem soğuk açılışta
    // (whoAmI zaten oturumu doğruladı) hem de LoginScreen.onLoggedIn → "home" geçişinde (yeniden
    // login farklı kullanıcı olabilir). AppNav'ın kendi ömrü boyunca TEK SEFER whoAmI() çağıran
    // startRoute LaunchedEffect(Unit)'e bu amaçla güvenilemez (process yeniden başlamadan
    // re-login olursa bayatlar) — bu yüzden ayrı ve route-tetiklemeli tutuluyor.
    var displayName by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(currentRoute) {
        if (currentRoute == AppRoutes.HOME) {
            displayName = session.currentDisplayName()
        }
    }

    // Drawer'dan erişilen 7 kök modül ekranı — hamburger ile açılır, aralarında geçiş back-stack'i
    // şişirmez (popUpTo(HOME) + launchSingleTop + restoreState, standart "bottom navigation"
    // deseni). "home" BİLİNÇLİ SABİT çapa: navController.graph.startDestinationId soğuk açılışta
    // "login" ya da "home" olabilir (startRoute!!, yukarıda) — bu yüzden
    // graph.findStartDestination() burada GÜVENİLMEZ (login sonrası "login" back stack'ten
    // tamamen çıkar; findStartDestination() login'e işaret etmeye devam eder ama popUpTo bir daha
    // hiçbir şeyi etkilemez). "home" ise hem login-sonrası hem soğuk-açılış-zaten-girişli akışının
    // HER İKİSİNDE de daima back stack'te sabit kalan tek ortak nokta.
    val topLevelRoutes = setOf(
        AppRoutes.HOME, AppRoutes.WAREHOUSE_HOME, AppRoutes.PRODUCTION_HOME,
        AppRoutes.PURCHASE_HOME, AppRoutes.SALES_HOME, AppRoutes.SHIPPING_HOME, AppRoutes.CHATS
    )

    val navigateToTopLevel: (String) -> Unit = { route ->
        // Drawer'ı kapatmak navigasyondan BAĞIMSIZ bir coroutine'de — navigate() senkron
        // olduğundan yeni ekran anında altta belirir, drawer kendi animasyonuyla üstünden kayar
        // (standart Compose drawer navigasyon deseni).
        scope.launch { drawerState.close() }
        if (route != currentRoute) {
            navController.navigate(route) {
                popUpTo(AppRoutes.HOME) { saveState = true }
                launchSingleTop = true
                restoreState = true
            }
        }
    }

    ModalNavigationDrawer(
        drawerState = drawerState,
        gesturesEnabled = currentRoute != null && currentRoute in topLevelRoutes,
        drawerContent = {
            AppDrawerContent(
                currentRoute = currentRoute,
                displayName = displayName,
                onNavigate = navigateToTopLevel,
                onLogout = {
                    // Ayrı coroutine: repo.logout() ağ çağrısı yavaş/askıda kalsa bile drawer
                    // hemen kapanır (kullanıcı "donmuş" hissetmesin).
                    scope.launch { drawerState.close() }
                    scope.launch {
                        repo.logout()
                        clearStackToLogin()
                    }
                }
            )
        }
    ) {
        NavHost(navController = navController, startDestination = startRoute!!) {
            composable("login") {
                LoginScreen(
                    onLoggedIn = {
                        navController.navigate(AppRoutes.HOME) {
                            popUpTo("login") { inclusive = true }
                        }
                    }
                )
            }
            composable(AppRoutes.HOME) {
                HomeScreen(
                    displayName = displayName,
                    onOpenDrawer = { scope.launch { drawerState.open() } }
                )
            }
            composable(AppRoutes.CHATS) {
                ChatListScreen(
                    onOpenChat = { phone -> navController.navigate("chat/${phone}") },
                    onOpenDrawer = { scope.launch { drawerState.open() } }
                )
            }
            composable("chat/{phone}") { entry ->
                val phone = entry.arguments?.getString("phone") ?: ""
                ChatDetailScreen(
                    phone = phone,
                    onBack = { navController.popBackStack() }
                )
            }
            composable(AppRoutes.WAREHOUSE_HOME) {
                WarehouseHomeScreen(
                    onOpenStockQuery  = { navController.navigate("warehouse_stock_query") },
                    onOpenStockIn     = { navController.navigate("warehouse_stock_in") },
                    onOpenStockOut    = { navController.navigate("warehouse_stock_out") },
                    onOpenTransfer    = { navController.navigate("warehouse_transfer") },
                    onOpenCount       = { navController.navigate("warehouse_count") },
                    onOpenDraftCounts = { navController.navigate("warehouse_draft_counts") },
                    onOpenDrawer      = { scope.launch { drawerState.open() } }
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
            // 2026-07-17 sol menü migration'ından SONRA PurchaseHomeScreen/SalesHomeScreen'deki
            // AYRI kartlardan navigate edilir (öncesinde WarehouseHomeScreen'deydi) — route
            // string'i ve DeliveryScreen'in kendisi DEĞİŞMEDİ, yalnız giriş noktası taşındı.
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
            // docType YOK, bkz. OpenOrderDetailScreen üstü KDoc). Sol menü migration'ı sonrası bu
            // route PurchaseHomeScreen/SalesHomeScreen/ShippingHomeScreen'den erişilir (WarehouseHome'dan
            // TAŞINDI) — route string'i ve hedef ekran DEĞİŞMEDİ.
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
            // FAZ D — Satın Alma / Satış / Sevkiyat kök modülleri (2026-07-17 sol menü migration'ı).
            composable(AppRoutes.PURCHASE_HOME) {
                PurchaseHomeScreen(
                    onOpenDeliveryPurchase   = { navController.navigate("warehouse_delivery/purchase") },
                    onOpenOpenOrdersPurchase = { navController.navigate("warehouse_open_orders/purchase") },
                    onOpenDrawer = { scope.launch { drawerState.open() } }
                )
            }
            composable(AppRoutes.SALES_HOME) {
                SalesHomeScreen(
                    onOpenDeliverySales   = { navController.navigate("warehouse_delivery/sales") },
                    onOpenOpenOrdersSales = { navController.navigate("warehouse_open_orders/sales") },
                    onOpenDrawer = { scope.launch { drawerState.open() } }
                )
            }
            composable(AppRoutes.SHIPPING_HOME) {
                ShippingHomeScreen(
                    onOpenOpenOrdersSales = { navController.navigate("warehouse_open_orders/sales") },
                    onOpenDrawer = { scope.launch { drawerState.open() } }
                )
            }
            composable(AppRoutes.PRODUCTION_HOME) {
                ProductionHomeScreen(
                    onOpenWorkOrders = { navController.navigate("production_work_orders") },
                    onOpenDrawer = { scope.launch { drawerState.open() } }
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
}
