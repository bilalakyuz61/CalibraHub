package com.calibrahub.mobile.ui.nav

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Assignment
import androidx.compose.material.icons.automirrored.filled.FactCheck
import androidx.compose.material.icons.automirrored.filled.ListAlt
import androidx.compose.material.icons.filled.Business
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.ChevronRight
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.material.icons.filled.Logout
import androidx.compose.material.icons.filled.MoveToInbox
import androidx.compose.material.icons.filled.Outbox
import androidx.compose.material.icons.filled.PendingActions
import androidx.compose.material.icons.filled.PrecisionManufacturing
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material.icons.filled.ReceiptLong
import androidx.compose.material.icons.filled.Sell
import androidx.compose.material.icons.filled.ShoppingCart
import androidx.compose.material.icons.filled.SwapHoriz
import androidx.compose.material.icons.filled.TaskAlt
import androidx.compose.material.icons.filled.Warehouse
import androidx.compose.material.icons.outlined.PushPin
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalDrawerSheet
import androidx.compose.material3.NavigationDrawerItem
import androidx.compose.material3.NavigationDrawerItemDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.calibrahub.mobile.session.SessionManager
import kotlinx.coroutines.launch

/**
 * Sol navigasyon menusu (drawer) route sabitleri — CalibraHubAndroid `ui/nav/AppDrawer.kt`
 * `AppRoutes` ile BIREBIR ayni (sadik port). [com.calibrahub.mobile.ui.nav.AppNavHost]'taki
 * NavHost route string'leriyle senkron tutulmasi ZORUNLU — tek kaynak burada TUTULMUYOR,
 * AppNavHost'un composable(...) bloklari bu sabitlerle insa edilir.
 *
 * Faz 2a KAPSAM NOTU: Depo (stok sorgu haric)/Uretim/Satin Alma/Satis/Sevkiyat yapraklarinin
 * hedefi [com.calibrahub.mobile.ui.common.PlaceholderScreen]'dir (Faz 2b/2c'de gercek ekranla
 * degistirilecek) — route string'lerinin KENDISI Android ile AYNI kalir, boylece Faz 2b/2c
 * yalnizca AppNavHost'taki composable(...) hedefini degistirir, route/drawer katmanina DOKUNMAZ.
 */
object AppRoutes {
    const val HOME = "home"

    const val WAREHOUSE_STOCK_QUERY = "warehouse_stock_query"
    const val WAREHOUSE_STOCK_IN = "warehouse_stock_in"
    const val WAREHOUSE_STOCK_OUT = "warehouse_stock_out"
    const val WAREHOUSE_TRANSFER = "warehouse_transfer"
    const val WAREHOUSE_COUNT = "warehouse_count"
    const val WAREHOUSE_DRAFT_COUNTS = "warehouse_draft_counts"

    const val PRODUCTION_WORK_ORDERS = "production_work_orders"

    /** Onay Bekleyenler — bana atanmis onaylar (bkz. PendingApprovalScreen). */
    const val APPROVALS = "approvals"

    /** Cari Karti — arama + salt-okunur detay (bkz. ContactCardScreen). */
    const val CONTACT_CARD = "contact_card"

    /** İhtiyaç Kaydı — talep listesi + yeni talep (bkz. PurchaseRequestScreen). */
    const val PURCHASE_REQUESTS = "purchase_requests"

    // İş emri detayı (Faz 2c EK) — drawer'da YOK, yalnız WorkOrderListScreen'den navigate edilir.
    // AYNI KISS gerekçesiyle PURCHASE/SALES_OPEN_ORDER_DETAIL ile AYNI desen: literal route +
    // AppNavHost'ta hoisted `pendingWorkOrderId` Int state (navigation-compose Multiplatform
    // 2.8.0-alpha10'un path-argument API yüzeyi bu erken alfa sürümde doğrulanmadı — risk).
    // Sonuç: process-death sonrası (çok nadir) id kaybolursa kullanıcı listeye geri düşer.
    const val PRODUCTION_WORK_ORDER_DETAIL = "production_work_order_detail"

    const val PURCHASE_DELIVERY = "warehouse_delivery/purchase"
    const val PURCHASE_OPEN_ORDERS = "warehouse_open_orders/purchase"

    const val SALES_DELIVERY = "warehouse_delivery/sales"
    const val SALES_OPEN_ORDERS = "warehouse_open_orders/sales"

    // Sevkiyat'in tek yapragi Satis'in "Acik Satis Siparisleri" ile AYNI route'a gider — bilincli
    // kismi ortusme (CalibraHubAndroid ile AYNI karar, bkz. orada AppRoutes KDoc'u).
    const val SHIPPING_OPEN_ORDERS = SALES_OPEN_ORDERS

    // Acik siparis detayi (Faz 2b, 2026-08-04 EK) — drawer'da YOK, yalniz OpenOrderListScreen'den
    // navigate edilir. Android path-parametreli tek route ("warehouse_open_order_detail/{docType}/{id}")
    // kullanirken, burada BILINCLI olarak iki AYRI literal route (PURCHASE/SALES icin PURCHASE_OPEN_ORDERS/
    // SALES_OPEN_ORDERS'la AYNI "iki yon = iki route" deseni) + AppNavHost'ta hoisted `pendingOrderId`
    // state'i tercih edildi — navigation-compose Multiplatform 2.8.0-alpha10'un path-argument (NavType/
    // navArgument) API yuzeyi bu erken alfa surumde dogrulanmadi (risk); literal route + basit Int state
    // KISS, derleme riskini sifirlar. Sonuc: process-death sonrasi (cok nadir, derin bir alt-ekran) id
    // kaybolursa kullanici listeye geri duser (bkz. AppNavHost fallback).
    const val PURCHASE_OPEN_ORDER_DETAIL = "warehouse_open_order_detail/purchase"
    const val SALES_OPEN_ORDER_DETAIL = "warehouse_open_order_detail/sales"

    // Ayarlar — TEK giris noktasi HomeScreen ust cubugundaki disli ikonu; drawerEntries'in
    // DISINDA tutulur (bkz. Android AppRoutes.SETTINGS KDoc'u, ayni karar).
    const val SETTINGS = "settings"
}

/**
 * Mobil menude kullanilan sunucu form kodlari — GET /api/mobile/session `permissions`
 * listesindeki degerlerle BIREBIR ayni string'ler (bkz. MobileApiController.MobileMenuFormCodes).
 * Sunucu tarafinda `FormCodes` sabitleridir; burada elle yazilir cunku KMP ortak kodu C#
 * sabitlerini goremez — ikisi degisirse BIRLIKTE guncellenmeli.
 */
object MenuFormCodes {
    const val STOCK_IN = "STOCK_IN"
    const val STOCK_OUT = "STOCK_OUT"
    const val TRANSFER = "TRANSFER"
    const val INVENTORY_COUNT = "INVENTORY_COUNT"
    const val SALES_DELIVERY = "SALES_DELIVERY"
    const val PURCHASE_DELIVERY = "PURCHASE_DELIVERY"
    const val WORK_ORDERS = "WORK_ORDERS"
    const val SHOP_FLOOR = "SHOP_FLOOR"
    const val APPROVAL_PENDING = "APPROVAL_PENDING"
    const val CONTACTS = "CONTACTS"
    const val PURCHASE_REQUEST = "PURCHASE_REQUEST"
}

/**
 * Drawer'da dogrudan navigate edilebilir tek bir yaprak hedef.
 *
 * [formCodes]: bu ekrani gorebilmek icin gereken form kodlarindan HERHANGI biri yeterlidir
 * (endpoint'lerdeki `RequirePermissionAsync(formCodes, ...)` ile ayni "any" semantigi).
 * BOS birakilirsa ekran her zaman gorunur (Ana Sayfa gibi yetkiye bagli olmayan hedefler).
 */
data class DrawerLeaf(
    val route: String,
    val label: String,
    val icon: ImageVector,
    val formCodes: List<String> = emptyList(),
)

/**
 * Bu yaprak verilen izin kumesiyle gorunur mu? [permissions] `null` ise (sunucu izin gondermiyor
 * ya da henuz sorulmadi) HER SEY gorunur — fail-open, bkz. SessionManager.permissions KDoc.
 */
fun DrawerLeaf.isVisibleFor(permissions: Set<String>?): Boolean =
    permissions == null || formCodes.isEmpty() || formCodes.any { it in permissions }

/**
 * Drawer'da akordeon gibi acilip kapanan bir modul basligi. KENDISI navigate ETMEZ — tiklaninca
 * yalniz kendi ac/kapa state'ini toggle eder; gercek navigasyon yalniz [leaves] icindeki bir
 * [DrawerLeaf]'e basildiginda olur.
 */
data class DrawerGroup(
    val key: String,
    val label: String,
    val icon: ImageVector,
    val leaves: List<DrawerLeaf>,
)

/** Drawer'in ust seviye girisi — ya dogrudan bir [DrawerLeaf] (Ana Sayfa) ya da
 * akordeon [DrawerGroup] (Depo, Uretim, Satin Alma, Satis, Sevkiyat). */
sealed class DrawerEntry {
    data class Single(val leaf: DrawerLeaf) : DrawerEntry()
    data class Expandable(val group: DrawerGroup) : DrawerEntry()
}

/**
 * Drawer icerigi — CalibraHubAndroid ile BIREBIR ayni sira/etiket/ikon: Ana Sayfa (yaprak) →
 * Depo/Uretim/Satin Alma/Satis/Sevkiyat (akordeon gruplari).
 */
val drawerEntries: List<DrawerEntry> = listOf(
    DrawerEntry.Single(DrawerLeaf(AppRoutes.HOME, "Ana Sayfa", Icons.Default.Home)),
    DrawerEntry.Single(
        DrawerLeaf(
            AppRoutes.APPROVALS, "Onay Bekleyenler", Icons.Default.TaskAlt,
            listOf(MenuFormCodes.APPROVAL_PENDING),
        ),
    ),
    DrawerEntry.Single(
        DrawerLeaf(
            AppRoutes.CONTACT_CARD, "Cari Kartı", Icons.Default.Business,
            listOf(MenuFormCodes.CONTACTS),
        ),
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "depo",
            label = "Depo",
            icon = Icons.Default.Warehouse,
            leaves = listOf(
                DrawerLeaf(
                    AppRoutes.WAREHOUSE_STOCK_QUERY, "Stok Sorgu", Icons.Default.Inventory2,
                    // Sunucudaki StockQueryFormCodes ile ayni: dort depo ekranindan birine
                    // yetkisi olan stok sorgulayabilir.
                    listOf(
                        MenuFormCodes.STOCK_IN, MenuFormCodes.STOCK_OUT,
                        MenuFormCodes.TRANSFER, MenuFormCodes.INVENTORY_COUNT,
                    ),
                ),
                DrawerLeaf(AppRoutes.WAREHOUSE_STOCK_IN, "Giriş", Icons.Default.MoveToInbox, listOf(MenuFormCodes.STOCK_IN)),
                DrawerLeaf(AppRoutes.WAREHOUSE_STOCK_OUT, "Çıkış", Icons.Default.Outbox, listOf(MenuFormCodes.STOCK_OUT)),
                DrawerLeaf(AppRoutes.WAREHOUSE_TRANSFER, "Transfer", Icons.Default.SwapHoriz, listOf(MenuFormCodes.TRANSFER)),
                DrawerLeaf(AppRoutes.WAREHOUSE_COUNT, "Sayım", Icons.Default.Checklist, listOf(MenuFormCodes.INVENTORY_COUNT)),
                DrawerLeaf(
                    AppRoutes.WAREHOUSE_DRAFT_COUNTS, "Taslak Sayımlar",
                    Icons.AutoMirrored.Filled.FactCheck, listOf(MenuFormCodes.INVENTORY_COUNT),
                ),
            ),
        ),
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "uretim",
            label = "Üretim",
            icon = Icons.Default.PrecisionManufacturing,
            leaves = listOf(
                DrawerLeaf(
                    AppRoutes.PRODUCTION_WORK_ORDERS, "İş Emirleri",
                    Icons.AutoMirrored.Filled.Assignment,
                    // Liste WORK_ORDERS, operasyon basla/bitir SHOP_FLOOR gerektirir —
                    // ikisinden biri yetiyorsa ekran anlamli.
                    listOf(MenuFormCodes.WORK_ORDERS, MenuFormCodes.SHOP_FLOOR),
                ),
            ),
        ),
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "satinalma",
            label = "Satın Alma",
            icon = Icons.Default.ShoppingCart,
            leaves = listOf(
                DrawerLeaf(
                    AppRoutes.PURCHASE_REQUESTS, "İhtiyaç Kaydı", Icons.AutoMirrored.Filled.ListAlt,
                    listOf(MenuFormCodes.PURCHASE_REQUEST),
                ),
                DrawerLeaf(
                    AppRoutes.PURCHASE_DELIVERY, "Alış İrsaliyesi", Icons.Default.LocalShipping,
                    listOf(MenuFormCodes.PURCHASE_DELIVERY),
                ),
                DrawerLeaf(
                    AppRoutes.PURCHASE_OPEN_ORDERS, "Açık Alış Siparişleri",
                    Icons.AutoMirrored.Filled.Assignment, listOf(MenuFormCodes.PURCHASE_DELIVERY),
                ),
            ),
        ),
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "satis",
            label = "Satış",
            icon = Icons.Default.Sell,
            leaves = listOf(
                DrawerLeaf(
                    AppRoutes.SALES_DELIVERY, "Satış İrsaliyesi", Icons.Default.ReceiptLong,
                    listOf(MenuFormCodes.SALES_DELIVERY),
                ),
                DrawerLeaf(
                    AppRoutes.SALES_OPEN_ORDERS, "Açık Satış Siparişleri",
                    Icons.Default.PendingActions, listOf(MenuFormCodes.SALES_DELIVERY),
                ),
            ),
        ),
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "sevkiyat",
            label = "Sevkiyat",
            icon = Icons.Default.LocalShipping,
            leaves = listOf(
                DrawerLeaf(
                    AppRoutes.SHIPPING_OPEN_ORDERS, "Açık Satış Siparişleri (Teslim)",
                    Icons.Default.PendingActions, listOf(MenuFormCodes.SALES_DELIVERY),
                ),
            ),
        ),
    ),
)

/** Drawer'dan DOGRUDAN erisilebilen tum route'lar — edge-swipe + flat navigasyon icin
 * "top-level route" tanimi BURADAN turetilir (tek kaynak). */
val drawerTopLevelRoutes: Set<String> = drawerEntries.flatMap { entry ->
    when (entry) {
        is DrawerEntry.Single -> listOf(entry.leaf.route)
        is DrawerEntry.Expandable -> entry.group.leaves.map { it.route }
    }
}.toSet()

/**
 * [drawerEntries]'ten turetilen, ana ekrana SABITLENEBILIR (pinnable) tum yapraklarin duz (flat)
 * listesi — Ana Sayfa haric tum Single + Expandable-grup yapraklari. CalibraHubAndroid ile
 * BIREBIR ayni `distinctBy` guvenlik onlemi (SHIPPING_OPEN_ORDERS = SALES_OPEN_ORDERS ayni rotayi
 * paylasiyor — dedup olmadan LazyVerticalGrid `key = { it.route }` cakisir).
 */
val pinnableDrawerLeaves: List<DrawerLeaf> = drawerEntries.flatMap { entry ->
    when (entry) {
        is DrawerEntry.Single -> if (entry.leaf.route == AppRoutes.HOME) emptyList() else listOf(entry.leaf)
        is DrawerEntry.Expandable -> entry.group.leaves
    }
}.distinctBy { it.route }

/** Yaprak route'u → UST GRUP etiketi ("Çıkış" → "Depo") eslemesi — HomeScreen kisayol
 * kutucugunda ust satirda kucuk punto ile gosterilir. */
/**
 * [drawerEntries]'in izne gore suzulmus hali — yetkisi olmayan yapraklar cikarilir, hicbir
 * yapragi kalmayan gruplar tamamen gizlenir. [permissions] `null` ise liste OLDUGU GIBI doner
 * (fail-open; bkz. [DrawerLeaf.isVisibleFor]).
 *
 * Bu bir GORUNURLUK suzgecidir, guvenlik siniri DEGIL: gizlenen bir ekrana route ile yine de
 * gidilse sunucu 403 doner. Amac "kapali kapiya goturen buton" gostermemek.
 */
fun visibleDrawerEntries(permissions: Set<String>?): List<DrawerEntry> {
    if (permissions == null) return drawerEntries
    return drawerEntries.mapNotNull { entry ->
        when (entry) {
            is DrawerEntry.Single ->
                if (entry.leaf.isVisibleFor(permissions)) entry else null
            is DrawerEntry.Expandable -> {
                val leaves = entry.group.leaves.filter { it.isVisibleFor(permissions) }
                if (leaves.isEmpty()) null
                else DrawerEntry.Expandable(entry.group.copy(leaves = leaves))
            }
        }
    }
}

val leafGroupLabels: Map<String, String> = drawerEntries.flatMap { entry ->
    when (entry) {
        is DrawerEntry.Single -> emptyList()
        is DrawerEntry.Expandable -> entry.group.leaves.map { it.route to entry.group.label }
    }
}.groupBy({ it.first }, { it.second }).mapValues { (_, labels) -> labels.first() }

/**
 * Sol navigasyon menusunun icerigi — CalibraHubAndroid `AppDrawerContent` ile ayni yapi/davranis
 * (sadik port), tek fark: [session] LocalContext üzerinden degil PARAMETRE olarak alinir (KMP'de
 * Android Context/Application-scoped singleton yok — bkz. AppNavHost'un SessionManager'i olusturma
 * deseni, App.kt POC'undan devralinan state-hoisting yaklasimi).
 *
 * BILINCLI SADELESTIRME: Android surumundeki logo (`R.drawable.calibrahub_logo`, PNG kaynak) yerine
 * basit bir marka rozeti (yuvarlak, birincil renk + "CH" harfleri) kullanilir — commonMain'de
 * multiplatform kaynak (compose.components.resources) altyapisi bu Faz'da KURULMADI (YAGNI,
 * Faz 2a kapsami disinda); ihtiyac dogarsa ayri bir is olarak eklenir.
 */
@Composable
fun AppDrawerContent(
    session: SessionManager,
    currentRoute: String?,
    displayName: String?,
    companyName: String? = null,
    onNavigate: (String) -> Unit,
    onLogout: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    val pinnedRoutes by session.pinnedRoutes.collectAsState()
    val permissions by session.permissions.collectAsState()
    val entries = remember(permissions) { visibleDrawerEntries(permissions) }
    val onTogglePin: (String) -> Unit = { route -> scope.launch { session.togglePinnedRoute(route) } }

    ModalDrawerSheet {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp, vertical = 20.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(40.dp)
                    .clip(CircleShape)
                    .background(MaterialTheme.colorScheme.primary),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = "CH",
                    color = MaterialTheme.colorScheme.onPrimary,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.Bold,
                )
            }
            Spacer(Modifier.width(12.dp))
            Column {
                Text(
                    text = "CalibraHub",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold,
                )
                if (!displayName.isNullOrBlank()) {
                    Text(
                        text = displayName,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                if (!companyName.isNullOrBlank()) {
                    Text(
                        text = companyName,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }
        HorizontalDivider()
        Spacer(Modifier.height(8.dp))

        Column(
            modifier = Modifier
                .weight(1f)
                .verticalScroll(rememberScrollState()),
        ) {
            entries.forEach { entry ->
                when (entry) {
                    is DrawerEntry.Single -> {
                        val pinnable = entry.leaf.route != AppRoutes.HOME
                        val pinBadge: (@Composable () -> Unit)? = if (!pinnable) null else {
                            {
                                PinToggleButton(
                                    pinned = entry.leaf.route in pinnedRoutes,
                                    onClick = { onTogglePin(entry.leaf.route) },
                                )
                            }
                        }
                        NavigationDrawerItem(
                            label = { Text(entry.leaf.label) },
                            selected = currentRoute == entry.leaf.route,
                            icon = { Icon(entry.leaf.icon, contentDescription = null) },
                            badge = pinBadge,
                            onClick = { onNavigate(entry.leaf.route) },
                            modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding),
                        )
                    }
                    is DrawerEntry.Expandable -> {
                        DrawerGroupSection(
                            group = entry.group,
                            currentRoute = currentRoute,
                            pinnedRoutes = pinnedRoutes,
                            onNavigate = onNavigate,
                            onTogglePin = onTogglePin,
                        )
                    }
                }
            }
        }

        HorizontalDivider()
        Text(
            text = "CalibraHub Mobile",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(horizontal = 20.dp, vertical = 8.dp),
        )
        NavigationDrawerItem(
            label = { Text("Çıkış") },
            selected = false,
            icon = { Icon(Icons.Default.Logout, contentDescription = null) },
            onClick = onLogout,
            modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding),
        )
        Spacer(Modifier.height(8.dp))
    }
}

/** Tek bir akordeon grubu — CalibraHubAndroid `DrawerGroupSection` ile ayni davranis (sadik port). */
@Composable
private fun DrawerGroupSection(
    group: DrawerGroup,
    currentRoute: String?,
    pinnedRoutes: Set<String>,
    onNavigate: (String) -> Unit,
    onTogglePin: (String) -> Unit,
) {
    var expanded by rememberSaveable(key = "drawerGroupExpanded_${group.key}") { mutableStateOf(false) }
    val containsCurrent = group.leaves.any { it.route == currentRoute }
    val isOpen = expanded || containsCurrent

    val chevronRotation by animateFloatAsState(targetValue = if (isOpen) 90f else 0f)

    NavigationDrawerItem(
        label = { Text(group.label) },
        selected = false,
        icon = { Icon(group.icon, contentDescription = null) },
        badge = {
            Icon(
                Icons.Default.ChevronRight,
                contentDescription = if (isOpen) "Daralt" else "Genişlet",
                modifier = Modifier.rotate(chevronRotation),
            )
        },
        onClick = { expanded = !expanded },
        modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding),
    )

    AnimatedVisibility(
        visible = isOpen,
        enter = expandVertically() + fadeIn(),
        exit = shrinkVertically() + fadeOut(),
    ) {
        Column(modifier = Modifier.padding(start = 16.dp)) {
            group.leaves.forEach { leaf ->
                NavigationDrawerItem(
                    label = { Text(leaf.label) },
                    selected = currentRoute == leaf.route,
                    icon = { Icon(leaf.icon, contentDescription = null) },
                    badge = {
                        PinToggleButton(
                            pinned = leaf.route in pinnedRoutes,
                            onClick = { onTogglePin(leaf.route) },
                        )
                    },
                    onClick = { onNavigate(leaf.route) },
                    modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding),
                )
            }
        }
    }
}

/** Bir [DrawerLeaf]'in sagindaki kucuk sabitleme (pin) dugmesi — CalibraHubAndroid
 * `PinToggleButton` ile ayni (sadik port). */
@Composable
private fun PinToggleButton(pinned: Boolean, onClick: () -> Unit) {
    IconButton(onClick = onClick) {
        Icon(
            imageVector = if (pinned) Icons.Default.PushPin else Icons.Outlined.PushPin,
            contentDescription = if (pinned) "Ana ekrandan kaldır" else "Ana ekrana sabitle",
            tint = if (pinned) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
