import org.jetbrains.kotlin.gradle.ExperimentalKotlinGradlePluginApi
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    alias(libs.plugins.kotlin.multiplatform)
    alias(libs.plugins.android.library)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.compose.multiplatform)
}

kotlin {
    androidTarget {
        @OptIn(ExperimentalKotlinGradlePluginApi::class)
        compilerOptions {
            jvmTarget.set(JvmTarget.JVM_17)
        }
    }

    // iosX64 (Intel simulator) 2026-08-20'de KALDIRILDI — Kotlin 2.2.20 whatsnew'de macosX64/iosX64
    // support tier-2'ye demote edildi (bkz. gradle/libs.versions.toml kotlin yorumu) VE Compose
    // Multiplatform 1.11.1'in cekirdek kutuphaneleri (runtime/foundation/ui/components-resources)
    // artik iosX64 icin klib YAYINLAMIYOR — `kmpPartiallyResolvedDependenciesChecker` build'i
    // "KMP Dependencies Resolution Failure" ile durduruyordu (appleMain/nativeMain 'Unresolved
    // platforms: [iosX64]'). Codemagic mac_mini_m2 (Apple Silicon) kullaniyor; iosX64 zaten hicbir
    // CI script'inde referans edilmiyor (iosArm64=cihaz, iosSimulatorArm64=Apple Silicon simulator
    // yeterli) — kaldirilmasi davranis kaybi degil.
    listOf(
        iosArm64(),
        iosSimulatorArm64(),
    ).forEach { iosTarget ->
        iosTarget.binaries.framework {
            baseName = "shared"
            // 2026-08-21: isStatic true -> false (DINAMIK framework).
            // Teshis: SwiftUI kabugu aciliyor ama MainViewController() cagrilinca uygulama
            // COKUYOR -> sorun Kotlin/Compose ayaga kalkmasinda. Statik framework'te Skia
            // (skiko) ve bagimli oldugu sistem framework'lerinin link/init kayitlari
            // dusebiliyor; dinamik framework bu bagimliliklari KENDI icinde cozup yaninda
            // tasir (embedAndSignAppleFrameworkForXcode uygulama paketine gomup imzalar).
            isStatic = false
        }
    }

    sourceSets {
        commonMain.dependencies {
            implementation(compose.runtime)
            implementation(compose.foundation)
            implementation(compose.material3)
            implementation(compose.ui)
            implementation(compose.components.resources)
            implementation(compose.materialIconsExtended)

            implementation(libs.ktor.client.core)
            implementation(libs.ktor.client.content.negotiation)
            implementation(libs.ktor.serialization.kotlinx.json)
            implementation(libs.kotlinx.coroutines.core)
            implementation(libs.jetbrains.navigation.compose)
        }
        androidMain.dependencies {
            implementation(libs.ktor.client.okhttp)
            implementation(libs.androidx.datastore.preferences)
            implementation(libs.zxing.android.embedded)
            implementation(libs.androidx.activity.compose) // PlatformBackHandler (androidx.activity BackHandler)
        }
        iosMain.dependencies {
            implementation(libs.ktor.client.darwin)
        }
    }
}

android {
    namespace = "com.calibrahub.mobile.shared"
    compileSdk = libs.versions.compileSdk.get().toInt()

    defaultConfig {
        minSdk = libs.versions.minSdk.get().toInt()
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    sourceSets["main"].manifest.srcFile("src/androidMain/AndroidManifest.xml")
}
