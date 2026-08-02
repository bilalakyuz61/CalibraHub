package com.calibrahub.mobile

import androidx.compose.ui.window.ComposeUIViewController
import com.calibrahub.mobile.ui.App

/**
 * iOS Xcode wrapper'in cagiracagi giris noktasi. Bu POC asamasinda Windows'ta
 * derlenemez (Mac/simulator yok) — Codemagic CI fazinda Xcode projesi bu
 * fonksiyonu framework uzerinden cagirir.
 */
fun MainViewController() = ComposeUIViewController { App() }
