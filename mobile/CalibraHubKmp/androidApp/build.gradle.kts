import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.compose.multiplatform)
}

// Kotlin 2.3'te `android { kotlinOptions { jvmTarget = "17" } }` DSL'i KALDIRILDI (hata veriyor:
// "Using 'jvmTarget: String' is an error. Please migrate to the compilerOptions DSL").
// Karsiligi bu top-level blok (shared modulu zaten androidTarget.compilerOptions ile ayni deseni kullaniyor).
kotlin {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
    }
}

android {
    namespace = "com.calibrahub.mobile.android"
    compileSdk = libs.versions.compileSdk.get().toInt()

    defaultConfig {
        applicationId = "com.calibrahub.mobile"
        minSdk = libs.versions.minSdk.get().toInt()
        targetSdk = libs.versions.targetSdk.get().toInt()
        versionCode = 1
        versionName = "0.1.0-poc"
    }

    buildTypes {
        debug {
            isMinifyEnabled = false
        }
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
    }

    sourceSets["main"].java.srcDir("src/main/kotlin")
}

dependencies {
    implementation(project(":shared"))
    implementation(libs.androidx.activity.compose)
}
