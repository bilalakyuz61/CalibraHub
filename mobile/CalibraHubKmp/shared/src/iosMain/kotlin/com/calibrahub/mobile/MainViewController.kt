package com.calibrahub.mobile

import androidx.compose.ui.window.ComposeUIViewController
import com.calibrahub.mobile.ui.AppRoot

/**
 * iOS giris noktasi — Xcode wrapper'inin (iosApp/ContentView.swift) cagirdigi tek fonksiyon.
 * [AppRoot] tema + drawer + NavHost'u kurar; uygulama dogrudan LOGIN ekraniyla acilir.
 *
 * TARIHCE (2026-08-21): burada gecici bir teshis ekrani (DiagnosticRoot + diagnosticPing/
 * diagnosticComposeInit sondalari) vardi — uygulama iOS'ta acilir acilmaz coküyordu ve crash
 * log'u ne cihazdan ne App Store Connect'ten alinabiliyordu. Kok neden Codemagic'in iOS Simulator
 * kosumunda (`ios-sim-run` workflow'u) bulundu: Info.plist'te `CADisableMinimumFrameDurationOnPhone`
 * anahtari yoktu, Compose Multiplatform acilista bunu denetleyip (PlistSanityCheck) bilerek
 * IllegalStateException firlatiyordu. Anahtar eklenince (iosApp/project.yml) uygulama acildi;
 * sondalar kaldirildi.
 */
fun MainViewController() = ComposeUIViewController { AppRoot() }
