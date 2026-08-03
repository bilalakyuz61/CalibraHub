package com.calibrahub.mobile

import androidx.compose.ui.window.ComposeUIViewController
import com.calibrahub.mobile.ui.AppRoot

/**
 * iOS Xcode wrapper'in cagiracagi giris noktasi — Faz 2a'dan itibaren [AppRoot] (tema + drawer +
 * NavHost) cagirir (POC'un basit `App()` sealed-state ekrani KALDIRILDI, bkz. AppRoot.kt).
 * Bu Windows'ta derlenemez (Mac/simulator yok) — Codemagic CI fazinda Xcode projesi bu
 * fonksiyonu framework uzerinden cagirir. `onDarkThemeResolved` varsayilan no-op birakilir:
 * Android'deki status/navigation bar ikon kontrasti native Android chrome'dur (bkz. AppRoot.kt
 * KDoc'u); iOS'un kendi status bar stili (UIViewController preferredStatusBarStyle) Faz 3'te
 * ayrica ele alinir (bu Faz'in KAPSAMI DISINDA).
 */
fun MainViewController() = ComposeUIViewController { AppRoot() }
