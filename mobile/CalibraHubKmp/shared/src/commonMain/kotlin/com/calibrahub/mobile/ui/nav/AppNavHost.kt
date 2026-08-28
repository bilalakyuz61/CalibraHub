package com.calibrahub.mobile.ui.nav

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.rememberDrawerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.withTimeoutOrNull
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.calibrahub.mobile.session.SessionManager
import com.calibrahub.mobile.session.SessionProbeResult
import com.calibrahub.mobile.ui.approval.PendingApprovalScreen
import com.calibrahub.mobile.ui.common.CalibrationIndicator
import com.calibrahub.mobile.ui.contact.ContactCardScreen
import com.calibrahub.mobile.ui.purchase.PurchaseRequestScreen
import com.calibrahub.mobile.ui.quality.InspectionScreen
import com.calibrahub.mobile.ui.warehouse.SavedDocsScreen
import com.calibrahub.mobile.ui.common.PlaceholderScreen
import com.calibrahub.mobile.ui.home.HomeScreen
import com.calibrahub.mobile.ui.login.LoginScreen
import com.calibrahub.mobile.ui.production.WorkOrderDetailScreen
import com.calibrahub.mobile.ui.production.WorkOrderListScreen
import com.calibrahub.mobile.ui.settings.SettingsScreen
import com.calibrahub.mobile.ui.warehouse.CountScreen
import com.calibrahub.mobile.ui.warehouse.DeliveryDocType
import com.calibrahub.mobile.ui.warehouse.DeliveryScreen
import com.calibrahub.mobile.ui.warehouse.DraftCountsScreen
import com.calibrahub.mobile.ui.warehouse.OpenOrderDetailScreen
import com.calibrahub.mobile.ui.warehouse.OpenOrderListScreen
import com.calibrahub.mobile.ui.warehouse.StockDocMode
import com.calibrahub.mobile.ui.warehouse.StockDocScreen
import com.calibrahub.mobile.ui.warehouse.StockQueryScreen
import com.calibrahub.mobile.ui.warehouse.TransferScreen
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Sol menü (navigation drawer) + tüm NavHost — CalibraHubAndroid `MainActivity.AppNav` ile
 * BİREBİR aynı akış/karar (sadik port), navigation-compose Multiplatform (bkz. gorev talimati
 * NAVİGASYON KARARI) üzerine kurulu. TEK [ModalNavigationDrawer] instance'ı NavHost'un TAMAMINI
 * sarar (LoginScreen dahil); [gesturesEnabled] ve hamburger giriş noktası yalnız
 * [drawerTopLevelRoutes] route'larında aktiftir.
 *
 * "Beni hatırla" otomatik giriş — İKİ koşul birden sağlanmalı: (1) kullanıcı "beni hatırla"yı
 * AÇIK bırakmış ([SessionManager.isRememberMeEnabled]), (2) kalıcı çerez var
 * ([SessionManager.hasStoredCookies]). İKİSİ de DEĞİLSE GET /api/mobile/session hiç ÇAĞRILMADAN
 * doğrudan login'e düşülür. İKİSİ de DOĞRUYSA sunucudan sor ([SessionManager.probeSession]):
 * 200 → oturum bilgisi restore + home; 401/hata → login.
 *
 * Faz 2b GÜNCELLEMESİ (2026-08-04): Depo (Stok Sorgu/Giriş/Çıkış/Transfer/Sayım/Taslak Sayımlar/
 * Alış+Satış İrsaliyesi/Açık Alış+Satış Siparişleri+Detay) tüm rotaları artık GERÇEK ekranlara
 * bağlıdır (bkz. `com.calibrahub.mobile.ui.warehouse` paketi).
 *
 * Faz 2c GÜNCELLEMESİ (2026-08-04): Üretim (İş Emirleri listesi + Detay, PIN operatör auth ile
 * operasyon başlat/tamamla) artık GERÇEK ekranlara bağlıdır (bkz.
 * `com.calibrahub.mobile.ui.production` paketi). Sohbetler (WhatsApp) grubu bu app'in kapsamından
 * TAMAMEN ÇIKARILDI (kullanıcı kararı, 2026-08-04) — drawer'da ve NavHost'ta karşılığı yoktur.
 */
@Composable
fun AppNavHost(session: SessionManager) {
    val navController = rememberNavController()
    val drawerState = rememberDrawerState(DrawerValue.Closed)
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    var startRoute by remember { mutableStateOf<String?>(null) }

    // Acik Siparisler -> Detay akisi icin hoisted id tasiyici — bkz. AppRoutes.PURCHASE_OPEN_ORDER_DETAIL/
    // SALES_OPEN_ORDER_DETAIL KDoc'u (literal route + Int state tercihinin gerekcesi).
    var pendingOrderId by rememberSaveable { mutableStateOf<Int?>(null) }

    // Is Emirleri -> Detay akisi icin hoisted id tasiyici — AYNI desen, bkz.
    // AppRoutes.PRODUCTION_WORK_ORDER_DETAIL KDoc'u (Faz 2c EK).
    var pendingWorkOrderId by rememberSaveable { mutableStateOf<Int?>(null) }

    LaunchedEffect(Unit) {
        val canAutoLogin = session.isRememberMeEnabled() && session.hasStoredCookies()
        startRoute = if (!canAutoLogin) {
            "login"
        } else {
            // ACILIS PROBE'U ICIN KISA TAVAN (3,5 sn): HttpClient'in genel zaman asimi
            // 20 sn istek / 15 sn baglanti (bkz. HttpClientFactory) — normal ekranlarda
            // dogru, ama ACILISTA sunucuya ulasilamiyorsa (yanlis IP, VPN, kapali sunucu)
            // kullanici 15-20 sn "Kalibre ediliyor..." ekranina bakiyordu. Tavan asilirsa
            // login'e dusulur; kalici cerez SILINMEZ (probe Error dalindaki ayni karar),
            // yani gecici bir ag sorunu "beni hatirla" tercihini bozmaz.
            val probe = withTimeoutOrNull(AUTO_LOGIN_PROBE_TIMEOUT_MS) { session.probeSession() }
                ?: SessionProbeResult.Error("zaman asimi")
            when (probe) {
                is SessionProbeResult.Success -> {
                    val dto = probe.dto
                    session.persistSessionDisplay(
                        email = dto.email,
                        displayName = dto.displayName,
                        companyId = dto.companyId,
                        companyName = dto.companyName,
                    )
                    AppRoutes.HOME
                }
                is SessionProbeResult.Unauthorized -> {
                    // Kimlik yok/expired — authExpiryInterceptor (SessionManager) kalıcı çerezi
                    // zaten temizledi; burada YİNE de çağırmak (idempotent) bu fonksiyonu tek
                    // başına da doğru kılar.
                    session.clearSession()
                    "login"
                }
                is SessionProbeResult.Error -> {
                    // Network/diğer hata — GEÇİCİ olabilir, kalıcı çerez SİLİNMEZ; bu
                    // çalıştırmada yalnız login'e düşülür.
                    "login"
                }
            }
        }
    }

    if (startRoute == null) {
        Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                CalibrationIndicator()
                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    text = "Kalibre ediliyor…",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        return
    }

    // navigation-compose Multiplatform'da NavGraph.id (Android'in int resource-id kokenli
    // kimligi) yerine ROTA-tabanli popUpTo kullanilir (bkz. gorev talimati NAVİGASYON KARARI —
    // org.jetbrains.androidx.navigation route-first tasarim). [startRoute] NavHost'un
    // startDestination'i olarak zaten sabit/bilinen tek string oldugundan (bu noktada non-null,
    // yukarida erken-return ile garanti edilir) "tum back stack'i temizle" ayni GEREKÇEYLE
    // bununla ifade edilir — CalibraHubAndroid `navController.graph.id` ile AYNI etki (grafiğin
    // KOKUNE kadar inclusive pop).
    val clearStackToLogin: () -> Unit = {
        navController.navigate("login") {
            popUpTo(startRoute!!) { inclusive = true }
        }
    }

    val navBackStackEntry by navController.currentBackStackEntryAsState()
    val currentRoute = navBackStackEntry?.destination?.route

    LaunchedEffect(Unit) {
        session.sessionExpiredEvents.collect {
            if (currentRoute != "login") clearStackToLogin()
        }
    }

    var displayName by remember { mutableStateOf<String?>(null) }
    var companyName by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(currentRoute) {
        if (currentRoute == AppRoutes.HOME) {
            displayName = session.currentDisplayName()
            companyName = session.currentCompanyName()
        }
    }

    val topLevelRoutes = drawerTopLevelRoutes

    // Drawer / Ana Sayfa modül ızgarası → yaprak ekran navigasyonu. HOME'u dipte TUTAR
    // (popUpTo(HOME) non-inclusive) → her yaprak ekrandan geri tuşu ANA SAYFA'ya döner, ana
    // sayfadan geri uygulamadan çıkar (beklenen Android davranışı).
    //
    // DİKKAT — `saveState=true`/`restoreState=true` BİLİNÇLİ olarak KULLANILMIYOR: o "bottom-nav
    // çoklu back-stack" deseni, top-level destination'ların GRAPH START'ının doğrudan çocukları
    // olduğu senaryo içindir. Burada graph start'ı dinamik ("login" ya da HOME) ve login-sonrası
    // pop'lanıyor; bu kombinasyonda org.jetbrains.androidx.navigation (multiplatform) saveState'i
    // HOME'u aktif geri-yığınında tutmuyordu → yaprak ekrandan geri tuşu HOME yerine uygulamadan
    // ÇIKIYORDU (2026-08 cihaz tekrar-üretimi). Sade popUpTo bu hatayı ortadan kaldırır.
    val navigateToTopLevel: (String) -> Unit = { route ->
        scope.launch { drawerState.close() }
        if (route != currentRoute) {
            navController.navigate(route) {
                popUpTo(AppRoutes.HOME)
                launchSingleTop = true
            }
        }
    }

    ModalNavigationDrawer(
        drawerState = drawerState,
        gesturesEnabled = currentRoute != null && currentRoute in topLevelRoutes,
        drawerContent = {
            AppDrawerContent(
                session = session,
                currentRoute = currentRoute,
                displayName = displayName,
                companyName = companyName,
                onNavigate = navigateToTopLevel,
                onLogout = {
                    scope.launch { drawerState.close() }
                    scope.launch {
                        // session.logout() basarisiz (network) olsa bile yerel oturumu KESIN
                        // temizle — kullanici acikca "Cikis" dedi, sunucuya ulasilamamasi yerel
                        // cookie/goruntu alanlarinin kalmasini haklı çıkarmaz.
                        session.logout()
                        session.clearSession()
                        clearStackToLogin()
                    }
                },
            )
        },
    ) {
        Box(modifier = Modifier.fillMaxSize()) {
        NavHost(navController = navController, startDestination = startRoute!!) {
            composable("login") {
                LoginScreen(
                    sessionManager = session,
                    onLoggedIn = {
                        navController.navigate(AppRoutes.HOME) {
                            popUpTo("login") { inclusive = true }
                        }
                    },
                )
            }
            composable(AppRoutes.HOME) {
                // Ana sayfada çift-geri ile çıkış: ilk geri uyarı gösterir + ~2sn "silahlanır";
                // bu pencerede ikinci geri BackHandler'ı DEVRE DIŞI bulur → sistem geri'sine düşer
                // (NavHost kök ekranda bir şey pop'lamaz) → uygulama çıkar. Kaza sonucu çıkışı önler.
                // (iOS'ta kök ekranda geri-jesti zaten no-op; bu mantık zararsızdır.)
                var backArmed by remember { mutableStateOf(false) }
                PlatformBackHandler(enabled = !backArmed) {
                    backArmed = true
                    scope.launch { snackbarHostState.showSnackbar("Çıkmak için tekrar geri'ye basın") }
                    scope.launch {
                        delay(2000)
                        backArmed = false
                    }
                }
                HomeScreen(
                    session = session,
                    displayName = displayName,
                    onOpenDrawer = { scope.launch { drawerState.open() } },
                    onOpenSettings = { navController.navigate(AppRoutes.SETTINGS) { launchSingleTop = true } },
                    onNavigate = navigateToTopLevel,
                )
            }
            composable(AppRoutes.SETTINGS) {
                SettingsScreen(session = session, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.WAREHOUSE_STOCK_QUERY) {
                StockQueryScreen(session = session, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.WAREHOUSE_STOCK_IN) {
                StockDocScreen(session = session, mode = StockDocMode.IN, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.WAREHOUSE_STOCK_OUT) {
                StockDocScreen(session = session, mode = StockDocMode.OUT, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.WAREHOUSE_TRANSFER) {
                TransferScreen(session = session, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.WAREHOUSE_COUNT) {
                CountScreen(session = session, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.WAREHOUSE_DRAFT_COUNTS) {
                DraftCountsScreen(session = session, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.SAVED_DOCS) {
                SavedDocsScreen(session = session, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.QUALITY_INSPECTION) {
                InspectionScreen(
                    session = session,
                    // Menuden acilista varsayilan MAL KABUL: sahada en sik yapilan muayene.
                    // Belgeden acilan akislar kendi tipini ve kaynagini gecirir.
                    inspectionType = com.calibrahub.mobile.data.InspectionTypes.INCOMING,
                    sourceKind = null,
                    sourceId = null,
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppRoutes.PURCHASE_REQUESTS) {
                PurchaseRequestScreen(
                    session = session,
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppRoutes.CONTACT_CARD) {
                ContactCardScreen(
                    session = session,
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppRoutes.APPROVALS) {
                PendingApprovalScreen(
                    session = session,
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppRoutes.PRODUCTION_WORK_ORDERS) {
                WorkOrderListScreen(
                    session = session,
                    onOpenDetail = { id ->
                        pendingWorkOrderId = id
                        navController.navigate(AppRoutes.PRODUCTION_WORK_ORDER_DETAIL) { launchSingleTop = true }
                    },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppRoutes.PRODUCTION_WORK_ORDER_DETAIL) {
                val id = pendingWorkOrderId
                if (id != null) {
                    WorkOrderDetailScreen(
                        session = session,
                        workOrderId = id,
                        onBack = { navController.popBackStack() },
                    )
                } else {
                    // Savunma — id kayboldu (ör. process-death sonrasi derin deep-link, cok nadir):
                    // listeye geri don, cokme.
                    PlaceholderScreen(
                        "İş Emri Detayı",
                        "İş emri bulunamadı, listeden tekrar seçin.",
                        onBack = { navController.popBackStack() },
                    )
                }
            }
            composable(AppRoutes.PURCHASE_DELIVERY) {
                DeliveryScreen(session = session, docType = DeliveryDocType.PURCHASE, onBack = { navController.popBackStack() })
            }
            composable(AppRoutes.PURCHASE_OPEN_ORDERS) {
                OpenOrderListScreen(
                    session = session,
                    docType = DeliveryDocType.PURCHASE,
                    onOpenDetail = { id ->
                        pendingOrderId = id
                        navController.navigate(AppRoutes.PURCHASE_OPEN_ORDER_DETAIL) { launchSingleTop = true }
                    },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppRoutes.PURCHASE_OPEN_ORDER_DETAIL) {
                val id = pendingOrderId
                if (id != null) {
                    OpenOrderDetailScreen(
                        session = session,
                        orderId = id,
                        docType = DeliveryDocType.PURCHASE,
                        onBack = { navController.popBackStack() },
                    )
                } else {
                    // Savunma — id kayboldu (ör. process-death sonrasi derin deep-link, cok nadir):
                    // listeye geri don, cokme.
                    PlaceholderScreen(
                        "Sipariş Detayı",
                        "Sipariş bulunamadı, listeden tekrar seçin.",
                        onBack = { navController.popBackStack() },
                    )
                }
            }
            composable(AppRoutes.SALES_DELIVERY) {
                DeliveryScreen(session = session, docType = DeliveryDocType.SALES, onBack = { navController.popBackStack() })
            }
            // NOT: AppRoutes.SHIPPING_OPEN_ORDERS == AppRoutes.SALES_OPEN_ORDERS (BİLİNÇLİ kısmi
            // örtüşme, bkz. AppDrawer.kt KDoc'u) — AYNI route string'i için İKİNCİ bir composable(...)
            // kaydı NavGraph'ta "duplicate destination" hatası verir; bu yüzden TEK kayıt yeterli.
            composable(AppRoutes.SALES_OPEN_ORDERS) {
                OpenOrderListScreen(
                    session = session,
                    docType = DeliveryDocType.SALES,
                    onOpenDetail = { id ->
                        pendingOrderId = id
                        navController.navigate(AppRoutes.SALES_OPEN_ORDER_DETAIL) { launchSingleTop = true }
                    },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(AppRoutes.SALES_OPEN_ORDER_DETAIL) {
                val id = pendingOrderId
                if (id != null) {
                    OpenOrderDetailScreen(
                        session = session,
                        orderId = id,
                        docType = DeliveryDocType.SALES,
                        onBack = { navController.popBackStack() },
                    )
                } else {
                    PlaceholderScreen(
                        "Sipariş Detayı",
                        "Sipariş bulunamadı, listeden tekrar seçin.",
                        onBack = { navController.popBackStack() },
                    )
                }
            }
        }
            SnackbarHost(
                hostState = snackbarHostState,
                modifier = Modifier.align(Alignment.BottomCenter),
            )
        }
    }
}

/**
 * Acilis oturum probe'u icin tavan sure. HttpClient'in genel zaman asimlarindan (20 sn istek /
 * 15 sn baglanti) BILINCLI olarak cok daha kisa: acilista amac "oturum hala gecerli mi" sorusuna
 * HIZLI yanit almak; ulasilamayan sunucu icin dogru davranis uzun beklemek degil, login ekranina
 * dusup kullaniciya kontrol vermektir (sunucu adresini oradan duzeltebilir).
 */
private const val AUTO_LOGIN_PROBE_TIMEOUT_MS = 3_500L
