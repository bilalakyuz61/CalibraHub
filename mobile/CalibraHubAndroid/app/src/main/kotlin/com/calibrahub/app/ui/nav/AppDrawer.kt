package com.calibrahub.app.ui.nav

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.Image
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
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.Chat
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
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.calibrahub.app.BuildConfig
import com.calibrahub.app.R
import com.calibrahub.app.app
import kotlinx.coroutines.launch

/**
 * Sol navigasyon menüsü (drawer) route sabitleri — [com.calibrahub.app.MainActivity]'deki NavHost
 * route string'leriyle BİREBİR aynı olmak ZORUNDA (tek kaynak burada TUTULMUYOR; MainActivity'nin
 * composable(...) blokları bu sabitlerle inşa edilir — [drawerEntries] ile NavHost route
 * tanımları senkron kalmalı, biri değişirse diğeri de güncellenmeli).
 *
 * 2026-07-17 akordeon migration: eski *_HOME "kart menüsü" route'ları (warehouse_home,
 * production_home, purchase_home, sales_home, shipping_home) KALDIRILDI — o ekranlar (
 * WarehouseHomeScreen, ProductionHomeScreen, PurchaseHomeScreen, SalesHomeScreen,
 * ShippingHomeScreen) SİLİNDİ. Drawer artık modül başlıklarını akordeon gibi açıp kapatıyor;
 * yapraklar (aşağıdaki sabitler) doğrudan gerçek form/ekran route'larına işaret eder.
 */
object AppRoutes {
    const val HOME  = "home"
    const val CHATS = "chats"

    const val WAREHOUSE_STOCK_QUERY  = "warehouse_stock_query"
    const val WAREHOUSE_STOCK_IN     = "warehouse_stock_in"
    const val WAREHOUSE_STOCK_OUT    = "warehouse_stock_out"
    const val WAREHOUSE_TRANSFER     = "warehouse_transfer"
    const val WAREHOUSE_COUNT        = "warehouse_count"
    const val WAREHOUSE_DRAFT_COUNTS = "warehouse_draft_counts"

    const val PRODUCTION_WORK_ORDERS = "production_work_orders"

    const val PURCHASE_DELIVERY    = "warehouse_delivery/purchase"
    const val PURCHASE_OPEN_ORDERS = "warehouse_open_orders/purchase"

    const val SALES_DELIVERY    = "warehouse_delivery/sales"
    const val SALES_OPEN_ORDERS = "warehouse_open_orders/sales"

    // Sevkiyat'ın tek yaprağı Satış'ın "Açık Satış Siparişleri" ile AYNI route'a gider — bilinçli
    // kısmi örtüşme (koordinatör spesifikasyonu, 2026-07-17): sevkiyat personeli Satış modülüne
    // girmeden doğrudan aynı teslimat akışına (OpenOrderListScreen) ulaşsın diye.
    const val SHIPPING_OPEN_ORDERS = SALES_OPEN_ORDERS

    // Ayarlar (2026-07-19, tekilleştirme) — drawerEntries'in DIŞINDA tutulur; TEK giriş
    // noktası [com.calibrahub.app.ui.home.HomeScreen]'in üst çubuğundaki dişli ikonudur. Başlangıçta
    // drawer'da da (Çıkış'a yakın) ayrı bir "Ayarlar" girişi vardı — iki farklı giriş noktası kafa
    // karıştırdığı için KALDIRILDI (kullanıcı kararı: "sağ üst köşedeki kalabilir"). Bu yüzden
    // drawerTopLevelRoutes'a (flat/tab navigasyon + edge-swipe seti) DAHİL DEĞİLDİR — normal "push"
    // navigasyonuyla açılır (MainActivity: navController.navigate(SETTINGS)), geri tuşu açıldığı
    // ekrana döner (diğer top-level yapraklar gibi popUpTo(HOME) flat-reset YAPMAZ).
    const val SETTINGS = "settings"
}

/** Drawer'da doğrudan navigate edilebilir tek bir yaprak hedef. */
data class DrawerLeaf(
    val route: String,
    val label: String,
    val icon: ImageVector
)

/**
 * Drawer'da akordeon gibi açılıp kapanan bir modül başlığı. KENDİSİ navigate ETMEZ — tıklanınca
 * yalnız kendi aç/kapa state'ini toggle eder (bkz. [DrawerGroupSection]); gerçek navigasyon
 * yalnız [leaves] içindeki bir [DrawerLeaf]'e basıldığında olur.
 */
data class DrawerGroup(
    val key: String,
    val label: String,
    val icon: ImageVector,
    val leaves: List<DrawerLeaf>
)

/** Drawer'ın üst seviye girişi — ya doğrudan bir [DrawerLeaf] (Ana Sayfa, Sohbetler) ya da
 * akordeon [DrawerGroup] (Depo, Üretim, Satın Alma, Satış, Sevkiyat). */
sealed class DrawerEntry {
    data class Single(val leaf: DrawerLeaf) : DrawerEntry()
    data class Expandable(val group: DrawerGroup) : DrawerEntry()
}

/**
 * Drawer içeriği — 2026-07-17 akordeon migration (koordinatör spesifikasyonu). Sıra = drawer'da
 * üstten alta görünüm sırası: Ana Sayfa (yaprak) → Depo/Üretim/Satın Alma/Satış/Sevkiyat
 * (akordeon grupları) → Sohbetler (yaprak).
 */
val drawerEntries: List<DrawerEntry> = listOf(
    DrawerEntry.Single(DrawerLeaf(AppRoutes.HOME, "Ana Sayfa", Icons.Default.Home)),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "depo",
            label = "Depo",
            icon = Icons.Default.Warehouse,
            leaves = listOf(
                DrawerLeaf(AppRoutes.WAREHOUSE_STOCK_QUERY, "Stok Sorgu", Icons.Default.Inventory2),
                DrawerLeaf(AppRoutes.WAREHOUSE_STOCK_IN, "Giriş", Icons.Default.MoveToInbox),
                DrawerLeaf(AppRoutes.WAREHOUSE_STOCK_OUT, "Çıkış", Icons.Default.Outbox),
                DrawerLeaf(AppRoutes.WAREHOUSE_TRANSFER, "Transfer", Icons.Default.SwapHoriz),
                DrawerLeaf(AppRoutes.WAREHOUSE_COUNT, "Sayım", Icons.Default.Checklist),
                DrawerLeaf(AppRoutes.WAREHOUSE_DRAFT_COUNTS, "Taslak Sayımlar", Icons.AutoMirrored.Filled.FactCheck),
            )
        )
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "uretim",
            label = "Üretim",
            icon = Icons.Default.PrecisionManufacturing,
            leaves = listOf(
                DrawerLeaf(AppRoutes.PRODUCTION_WORK_ORDERS, "İş Emirleri", Icons.AutoMirrored.Filled.Assignment),
            )
        )
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "satinalma",
            label = "Satın Alma",
            icon = Icons.Default.ShoppingCart,
            leaves = listOf(
                DrawerLeaf(AppRoutes.PURCHASE_DELIVERY, "Alış İrsaliyesi", Icons.Default.LocalShipping),
                DrawerLeaf(AppRoutes.PURCHASE_OPEN_ORDERS, "Açık Alış Siparişleri", Icons.AutoMirrored.Filled.Assignment),
            )
        )
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "satis",
            label = "Satış",
            icon = Icons.Default.Sell,
            leaves = listOf(
                DrawerLeaf(AppRoutes.SALES_DELIVERY, "Satış İrsaliyesi", Icons.Default.ReceiptLong),
                DrawerLeaf(AppRoutes.SALES_OPEN_ORDERS, "Açık Satış Siparişleri", Icons.Default.PendingActions),
            )
        )
    ),
    DrawerEntry.Expandable(
        DrawerGroup(
            key = "sevkiyat",
            label = "Sevkiyat",
            icon = Icons.Default.LocalShipping,
            leaves = listOf(
                DrawerLeaf(AppRoutes.SHIPPING_OPEN_ORDERS, "Açık Satış Siparişleri (Teslim)", Icons.Default.PendingActions),
            )
        )
    ),
    DrawerEntry.Single(DrawerLeaf(AppRoutes.CHATS, "Sohbetler", Icons.Default.Chat)),
)

/**
 * Drawer'dan DOĞRUDAN erişilebilen tüm route'lar (Single yaprakları + Expandable gruplarının TÜM
 * yaprakları) — [com.calibrahub.app.MainActivity]'nin edge-swipe gesture'ı ve popUpTo/
 * launchSingleTop "flat" navigasyon deseni için "top-level route" tanımı BURADAN türetilir (tek
 * kaynak — [drawerEntries] değişirse otomatik senkron kalır, ayrı bir liste elle bakımlanmaz).
 */
val drawerTopLevelRoutes: Set<String> = drawerEntries.flatMap { entry ->
    when (entry) {
        is DrawerEntry.Single -> listOf(entry.leaf.route)
        is DrawerEntry.Expandable -> entry.group.leaves.map { it.route }
    }
}.toSet()

/**
 * [drawerEntries]'ten türetilen, ana ekrana SABİTLENEBİLİR (pinnable) tüm yaprakların DÜZ (flat)
 * listesi (2026-07-19) — Ana Sayfa (zaten o an gösterildiği ekran olduğu için sabitlenemez) HARİÇ
 * tüm Single + Expandable-grup yaprakları. Akordeon grup BAŞLIKLARI (Depo/Üretim/Satın Alma/Satış/
 * Sevkiyat) bir route'a gitmediği için burada YER ALMAZ — yalnız GERÇEK navigasyon yapan yapraklar.
 *
 * Sıra = [drawerEntries]'teki doğal görünüm sırası — [com.calibrahub.app.ui.home.HomeScreen] ızgara
 * sıralamasını sabitleme sırasına DEĞİL bu doğal sıraya göre yapar (bkz. HomeScreen KDoc'u).
 *
 * Tek kaynak: route+etiket+ikon tanımı yalnız [drawerEntries]'te yaşar; ne [AppDrawerContent] ne de
 * [com.calibrahub.app.ui.home.HomeScreen] kendi kopyasını TUTMAZ — ikisi de BU listeyi
 * [com.calibrahub.app.data.SessionManager.pinnedRoutes] ile kesiştirerek besler.
 */
val pinnableDrawerLeaves: List<DrawerLeaf> = drawerEntries.flatMap { entry ->
    when (entry) {
        is DrawerEntry.Single -> if (entry.leaf.route == AppRoutes.HOME) emptyList() else listOf(entry.leaf)
        is DrawerEntry.Expandable -> entry.group.leaves
    }
}

/**
 * Sol navigasyon menüsünün içeriği — [androidx.compose.material3.ModalNavigationDrawer]'ın
 * drawerContent'i (bkz. MainActivity.AppNav; TEK instance tüm NavHost'u sarar, her ekran kendi
 * drawer'ını kurmaz). Navigasyon açısından saf state-hoisting composable: drawerState/
 * navController'a dokunmaz, yalnız [onNavigate] (bir [DrawerLeaf]'e basılınca) / [onLogout]
 * callback'lerini çağırır — drawer'ı kapatmak/navigate etmek çağıran tarafın (AppNav)
 * sorumluluğundadır. Akordeon aç/kapa state'i ([DrawerGroupSection] içinde) BU composable'ın kendi
 * sorumluluğudur, dışarı sızmaz.
 *
 * Düzen: üstte logo + "CalibraHub" + kullanıcı adı + şirket adı, ortada kaydırılabilir modül
 * listesi (Ana Sayfa yaprağı + 5 akordeon grubu + Sohbetler yaprağı — [currentRoute] ile eşleşen
 * yaprak vurgulanır), altta ayraç + sürüm bilgisi + Çıkış. 2026-07-19 tekilleştirme: "Ayarlar"
 * girişi buradan KALDIRILDI — tek giriş noktası artık [com.calibrahub.app.ui.home.HomeScreen]'in
 * üst çubuğundaki dişli ikonu (bkz. [AppRoutes.SETTINGS] KDoc'u).
 *
 * Ana ekran kısayol sabitleme (2026-07-19): her sabitlenebilir yaprağın ([pinnableDrawerLeaves])
 * sağında bir pin ([PinToggleButton]) gösterilir — [com.calibrahub.app.data.SessionManager.
 * pinnedRoutes] Flow'u BURADA (tema tercihiyle AYNI desen — bkz. SessionManager.themeMode KDoc'u)
 * doğrudan [LocalContext] üzerinden okunur/yazılır, dışarıdan parametre olarak HOISTLENMEZ; pine
 * dokunmak yalnız o route'un sabitleme durumunu toggle eder, [onNavigate]'i TETİKLEMEZ (ayrı bir
 * [IconButton], satırın kendi tıklama alanından bağımsız — nested clickable, iç olan tüketir).
 */
@Composable
fun AppDrawerContent(
    currentRoute: String?,
    displayName: String?,
    companyName: String? = null,
    onNavigate: (String) -> Unit,
    onLogout: () -> Unit
) {
    val context = LocalContext.current
    val session = context.app.session
    val scope = rememberCoroutineScope()
    val pinnedRoutes by session.pinnedRoutes.collectAsState(initial = emptySet<String>())
    val onTogglePin: (String) -> Unit = { route -> scope.launch { session.togglePinnedRoute(route) } }

    ModalDrawerSheet {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp, vertical = 20.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Image(
                painter = painterResource(id = R.drawable.calibrahub_logo),
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier
                    .size(40.dp)
                    .clip(CircleShape)
            )
            Spacer(Modifier.width(12.dp))
            Column {
                Text(
                    text = "CalibraHub",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold
                )
                if (!displayName.isNullOrBlank()) {
                    Text(
                        text = displayName,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
                if (!companyName.isNullOrBlank()) {
                    Text(
                        text = companyName,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }
        }
        HorizontalDivider()
        Spacer(Modifier.height(8.dp))

        // Kaydırılabilir orta bölge: header sabit üstte, Sürüm+Çıkış sabit altta kalır (bkz.
        // aşağıdaki HorizontalDivider sonrası blok) — akordeon TÜMÜYLE açıldığında (~19 satır)
        // küçük ekranlarda taşmasın diye. weight(1f) kalan tüm alanı doldurur.
        Column(
            modifier = Modifier
                .weight(1f)
                .verticalScroll(rememberScrollState())
        ) {
            drawerEntries.forEach { entry ->
                when (entry) {
                    is DrawerEntry.Single -> {
                        // Ana Sayfa hariç (zaten o an gösterilen ekran) tüm Single yapraklar
                        // sabitlenebilir — bkz. pinnableDrawerLeaves KDoc'u.
                        val pinnable = entry.leaf.route != AppRoutes.HOME
                        val pinBadge: (@Composable () -> Unit)? = if (!pinnable) null else {
                            {
                                PinToggleButton(
                                    pinned = entry.leaf.route in pinnedRoutes,
                                    onClick = { onTogglePin(entry.leaf.route) }
                                )
                            }
                        }
                        NavigationDrawerItem(
                            label = { Text(entry.leaf.label) },
                            selected = currentRoute == entry.leaf.route,
                            icon = { Icon(entry.leaf.icon, contentDescription = null) },
                            badge = pinBadge,
                            onClick = { onNavigate(entry.leaf.route) },
                            modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding)
                        )
                    }
                    is DrawerEntry.Expandable -> {
                        DrawerGroupSection(
                            group = entry.group,
                            currentRoute = currentRoute,
                            pinnedRoutes = pinnedRoutes,
                            onNavigate = onNavigate,
                            onTogglePin = onTogglePin
                        )
                    }
                }
            }
        }

        HorizontalDivider()
        Text(
            text = "Sürüm ${BuildConfig.VERSION_NAME}" + if (BuildConfig.DEBUG) " (debug)" else "",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(horizontal = 20.dp, vertical = 8.dp)
        )
        NavigationDrawerItem(
            label = { Text("Çıkış") },
            selected = false,
            icon = { Icon(Icons.Default.Logout, contentDescription = null) },
            onClick = onLogout,
            modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding)
        )
        Spacer(Modifier.height(8.dp))
    }
}

/**
 * Tek bir akordeon grubu — başlık satırı ([NavigationDrawerItem], trailing'de dönen chevron ile)
 * tıklanınca yalnız kendi [expanded] state'ini toggle eder (navigate ETMEZ). [currentRoute] bu
 * grubun bir yaprağındaysa ([containsCurrent]) grup HER ZAMAN açık gösterilir — küçük UX kararı:
 * şu an içinde bulunulan ekranın grubu kullanıcı elle kapatamaz (nereye "kaybolduğu" kafa
 * karıştırmasın diye). [expanded] yalnız DİĞER durumda (grup aktif DEĞİLKEN) kullanıcı toggle'ını
 * sürer; [rememberSaveable] ile grup başına ayrıştırılmış `key` sayesinde config change/process
 * restore'da hayatta kalır.
 *
 * Grubun kendisi (başlık satırı) sabitlenemez — bir route'a gitmez (bkz. [pinnableDrawerLeaves]
 * KDoc'u); yalnız İÇİNDEKİ yapraklar [PinToggleButton] gösterir ([pinnedRoutes]/[onTogglePin]
 * [AppDrawerContent]'ten hoistlenir, burada Flow'a doğrudan erişilmez).
 */
@Composable
private fun DrawerGroupSection(
    group: DrawerGroup,
    currentRoute: String?,
    pinnedRoutes: Set<String>,
    onNavigate: (String) -> Unit,
    onTogglePin: (String) -> Unit
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
                modifier = Modifier.rotate(chevronRotation)
            )
        },
        onClick = { expanded = !expanded },
        modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding)
    )

    AnimatedVisibility(
        visible = isOpen,
        enter = expandVertically() + fadeIn(),
        exit = shrinkVertically() + fadeOut()
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
                            onClick = { onTogglePin(leaf.route) }
                        )
                    },
                    onClick = { onNavigate(leaf.route) },
                    modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding)
                )
            }
        }
    }
}

/**
 * Bir [DrawerLeaf]'in sağındaki küçük sabitleme (pin) düğmesi (2026-07-19) — [pinned] ise
 * dolu ikon + vurgulu (`primary`) renk, değilse anahat (outlined) ikon + soluk
 * (`onSurfaceVariant`) renk. Kendi [IconButton]'ı olduğu için tıklaması SATIRIN
 * [NavigationDrawerItem.onClick]'ini TETİKLEMEZ (nested clickable — iç olan dokunuşu tüketir),
 * drawer açık kalır ve kullanıcı art arda birden çok modül sabitleyebilir.
 */
@Composable
private fun PinToggleButton(pinned: Boolean, onClick: () -> Unit) {
    IconButton(onClick = onClick) {
        Icon(
            imageVector = if (pinned) Icons.Default.PushPin else Icons.Outlined.PushPin,
            contentDescription = if (pinned) "Ana ekrandan kaldır" else "Ana ekrana sabitle",
            tint = if (pinned) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}
