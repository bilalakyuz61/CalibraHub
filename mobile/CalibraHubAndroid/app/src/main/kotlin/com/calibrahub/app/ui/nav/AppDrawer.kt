package com.calibrahub.app.ui.nav

import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Chat
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.material.icons.filled.Logout
import androidx.compose.material.icons.filled.PrecisionManufacturing
import androidx.compose.material.icons.filled.Sell
import androidx.compose.material.icons.filled.ShoppingCart
import androidx.compose.material.icons.filled.Warehouse
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalDrawerSheet
import androidx.compose.material3.NavigationDrawerItem
import androidx.compose.material3.NavigationDrawerItemDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.calibrahub.app.R

/**
 * Sol navigasyon menüsü (drawer) kök route sabitleri — [com.calibrahub.app.MainActivity]'deki
 * NavHost route string'leriyle BİREBİR aynı olmak ZORUNDA (tek kaynak burada TUTULMUYOR;
 * MainActivity'nin composable(...) blokları bu sabitlerle inşa edilir — [drawerDestinations] ile
 * NavHost route tanımları senkron kalmalı, biri değişirse diğeri de güncellenmeli).
 * Alt ekran route'ları (warehouse_stock_query, production_work_orders, chat/{phone} vb.) buraya
 * DAHİL DEĞİL — yalnız drawer'da doğrudan listelenen 7 kök modül ekranı.
 */
object AppRoutes {
    const val HOME            = "home"
    const val WAREHOUSE_HOME  = "warehouse_home"
    const val PRODUCTION_HOME = "production_home"
    const val PURCHASE_HOME   = "purchase_home"
    const val SALES_HOME      = "sales_home"
    const val SHIPPING_HOME   = "shipping_home"
    const val CHATS           = "chats"
}

/** Drawer'da listelenen tek bir kök modül girişi. */
data class DrawerDestination(
    val route: String,
    val label: String,
    val icon: ImageVector
)

/** Sıra = drawer'da üstten alta görünüm sırası (koordinatör spesifikasyonu, 2026-07-17). */
val drawerDestinations: List<DrawerDestination> = listOf(
    DrawerDestination(AppRoutes.HOME,            "Ana Sayfa",  Icons.Default.Home),
    DrawerDestination(AppRoutes.WAREHOUSE_HOME,  "Depo",       Icons.Default.Warehouse),
    DrawerDestination(AppRoutes.PRODUCTION_HOME, "Üretim",     Icons.Default.PrecisionManufacturing),
    DrawerDestination(AppRoutes.PURCHASE_HOME,   "Satın Alma", Icons.Default.ShoppingCart),
    DrawerDestination(AppRoutes.SALES_HOME,      "Satış",      Icons.Default.Sell),
    DrawerDestination(AppRoutes.SHIPPING_HOME,   "Sevkiyat",   Icons.Default.LocalShipping),
    DrawerDestination(AppRoutes.CHATS,           "Sohbetler",  Icons.Default.Chat),
)

/**
 * Sol navigasyon menüsünün içeriği — [androidx.compose.material3.ModalNavigationDrawer]'ın
 * drawerContent'i (bkz. MainActivity.AppNav; TEK instance tüm NavHost'u sarar, her ekran kendi
 * drawer'ını kurmaz). Saf state-hoisting composable: drawerState/navController'a dokunmaz, yalnız
 * [onNavigate]/[onLogout] callback'lerini çağırır — drawer'ı kapatmak/navigate etmek çağıran
 * tarafın (AppNav) sorumluluğundadır.
 *
 * Düzen: üstte logo + "CalibraHub" + kullanıcı adı, ortada 7 kök modül ([currentRoute] ile
 * eşleşen seçili gösterilir), altta ayraçla Çıkış — aradaki weight(1f) spacer Çıkış'ı drawer'ın
 * en altına iter.
 */
@Composable
fun AppDrawerContent(
    currentRoute: String?,
    displayName: String?,
    onNavigate: (String) -> Unit,
    onLogout: () -> Unit
) {
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
            }
        }
        HorizontalDivider()
        Spacer(Modifier.height(8.dp))

        drawerDestinations.forEach { dest ->
            NavigationDrawerItem(
                label = { Text(dest.label) },
                selected = currentRoute == dest.route,
                icon = { Icon(dest.icon, contentDescription = null) },
                onClick = { onNavigate(dest.route) },
                modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding)
            )
        }

        Spacer(Modifier.weight(1f))

        HorizontalDivider()
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
