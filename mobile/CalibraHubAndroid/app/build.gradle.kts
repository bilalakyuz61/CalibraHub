plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.calibrahub.app"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.calibrahub.app"
        minSdk = 26
        targetSdk = 34
        versionCode = 1
        versionName = "0.1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        vectorDrawables.useSupportLibrary = true

        // BuildConfig'e enjekte edilen base URL — flavor / build type'a göre değişir.
        // Emulator için 10.0.2.2 host alias'ı; LAN cihazı için override gerekir.
        buildConfigField("String", "DEFAULT_BASE_URL", "\"http://10.0.2.2:61001/\"")
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
            // Production build için release URL — gerçek deployment'a göre değiştir.
            buildConfigField("String", "DEFAULT_BASE_URL", "\"https://erp.calibrahub.com/\"")
        }
        debug {
            isMinifyEnabled = false
            applicationIdSuffix = ".debug"
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions { jvmTarget = "17" }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    composeOptions {
        kotlinCompilerExtensionVersion = "1.5.8"
    }

    packaging {
        resources {
            excludes += setOf(
                "/META-INF/{AL2.0,LGPL2.1}",
                "/META-INF/DEPENDENCIES",
                "/META-INF/LICENSE",
                "/META-INF/NOTICE",
                "/META-INF/INDEX.LIST"
            )
        }
    }

    sourceSets["main"].java.srcDir("src/main/kotlin")
}

dependencies {
    // ── Compose BOM (tüm Compose lib'leri tek sürüm yönetimi) ─────────────
    implementation(platform("androidx.compose:compose-bom:2024.02.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    debugImplementation("androidx.compose.ui:ui-tooling")
    debugImplementation("androidx.compose.ui:ui-test-manifest")

    // ── Activity + Navigation + ViewModel ─────────────────────────────────
    implementation("androidx.activity:activity-compose:1.8.2")
    implementation("androidx.navigation:navigation-compose:2.7.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.7.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.7.0")

    // ── DataStore (cookie + token persistence) ────────────────────────────
    implementation("androidx.datastore:datastore-preferences:1.0.0")

    // ── WorkManager (background polling) ──────────────────────────────────
    implementation("androidx.work:work-runtime-ktx:2.9.0")

    // ── Network: Retrofit + OkHttp + Moshi ────────────────────────────────
    implementation("com.squareup.retrofit2:retrofit:2.9.0")
    implementation("com.squareup.retrofit2:converter-moshi:2.9.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.squareup.okhttp3:logging-interceptor:4.12.0")
    implementation("com.squareup.moshi:moshi:1.15.1")
    implementation("com.squareup.moshi:moshi-kotlin:1.15.1")

    // ── Image loading (Coil) ──────────────────────────────────────────────
    implementation("io.coil-kt:coil-compose:2.5.0")

    // ── Coroutines ────────────────────────────────────────────────────────
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.7.3")

    // ── Test ──────────────────────────────────────────────────────────────
    testImplementation("junit:junit:4.13.2")
    androidTestImplementation("androidx.test.ext:junit:1.1.5")
    androidTestImplementation("androidx.test.espresso:espresso-core:3.5.1")
}
