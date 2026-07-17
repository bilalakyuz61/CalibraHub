package com.calibrahub.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.rememberDrawerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
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
import com.calibrahub.app.ui.nav.drawerTopLevelRoutes
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
 * Sol menü (navigation drawer) + tüm NavHost. TEK [ModalNavigationDrawer] instance'ı burada
 * kurulur ve NavHost'un TAMAMINI sarar (LoginScreen dahil) — ama [gesturesEnabled] (edge-swipe)
 * ve hamburger giriş noktası yalnız [drawerTopLevelRoutes] (Ana Sayfa/Sohbetler yaprakları + 5
 * akordeon grubunun TÜM yaprakları — bkz. AppDrawer.kt) route'larında aktif olduğu için
 * LoginScreen/alt ekranlarda görsel/işlevsel HİÇBİR fark yok (drawerState hep Closed kalır, kimse
 * açmaz).
 *
 * 2026-07-17 akordeon migration: drawer'daki modül başlıkları (Depo/Üretim/Satın Alma/Satış/
 * Sevkiyat) artık ayrı bir "kart menüsü" ekranına navigate ETMEZ — drawer İÇİNDE akordeon gibi
 * açılıp kapanır (bkz. [com.calibrahub.app.ui.nav.AppDrawerContent]); bir yaprağa basılınca drawer
 * kapanır ve doğrudan o form/ekran açılır. Eski *HomeScreen kart ekranları (WarehouseHomeScreen,
 * ProductionHomeScreen, PurchaseHomeScreen, SalesHomeScreen, ShippingHomeScreen) SİLİNDİ.
 */
@Composable
private fun AppNav() {
    val navController = rememberNavController()
    val drawerState = rememberDrawerState(DrawerValue.Closed)
    val scope = rememberCoroutineScope()

    val ctx = androidx.compose.ui.platform.LocalContext.current
    val repo = ctx.app.repository
    val session = ctx.app.session

    // "Beni hatırla" otomatik giriş (2026-07-17) — İKİ koşul birden sağlanmalı: (1) kullanıcı
    // "beni hatırla"yı AÇIK bırakmış, (2) PersistentCookieJar'da DataStore'dan yüklenmiş kalıcı
    // bir oturum çerezi var (yalnız önceki bir "beni hatırla AÇIK" oturumundan gelebilir — bkz.
    // PersistentCookieJar.persistenceEnabled). İKİSİ de DEĞİLSE GET /api/mobile/session'ı hiç
    // ÇAĞIRMADAN doğrudan login'e düşülür (gereksiz ağ çağrısı yok, "kontrol ediliyor" flaşı yok).
    // İKİSİ de DOĞRUYSA sunucudan sor: 200 → yanıttan displayName/şirket restore + home; 401/hata →
    // login (401 ise kalıcı çerez ayrıca temizlenir, bkz. fetchSession + WhatsAppRepository KDoc).
    var startRoute by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(Unit) {
        val canAutoLogin = session.isRememberMeEnabled() && session.cookieJar.hasStoredCookies()
        startRoute = if (!canAutoLogin) {
            "login"
        } else {
            repo.fetchSession().fold(
                onSuccess = { dto ->
                    if (dto != null) {
                        session.persistSessionDisplay(
                            email = dto.email,
                            displayName = dto.displayName,
                            companyId = dto.companyId,
                            companyName = dto.companyName
                        )
                        AppRoutes.HOME
                    } else {
                        // 401 — kimlik yok/expired. authExpiryInterceptor (SessionManager)
                        // kalıcı çerezi zaten temizledi; burada YİNE de çağırmak (idempotent)
                        // bu fonksiyonu tek başına da doğru kılar (interceptor'a bağımlı kalmaz).
                        session.clearSession()
                        "login"
                    }
                },
                onFailure = {
                    // Network/diğer hata — GEÇİCİ olabilir (sunucuya ulaşılamadı vb.), kalıcı
                    // çerez SİLİNMEZ; bu çalıştırmada yalnız login'e düşülür (kullanıcı adı dolu).
                    "login"
                }
            )
        }
    }

    if (startRoute == null) {
        // İlk auth check sürerken kısa bir "kontrol ediliyor" durumu — login'i gösterip hemen
        // home'a zıplatma flash'ı OLMASIN diye (bkz. yukarıdaki LaunchedEffect).
        Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                CircularProgressIndicator()
                Spacer(modifier = Modifier.height(12.dp))
                Text(
                    text = "Kontrol ediliyor…",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
        return
    }

    // Logout: home/chats/warehouse/production her neredeyse, TÜM back stack'i temizleyip
    // login'e döner (graph.id'ye kadar inclusive pop — stale authenticated ekran kalmaz).
    val clearStackToLogin: () -> Unit = {
        navController.navigate("login") {
            popUpTo(navController.graph.id) { inclusive = true }
        }
    }

    val navBackStackEntry by navController.currentBackStackEntryAsState()
    val currentRoute = navBackStackEntry?.destination?.route

    // Herhangi bir authed /api/mobile/* çağrısı 401 dönerse (SessionManager.authExpiryInterceptor)
    // login ekranına yönlendirir. KRİTİK: bu dinleyici yalnız startRoute != null (NavHost en az bir
    // kez composed olacak) olduğu bu noktadan SONRA eklenir — daha erken bir emisyon
    // navController.graph henüz set edilmemişken navigate() çağrısını çökertir (bkz.
    // SessionManager.sessionExpiredEvents KDoc). currentRoute != "login" kontrolü zaten login'deyken
    // gereksiz bir yeniden-navigate'i atlar (zararsız olurdu ama gereksiz).
    LaunchedEffect(Unit) {
        session.sessionExpiredEvents.collect {
            if (currentRoute != "login") clearStackToLogin()
        }
    }

    // Drawer başlığında gösterilen kullanıcı adı + şirket adı — SessionManager'da login()
    // sırasında (bkz. WhatsAppRepository.login + LoginScreen.doLogin) ya da açılış otomatik-giriş
    // restore'unda (yukarıdaki LaunchedEffect) set edilir. "home" rotasına her girişte tazelenir:
    // hem soğuk açılışta hem de LoginScreen.onLoggedIn → "home" geçişinde (yeniden login farklı
    // kullanıcı/şirket olabilir) — startRoute'u çözen LaunchedEffect(Unit)'e bu amaçla
    // güvenilemez (process yeniden başlamadan re-login olursa bayatlar).
    var displayName by remember { mutableStateOf<String?>(null) }
    var companyName by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(currentRoute) {
        if (currentRoute == AppRoutes.HOME) {
            displayName = session.currentDisplayName()
            companyName = session.currentCompanyName()
        }
    }

    // Drawer'dan doğrudan erişilebilen tüm route'lar (Ana Sayfa/Sohbetler yaprakları + 5 akordeon
    // grubunun TÜM yaprakları — bkz. AppDrawer.kt drawerTopLevelRoutes KDoc, tek kaynak). Aralarında
    // geçiş back-stack'i şişirmez (popUpTo(HOME) + launchSingleTop + restoreState, standart "bottom
    // navigation" deseni) ve edge-swipe yalnız bu route'larda aktiftir. "home" BİLİNÇLİ SABİT çapa:
    // navController.graph.startDestinationId soğuk açılışta "login" ya da "home" olabilir
    // (startRoute!!, yukarıda) — bu yüzden graph.findStartDestination() burada GÜVENİLMEZ (login
    // sonrası "login" back stack'ten tamamen çıkar; findStartDestination() login'e işaret etmeye
    // devam eder ama popUpTo bir daha hiçbir şeyi etkilemez). "home" ise hem login-sonrası hem
    // soğuk-açılış-zaten-girişli akışının HER İKİSİNDE de daima back stack'te sabit kalan tek ortak
    // nokta.
    val topLevelRoutes = drawerTopLevelRoutes

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
                companyName = companyName,
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
            // 2026-07-17 akordeon migration'ından SONRA drawer'ın Satın Alma/Satış gruplarındaki
            // AYRI yapraklardan doğrudan navigate edilir (eski PurchaseHomeScreen/SalesHomeScreen
            // kart ekranları SİLİNDİ) — route string'i ve DeliveryScreen'in kendisi DEĞİŞMEDİ.
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
            // docType YOK, bkz. OpenOrderDetailScreen üstü KDoc). Akordeon migration'ı sonrası bu route
            // drawer'ın Satın Alma/Satış/Sevkiyat gruplarındaki yapraklardan doğrudan erişilir (eski
            // *HomeScreen kart ekranları SİLİNDİ) — route string'i ve hedef ekran DEĞİŞMEDİ.
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
